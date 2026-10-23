// Cliente del puente con el programa de escritorio (WebView2 → Puente.cs).
import type { Progreso } from './tipos'

interface Respuesta {
  id?: number
  ok?: boolean
  resultado?: unknown
  error?: string
  cancelado?: boolean
  evento?: string
  datos?: unknown
}

interface WebViewHost {
  postMessage(mensaje: unknown): void
  addEventListener(tipo: 'message', manejador: (e: MessageEvent<Respuesta>) => void): void
}

declare global {
  interface Window {
    chrome?: { webview?: WebViewHost }
  }
}

export class ErrorPuente extends Error {
  constructor(
    mensaje: string,
    readonly cancelado = false,
  ) {
    super(mensaje)
  }
}

const host = window.chrome?.webview
const pendientes = new Map<number, { resolver: (v: unknown) => void; rechazar: (e: Error) => void }>()
const oyentesProgreso = new Set<(p: Progreso) => void>()
let siguiente = 1

host?.addEventListener('message', (e) => {
  const m = e.data
  if (m.evento === 'progreso') {
    oyentesProgreso.forEach((f) => f(m.datos as Progreso))
    return
  }
  if (m.id === undefined) return
  const p = pendientes.get(m.id)
  if (!p) return
  pendientes.delete(m.id)
  if (m.ok) p.resolver(m.resultado)
  else p.rechazar(new ErrorPuente(m.error ?? 'Error desconocido', m.cancelado ?? false))
})

export const enEscritorio = host !== undefined

export function llamar<T>(metodo: string, args: Record<string, unknown> = {}): Promise<T> {
  if (!host) return Promise.reject(new ErrorPuente('La interfaz debe abrirse desde el programa Testado.'))
  const id = siguiente++
  return new Promise<T>((resolver, rechazar) => {
    pendientes.set(id, { resolver: resolver as (v: unknown) => void, rechazar })
    host.postMessage({ id, metodo, args })
  })
}

export function alProgreso(f: (p: Progreso) => void): () => void {
  oyentesProgreso.add(f)
  return () => oyentesProgreso.delete(f)
}
