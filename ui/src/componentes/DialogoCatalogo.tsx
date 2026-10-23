import { useMemo, useState } from 'react'
import { claveNueva, fundamentoPendiente, validarCatalogo } from '../catalogo'
import { ErrorPuente, llamar } from '../puente'
import type { Catalogo, Categoria } from '../tipos'

interface Props {
  catalogo: Catalogo
  /** Zonas del documento abierto por categoría: las que están en uso no se pueden borrar. */
  enUso: Map<string, number>
  alGuardar: (c: Catalogo) => void
  alCerrar: () => void
  avisar: (texto: string, tipo?: 'info' | 'exito' | 'error') => void
}

type Confirmar = 'restaurar' | 'cerrar' | null

const sinTexto = (s: string) =>
  s
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .toLowerCase()

/**
 * Edición del catálogo: fundamento general, dominios institucionales y, por
 * categoría, nombre, grupo, tipo, motivación y fundamento propio.
 */
export function DialogoCatalogo({ catalogo, enUso, alGuardar, alCerrar, avisar }: Props) {
  const [borrador, setBorrador] = useState<Catalogo>(catalogo)
  const [dominiosTexto, setDominiosTexto] = useState((catalogo.dominiosInstitucionales ?? []).join('\n'))
  const [abierta, setAbierta] = useState<number | null>(null)
  const [filtro, setFiltro] = useState('')
  const [confirmar, setConfirmar] = useState<Confirmar>(null)
  const [guardando, setGuardando] = useState(false)
  const [nuevas, setNuevas] = useState<Set<number>>(new Set())

  const dominios = dominiosTexto
    .split(/[\n,;]+/)
    .map((d) => d.trim().replace(/^@/, '').toLowerCase())
    .filter(Boolean)
  const resultado: Catalogo = { ...borrador, dominiosInstitucionales: dominios }
  const errores = validarCatalogo(resultado)
  const cambios = JSON.stringify(resultado) !== JSON.stringify({ ...catalogo, dominiosInstitucionales: catalogo.dominiosInstitucionales ?? [] })
  const grupos = useMemo(() => [...new Set(borrador.categorias.map((c) => c.grupo))].sort(), [borrador.categorias])
  const pendientes = borrador.categorias.filter((c) => fundamentoPendiente(c, borrador)).length

  const visibles = borrador.categorias
    .map((c, i) => ({ c, i }))
    .filter(({ c }) => !filtro || sinTexto(`${c.nombre} ${c.grupo}`).includes(sinTexto(filtro)))

  const cambiar = (i: number, cambios: Partial<Categoria>) =>
    setBorrador({ ...borrador, categorias: borrador.categorias.map((c, j) => (j === i ? { ...c, ...cambios } : c)) })

  const agregar = () => {
    const categoria: Categoria = { id: '', nombre: '', grupo: 'Otros', tipo: 'confidencial', motivacion: '', fundamento: '' }
    const indice = borrador.categorias.length
    setBorrador({ ...borrador, categorias: [...borrador.categorias, categoria] })
    setNuevas(new Set([...nuevas, indice]))
    setAbierta(indice)
    setFiltro('')
  }

  const quitar = (i: number) => {
    setBorrador({ ...borrador, categorias: borrador.categorias.filter((_, j) => j !== i) })
    setNuevas(new Set([...nuevas].filter((j) => j !== i).map((j) => (j > i ? j - 1 : j))))
    setAbierta(null)
  }

  /** Las categorías nuevas reciben su clave a partir del nombre al guardar. */
  const conClaves = (c: Catalogo): Catalogo => {
    const usadas = new Set(c.categorias.filter((x) => x.id).map((x) => x.id))
    return {
      ...c,
      categorias: c.categorias.map((x) => {
        if (x.id) return x
        const id = claveNueva(x.nombre, usadas)
        usadas.add(id)
        return { ...x, id }
      }),
    }
  }

  const guardar = async () => {
    setGuardando(true)
    try {
      const guardado = await llamar<Catalogo>('guardarCatalogo', { catalogo: conClaves(resultado) })
      alGuardar(guardado)
      avisar('Catálogo guardado. Se aplica a las siguientes versiones públicas.', 'exito')
      alCerrar()
    } catch (e) {
      avisar((e as ErrorPuente).message, 'error')
    } finally {
      setGuardando(false)
    }
  }

  const restaurar = async () => {
    setConfirmar(null)
    try {
      const fabrica = await llamar<Catalogo>('restaurarCatalogo')
      alGuardar(fabrica)
      setBorrador(fabrica)
      setDominiosTexto(fabrica.dominiosInstitucionales.join('\n'))
      setNuevas(new Set())
      setAbierta(null)
      avisar('Se restauró el catálogo de fábrica.', 'exito')
    } catch (e) {
      avisar((e as ErrorPuente).message, 'error')
    }
  }

  const importar = async () => {
    try {
      const importado = await llamar<Catalogo | null>('importarCatalogo')
      if (!importado) return
      setBorrador(importado)
      setDominiosTexto(importado.dominiosInstitucionales.join('\n'))
      setNuevas(new Set())
      setAbierta(null)
      avisar(`Se cargaron ${importado.categorias.length} categorías. Revise y pulse Guardar para aplicarlas.`)
    } catch (e) {
      avisar((e as ErrorPuente).message, 'error')
    }
  }

  const exportar = async () => {
    try {
      const ruta = await llamar<string | null>('exportarCatalogo', { catalogo: conClaves(resultado) })
      if (ruta) avisar('Catálogo exportado. Compártalo y cada persona lo importa desde esta pantalla.', 'exito')
    } catch (e) {
      avisar((e as ErrorPuente).message, 'error')
    }
  }

  const cerrar = () => (cambios ? setConfirmar('cerrar') : alCerrar())

  return (
    <div className="fondo-modal" role="dialog" aria-modal="true" aria-labelledby="titulo-catalogo">
      <div className="modal ancha">
        <header>
          <h2 id="titulo-catalogo">Catálogo de datos y fundamentos</h2>
          <button className="icono" onClick={cerrar} aria-label="Cerrar">
            ✕
          </button>
        </header>

        <div className="modal-cuerpo">
          <fieldset>
            <legend>Fundamento general</legend>
            <textarea
              rows={3}
              value={borrador.fundamentoGeneral}
              onChange={(e) => setBorrador({ ...borrador, fundamentoGeneral: e.target.value })}
              placeholder="Artículos de la ley de transparencia y de la ley de protección de datos personales que aplican…"
            />
            <small className="ayuda">
              Se usa en la leyenda de toda categoría que no tenga un fundamento propio.
              {pendientes > 0 && (
                <b className="alerta"> {pendientes} categoría(s) quedan sin fundamento.</b>
              )}
            </small>
          </fieldset>

          <fieldset>
            <legend>Correos institucionales</legend>
            <textarea rows={2} value={dominiosTexto} onChange={(e) => setDominiosTexto(e.target.value)} placeholder="gob.mx" />
            <small className="ayuda">
              Un dominio por renglón. Los correos de estos dominios (y sus subdominios) se ofrecen sin marcar al detectar.
            </small>
          </fieldset>

          <fieldset>
            <legend>Categorías ({borrador.categorias.length})</legend>
            <div className="fila-controles">
              <input type="search" placeholder="Buscar categoría…" value={filtro} onChange={(e) => setFiltro(e.target.value)} />
              <button onClick={agregar}>+ Agregar categoría</button>
            </div>
            <datalist id="grupos-catalogo">
              {grupos.map((g) => (
                <option key={g} value={g} />
              ))}
            </datalist>
            <ul className="categorias">
              {visibles.map(({ c, i }) => {
                const usos = enUso.get(c.id) ?? 0
                const pendiente = fundamentoPendiente(c, borrador)
                return (
                  <li key={i} className={abierta === i ? 'abierta' : ''}>
                    <button className="enlace fila-categoria" onClick={() => setAbierta(abierta === i ? null : i)}>
                      <span className="nombre-categoria">{c.nombre || <i>Nueva categoría</i>}</span>
                      <span className="etiqueta-suave">{c.grupo}</span>
                      {c.tipo === 'reservada' && <span className="etiqueta-suave">reservada</span>}
                      {c.fundamento ? (
                        <span className="etiqueta-suave propio">fundamento propio</span>
                      ) : null}
                      {pendiente && <span className="marca-escaneo">sin fundamento</span>}
                      <span className="chevron">{abierta === i ? '▾' : '▸'}</span>
                    </button>
                    {abierta === i && (
                      <div className="editor-categoria">
                        <div className="fila-campos">
                          <label className="campo crece">
                            <span>Nombre</span>
                            <input value={c.nombre} autoFocus={nuevas.has(i)} onChange={(e) => cambiar(i, { nombre: e.target.value })} />
                          </label>
                          <label className="campo">
                            <span>Grupo</span>
                            <input list="grupos-catalogo" value={c.grupo} onChange={(e) => cambiar(i, { grupo: e.target.value })} />
                          </label>
                          <label className="campo">
                            <span>Tipo</span>
                            <select value={c.tipo} onChange={(e) => cambiar(i, { tipo: e.target.value as Categoria['tipo'] })}>
                              <option value="confidencial">Confidencial</option>
                              <option value="reservada">Reservada</option>
                            </select>
                          </label>
                        </div>
                        <label className="campo">
                          <span>Motivación</span>
                          <textarea rows={2} value={c.motivacion} onChange={(e) => cambiar(i, { motivacion: e.target.value })} />
                          <small>
                            En la leyenda se lee: «Motivación: se testa <b>{c.motivacion || '…'}</b>».
                          </small>
                        </label>
                        <label className="campo">
                          <span>Fundamento propio</span>
                          <textarea
                            rows={3}
                            value={c.fundamento}
                            placeholder="Vacío: se usa el fundamento general."
                            onChange={(e) => cambiar(i, { fundamento: e.target.value })}
                          />
                        </label>
                        <div className="lista-acciones">
                          <span className="nota">
                            {c.id ? `Clave: ${c.id}` : 'La clave se asigna al guardar.'}
                            {usos > 0 && ` · En uso en ${usos} zona(s) del documento abierto.`}
                          </span>
                          <button
                            className="peligro"
                            disabled={usos > 0}
                            title={usos > 0 ? 'Cambie la categoría de esas zonas antes de borrarla.' : undefined}
                            onClick={() => quitar(i)}
                          >
                            Eliminar categoría
                          </button>
                        </div>
                      </div>
                    )}
                  </li>
                )
              })}
              {visibles.length === 0 && <li className="vacio">Ninguna categoría coincide con la búsqueda.</li>}
            </ul>
          </fieldset>

          {errores.length > 0 && (
            <ul className="hallazgos">
              {errores.map((e) => (
                <li key={e}>{e}</li>
              ))}
            </ul>
          )}

          {confirmar && (
            <div className="tarjeta aviso">
              <p>
                {confirmar === 'restaurar'
                  ? 'Se reemplazará el catálogo por el de fábrica y se perderán los fundamentos capturados. ¿Continuar?'
                  : 'Hay cambios sin guardar. ¿Cerrar sin guardarlos?'}
              </p>
              <div className="fila-controles">
                <button onClick={() => setConfirmar(null)}>No</button>
                <button className="peligro" onClick={confirmar === 'restaurar' ? restaurar : alCerrar}>
                  Sí, {confirmar === 'restaurar' ? 'restaurar' : 'cerrar'}
                </button>
              </div>
            </div>
          )}
        </div>

        <footer>
          <button onClick={() => setConfirmar('restaurar')}>Restaurar de fábrica</button>
          <button onClick={importar}>Importar…</button>
          <button onClick={exportar} disabled={errores.length > 0}>
            Exportar…
          </button>
          <span className="relleno" />
          <button onClick={cerrar}>Cancelar</button>
          <button className="primario" disabled={!cambios || errores.length > 0 || guardando} onClick={guardar}>
            {guardando ? 'Guardando…' : 'Guardar'}
          </button>
        </footer>
      </div>
    </div>
  )
}
