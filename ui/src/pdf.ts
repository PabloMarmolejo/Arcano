// Visualización con pdf.js. Solo dibuja: el texto y las coordenadas salen del
// motor (PDFium), que es el mismo que genera la versión pública.
import * as pdfjs from 'pdfjs-dist'
import type { PDFDocumentProxy, RenderTask } from 'pdfjs-dist'
import urlTrabajador from 'pdfjs-dist/build/pdf.worker.min.mjs?url'

pdfjs.GlobalWorkerOptions.workerSrc = urlTrabajador

export type DocumentoPdf = PDFDocumentProxy

export function cargarPdf(url: string): Promise<PDFDocumentProxy> {
  return pdfjs.getDocument({
    url,
    cMapUrl: '/pdfjs/cmaps/',
    cMapPacked: true,
    standardFontDataUrl: '/pdfjs/standard_fonts/',
    wasmUrl: '/pdfjs/wasm/',
    iccUrl: '/pdfjs/iccs/',
    enableXfa: false,
  }).promise
}

/**
 * Dibuja una página en el lienzo al ancho indicado (en px CSS), con nitidez
 * según la densidad de pantalla. Devuelve la tarea para poder cancelarla.
 */
export async function dibujarPagina(
  doc: PDFDocumentProxy,
  indice: number,
  lienzo: HTMLCanvasElement,
  anchoCss: number,
): Promise<RenderTask> {
  const pagina = await doc.getPage(indice + 1)
  const base = pagina.getViewport({ scale: 1 })
  const densidad = Math.min(window.devicePixelRatio || 1, 2)
  const vista = pagina.getViewport({ scale: (anchoCss / base.width) * densidad })
  lienzo.width = Math.floor(vista.width)
  lienzo.height = Math.floor(vista.height)
  return pagina.render({ canvas: lienzo, viewport: vista })
}
