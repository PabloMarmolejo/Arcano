import { useState } from 'react'
import { useArcano } from '../contexto'
import { ErrorPuente, llamar } from '../puente'
import type { Coincidencia, InfoPagina } from '../tipos'
import { ListaCoincidencias } from './Comunes'

interface Props {
  alReconocer: (paginas: number[]) => Promise<InfoPagina[] | null>
}

/** Revisión previa: páginas sin texto, apariciones que quedaron sin testar, zonas sin categoría. */
export function PanelRevision({ alReconocer }: Props) {
  const t = useArcano()
  const [repeticiones, setRepeticiones] = useState<Coincidencia[] | null>(null)
  const [revisando, setRevisando] = useState(false)
  const [incluirConTexto, setIncluirConTexto] = useState(false)

  const sinTexto = t.doc.paginas.filter((p) => p.texto === 'SinTexto').map((p) => p.indice)
  const sinCategoria = t.estado.regiones.filter((r) => !t.categoria(r.categoriaId))

  const reconocer = async () => {
    const paginas = incluirConTexto ? t.doc.paginas.map((p) => p.indice) : sinTexto
    if (paginas.length === 0) return
    await alReconocer(paginas)
  }

  const revisar = async () => {
    setRevisando(true)
    try {
      setRepeticiones(await llamar<Coincidencia[]>('repeticiones', { id: t.doc.id, regiones: t.estado.regiones }))
    } catch (e) {
      t.avisar((e as ErrorPuente).message, 'error')
    } finally {
      setRevisando(false)
    }
  }

  return (
    <div className="panel-revision">
      {t.doc.advertencias.length > 0 && (
        <section className="tarjeta aviso">
          <h3>Al abrir el documento</h3>
          <ul>
            {t.doc.advertencias.map((a) => (
              <li key={a}>{a}</li>
            ))}
          </ul>
        </section>
      )}

      <section className="tarjeta">
        <h3>Texto de páginas escaneadas</h3>
        {!t.inicio.ocrDisponible ? (
          <p className="alerta">{t.inicio.errorOcr ?? 'El OCR no está disponible.'}</p>
        ) : sinTexto.length > 0 ? (
          <p>
            {sinTexto.length} página(s) no tienen texto (son imágenes). Reconozca su texto para poder buscar y detectar datos
            en ellas: {sinTexto.map((p) => p + 1).join(', ')}.
          </p>
        ) : (
          <p>Todas las páginas tienen texto.</p>
        )}
        {t.inicio.ocrDisponible && (
          <>
            <label className="opcion">
              <input type="checkbox" checked={incluirConTexto} onChange={(e) => setIncluirConTexto(e.target.checked)} />
              Reconocer también páginas con texto (anexos escaneados pegados como imagen)
            </label>
            <button className="primario" disabled={!incluirConTexto && sinTexto.length === 0} onClick={reconocer}>
              Reconocer texto (OCR)
            </button>
          </>
        )}
      </section>

      <section className="tarjeta">
        <h3>Apariciones sin testar</h3>
        <p>Busca en todo el documento lo que ya testó y avisa dónde vuelve a aparecer sin testar.</p>
        <button className="primario" disabled={revisando || t.estado.regiones.length === 0} onClick={revisar}>
          {revisando ? 'Revisando…' : 'Revisar'}
        </button>
        {repeticiones && (
          <ListaCoincidencias
            coincidencias={repeticiones}
            categoriaPorDefecto={t.categoriaActiva}
            vacio="No hay apariciones sin testar."
          />
        )}
      </section>

      {sinCategoria.length > 0 && (
        <section className="tarjeta aviso">
          <h3>Zonas sin categoría</h3>
          <p>{sinCategoria.length} zona(s) no tienen categoría y aparecerán como «Otro dato personal» en la leyenda.</p>
          <button
            onClick={() => {
              t.despachar({ tipo: 'seleccionar', id: sinCategoria[0].id })
              t.irA(sinCategoria[0].pagina, sinCategoria[0].caja)
            }}
          >
            Ir a la primera
          </button>
        </section>
      )}
    </div>
  )
}
