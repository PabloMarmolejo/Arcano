// Copia a public/ los recursos que pdf.js carga aparte (mapas de caracteres,
// fuentes estándar, decodificadores wasm y perfiles de color), para que la interfaz funcione sin conexión.
import { cpSync, existsSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath } from 'node:url'

const raiz = join(dirname(fileURLToPath(import.meta.url)), '..')
const origen = join(raiz, 'node_modules', 'pdfjs-dist')
const destino = join(raiz, 'public', 'pdfjs')

for (const carpeta of ['cmaps', 'standard_fonts', 'wasm', 'iccs']) {
  const de = join(origen, carpeta)
  if (!existsSync(de)) throw new Error(`No se encontró ${de}; ejecute npm install.`)
  cpSync(de, join(destino, carpeta), { recursive: true })
}
