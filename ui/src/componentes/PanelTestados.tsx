import { useTestado } from '../contexto'
import type { Region } from '../tipos'
import { SelectorCategoria } from './Comunes'

const ORIGENES: Record<string, string> = {
  manual: 'Área',
  texto: 'Texto',
  busqueda: 'Búsqueda',
  repeticion: 'Revisión',
  imagen: 'Imagen',
}

const nombreOrigen = (o: string, detectores: { id: string; nombre: string }[]) =>
  o.startsWith('deteccion:') ? (detectores.find((d) => `deteccion:${d.id}` === o)?.nombre ?? 'Detector') : (ORIGENES[o] ?? o)

/** Lista de zonas testadas, una entrada por aparición (las cajas de un mismo grupo van juntas). */
export function PanelTestados() {
  const t = useTestado()
  const { estado, despachar } = t

  const apariciones: Region[][] = []
  const vistos = new Map<string, Region[]>()
  for (const r of [...estado.regiones].sort((a, b) => a.pagina - b.pagina || a.caja.y - b.caja.y || a.caja.x - b.caja.x)) {
    const clave = r.grupo ?? r.id
    const lista = vistos.get(clave)
    if (lista) lista.push(r)
    else {
      const nueva = [r]
      vistos.set(clave, nueva)
      apariciones.push(nueva)
    }
  }
  const sinCategoria = apariciones.filter((a) => !t.categoria(a[0].categoriaId)).length

  if (apariciones.length === 0)
    return (
      <div className="panel-ayuda">
        <p className="vacio">Todavía no hay nada testado.</p>
        <ul>
          <li>
            Con la herramienta <b>Texto</b> arrastre sobre las palabras o haga clic en una para testarla.
          </li>
          <li>
            Con <b>Área</b> dibuje un recuadro sobre firmas, fotografías o cualquier zona.
          </li>
          <li>
            Use <b>Buscar</b> para testar todas las apariciones de un nombre y <b>Detectar</b> para CURP, RFC, teléfonos y
            otros datos.
          </li>
        </ul>
      </div>
    )

  return (
    <div className="panel-testados">
      <div className="lista-acciones">
        <span>
          {apariciones.length} testado(s){sinCategoria > 0 && <b className="alerta"> · {sinCategoria} sin categoría</b>}
        </span>
      </div>
      <ul className="testados">
        {apariciones.map((grupo) => {
          const r = grupo[0]
          const seleccionada = grupo.some((x) => x.id === estado.seleccion)
          return (
            <li key={r.grupo ?? r.id} className={seleccionada ? 'seleccionado' : ''}>
              <button
                className="enlace"
                onClick={() => {
                  despachar({ tipo: 'seleccionar', id: r.id })
                  t.irA(r.pagina, r.caja)
                }}
              >
                <span className="pag">p. {r.pagina + 1}</span>
                <span className="texto-testado">{r.texto || <i>(sin texto: imagen o área)</i>}</span>
                <span className="origen">{nombreOrigen(r.origen, t.inicio.detectores)}</span>
              </button>
              <div className="fila-controles">
                <SelectorCategoria
                  valor={r.categoriaId}
                  vacia="— Sin categoría —"
                  alCambiar={(id) => despachar({ tipo: 'categoria', ids: [r.id], categoriaId: id })}
                />
                <button className="icono" title="Quitar este testado" onClick={() => despachar({ tipo: 'quitar', ids: [r.id] })}>
                  ✕
                </button>
              </div>
            </li>
          )
        })}
      </ul>
    </div>
  )
}
