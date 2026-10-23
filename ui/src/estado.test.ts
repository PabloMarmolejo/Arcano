import { describe, expect, it } from 'vitest'
import { estadoInicial, reductor } from './estado'
import { coincidenciaCubierta, regionesDe } from './geometria'
import type { Coincidencia, Region } from './tipos'

const region = (id: string, x: number, grupo?: string): Region => ({
  id,
  pagina: 0,
  caja: { x, y: 0.1, ancho: 0.1, alto: 0.02 },
  categoriaId: 'nombre',
  origen: 'manual',
  grupo,
})

describe('reductor de zonas', () => {
  it('no agrega una zona que ya está cubierta', () => {
    let e = reductor(estadoInicial, { tipo: 'agregar', regiones: [region('a', 0.1)] })
    e = reductor(e, { tipo: 'agregar', regiones: [{ ...region('b', 0.1), caja: { x: 0.11, y: 0.1, ancho: 0.05, alto: 0.02 } }] })
    expect(e.regiones.map((r) => r.id)).toEqual(['a'])
  })

  it('quita todas las cajas del grupo y se puede deshacer', () => {
    let e = reductor(estadoInicial, { tipo: 'agregar', regiones: [region('a', 0.1, 'g'), region('b', 0.5, 'g'), region('c', 0.8)] })
    e = reductor(e, { tipo: 'quitar', ids: ['a'] })
    expect(e.regiones.map((r) => r.id)).toEqual(['c'])
    e = reductor(e, { tipo: 'deshacer' })
    expect(e.regiones).toHaveLength(3)
    e = reductor(e, { tipo: 'rehacer' })
    expect(e.regiones).toHaveLength(1)
  })

  it('cambia la categoría de todo el grupo', () => {
    let e = reductor(estadoInicial, { tipo: 'agregar', regiones: [region('a', 0.1, 'g'), region('b', 0.5, 'g')] })
    e = reductor(e, { tipo: 'categoria', ids: ['b'], categoriaId: 'curp' })
    expect(e.regiones.every((r) => r.categoriaId === 'curp')).toBe(true)
  })

  it('las actualizaciones sin historial no se deshacen por separado', () => {
    let e = reductor(estadoInicial, { tipo: 'agregar', regiones: [region('a', 0.1)] })
    e = reductor(e, { tipo: 'actualizar', id: 'a', cambios: { texto: 'Juan' }, historial: false })
    expect(e.pasado).toHaveLength(1)
  })
})

describe('coincidencias', () => {
  const c: Coincidencia = {
    pagina: 0,
    texto: 'Juan Pérez',
    cajas: [
      { x: 0.8, y: 0.1, ancho: 0.15, alto: 0.02 },
      { x: 0.05, y: 0.13, ancho: 0.1, alto: 0.02 },
    ],
    origen: 'busqueda',
    sugerida: true,
    contexto: '',
  }

  it('una aparición en dos renglones produce dos cajas con el mismo grupo', () => {
    const r = regionesDe(c, 'nombre')
    expect(r).toHaveLength(2)
    expect(r[0].grupo).toBeTruthy()
    expect(r[0].grupo).toBe(r[1].grupo)
    expect(coincidenciaCubierta(c, r)).toBe(true)
    expect(coincidenciaCubierta(c, r.slice(0, 1))).toBe(false)
  })
})
