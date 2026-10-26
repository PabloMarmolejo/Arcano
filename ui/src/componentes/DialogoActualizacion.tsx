import type { ActualizacionDisponible } from '../tipos'

interface Props {
  actualizacion: ActualizacionDisponible
  versionActual: string
  hayCambios: boolean
  alActualizar: () => void
  alDescargar: () => void
  alCerrar: () => void
}

/** Aviso de versión nueva: se instala en un paso o, en la versión portátil, se ofrece la descarga. */
export function DialogoActualizacion({ actualizacion, versionActual, hayCambios, alActualizar, alDescargar, alCerrar }: Props) {
  return (
    <div className="fondo-modal" role="dialog" aria-modal="true" aria-labelledby="titulo-actualizacion">
      <div className="modal">
        <header>
          <h2 id="titulo-actualizacion">Hay una versión nueva de Arcano</h2>
          <button className="icono" onClick={alCerrar} aria-label="Cerrar">
            ✕
          </button>
        </header>
        <div className="modal-cuerpo">
          <p>
            Versión instalada: <b>{versionActual}</b> · Versión nueva: <b>{actualizacion.version.replace(/^v/i, '')}</b>
          </p>
          {actualizacion.notas && <div className="notas-version">{actualizacion.notas}</div>}
          {actualizacion.instalable ? (
            <p className="nota">
              Arcano descargará la versión nueva, se cerrará para instalarla y se volverá a abrir.
              {hayCambios && ' Antes se guardará el trabajo del documento abierto.'} El catálogo y las preferencias se conservan.
            </p>
          ) : (
            <p className="nota">Esta copia de Arcano no se instaló con el instalador: descargue la versión nueva desde su página.</p>
          )}
        </div>
        <footer>
          <button onClick={alCerrar}>Más tarde</button>
          {actualizacion.instalable ? (
            <button className="primario" onClick={alActualizar}>
              Actualizar ahora
            </button>
          ) : (
            <button className="primario" onClick={alDescargar} disabled={!actualizacion.pagina}>
              Ir a la descarga
            </button>
          )}
        </footer>
      </div>
    </div>
  )
}
