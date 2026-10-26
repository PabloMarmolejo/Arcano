import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from 'react'
import { ContextoArcano } from './contexto'
import type { Aviso, Contexto, Herramienta } from './contexto'
import { estadoInicial, idsDelGrupo, reductor } from './estado'
import { cargarPdf } from './pdf'
import type { DocumentoPdf } from './pdf'
import { alProgreso, enEscritorio, ErrorPuente, llamar } from './puente'
import type { ActualizacionDisponible, Coincidencia, DatosLeyenda, DocumentoAbierto, InfoPagina, Inicio, Palabra, Progreso, RectN } from './tipos'
import { SelectorCategoria } from './componentes/Comunes'
import { DialogoActualizacion } from './componentes/DialogoActualizacion'
import { DialogoCatalogo } from './componentes/DialogoCatalogo'
import { DialogoGenerar } from './componentes/DialogoGenerar'
import { Miniaturas } from './componentes/Miniaturas'
import { PanelBuscar } from './componentes/PanelBuscar'
import { PanelDetectar } from './componentes/PanelDetectar'
import { PanelRevision } from './componentes/PanelRevision'
import { PanelTestados } from './componentes/PanelTestados'
import { PT_A_PX, Visor } from './componentes/Visor'

type Pestana = 'testados' | 'buscar' | 'detectar' | 'revision'

const PESTANAS: { id: Pestana; nombre: string }[] = [
  { id: 'testados', nombre: 'Testados' },
  { id: 'buscar', nombre: 'Buscar' },
  { id: 'detectar', nombre: 'Detectar' },
  { id: 'revision', nombre: 'Revisión' },
]

const leyendaVacia: DatosLeyenda = { documento: '', textoAdicional: '' }

