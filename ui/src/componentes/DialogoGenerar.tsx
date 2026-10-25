import { useEffect, useState } from 'react'
import { fundamentoPendiente } from '../catalogo'
import { useArcano } from '../contexto'
import { alProgreso, ErrorPuente, llamar } from '../puente'
import type { Coincidencia, DatosLeyenda, InfoPagina, OpcionesGeneracion, Progreso, RectN, ResultadoGeneracion } from '../tipos'

interface Props {
  leyenda: DatosLeyenda
  alCambiarLeyenda: (l: DatosLeyenda) => void
  alCerrar: () => void
  alGuardarProyecto: () => Promise<void>
  alRevisar: () => void
  alEditarCatalogo: () => void
  /** Reconoce el texto (OCR) de páginas escaneadas; devuelve las páginas actualizadas. */
  alReconocer: (paginas: number[]) => Promise<InfoPagina[] | null>
  /** Cierra el diálogo y lleva a una página del documento. */
  alIrA: (pagina: number, caja?: RectN) => void
  /** Cierra el diálogo, lleva a la página y busca ahí el término que quedó legible. */
  alRevisarTexto: (termino: string, pagina: number) => void
}

type Fase = 'datos' | 'escaneadas' | 'aviso' | 'generando' | 'listo'

const campos: { clave: keyof DatosLeyenda; etiqueta: string; ayuda?: string; largo?: boolean }[] = [
  { clave: 'documento', etiqueta: 'Documento', ayuda: 'Tipo, número de expediente y fecha. Si se deja vacío se usa el nombre del archivo.' },
  { clave: 'textoAdicional', etiqueta: 'Texto adicional', largo: true },
]

