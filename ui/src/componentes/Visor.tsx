import { useCallback, useEffect, useRef } from 'react'
import { useArcano } from '../contexto'
import type { RectN } from '../tipos'
import { Pagina } from './Pagina'

export const PT_A_PX = 96 / 72

interface Props {
  alDesplazar: (pagina: number) => void
  alMedir: (anchoDisponible: number) => void
}

export function Visor({ alDesplazar, alMedir }: Props) {
  const t = useArcano()
  const caja = useRef<HTMLDivElement>(null)
  const { registrarIrA } = t

  const irA = useCallback((pagina: number, objetivo?: RectN) => {
    const el = caja.current?.querySelector<HTMLElement>(`[data-pagina="${pagina}"]`)
    const contenedor = caja.current
    if (!el || !contenedor) return
    const y = objetivo ? el.offsetTop + objetivo.y * el.offsetHeight - contenedor.clientHeight / 3 : el.offsetTop - 12
    const x = objetivo ? el.offsetLeft + objetivo.x * el.offsetWidth - contenedor.clientWidth / 3 : contenedor.scrollLeft
    contenedor.scrollTo({ top: Math.max(0, y), left: Math.max(0, x), behavior: 'smooth' })
  }, [])

  useEffect(() => registrarIrA(irA), [irA, registrarIrA])

  // Ancho disponible para «ajustar al ancho».
  useEffect(() => {
    const el = caja.current
    if (!el) return
    const obs = new ResizeObserver(() => alMedir(el.clientWidth - 48))
    obs.observe(el)
    return () => obs.disconnect()
  }, [alMedir])

  const alScroll = () => {
    const el = caja.current
    if (!el) return
    const centro = el.scrollTop + el.clientHeight / 2
    const paginas = el.querySelectorAll<HTMLElement>('[data-pagina]')
    for (const p of paginas) {
      if (p.offsetTop + p.offsetHeight >= centro) {
        alDesplazar(Number(p.dataset.pagina))
        break
      }
    }
  }

  return (
    <div className="visor" ref={caja} onScroll={alScroll}>
      {t.doc.paginas.map((info) => (
        <Pagina key={info.indice} info={info} anchoCss={Math.round(info.anchoPt * t.zoom * PT_A_PX)} />
      ))}
    </div>
  )
}