export function App() {
  const [inicio, setInicio] = useState<Inicio | null>(null)
  const [errorInicio, setErrorInicio] = useState<string | null>(null)
  const [doc, setDoc] = useState<DocumentoAbierto | null>(null)
  const [pdf, setPdf] = useState<DocumentoPdf | null>(null)
  const [estado, despachar] = useReducer(reductor, estadoInicial)
  const [categoriaActiva, setCategoriaActiva] = useState('nombre')
  const [herramienta, setHerramienta] = useState<Herramienta>('texto')
  const [vistaPrevia, setVistaPrevia] = useState(false)
  const [zoom, setZoom] = useState(1)
  const [anchoDisponible, setAnchoDisponible] = useState(900)
  const [candidatas, setCandidatas] = useState<Coincidencia[]>([])
  const [foco, setFoco] = useState<Coincidencia | null>(null)
  const [pestana, setPestana] = useState<Pestana>('testados')
  const [paginaActual, setPaginaActual] = useState(0)
  const [terminos, setTerminos] = useState<string[]>([])
  const [busquedaPedida, setBusquedaPedida] = useState<{ termino: string; id: number } | null>(null)
  const [leyenda, setLeyenda] = useState<DatosLeyenda>(leyendaVacia)
  const [generando, setGenerando] = useState(false)
  const [editandoCatalogo, setEditandoCatalogo] = useState(false)
  const [actualizacion, setActualizacion] = useState<ActualizacionDisponible | null>(null)
  const [viendoActualizacion, setViendoActualizacion] = useState(false)
  const [tarea, setTarea] = useState<Progreso | null>(null)
  const [avisos, setAvisos] = useState<Aviso[]>([])
  const [confirmacion, setConfirmacion] = useState<{ texto: string; si: string; no: string; resolver: (r: boolean) => void } | null>(null)
  const irARef = useRef<(p: number, c?: RectN) => void>(() => {})
  const palabrasCache = useRef(new Map<number, Promise<Palabra[]>>())
  const siguienteAviso = useRef(1)

  const avisar = useCallback((texto: string, tipo: Aviso['tipo'] = 'info') => {
    const id = siguienteAviso.current++
    setAvisos((a) => [...a, { id, texto, tipo }])
    window.setTimeout(() => setAvisos((a) => a.filter((x) => x.id !== id)), tipo === 'error' ? 8000 : 4000)
  }, [])

  const confirmar = (texto: string, si = 'Sí', no = 'No') =>
    new Promise<boolean>((resolver) => setConfirmacion({ texto, si, no, resolver }))

  useEffect(() => {
    llamar<Inicio>('iniciar')
      .then((i) => {
        setInicio(i)
        setLeyenda({ ...leyendaVacia, ...i.preferencias.leyenda })
        if (i.archivoInicial) void abrir(i.archivoInicial)
        // Sin bloquear el arranque: si no hay red, simplemente no se avisa. Si se
        // abrió sin documento, el aviso se muestra de una vez; si se abrió con uno,
        // queda solo el botón de la barra para no interrumpir.
        llamar<ActualizacionDisponible | null>('buscarActualizacion')
          .then((a) => {
            setActualizacion(a)
            if (a && !i.archivoInicial) setViendoActualizacion(true)
          })
          .catch(() => {})
      })
      .catch((e: Error) => setErrorInicio(e.message))
  }, [])

  useEffect(() => {
    if (doc) void llamar('marcarCambios', { pendientes: estado.pendientes })
  }, [estado.pendientes, doc])

  // Solo se muestra el avance de tareas que abrió esta ventana (abrir, OCR); el
  // diálogo de generar muestra el suyo.
  useEffect(
    () => alProgreso((p) => (p.etapa === 'ocr' || p.etapa === 'conversion' || p.etapa === 'actualizacion' ? setTarea((actual) => (actual ? p : actual)) : undefined)),
    [],
  )

  const abrir = async (ruta?: string) => {
    if (estado.pendientes && !(await confirmar('Hay cambios sin guardar. ¿Abrir otro documento de todos modos?'))) return
    try {
      setTarea({ etapa: 'abrir', actual: 0, total: 1, detalle: 'Abriendo el documento…' })
      const abierto = await llamar<DocumentoAbierto | null>('abrir', ruta ? { ruta } : {})
      if (!abierto) return
      const documento = await cargarPdf(abierto.url)
      if (doc) {
        void llamar('cerrar', { id: doc.id })
        await pdf?.destroy()
      }
      palabrasCache.current.clear()
      setDoc(abierto)
      setPdf(documento)
      setCandidatas([])
      setPaginaActual(0)
      setPestana('testados')
      setTerminos([])
      despachar({ tipo: 'cargar', regiones: [] })
      const p = abierto.proyecto
      if (p) {
        const fecha = new Date(p.modificado).toLocaleString('es-MX')
        if (await confirmar(`Se encontró un trabajo guardado de este documento (${p.regiones.length} zonas, ${fecha}). ¿Continuarlo?`, 'Continuar', 'Empezar de nuevo')) {
          despachar({ tipo: 'cargar', regiones: p.regiones })
          setTerminos(p.terminos)
          if (p.leyenda) setLeyenda({ ...leyendaVacia, ...p.leyenda })
        }
      } else if (abierto.proyectoDistinto) {
        avisar('Hay un trabajo guardado, pero corresponde a otra versión del archivo; no se cargó.', 'error')
      }
      if (abierto.advertencias.length > 0) {
        setPestana('revision')
        avisar(abierto.advertencias[0])
      }
      const sinTexto = abierto.paginas.filter((x) => x.texto === 'SinTexto').length
      if (sinTexto > 0) avisar(`${sinTexto} página(s) son escaneos sin texto. Puede reconocer su texto en la pestaña Revisión.`)
    } catch (e) {
      avisar((e as ErrorPuente).message, 'error')
    } finally {
      setTarea(null)
    }
  }

  const guardarProyecto = useCallback(async () => {
    if (!doc) return
    try {
      await llamar<string>('guardarProyecto', { id: doc.id, regiones: estado.regiones, terminos, leyenda })
      despachar({ tipo: 'guardado' })
      avisar('Trabajo guardado junto al documento.', 'exito')
    } catch (e) {
      avisar((e as ErrorPuente).message, 'error')
    }
  }, [doc, estado.regiones, terminos, leyenda, avisar])

  const actualizar = async () => {
    setViendoActualizacion(false)
    if (doc && estado.pendientes) await guardarProyecto()
    setTarea({ etapa: 'actualizacion', actual: 0, total: 1, detalle: 'Descargando la versión nueva…' })
    try {
      await llamar('instalarActualizacion')
      setTarea({ etapa: 'actualizacion', actual: 1, total: 1, detalle: 'Instalando; Arcano se volverá a abrir…' })
    } catch (e) {
      const err = e as ErrorPuente
      avisar(err.cancelado ? 'Se canceló la actualización.' : err.message, err.cancelado ? 'info' : 'error')
      setTarea(null)
    }
  }

  const reconocer = async (paginas: number[]): Promise<InfoPagina[] | null> => {
    if (!doc) return null
    setTarea({ etapa: 'ocr', actual: 0, total: paginas.length, detalle: 'Preparando el reconocimiento…' })
    try {
      const nuevas = await llamar<InfoPagina[]>('reconocer', { id: doc.id, paginas })
      palabrasCache.current.clear()
      setDoc({ ...doc, paginas: nuevas })
      avisar('Texto reconocido. Ya puede buscar y detectar datos en esas páginas.', 'exito')
      return nuevas
    } catch (e) {
      const err = e as ErrorPuente
      avisar(err.cancelado ? 'Se canceló el reconocimiento.' : err.message, err.cancelado ? 'info' : 'error')
      return null
    } finally {
      setTarea(null)
    }
  }

  /**
   * Desde un aviso de la verificación: lleva a la página, reconoce su texto si es
   * un escaneo y busca el término en todo el documento para testarlo.
   */
  const revisarTexto = async (termino: string, pagina: number) => {
    setGenerando(false)
    irA(pagina)
    const escaneadas = (doc?.paginas ?? []).filter((p) => p.texto === 'SinTexto').map((p) => p.indice)
    if (escaneadas.length > 0 && inicio?.ocrDisponible) await reconocer(escaneadas)
    setPestana('buscar')
    setBusquedaPedida({ termino, id: Date.now() })
    window.setTimeout(() => irA(pagina), 300)
  }

  const palabras = useCallback(
    (pagina: number) => {
      if (!doc) return Promise.resolve([])
      let p = palabrasCache.current.get(pagina)
      if (!p) {
        p = llamar<Palabra[]>('palabras', { id: doc.id, pagina })
        palabrasCache.current.set(pagina, p)
      }
      return p
    },
    [doc],
  )

  const anchoMaximoPt = doc ? Math.max(...doc.paginas.map((p) => p.anchoPt)) : 612
  const ajustarAncho = useCallback(() => setZoom(Math.max(0.3, Math.min(3, anchoDisponible / (anchoMaximoPt * PT_A_PX)))), [anchoDisponible, anchoMaximoPt])
  const primeraMedida = useRef(true)
  useEffect(() => {
    if (doc && primeraMedida.current) {
      ajustarAncho()
      primeraMedida.current = false
    }
  }, [doc, ajustarAncho])
  useEffect(() => {
    primeraMedida.current = true
  }, [doc?.id])

  // Atajos de teclado.
  useEffect(() => {
    const alTecla = (e: KeyboardEvent) => {
      const enCampo = (e.target as HTMLElement).closest('input, textarea, select')
      if (e.ctrlKey && e.key.toLowerCase() === 'o') {
        e.preventDefault()
        void abrir()
        return
      }
      if (!doc) return
      if (e.ctrlKey && e.key.toLowerCase() === 's') {
        e.preventDefault()
        void guardarProyecto()
      } else if (enCampo) {
        return
      } else if (e.ctrlKey && e.key.toLowerCase() === 'z') {
        e.preventDefault()
        despachar({ tipo: e.shiftKey ? 'rehacer' : 'deshacer' })
      } else if (e.ctrlKey && e.key.toLowerCase() === 'y') {
        e.preventDefault()
        despachar({ tipo: 'rehacer' })
      } else if ((e.key === 'Delete' || e.key === 'Backspace') && estado.seleccion) {
        despachar({ tipo: 'quitar', ids: [estado.seleccion] })
      } else if (e.key === 'Escape') {
        despachar({ tipo: 'seleccionar', id: null })
      } else if (e.key.toLowerCase() === 't') {
        setHerramienta('texto')
      } else if (e.key.toLowerCase() === 'a') {
        setHerramienta('area')
      } else if (e.key.toLowerCase() === 'v') {
        setVistaPrevia((v) => !v)
      } else if (e.key === '+' || e.key === '=') {
        setZoom((z) => Math.min(4, z * 1.2))
      } else if (e.key === '-') {
        setZoom((z) => Math.max(0.25, z / 1.2))
      }
    }
    window.addEventListener('keydown', alTecla)
    return () => window.removeEventListener('keydown', alTecla)
  })

  const categoria = useCallback((id?: string | null) => (id ? inicio?.catalogo.categorias.find((c) => c.id === id) : undefined), [inicio])
  const registrarIrA = useCallback((f: (p: number, c?: RectN) => void) => {
    irARef.current = f
  }, [])
  const irA = useCallback((p: number, c?: RectN) => irARef.current(p, c), [])

  const contexto = useMemo<Contexto | null>(
    () =>
      inicio && doc && pdf
        ? {
            inicio, doc, pdf, estado, despachar, categoriaActiva, setCategoriaActiva, herramienta, setHerramienta,
            vistaPrevia, zoom, candidatas, setCandidatas, foco, setFoco, irA, registrarIrA, palabras,
            actualizarDoc: setDoc, avisar, categoria,
          }
        : null,
    [inicio, doc, pdf, estado, categoriaActiva, herramienta, vistaPrevia, zoom, candidatas, foco, irA, registrarIrA, palabras, avisar, categoria],
  )

  // Si la selección cambia de categoría desde la barra, se aplica a la zona elegida.
  const cambiarCategoriaActiva = (id: string) => {
    setCategoriaActiva(id)
    if (estado.seleccion) despachar({ tipo: 'categoria', ids: idsDelGrupo(estado.regiones, estado.seleccion), categoriaId: id })
  }

  if (!enEscritorio) return <div className="pantalla-centro">Abra esta interfaz desde el programa Arcano.</div>
  if (errorInicio) return <div className="pantalla-centro alerta">No se pudo iniciar: {errorInicio}</div>
  if (!inicio) return <div className="pantalla-centro">Cargando…</div>

  const seleccionada = estado.regiones.find((r) => r.id === estado.seleccion)
  const enUso = new Map<string, number>()
  for (const r of estado.regiones) if (r.categoriaId) enUso.set(r.categoriaId, (enUso.get(r.categoriaId) ?? 0) + 1)

  const cuerpo = (
    <div className="app">
      <header className="barra">
        <button onClick={() => abrir()} title="Abrir PDF o Word (Ctrl+O)">
          Abrir…
        </button>
        <button onClick={() => setEditandoCatalogo(true)} title="Categorías, motivación y fundamentos de la leyenda">
          Catálogo
        </button>
        {actualizacion && (
          <button className="aviso-version" onClick={() => setViendoActualizacion(true)} title="Ver la versión nueva">
            Versión {actualizacion.version.replace(/^v/i, '')} disponible
          </button>
        )}
        {contexto && doc && (
          <>
            <button onClick={guardarProyecto} disabled={!estado.pendientes} title="Guardar el trabajo junto al documento (Ctrl+S)">
              Guardar
            </button>
            <span className="nombre-doc" title={doc.ruta}>
              {doc.nombre}
              {estado.pendientes && <span className="punto-cambios" title="Cambios sin guardar"> ●</span>}
            </span>
            <span className="separador" />
            <div className="grupo-botones" role="group" aria-label="Herramienta">
              <button className={herramienta === 'texto' ? 'activo' : ''} onClick={() => setHerramienta('texto')} title="Seleccionar palabras (T)">
                Texto
              </button>
              <button className={herramienta === 'area' ? 'activo' : ''} onClick={() => setHerramienta('area')} title="Dibujar un recuadro (A)">
                Área
              </button>
            </div>
            <SelectorCategoria valor={seleccionada?.categoriaId ?? categoriaActiva} alCambiar={cambiarCategoriaActiva} etiqueta="Categoría" />
            <span className="separador" />
            <button className="icono" onClick={() => despachar({ tipo: 'deshacer' })} disabled={estado.pasado.length === 0} title="Deshacer (Ctrl+Z)">
              ↶
            </button>
            <button className="icono" onClick={() => despachar({ tipo: 'rehacer' })} disabled={estado.futuro.length === 0} title="Rehacer (Ctrl+Y)">
              ↷
            </button>
            <span className="separador" />
            <button className="icono" onClick={() => setZoom((z) => Math.max(0.25, z / 1.2))} title="Alejar (−)">
              −
            </button>
            <button className="zoom" onClick={ajustarAncho} title="Ajustar al ancho">
              {Math.round(zoom * 100)} %
            </button>
            <button className="icono" onClick={() => setZoom((z) => Math.min(4, z * 1.2))} title="Acercar (+)">
              +
            </button>
            <label className="interruptor" title="Ver los testados opacos, como quedarán (V)">
              <input type="checkbox" checked={vistaPrevia} onChange={(e) => setVistaPrevia(e.target.checked)} />
              Vista previa
            </label>
            <span className="relleno" />
            <button className="primario" onClick={() => setGenerando(true)}>
              Generar versión pública
            </button>
          </>
        )}
      </header>

      {!contexto ? (
        <main className="bienvenida">
          <img className="logo" src="/arcano.png" alt="" width="96" height="96" />
          <h1>Arcano</h1>
          <p className="subtitulo">Generador de versiones públicas</p>
          <p>Abra un PDF o un documento de Word para testar los datos personales.</p>
          <button className="primario grande" onClick={() => abrir()}>
            Abrir documento…
          </button>
          <ul className="claves">
            <li>El resultado es un PDF nuevo en el que lo testado no existe: no se puede recuperar quitando recuadros.</li>
            <li>Las páginas escaneadas también se pueden buscar con reconocimiento de texto.</li>
            <li>El trabajo se guarda junto al documento como «.arcano» para continuarlo después.</li>
          </ul>
          <p className="version">Versión {inicio.version}</p>
        </main>
      ) : (
        <>
          <main className="espacio">
            <Miniaturas paginaActual={paginaActual} />
            <Visor alDesplazar={setPaginaActual} alMedir={setAnchoDisponible} />
            <aside className="panel">
              <nav className="pestanas" role="tablist">
                {PESTANAS.map((p) => (
                  <button key={p.id} role="tab" aria-selected={pestana === p.id} className={pestana === p.id ? 'activa' : ''} onClick={() => setPestana(p.id)}>
                    {p.nombre}
                    {p.id === 'testados' && estado.regiones.length > 0 && (
                      <span className="insignia">{new Set(estado.regiones.map((r) => r.grupo ?? r.id)).size}</span>
                    )}
                  </button>
                ))}
              </nav>
              <div className="panel-cuerpo">
                {pestana === 'testados' && <PanelTestados />}
                {pestana === 'buscar' && (
                  <PanelBuscar
                    terminos={terminos}
                    pedida={busquedaPedida}
                    alAgregarTermino={(x) => setTerminos((ts) => (ts.includes(x) ? ts : [...ts, x]))}
                  />
                )}
                {pestana === 'detectar' && <PanelDetectar />}
                {pestana === 'revision' && <PanelRevision alReconocer={reconocer} />}
              </div>
            </aside>
          </main>
          {generando && (
            <DialogoGenerar
              leyenda={leyenda}
              alCambiarLeyenda={setLeyenda}
              alCerrar={() => setGenerando(false)}
              alGuardarProyecto={guardarProyecto}
              alRevisar={() => setPestana('revision')}
              alReconocer={reconocer}
              alIrA={(pagina, caja) => {
                setGenerando(false)
                irA(pagina, caja)
              }}
              alRevisarTexto={revisarTexto}
              alEditarCatalogo={() => {
                setGenerando(false)
                setEditandoCatalogo(true)
              }}
            />
          )}
        </>
      )}

      {viendoActualizacion && actualizacion && (
        <DialogoActualizacion
          actualizacion={actualizacion}
          versionActual={inicio.version}
          hayCambios={!!doc && estado.pendientes}
          alActualizar={actualizar}
          alDescargar={() => actualizacion.pagina && void llamar('abrirArchivo', { ruta: actualizacion.pagina })}
          alCerrar={() => setViendoActualizacion(false)}
        />
      )}

      {editandoCatalogo && (
        <DialogoCatalogo
          catalogo={inicio.catalogo}
          enUso={enUso}
          alGuardar={(catalogo) => setInicio({ ...inicio, catalogo })}
          alCerrar={() => setEditandoCatalogo(false)}
          avisar={avisar}
        />
      )}

      {tarea && (
        <div className="fondo-modal">
          <div className="modal pequena">
            <div className="modal-cuerpo">
              <p>{tarea.detalle ?? 'Trabajando…'}</p>
              {tarea.total > 1 && <progress max={tarea.total} value={tarea.actual} />}
              {(tarea.etapa === 'ocr' || tarea.etapa === 'actualizacion') && (
                <footer>
                  <button onClick={() => llamar('cancelar')}>Cancelar</button>
                </footer>
              )}
            </div>
          </div>
        </div>
      )}

      {confirmacion && (
        <div className="fondo-modal">
          <div className="modal pequena" role="alertdialog">
            <div className="modal-cuerpo">
              <p>{confirmacion.texto}</p>
            </div>
            <footer>
              <button
                onClick={() => {
                  confirmacion.resolver(false)
                  setConfirmacion(null)
                }}
              >
                {confirmacion.no}
              </button>
              <button
                className="primario"
                autoFocus
                onClick={() => {
                  confirmacion.resolver(true)
                  setConfirmacion(null)
                }}
              >
                {confirmacion.si}
              </button>
            </footer>
          </div>
        </div>
      )}

      <div className="avisos" aria-live="polite">
        {avisos.map((a) => (
          <div key={a.id} className={`aviso-flotante ${a.tipo}`}>
            {a.texto}
          </div>
        ))}
      </div>
    </div>
  )

  return contexto ? <ContextoArcano.Provider value={contexto}>{cuerpo}</ContextoArcano.Provider> : cuerpo
}
