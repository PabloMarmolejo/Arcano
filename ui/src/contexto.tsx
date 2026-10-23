import { createContext, useContext } from 'react'
import type { Accion, Estado } from './estado'
import type { DocumentoPdf } from './pdf'
import type { Categoria, Coincidencia, DocumentoAbierto, Inicio, Palabra, RectN } from './tipos'

export type Herramienta = 'texto' | 'area'

export interface Aviso {
  id: number
  texto: string
  tipo: 'info' | 'exito' | 'error'
}

export interface Contexto {
  inicio: Inicio
  doc: DocumentoAbierto
  pdf: DocumentoPdf
  estado: Estado
  despachar: (a: Accion) => void
  categoriaActiva: string
  setCategoriaActiva: (id: string) => void
  herramienta: Herramienta
  setHerramienta: (h: Herramienta) => void
  vistaPrevia: boolean
  zoom: number
  /** Coincidencias que un panel muestra sobre las páginas, aún sin testar. */
  candidatas: Coincidencia[]
  setCandidatas: (c: Coincidencia[]) => void
  /** Coincidencia resaltada (al pasar el cursor por una lista). */
  foco: Coincidencia | null
  setFoco: (c: Coincidencia | null) => void
  irA: (pagina: number, caja?: RectN) => void
  registrarIrA: (f: (pagina: number, caja?: RectN) => void) => void
  palabras: (pagina: number) => Promise<Palabra[]>
  /** Se llama después de un OCR: el texto de las páginas cambió. */
  actualizarDoc: (doc: DocumentoAbierto) => void
  avisar: (texto: string, tipo?: Aviso['tipo']) => void
  categoria: (id?: string | null) => Categoria | undefined
}

export const ContextoArcano = createContext<Contexto | null>(null)

export function useArcano(): Contexto {
  const c = useContext(ContextoArcano)
  if (!c) throw new Error('useArcano fuera del documento')
  return c
}
