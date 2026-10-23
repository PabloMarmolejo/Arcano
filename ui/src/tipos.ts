// Tipos que intercambia la interfaz con el motor (Arcano.Motor, en JSON camelCase).

/** Rectángulo normalizado (0–1) sobre la página ya rotada; origen arriba a la izquierda. */
export interface RectN {
  x: number
  y: number
  ancho: number
  alto: number
}

export type OrigenTexto = 'Nativo' | 'Ocr' | 'SinTexto'

export interface InfoPagina {
  indice: number
  anchoPt: number
  altoPt: number
  texto: OrigenTexto
}

export interface Region {
  id: string
  pagina: number
  caja: RectN
  categoriaId?: string | null
  /** manual, texto, busqueda, deteccion:<id>, imagen, repeticion */
  origen: string
  texto?: string | null
  grupo?: string | null
}

export interface Coincidencia {
  pagina: number
  texto: string
  cajas: RectN[]
  origen: string
  categoriaId?: string | null
  sugerida: boolean
  contexto: string
}

export interface Palabra {
  texto: string
  caja: RectN
  inicio: number
  fin: number
}

export interface ImagenPagina {
  pagina: number
  caja: RectN
}

export interface Categoria {
  id: string
  nombre: string
  grupo: string
  tipo: 'confidencial' | 'reservada'
  motivacion: string
  fundamento: string
}

export interface Catalogo {
  version: number
  nota: string
  fundamentoGeneral: string
  dominiosInstitucionales: string[]
  categorias: Categoria[]
}

export interface Detector {
  id: string
  nombre: string
  descripcion: string
  categoriaId?: string | null
  sugerida: boolean
}

export interface DatosLeyenda {
  documento: string
  textoAdicional: string
}

export interface OpcionesGeneracion {
  dpi: number
  calidadJpeg: number
  color: string
  numerar: boolean
  capaTexto: boolean
  incluirLeyenda: boolean
  leyenda: DatosLeyenda
}

export interface Preferencias {
  leyenda: DatosLeyenda
  opciones: OpcionesGeneracion
  ultimaCarpeta?: string | null
}

export interface Inicio {
  version: string
  catalogo: Catalogo
  detectores: Detector[]
  ocrDisponible: boolean
  errorOcr?: string | null
  preferencias: Preferencias
  archivoInicial?: string | null
}

export interface ProyectoGuardado {
  documento: string
  sha256: string
  regiones: Region[]
  terminos: string[]
  leyenda?: DatosLeyenda | null
  modificado: string
}

export interface DocumentoAbierto {
  id: string
  nombre: string
  ruta: string
  url: string
  paginas: InfoPagina[]
  advertencias: string[]
  proyecto?: ProyectoGuardado | null
  proyectoDistinto: boolean
}

export interface Progreso {
  etapa: string
  actual: number
  total: number
  detalle?: string | null
}

export interface Hallazgo {
  tipo: string
  descripcion: string
  pagina?: number | null
}

export interface ResultadoGeneracion {
  ruta: string
  paginas: number
  testados: number
  bytes: number
  verificacion: {
    correcta: boolean
    comprobaciones: string[]
    hallazgos: Hallazgo[]
  }
}
