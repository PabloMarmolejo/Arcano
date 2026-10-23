import { useEffect, useMemo, useState } from 'react'
import { useArcano } from '../contexto'
import { claveCoincidencia, coincidenciaCubierta, regionesDe } from '../geometria'
import type { Coincidencia } from '../tipos'

export function SelectorCategoria({
  valor,
  alCambiar,
  vacia,
  etiqueta,
}: {
  valor: string | null | undefined
  alCambiar: (id: string) => void
  vacia?: string
  etiqueta?: string
}) {
  const { inicio } = useArcano()
  const grupos = useMemo(() => {
    const m = new Map<string, typeof inicio.catalogo.categorias>()
    for (const c of inicio.catalogo.categorias) m.set(c.grupo, [...(m.get(c.grupo) ?? []), c])
    return [...m.entries()]
  }, [inicio.catalogo])
  return (
    <select className="selector-categoria" value={valor ?? ''} onChange={(e) => alCambiar(e.target.value)} aria-label={etiqueta ?? 'Categoría'}>
      {vacia !== undefined && <option value="">{vacia}</option>}
      {grupos.map(([grupo, cats]) => (
        <optgroup key={grupo} label={grupo}>
          {cats.map((c) => (
            <option key={c.id} value={c.id}>
              {c.nombre}
            </option>
          ))}
        </optgroup>
      ))}
    </select>
  )
}

/** Muestra el contexto «…antes [coincidencia] después…» con la coincidencia resaltada. */
export function Contexto({ texto }: { texto: string }) {
  const i = texto.indexOf('[')
  const j = texto.lastIndexOf(']')
  if (i < 0 || j < i) return <span className="contexto">{texto}</span>
  return (
    <span className="contexto">
      {texto.slice(0, i)}
      <mark>{texto.slice(i + 1, j)}</mark>
      {texto.slice(j + 1)}
    </span>
  )
}

interface ListaProps {
  coincidencias: Coincidencia[]
  /** Agrupa por este título (p. ej., el nombre del detector). */
  agruparPor?: (c: Coincidencia) => string
  /** Categoría para las que no traen una. */
  categoriaPorDefecto: string
  vacio: string
}

/**
 * Lista de coincidencias con casillas. Las marcadas se pueden testar de una vez;
 * las que ya están cubiertas por una zona se muestran como testadas.
 */
export function ListaCoincidencias({ coincidencias, agruparPor, categoriaPorDefecto, vacio }: ListaProps) {
  const t = useArcano()
  const [marcadas, setMarcadas] = useState<Set<string>>(new Set())

  useEffect(() => {
    setMarcadas(new Set(coincidencias.filter((c) => c.sugerida).map(claveCoincidencia)))
  }, [coincidencias])

  const pendientes = coincidencias.filter((c) => !coincidenciaCubierta(c, t.estado.regiones))
  const seleccion = pendientes.filter((c) => marcadas.has(claveCoincidencia(c)))

  const { setCandidatas } = t
  useEffect(() => {
    setCandidatas(pendientes)
    // Las candidatas se recalculan al cambiar las zonas o la lista.
  }, [coincidencias, t.estado.regiones])
  useEffect(() => () => setCandidatas([]), [setCandidatas])

  const grupos = useMemo(() => {
    const m = new Map<string, Coincidencia[]>()
    for (const c of coincidencias) {
      const g = agruparPor?.(c) ?? ''
      m.set(g, [...(m.get(g) ?? []), c])
    }
    return [...m.entries()]
  }, [coincidencias, agruparPor])

  const alternar = (c: Coincidencia) => {
    const k = claveCoincidencia(c)
    const s = new Set(marcadas)
    if (s.has(k)) s.delete(k)
    else s.add(k)
    setMarcadas(s)
  }

  const alternarGrupo = (lista: Coincidencia[], marcar: boolean) => {
    const s = new Set(marcadas)
    for (const c of lista) {
      if (marcar) s.add(claveCoincidencia(c))
      else s.delete(claveCoincidencia(c))
    }
    setMarcadas(s)
  }

  const testar = () => {
    const regiones = seleccion.flatMap((c) => regionesDe(c, categoriaPorDefecto))
    t.despachar({ tipo: 'agregar', regiones })
    t.avisar(`Se testaron ${seleccion.length} aparición(es).`, 'exito')
  }

  if (coincidencias.length === 0) return <p className="vacio">{vacio}</p>

  return (
    <div className="lista-coincidencias">
      <div className="lista-acciones">
        <span>
          {pendientes.length} por testar · {coincidencias.length - pendientes.length} ya testada(s)
        </span>
        <button className="primario" disabled={seleccion.length === 0} onClick={testar}>
          Testar marcadas ({seleccion.length})
        </button>
      </div>
      {grupos.map(([titulo, lista]) => {
        const pendientesGrupo = lista.filter((c) => !coincidenciaCubierta(c, t.estado.regiones))
        const todas = pendientesGrupo.length > 0 && pendientesGrupo.every((c) => marcadas.has(claveCoincidencia(c)))
        return (
          <section key={titulo} className="grupo-coincidencias">
            {titulo && (
              <header>
                <label>
                  <input type="checkbox" checked={todas} disabled={pendientesGrupo.length === 0} onChange={(e) => alternarGrupo(pendientesGrupo, e.target.checked)} />
                  {titulo}
                </label>
                <span className="cuenta">{lista.length}</span>
              </header>
            )}
            <ul>
              {lista.map((c) => {
                const k = claveCoincidencia(c)
                const hecha = coincidenciaCubierta(c, t.estado.regiones)
                return (
                  <li
                    key={k}
                    className={hecha ? 'hecha' : ''}
                    onMouseEnter={() => t.setFoco(c)}
                    onMouseLeave={() => t.setFoco(null)}
                  >
                    <input type="checkbox" checked={hecha || marcadas.has(k)} disabled={hecha} onChange={() => alternar(c)} aria-label={`Marcar ${c.texto}`} />
                    <button className="enlace" onClick={() => t.irA(c.pagina, c.cajas[0])}>
                      <span className="pag">p. {c.pagina + 1}</span>
                      <Contexto texto={c.contexto} />
                    </button>
                  </li>
                )
              })}
            </ul>
          </section>
        )
      })}
    </div>
  )
}
