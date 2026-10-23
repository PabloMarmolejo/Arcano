import { defineConfig, type Plugin } from 'vite'
import react from '@vitejs/plugin-react-swc'

// Política de contenido solo en la compilación: el servidor de desarrollo de
// Vite inyecta scripts en línea. 'wasm-unsafe-eval' es para los decodificadores
// de imagen de pdf.js.
const politica: Plugin = {
  name: 'politica-contenido',
  apply: 'build',
  transformIndexHtml: () => [
    {
      tag: 'meta',
      attrs: {
        'http-equiv': 'Content-Security-Policy',
        content:
          "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; " +
          "script-src 'self' 'wasm-unsafe-eval'; worker-src 'self' blob:; connect-src 'self'; object-src 'none'; base-uri 'none'",
      },
      injectTo: 'head-prepend',
    },
  ],
}

// La interfaz se sirve desde el origen https://app.arcano dentro de WebView2.
export default defineConfig({
  plugins: [react(), politica],
  base: '/',
  build: { outDir: 'dist', emptyOutDir: true, chunkSizeWarningLimit: 2000 },
  server: { port: 5173, strictPort: true },
})
