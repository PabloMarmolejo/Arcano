import { memo, useEffect, useRef, useState } from 'react'
import type { PointerEvent as PE } from 'react'
import { useTestado } from '../contexto'
import { acotar, claveCoincidencia, desdeBordes, nuevoId, seIntersectan, union } from '../geometria'
import { dibujarPagina } from '../pdf'
import { llamar } from '../puente'
import type { InfoPagina, Palabra, RectN, Region } from '../tipos'

interface Props {
  info: InfoPagina
  anchoCss: number
}

type Arrastre =
  | { modo: 'crear'; x0: number; y0: number; x: number; y: number }
  | { modo: 'mover'; id: string; x0: number; y0: number; caja: RectN }
  | { modo: 'redimensionar'; id: string; esquina: 'nw' | 'ne' | 'sw' | 'se'; caja: RectN }

const pct = (v: number) => `${v * 100}%`
const estiloCaja = (c: RectN) => ({ left: pct(c.x), top: pct(c.y), width: pct(c.ancho), height: pct(c.alto) })

/** Agrupa palabras por renglón y devuelve una caja por renglón. */
function cajasPorRenglon(palabras: Palabra[]): RectN[] {
  const ordenadas = [...palabras].sort((a, b) => a.caja.y - b.caja.y || a.caja.x - b.caja.x)
  const renglones: RectN[] = []
  for (const p of ordenadas) {
    const ultimo = renglones[renglones.length - 1]
    const traslape = ultimo
      ? Math.min(ultimo.y + ultimo.alto, p.caja.y + p.caja.alto) - Math.max(ultimo.y, p.caja.y)
      : 0
    if (ultimo && traslape >= Math.min(ultimo.alto, p.caja.alto) * 0.5) {
      renglones[renglones.length - 1] = union(ultimo, p.caja)
    } else {
      renglones.push(p.caja)
    }
  }
  return renglones
}

