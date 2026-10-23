import type { Coincidencia, RectN, Region } from './tipos'

export const area = (r: RectN) => Math.max(0, r.ancho) * Math.max(0, r.alto)

export function interseccion(a: RectN, b: RectN): number {
  const ancho = Math.min(a.x + a.ancho, b.x + b.ancho) - Math.max(a.x, b.x)
  const alto = Math.min(a.y + a.alto, b.y + b.alto) - Math.max(a.y, b.y)
  return ancho > 0 && alto > 0 ? ancho * alto : 0
}

export const seIntersectan = (a: RectN, b: RectN) => interseccion(a, b) > 0

export function desdeBordes(x1: number, y1: number, x2: number, y2: number): RectN {
  return { x: Math.min(x1, x2), y: Math.min(y1, y2), ancho: Math.abs(x2 - x1), alto: Math.abs(y2 - y1) }
}

export function union(a: RectN, b: RectN): RectN {
  return desdeBordes(
    Math.min(a.x, b.x),
    Math.min(a.y, b.y),
    Math.max(a.x + a.ancho, b.x + b.ancho),
    Math.max(a.y + a.alto, b.y + b.alto),
  )
}

export function acotar(r: RectN): RectN {
  const x1 = Math.min(1, Math.max(0, r.x))
  const y1 = Math.min(1, Math.max(0, r.y))
  const x2 = Math.min(1, Math.max(0, r.x + r.ancho))
  const y2 = Math.min(1, Math.max(0, r.y + r.alto))
  return desdeBordes(x1, y1, x2, y2)
}

/** Una caja queda cubierta si las zonas de su página tapan al menos el 90 % de ella. */
export function cubierta(pagina: number, caja: RectN, regiones: Region[]): boolean {
  const total = regiones
    .filter((r) => r.pagina === pagina)
    .reduce((s, r) => s + interseccion(r.caja, caja), 0)
  return total >= area(caja) * 0.9
}

export const coincidenciaCubierta = (c: Coincidencia, regiones: Region[]) =>
  c.cajas.every((caja) => cubierta(c.pagina, caja, regiones))

export const nuevoId = () => crypto.randomUUID().slice(0, 13)

/** Convierte una coincidencia en zonas a testar, una por renglón, con grupo común. */
export function regionesDe(c: Coincidencia, categoriaPorDefecto: string): Region[] {
  const grupo = c.cajas.length > 1 ? nuevoId() : null
  return c.cajas.map((caja) => ({
    id: nuevoId(),
    pagina: c.pagina,
    caja,
    categoriaId: c.categoriaId ?? categoriaPorDefecto,
    origen: c.origen,
    texto: c.texto,
    grupo,
  }))
}

/** Clave estable de una coincidencia, para recordar si está marcada en una lista. */
export const claveCoincidencia = (c: Coincidencia) =>
  `${c.pagina}:${c.cajas.map((k) => `${k.x.toFixed(4)},${k.y.toFixed(4)}`).join('|')}`