export function DialogoGenerar({
  leyenda, alCambiarLeyenda, alCerrar, alGuardarProyecto, alRevisar, alEditarCatalogo, alReconocer, alIrA, alRevisarTexto,
}: Props) {
  const t = useArcano()
  const [opciones, setOpciones] = useState<OpcionesGeneracion>(t.inicio.preferencias.opciones)
  const [fase, setFase] = useState<Fase>('datos')
  const [pendientes, setPendientes] = useState<Coincidencia[]>([])
  const [progreso, setProgreso] = useState<Progreso | null>(null)
  const [resultado, setResultado] = useState<ResultadoGeneracion | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => alProgreso(setProgreso), [])

  const fijar = <K extends keyof OpcionesGeneracion>(k: K, v: OpcionesGeneracion[K]) => setOpciones({ ...opciones, [k]: v })

  const sinCategoria = t.estado.regiones.filter((r) => !t.categoria(r.categoriaId)).length
  const paginasSinTexto = t.doc.paginas.filter((p) => p.texto === 'SinTexto').map((p) => p.indice)
  const sinTexto = paginasSinTexto.length
  const sinFundamento = [...new Set(t.estado.regiones.map((r) => r.categoriaId ?? 'otro'))]
    .map((id) => t.categoria(id))
    .filter((c) => c && fundamentoPendiente(c, t.inicio.catalogo))
    .map((c) => c!.nombre)

  /**
   * Antes de buscar repeticiones conviene reconocer las páginas escaneadas: si no,
   * un nombre testado que aparece en un escaneo solo se descubre al final.
   */
  const continuar = async () => {
    setError(null)
    if (sinTexto > 0 && t.inicio.ocrDisponible) {
      setFase('escaneadas')
      return
    }
    await revisarRepeticiones()
  }

  const reconocerYRevisar = async () => {
    const nuevas = await alReconocer(paginasSinTexto)
    if (nuevas === null) return
    await revisarRepeticiones()
  }

  const revisarRepeticiones = async () => {
    setError(null)
    const repeticiones = await llamar<Coincidencia[]>('repeticiones', { id: t.doc.id, regiones: t.estado.regiones }).catch(() => [])
    if (repeticiones.length > 0 || sinCategoria > 0) {
      setPendientes(repeticiones)
      setFase('aviso')
      return
    }
    await generar()
  }

  const generar = async () => {
    setFase('generando')
    setProgreso(null)
    try {
      await llamar('guardarPreferencias', { leyenda, opciones: { ...opciones, leyenda } })
      await alGuardarProyecto()
      const r = await llamar<ResultadoGeneracion | null>('generar', {
        id: t.doc.id,
        regiones: t.estado.regiones,
        opciones: { ...opciones, leyenda },
      })
      if (!r) {
        setFase('datos')
        return
      }
      setResultado(r)
      setFase('listo')
    } catch (e) {
      const err = e as ErrorPuente
      setError(err.cancelado ? 'Se canceló la generación.' : err.message)
      setFase('datos')
    }
  }

  return (
    <div className="fondo-modal" role="dialog" aria-modal="true" aria-labelledby="titulo-generar">
      <div className="modal">
        <header>
          <h2 id="titulo-generar">Generar versión pública</h2>
          {fase !== 'generando' && (
            <button className="icono" onClick={alCerrar} aria-label="Cerrar">
              ✕
            </button>
          )}
        </header>

        {fase === 'datos' && (
          <div className="modal-cuerpo">
            <p className="resumen">
              {new Set(t.estado.regiones.map((r) => r.grupo ?? r.id)).size} testado(s) en {t.doc.paginas.length} página(s).
            </p>
            {error && <p className="alerta">{error}</p>}
            <fieldset>
              <legend>Leyenda de clasificación</legend>
              <label className="opcion">
                <input type="checkbox" checked={opciones.incluirLeyenda} onChange={(e) => fijar('incluirLeyenda', e.target.checked)} />
                Agregar la hoja de leyenda al final
              </label>
              {opciones.incluirLeyenda &&
                campos.map((c) => (
                  <label key={c.clave} className="campo">
                    <span>{c.etiqueta}</span>
                    {c.largo ? (
                      <textarea rows={3} value={leyenda[c.clave]} onChange={(e) => alCambiarLeyenda({ ...leyenda, [c.clave]: e.target.value })} />
                    ) : (
                      <input value={leyenda[c.clave]} onChange={(e) => alCambiarLeyenda({ ...leyenda, [c.clave]: e.target.value })} />
                    )}
                    {c.ayuda && <small>{c.ayuda}</small>}
                  </label>
                ))}
            </fieldset>
            <fieldset>
              <legend>Archivo</legend>
              <div className="fila-campos">
                <label className="campo">
                  <span>Resolución</span>
                  <select value={opciones.dpi} onChange={(e) => fijar('dpi', Number(e.target.value))}>
                    <option value={200}>200 ppp (archivo ligero)</option>
                    <option value={300}>300 ppp (recomendada)</option>
                    <option value={400}>400 ppp (planos y letra pequeña)</option>
                  </select>
                </label>
                <label className="campo">
                  <span>Color del testado</span>
                  <input type="color" value={opciones.color} onChange={(e) => fijar('color', e.target.value)} />
                </label>
              </div>
              <label className="opcion">
                <input type="checkbox" checked={opciones.numerar} onChange={(e) => fijar('numerar', e.target.checked)} />
                Numerar cada testado (el número remite a la leyenda)
              </label>
              <label className="opcion" title="Las páginas sin testados se copian del original sin comentarios ni metadatos. Marque esta opción si el original puede traer contenido oculto (capas, texto blanco).">
                <input type="checkbox" checked={opciones.rasterizarTodas ?? false} onChange={(e) => fijar('rasterizarTodas', e.target.checked)} />
                Convertir en imagen también las páginas sin testados (archivo más pesado y lento)
              </label>
              <label className="opcion">
                <input
                  type="checkbox"
                  checked={opciones.capaTexto && t.inicio.ocrDisponible}
                  disabled={!t.inicio.ocrDisponible}
                  onChange={(e) => fijar('capaTexto', e.target.checked)}
                />
                Permitir buscar y copiar el texto de las páginas convertidas (OCR sobre la versión ya testada)
              </label>
            </fieldset>
            {sinFundamento.length > 0 && (
              <div className="tarjeta aviso">
                <p>
                  La leyenda saldrá con el fundamento pendiente en: {sinFundamento.join(', ')}.
                </p>
                <button onClick={alEditarCatalogo}>Capturar fundamentos</button>
              </div>
            )}
            {sinTexto > 0 && (
              <p className="nota">
                Hay {sinTexto} página(s) escaneadas cuyo texto no se ha revisado con búsqueda ni detectores.
              </p>
            )}
          </div>
        )}

        {fase === 'escaneadas' && (
          <div className="modal-cuerpo">
            <p>
              {sinTexto} página(s) son escaneos sin texto reconocido: {paginasSinTexto.map((p) => p + 1).join(', ')}.
            </p>
            <p>
              Para avisarle si algo que ya testó aparece también en ellas, primero hay que reconocer su texto (OCR). Si no,
              esas apariciones solo se descubrirán al verificar el archivo terminado.
            </p>
          </div>
        )}

        {fase === 'aviso' && (
          <div className="modal-cuerpo">
            {pendientes.length > 0 && (
              <>
                <p className="alerta">
                  {pendientes.length} aparición(es) de texto ya testado siguen visibles en otras partes del documento:
                </p>
                <ul className="hallazgos enlaces">
                  {pendientes.map((c, i) => (
                    <li key={i}>
                      <button className="enlace" onClick={() => alIrA(c.pagina, c.cajas[0])}>
                        «{c.texto}» en la página {c.pagina + 1}
                      </button>
                    </li>
                  ))}
                </ul>
              </>
            )}
            {sinCategoria > 0 && <p className="alerta">{sinCategoria} zona(s) no tienen categoría.</p>}
            <p>Puede revisarlas antes de generar o generar de todos modos.</p>
          </div>
        )}

        {fase === 'generando' && (
          <div className="modal-cuerpo">
            <p>{progreso?.detalle ?? 'Preparando…'}</p>
            <progress max={Math.max(1, progreso?.total ?? 1)} value={progreso?.actual ?? 0} />
            <p className="nota">Las páginas con testados se convierten en imagen y se reconoce su texto; las demás se copian. Puede tardar unos minutos.</p>
          </div>
        )}

        {fase === 'listo' && resultado && (
          <div className="modal-cuerpo">
            <p className={resultado.verificacion.correcta ? 'exito' : 'alerta'}>
              {resultado.verificacion.correcta
                ? 'Versión pública generada y verificada.'
                : 'Se generó la versión pública, pero la verificación encontró puntos a revisar.'}
            </p>
            <p className="ruta">{resultado.ruta}</p>
            <p>
              {resultado.paginas} página(s) · {resultado.testados} testado(s) · {(resultado.bytes / 1024 / 1024).toFixed(1)} MB ·{' '}
              {resultado.paginasConvertidas} página(s) convertida(s) en imagen
            </p>
            {resultado.avisos.length > 0 && (
              <ul className="hallazgos">
                {resultado.avisos.map((a) => (
                  <li key={a}>{a}</li>
                ))}
              </ul>
            )}
            {resultado.verificacion.hallazgos.length > 0 && (
              <>
                <ul className="hallazgos enlaces">
                  {resultado.verificacion.hallazgos.map((h, i) => {
                    const enDocumento = h.pagina != null && h.pagina < t.doc.paginas.length
                    return (
                      <li key={i}>
                        {enDocumento && h.texto ? (
                          <button className="enlace" onClick={() => alRevisarTexto(h.texto!, h.pagina!)} title="Ir a la página y buscar el texto">
                            {h.descripcion} <span className="ir">Ir a la página →</span>
                          </button>
                        ) : (
                          h.descripcion
                        )}
                      </li>
                    )
                  })}
                </ul>
                <p className="nota">Teste esas apariciones y vuelva a generar la versión pública.</p>
              </>
            )}
            <ul className="comprobaciones">
              {resultado.verificacion.comprobaciones.map((c) => (
                <li key={c}>{c}</li>
              ))}
            </ul>
          </div>
        )}

        <footer>
          {fase === 'datos' && (
            <>
              <button onClick={alCerrar}>Cancelar</button>
              <button className="primario" onClick={continuar}>
                Generar…
              </button>
            </>
          )}
          {fase === 'escaneadas' && (
            <>
              <button onClick={revisarRepeticiones}>Continuar sin reconocer</button>
              <button className="primario" onClick={reconocerYRevisar}>
                Reconocer y revisar
              </button>
            </>
          )}
          {fase === 'aviso' && (
            <>
              <button
                onClick={() => {
                  alRevisar()
                  alCerrar()
                }}
              >
                Revisar
              </button>
              <button className="primario" onClick={generar}>
                Generar de todos modos
              </button>
            </>
          )}
          {fase === 'generando' && <button onClick={() => llamar('cancelar')}>Cancelar</button>}
          {fase === 'listo' && resultado && (
            <>
              <button onClick={() => llamar('mostrarEnCarpeta', { ruta: resultado.ruta })}>Mostrar en carpeta</button>
              <button onClick={() => llamar('abrirArchivo', { ruta: resultado.ruta })}>Abrir PDF</button>
              <button className="primario" onClick={alCerrar}>
                Cerrar
              </button>
            </>
          )}
        </footer>
      </div>
    </div>
  )
}
