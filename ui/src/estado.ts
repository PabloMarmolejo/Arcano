// Zonas a testar con historial para deshacer y rehacer.
import { cubierta } from './geometria'
import type { Region } from './tipos'

export interface Estado {
  regiones: Region[]
  pasado: Region[][]
  futuro: Region[][]
  seleccion: string | null
  pendientes: boolean
}

export type Accion =
  | { tipo: 'cargar'; regiones: Region[] }
  | { tipo: 'agregar'; regiones: Region[]; seleccionar?: boolean }
  | { tipo: 'quitar'; ids: string[] }
  | { tipo: 'actualizar'; id: string; cambios: Partial<Region>; historial?: boolean }
  | { tipo: 'categoria'; ids: string[]; categoriaId: string }
  | { tipo: 'seleccionar'; id: string | null }
  | { tipo: 'deshacer' }
  | { tipo: 'rehacer' }
  | { tipo: 'guardado' }

export const estadoInicial: Estado = { regiones: [], pasado: [], futuro: [], seleccion: null, pendientes: false }

const LIMITE_HISTORIAL = 200

function conHistorial(e: Estado, regiones: Region[]): Estado {
  return {
    ...e,
    regiones,
    pasado: [...e.pasado.slice(-LIMITE_HISTORIAL), e.regiones],
    futuro: [],
    pendientes: true,
  }
}

/** Ids de la zona y de las demás de su grupo (una aparición partida en renglones). */
export function idsDelGrupo(regiones: Region[], id: string): string[] {
  const r = regiones.find((x) => x.id === id)
  if (!r?.grupo) return [id]
  return regiones.filter((x) => x.grupo === r.grupo).map((x) => x.id)
}

export function reductor(e: Estado, a: Accion): Estado {
  switch (a.tipo) {
    case 'cargar':
      return { ...estadoInicial, regiones: a.regiones }
    case 'agregar': {
      const nuevas = a.regiones.filter((r) => !cubierta(r.pagina, r.caja, e.regiones))
      if (nuevas.length === 0) return e
      const siguiente = conHistorial(e, [...e.regiones, ...nuevas])
      return a.seleccionar ? { ...siguiente, seleccion: nuevas[0].id } : siguiente
    }
    case 'quitar': {
      const quitar = new Set(a.ids.flatMap((id) => idsDelGrupo(e.regiones, id)))
      if (quitar.size === 0) return e
      return {
        ...conHistorial(e, e.regiones.filter((r) => !quitar.has(r.id))),
        seleccion: e.seleccion && quitar.has(e.seleccion) ? null : e.seleccion,
      }
    }
    case 'actualizar': {
      const regiones = e.regiones.map((r) => (r.id === a.id ? { ...r, ...a.cambios } : r))
      return a.historial === false ? { ...e, regiones, pendientes: true } : conHistorial(e, regiones)
    }
    case 'categoria': {
      const ids = new Set(a.ids.flatMap((id) => idsDelGrupo(e.regiones, id)))
      return conHistorial(
        e,
        e.regiones.map((r) => (ids.has(r.id) ? { ...r, categoriaId: a.categoriaId } : r)),
      )
    }
    case 'seleccionar':
      return { ...e, seleccion: a.id }
    case 'deshacer': {
      if (e.pasado.length === 0) return e
      return {
        ...e,
        regiones: e.pasado[e.pasado.length - 1],
        pasado: e.pasado.slice(0, -1),
        futuro: [e.regiones, ...e.futuro],
        seleccion: null,
        pendientes: true,
      }
    }
    case 'rehacer': {
      if (e.futuro.length === 0) return e
      return {
        ...e,
        regiones: e.futuro[0],
        pasado: [...e.pasado, e.regiones],
        futuro: e.futuro.slice(1),
        seleccion: null,
        pendientes: true,
      }
    }
    case 'guardado':
      return { ...e, pendientes: false }
  }
}
