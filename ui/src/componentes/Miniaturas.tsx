import { memo, useEffect, useRef, useState } from 'react'
import { useTestado } from '../contexto'
import { dibujarPagina } from '../pdf'
import type { InfoPagina } from '../tipos'

const ANCHO = 112

function Miniatura({ info, activa, testados }: { info: InfoPagina; activa: boolean; testados: number }) {
  const { pdf, irA } = useTestado()
  const lienzo = useRef<HTMLCanvasElement>(null)
  const [visible, setVisible] = useState(false)
  const caja = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    const el = caja.current
    if (!el) return
    const obs = new IntersectionObserver(([e]) => e.isIntersecting && setVisible(true), { rootMargin: '300px 0px' })
    obs.observe(el)
    return () => obs.disconnect()
  }, [])

  useEffect(() => {
    if (!visible || !lienzo.current) return
    let tarea: Awaited<ReturnType<typeof dibujarPagina>> | null = null
    dibujarPagina(pdf, info.indice, lienzo.current, ANCHO)
      .then((t) => {
        tarea = t
        return t.promise
      })
      .catch(() => {})
    return () => tarea?.cancel()
  }, [visible, pdf, info.indice])

  useEffect(() => {
    if (activa) caja.current?.scrollIntoView({ block: 'nearest' })
  }, [activa])

  return (
    <button ref={caja} className={`miniatura${activa ? ' activa' : ''}`} onClick={() => irA(info.indice)}>
      <canvas ref={lienzo} style={{ width: ANCHO, height: (ANCHO * info.altoPt) / info.anchoPt }} />
      <span className="miniatura-pie">
        {info.indice + 1}
        {testados > 0 && <span className="insignia">{testados}</span>}
        {info.texto === 'SinTexto' && <span className="marca-escaneo">sin texto</span>}
      </span>
    </button>
  )
}

const MiniaturaMemo = memo(Miniatura)

export function Miniaturas({ paginaActual }: { paginaActual: number }) {
  const { doc, estado } = useTestado()
  const porPagina = new Map<number, Set<string>>()
  for (const r of estado.regiones) {
    const s = porPagina.get(r.pagina) ?? new Set<string>()
    s.add(r.grupo ?? r.id)
    porPagina.set(r.pagina, s)
  }
  return (
    <nav className="miniaturas" aria-label="Páginas">
      {doc.paginas.map((p) => (
        <MiniaturaMemo key={p.indice} info={p} activa={p.indice === paginaActual} testados={porPagina.get(p.indice)?.size ?? 0} />
      ))}
    </nav>
  )
}
