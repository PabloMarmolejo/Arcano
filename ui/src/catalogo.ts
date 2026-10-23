// Reglas del catálogo que comparten la pantalla de Catálogo y el diálogo de generar.
import type { Catalogo, Categoria } from './tipos'

/** El fundamento falta si la categoría no tiene uno y el general está vacío o marcado como pendiente. */
export function fundamentoPendiente(cat: Categoria, catalogo: Catalogo): boolean {
  const efectivo = (cat.fundamento || catalogo.fundamentoGeneral).trim()
  return efectivo.length === 0 || efectivo.includes('[PENDIENTE')
}

/** Clave a partir del nombre: minúsculas, sin acentos, con guiones; única dentro del catálogo. */
export function claveNueva(nombre: string, existentes: Set<string>): string {
  const base =
    nombre
      .normalize('NFD')
      .replace(/[̀-ͯ]/g, '')
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, '-')
      .replace(/^-+|-+$/g, '')
      .slice(0, 40) || 'categoria'
  let clave = base
  for (let i = 2; existentes.has(clave); i++) clave = `${base}-${i}`
  return clave
}

/** Errores que impiden guardar; el motor repite la validación al guardar. */
export function validarCatalogo(c: Catalogo): string[] {
  const errores: string[] = []
  if (c.categorias.length === 0) errores.push('El catálogo debe tener al menos una categoría.')
  const nombres = new Map<string, number>()
  for (const cat of c.categorias) {
    const n = cat.nombre.trim().toLowerCase()
    if (!n) errores.push('Hay una categoría sin nombre.')
    else nombres.set(n, (nombres.get(n) ?? 0) + 1)
  }
  for (const [n, veces] of nombres) if (veces > 1) errores.push(`Hay ${veces} categorías llamadas «${n}».`)
  for (const d of c.dominiosInstitucionales) {
    if (!d.includes('.') || /\s/.test(d)) errores.push(`«${d}» no parece un dominio de correo (ejemplo: tribunal.gob.mx).`)
  }
  return errores
}
