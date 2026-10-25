import { useEffect, useState } from 'react'
import type { FormEvent } from 'react'
import { useArcano } from '../contexto'
import { ErrorPuente, llamar } from '../puente'
import type { Coincidencia } from '../tipos'
import { ListaCoincidencias, SelectorCategoria } from './Comunes'

interface Props {
  terminos: string[]
  alAgregarTermino: (t: string) => void
  /** Búsqueda pedida desde fuera del panel (por ejemplo, un aviso de la verificación). */
  pedida?: { termino: string; id: number } | null
}

/** Busca un nombre o frase en todo el documento (sin distinguir acentos ni mayúsculas). */
export function PanelBuscar({ terminos, alAgregarTermino, pedida }: Props) {
  const t = useArcano()
  const [termino, setTermino] = useState('')
  const [palabraCompleta, setPalabraCompleta] = useState(true)
  const [variantes, setVariantes] = useState(true)
  const [categoria, setCategoria] = useState('nombre')
  const [resultado, setResultado] = useState<Coincidencia[] | null>(null)
  const [buscando, setBuscando] = useState(false)

  const buscar = async (valor: string) => {
    const limpio = valor.trim()
    if (limpio.length < 2) return
    setBuscando(true)
    try {
      const r = await llamar<Coincidencia[]>('buscar', {
        id: t.doc.id,
        termino: limpio,
        palabraCompleta,
        variantes,
        categoriaId: categoria,
      })
      setResultado(r)
      alAgregarTermino(limpio)
    } catch (e) {
      t.avisar((e as ErrorPuente).message, 'error')
    } finally {
      setBuscando(false)
    }
  }

  useEffect(() => {
    if (!pedida) return
    setTermino(pedida.termino)
    void buscar(pedida.termino)
    // Solo cuando llega una petición nueva.
  }, [pedida?.id])

  const alEnviar = (e: FormEvent) => {
    e.preventDefault()
    void buscar(termino)
  }

  const sinTexto = t.doc.paginas.filter((p) => p.texto === 'SinTexto').length

  return (
    <div className="panel-buscar">
      <form onSubmit={alEnviar} className="formulario-busqueda">
        <input
          type="search"
          placeholder="Nombre, domicilio, cualquier frase…"
          value={termino}
          onChange={(e) => setTermino(e.target.value)}
          autoFocus
        />
        <button className="primario" disabled={buscando || termino.trim().length < 2}>
          {buscando ? 'Buscando…' : 'Buscar'}
        </button>
      </form>
      <div className="opciones-busqueda">
        <label>
          <input type="checkbox" checked={palabraCompleta} onChange={(e) => setPalabraCompleta(e.target.checked)} />
          Palabra completa
        </label>
        <label title="Para «Juan Pérez López» busca también «Juan Pérez» y «Pérez López»">
          <input type="checkbox" checked={variantes} onChange={(e) => setVariantes(e.target.checked)} />
          Variantes del nombre
        </label>
      </div>
      <div className="fila-controles">
        <span className="etiqueta">Testar como</span>
        <SelectorCategoria valor={categoria} alCambiar={setCategoria} />
      </div>
      {sinTexto > 0 && (
        <p className="nota">
          {sinTexto} página(s) escaneada(s) no se incluyen en la búsqueda hasta reconocer su texto (pestaña Revisión).
        </p>
      )}
      {terminos.length > 0 && (
        <div className="terminos">
          {terminos.map((x) => (
            <button
              key={x}
              className="chip"
              onClick={() => {
                setTermino(x)
                void buscar(x)
              }}
            >
              {x}
            </button>
          ))}
        </div>
      )}
      {resultado && (
        <ListaCoincidencias coincidencias={resultado} categoriaPorDefecto={categoria} vacio="No se encontraron apariciones." />
      )}
    </div>
  )
}
