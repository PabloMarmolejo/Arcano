import { useCallback, useState } from 'react'
import { useArcano } from '../contexto'
import { ErrorPuente, llamar } from '../puente'
import type { Coincidencia, ImagenPagina } from '../tipos'
import { ListaCoincidencias, SelectorCategoria } from './Comunes'

/** Detectores automáticos de datos personales e imágenes del documento. */
export function PanelDetectar() {
  const t = useArcano()
  const [elegidos, setElegidos] = useState<Set<string>>(new Set(t.inicio.detectores.map((d) => d.id)))
  const [resultado, setResultado] = useState<Coincidencia[] | null>(null)
  const [trabajando, setTrabajando] = useState(false)
  const [categoriaImagen, setCategoriaImagen] = useState('firma')

  const nombreDetector = useCallback(
    (c: Coincidencia) =>
      c.origen === 'imagen' ? 'Imágenes (firmas, fotografías, sellos)' : (t.inicio.detectores.find((d) => `deteccion:${d.id}` === c.origen)?.nombre ?? c.origen),
    [t.inicio.detectores],
  )

  const detectar = async () => {
    setTrabajando(true)
    try {
      setResultado(await llamar<Coincidencia[]>('detectar', { id: t.doc.id, detectores: [...elegidos] }))
    } catch (e) {
      t.avisar((e as ErrorPuente).message, 'error')
    } finally {
      setTrabajando(false)
    }
  }

  const buscarImagenes = async () => {
    setTrabajando(true)
    try {
      const imagenes = await llamar<ImagenPagina[]>('imagenes', { id: t.doc.id })
      setResultado(
        imagenes.map((i) => ({
          pagina: i.pagina,
          texto: '',
          cajas: [i.caja],
          origen: 'imagen',
          categoriaId: categoriaImagen,
          sugerida: false,
          contexto: `Imagen de ${Math.round(i.caja.ancho * 100)} × ${Math.round(i.caja.alto * 100)} % de la página`,
        })),
      )
      if (imagenes.length === 0) t.avisar('El documento no tiene imágenes incrustadas (fuera del fondo de páginas escaneadas).')
    } catch (e) {
      t.avisar((e as ErrorPuente).message, 'error')
    } finally {
      setTrabajando(false)
    }
  }

  const alternar = (id: string) => {
    const s = new Set(elegidos)
    if (s.has(id)) s.delete(id)
    else s.add(id)
    setElegidos(s)
  }

  return (
    <div className="panel-detectar">
      <ul className="detectores">
        {t.inicio.detectores.map((d) => (
          <li key={d.id}>
            <label title={d.descripcion}>
              <input type="checkbox" checked={elegidos.has(d.id)} onChange={() => alternar(d.id)} />
              {d.nombre}
              {!d.sugerida && <span className="etiqueta-suave">revisar</span>}
            </label>
          </li>
        ))}
      </ul>
      <div className="lista-acciones">
        <button className="primario" disabled={trabajando || elegidos.size === 0} onClick={detectar}>
          {trabajando ? 'Buscando…' : 'Detectar datos'}
        </button>
        <span className="separador" />
        <SelectorCategoria valor={categoriaImagen} alCambiar={setCategoriaImagen} etiqueta="Categoría de imágenes" />
        <button disabled={trabajando} onClick={buscarImagenes}>
          Imágenes
        </button>
      </div>
      <p className="nota">
        Las coincidencias marcadas «revisar» se ofrecen sin seleccionar: confirme cada una. Ningún detector sustituye la
        lectura completa del documento.
      </p>
      {resultado && (
        <ListaCoincidencias
          coincidencias={resultado}
          agruparPor={nombreDetector}
          categoriaPorDefecto={t.categoriaActiva}
          vacio="No se detectaron datos con los criterios elegidos."
        />
      )}
    </div>
  )
}