function PaginaBase({ info, anchoCss }: Props) {
  const t = useTestado()
  const { estado, despachar, pdf, vistaPrevia, herramienta, candidatas, foco } = t
  const contenedor = useRef<HTMLDivElement>(null)
  const lienzo = useRef<HTMLCanvasElement>(null)
  const [visible, setVisible] = useState(false)
  const [dibujada, setDibujada] = useState(0)
  const [arrastre, setArrastre] = useState<Arrastre | null>(null)

  const altoCss = (anchoCss * info.altoPt) / info.anchoPt
  const regiones = estado.regiones.filter((r) => r.pagina === info.indice)
  const candidatasPagina = candidatas.filter((c) => c.pagina === info.indice)

  // Solo se dibujan las páginas cercanas a la vista.
  useEffect(() => {
    const el = contenedor.current
    if (!el) return
    const obs = new IntersectionObserver(([e]) => setVisible(e.isIntersecting), { rootMargin: '600px 0px' })
    obs.observe(el)
    return () => obs.disconnect()
  }, [])

  useEffect(() => {
    if (!visible || !lienzo.current || dibujada === anchoCss) return
    let cancelada = false
    let tarea: Awaited<ReturnType<typeof dibujarPagina>> | null = null
    const temporizador = window.setTimeout(async () => {
      try {
        tarea = await dibujarPagina(pdf, info.indice, lienzo.current!, anchoCss)
        if (cancelada) return tarea.cancel()
        await tarea.promise
        if (!cancelada) setDibujada(anchoCss)
      } catch {
        // Render cancelado por cambio de zoom o desplazamiento.
      }
    }, 60)
    return () => {
      cancelada = true
      window.clearTimeout(temporizador)
      tarea?.cancel()
    }
  }, [visible, anchoCss, pdf, info.indice, dibujada])

  const punto = (e: { clientX: number; clientY: number }) => {
    const r = contenedor.current!.getBoundingClientRect()
    return {
      x: Math.min(1, Math.max(0, (e.clientX - r.left) / r.width)),
      y: Math.min(1, Math.max(0, (e.clientY - r.top) / r.height)),
    }
  }

  const alPresionarFondo = (e: PE<HTMLDivElement>) => {
    if (e.button !== 0) return
    e.currentTarget.setPointerCapture(e.pointerId)
    const p = punto(e)
    despachar({ tipo: 'seleccionar', id: null })
    setArrastre({ modo: 'crear', x0: p.x, y0: p.y, x: p.x, y: p.y })
  }

  const alPresionarRegion = (e: PE<HTMLDivElement>, r: Region) => {
    if (e.button !== 0) return
    e.stopPropagation()
    contenedor.current!.setPointerCapture(e.pointerId)
    despachar({ tipo: 'seleccionar', id: r.id })
    const p = punto(e)
    setArrastre({ modo: 'mover', id: r.id, x0: p.x, y0: p.y, caja: r.caja })
  }

  const alPresionarAsa = (e: PE<HTMLDivElement>, r: Region, esquina: 'nw' | 'ne' | 'sw' | 'se') => {
    e.stopPropagation()
    contenedor.current!.setPointerCapture(e.pointerId)
    setArrastre({ modo: 'redimensionar', id: r.id, esquina, caja: r.caja })
  }

  const alMover = (e: PE<HTMLDivElement>) => {
    if (!arrastre) return
    const p = punto(e)
    if (arrastre.modo === 'crear') {
      setArrastre({ ...arrastre, x: p.x, y: p.y })
    } else if (arrastre.modo === 'mover') {
      const c = arrastre.caja
      const caja = acotar({ ...c, x: c.x + p.x - arrastre.x0, y: c.y + p.y - arrastre.y0 })
      despachar({ tipo: 'actualizar', id: arrastre.id, cambios: { caja: { ...caja, ancho: c.ancho, alto: c.alto } }, historial: false })
    } else {
      const c = arrastre.caja
      const fijoX = arrastre.esquina.includes('w') ? c.x + c.ancho : c.x
      const fijoY = arrastre.esquina.includes('n') ? c.y + c.alto : c.y
      despachar({ tipo: 'actualizar', id: arrastre.id, cambios: { caja: desdeBordes(fijoX, fijoY, p.x, p.y) }, historial: false })
    }
  }

  const alSoltar = async (e: PE<HTMLDivElement>) => {
    const a = arrastre
    setArrastre(null)
    if (!a) return
    if (a.modo !== 'crear') {
      // El movimiento se aplicó sin historial; se registra un solo paso al soltar.
      const actual = estado.regiones.find((r) => r.id === a.id)
      if (actual && (actual.caja.x !== a.caja.x || actual.caja.y !== a.caja.y || actual.caja.ancho !== a.caja.ancho || actual.caja.alto !== a.caja.alto)) {
        despachar({ tipo: 'actualizar', id: a.id, cambios: { caja: a.caja }, historial: false })
        despachar({ tipo: 'actualizar', id: a.id, cambios: { caja: actual.caja } })
        const texto = await llamar<string>('textoBajo', { id: t.doc.id, pagina: info.indice, caja: actual.caja }).catch(() => '')
        despachar({ tipo: 'actualizar', id: a.id, cambios: { texto }, historial: false })
      }
      return
    }
    const p = punto(e)
    const arrastrado = desdeBordes(a.x0, a.y0, p.x, p.y)
    const esClic = arrastrado.ancho < 0.003 && arrastrado.alto < 0.003

    if (herramienta === 'texto' && info.texto !== 'SinTexto') {
      const palabras = await t.palabras(info.indice)
      const elegidas = esClic
        ? palabras.filter((w) => p.x >= w.caja.x && p.x <= w.caja.x + w.caja.ancho && p.y >= w.caja.y && p.y <= w.caja.y + w.caja.alto)
        : palabras.filter((w) => seIntersectan(w.caja, arrastrado))
      if (elegidas.length === 0) return
      const cajas = cajasPorRenglon(elegidas)
      const grupo = cajas.length > 1 ? nuevoId() : null
      const texto = [...elegidas].sort((x, y) => x.inicio - y.inicio).map((w) => w.texto).join(' ')
      despachar({
        tipo: 'agregar',
        seleccionar: true,
        regiones: cajas.map((caja) => ({
          id: nuevoId(), pagina: info.indice, caja, categoriaId: t.categoriaActiva, origen: 'texto', texto, grupo,
        })),
      })
      return
    }

    if (esClic) return
    if (herramienta === 'texto') t.avisar('Esta página no tiene texto: se marcó el área. Para buscar en ella, use Revisión → Reconocer texto.')
    const region: Region = { id: nuevoId(), pagina: info.indice, caja: arrastrado, categoriaId: t.categoriaActiva, origen: 'manual' }
    despachar({ tipo: 'agregar', regiones: [region], seleccionar: true })
    const texto = await llamar<string>('textoBajo', { id: t.doc.id, pagina: info.indice, caja: arrastrado }).catch(() => '')
    if (texto) despachar({ tipo: 'actualizar', id: region.id, cambios: { texto }, historial: false })
  }

  const rectArrastre = arrastre?.modo === 'crear' ? desdeBordes(arrastre.x0, arrastre.y0, arrastre.x, arrastre.y) : null
  const enfocadas = foco && foco.pagina === info.indice ? foco.cajas : []

  return (
    <div className="pagina" style={{ width: anchoCss, height: altoCss }} data-pagina={info.indice}>
      <canvas ref={lienzo} className="pagina-lienzo" style={{ width: anchoCss, height: altoCss }} />
      {dibujada === 0 && <div className="pagina-cargando">Página {info.indice + 1}</div>}
      <div
        ref={contenedor}
        className={`pagina-capa herramienta-${herramienta}`}
        onPointerDown={alPresionarFondo}
        onPointerMove={alMover}
        onPointerUp={alSoltar}
      >
        {candidatasPagina.map((c) =>
          c.cajas.map((caja, i) => (
            <div key={`${claveCoincidencia(c)}-${i}`} className="candidata" style={estiloCaja(caja)} />
          )),
        )}
        {regiones.map((r) => {
          const seleccionada = estado.seleccion === r.id
          const cat = t.categoria(r.categoriaId)
          return (
            <div
              key={r.id}
              className={`region${vistaPrevia ? ' opaca' : ''}${seleccionada ? ' seleccionada' : ''}${!cat ? ' sin-categoria' : ''}`}
              style={estiloCaja(r.caja)}
              title={`${cat?.nombre ?? 'Sin categoría'}${r.texto ? ` · ${r.texto}` : ''}`}
              onPointerDown={(e) => alPresionarRegion(e, r)}
            >
              {seleccionada &&
                (['nw', 'ne', 'sw', 'se'] as const).map((esq) => (
                  <div key={esq} className={`asa asa-${esq}`} onPointerDown={(e) => alPresionarAsa(e, r, esq)} />
                ))}
            </div>
          )
        })}
        {enfocadas.map((caja, i) => (
          <div key={`foco-${i}`} className="enfocada" style={estiloCaja(caja)} />
        ))}
        {rectArrastre && <div className={`arrastre arrastre-${herramienta}`} style={estiloCaja(rectArrastre)} />}
      </div>
      <div className="pagina-numero">
        {info.indice + 1}
        {info.texto === 'SinTexto' && <span className="marca-escaneo" title="Página escaneada sin texto reconocido">sin texto</span>}
        {info.texto === 'Ocr' && <span className="marca-ocr" title="Texto reconocido por OCR">OCR</span>}
      </div>
    </div>
  )
}

export const Pagina = memo(PaginaBase)
