<img src="recursos/marca/arcano.png" alt="" width="72" align="left" />

# Arcano

**Generador de versiones públicas**

<br clear="left" />

Testa datos personales en documentos PDF o Word de autoridades mexicanas y
produce un PDF nuevo en el que **lo testado no existe**: no hay texto debajo de
los recuadros ni capas que se puedan quitar.

> Hasta la versión 0.1 se llamó *Testado*. Arcano lee los trabajos `.testado`
> y conserva el catálogo capturado.

Es un programa de escritorio para Windows. El motor es una biblioteca .NET
independiente de la interfaz, de modo que puede integrarse en otros sistemas
(una API que use `Arcano.Motor` y un frontend que reutilice el editor).

## Descarga

Instalador y versión portátil para Windows 10/11 de 64 bits en
[Releases](https://github.com/PabloMarmolejo/Arcano/releases/latest). Antes de
usarlo, capture los fundamentos legales de su institución en **Catálogo**.

## Cómo funciona

1. **Abrir.** PDF directo; Word (.docx, .doc, .rtf, .odt) se convierte a PDF
   con Microsoft Word (o LibreOffice) tomando la versión final: sin marcas de
   revisión ni comentarios, y avisando si los había.
2. **Marcar.** Sobre el documento se marcan zonas:
   - **Texto**: arrastrar sobre palabras o hacer clic en una.
   - **Área**: recuadro libre para firmas, fotografías o sellos.
   - **Buscar**: todas las apariciones de un nombre o frase, sin distinguir
     acentos ni mayúsculas, con variantes del nombre.
   - **Detectar**: CURP, RFC, clave de elector, teléfono, correo, CLABE (con
     dígito verificador), tarjeta (Luhn), código postal, posibles domicilios,
     nombres tras «C.», «actor», «promovente»…, placas e imágenes incrustadas.
     También reconoce las marcas `|…|` de las macros de Word que ya se usan.
   - **OCR**: las páginas escaneadas se reconocen con Tesseract (español) para
     poder buscar y detectar en ellas.
3. **Revisar.** Avisa cuando algo ya testado vuelve a aparecer sin testar en
   otra página, y cuando hay zonas sin categoría.
4. **Generar.** Cada página con testados se dibuja como imagen, se pintan los
   testados sobre los píxeles y **después** se aplica OCR para agregar una capa
   de texto invisible (el PDF se puede buscar, pero lo testado ya no estaba en
   la imagen). Las páginas sin testados se copian del original, sin
   anotaciones, acciones ni metadatos, lo que mantiene el archivo ligero; del
   documento original no se lleva nada más (adjuntos, JavaScript, marcadores,
   formularios). Una opción permite convertir todas las páginas en imagen. Al final se agrega la hoja de leyenda: documento, testados
   por categoría con páginas, motivación y fundamento.
5. **Verificar.** Antes de entregar el archivo se comprueba que no tenga
   anotaciones, adjuntos ni metadatos del original, y que ningún texto testado
   se pueda leer en él.

El trabajo se guarda junto al documento como `<archivo>.arcano` (JSON con las
zonas), ligado al documento por su SHA-256.

## Estructura

```
src/Arcano.Motor        Biblioteca .NET 10 (multiplataforma)
  Pdfium/                Enlace con PDFium: render, texto con coordenadas, imágenes
  Texto/                 Texto de página normalizado (sin acentos) con mapa a cajas
  Ocr/                   Tesseract
  Deteccion/             Búsqueda y detectores
  Generacion/            Versión pública, leyenda y verificación
  Catalogo/              Categorías de datos personales (catalogo.json)
  Conversion/            Word → PDF
src/Arcano.Escritorio   WPF + WebView2: hospeda la interfaz y expone el motor (Puente.cs)
ui/                      React + TypeScript + pdf.js (Vite)
tests/                   Pruebas del motor (xUnit); la interfaz usa Vitest
scripts/publicar.ps1     Paquete autocontenido e instalador para Windows x64
instalador/Arcano.iss   Script de Inno Setup
recursos/marca           Ícono (scripts/generar-icono.py)
```

Todas las coordenadas viajan como `RectN`: rectángulo normalizado (0–1) sobre
la página tal como se ve (ya rotada). El motor usa PDFium tanto para el texto
como para generar las imágenes, así que lo que se marca coincide con lo que se
pinta.

## Desarrollo

Requisitos: .NET SDK 10, Node 24, Windows con WebView2.

```powershell
dotnet test                                   # pruebas del motor
cd ui; npm install; npm test                  # pruebas de la interfaz
dotnet build src/Arcano.Escritorio           # compila también la interfaz
src/Arcano.Escritorio/bin/Debug/net10.0-windows/win-x64/Arcano.exe [archivo.pdf]
```

Para trabajar en la interfaz con recarga en caliente:

```powershell
cd ui; npm run dev
$env:ARCANO_UI = 'http://localhost:5173'; dotnet run --project src/Arcano.Escritorio -p:OmitirUi=true
```

Con `ARCANO_UI` se habilitan además las herramientas de desarrollo (F12).

El modelo de OCR (`spa.traineddata`, 18 MB) se descarga en `recursos/tessdata`
la primera vez que se compila.

## Publicar

```powershell
winget install JRSoftware.InnoSetup   # una vez, para el instalador
pwsh scripts/publicar.ps1
```

Genera en `publicar/`:

- `Arcano-<versión>-instalador.exe`: instalador para el usuario, sin permisos de
  administrador (va a `%LOCALAPPDATA%\Programs\Arcano`; con permisos se puede
  instalar para todos). Crea el acceso en el menú Inicio, agrega «Abrir con →
  Arcano» para PDF y Word, y asocia los archivos `.arcano`. Avisa si faltan
  WebView2 o Visual C++.
- `Arcano-<versión>-win-x64.zip`: la misma carpeta, para copiar sin instalar.

El equipo destino necesita Windows 10/11 con WebView2 Runtime (viene con Edge)
y Visual C++ 2015-2022 x64 (lo usa el OCR). Para abrir Word, Microsoft Word o
LibreOffice. Al desinstalar se conservan el catálogo y las preferencias.

**Actualizaciones.** Al abrirse, Arcano busca una versión más nueva y, si la
hay, muestra «Versión X disponible» en la barra superior. Con *Actualizar
ahora* descarga el instalador, comprueba su huella SHA-256, se cierra, se
instala en silencio y se vuelve a abrir (la versión portátil solo ofrece la
descarga). La fuente se configura con un archivo `actualizaciones.json` junto a
`Arcano.exe`:

```json
{ "manifiesto": "https://mi-cuenta.blob.core.windows.net/arcano/version.json" }
```

donde `version.json` es
`{ "version": "0.4.0", "instalador": "https://…/Arcano-0.4.0-instalador.exe", "sha256": "…", "notas": "…" }`.
Sin ese archivo se consultan las Releases de este repositorio (el instalador
debe llamarse `*-instalador.exe`); con `{ "desactivadas": true }` no se busca.

**Firma.** El instalador no está firmado: al abrirlo después de descargarlo,
Windows SmartScreen muestra «Windows protegió su equipo». Se continúa con
*Más información → Ejecutar de todas formas*. Si se copia desde una carpeta de
la red interna normalmente no aparece el aviso.

Datos del usuario en `%LOCALAPPDATA%\Arcano`: `catalogo.json`,
`preferencias.json` y `bitacora.log` (solo errores técnicos, sin contenido de
documentos).

## Pendientes antes de producción

- **Fundamentos legales.** El catálogo trae las categorías con motivación,
  pero el fundamento está como `[PENDIENTE…]`. Cada institución lo captura en
  la pantalla **Catálogo** (botón en la barra superior): un fundamento general
  y, si hace falta, uno propio por categoría. Ahí también se configuran los
  dominios de correo institucionales y se puede **exportar** el catálogo para
  que otras personas lo **importen**. Se guarda en
  `%LOCALAPPDATA%\Arcano\catalogo.json`.
- Firma de código del instalador (por ejemplo, SignPath para proyectos de código abierto).
- Integración con sistemas de expedientes: endpoint que reciba el documento y
  las zonas, y precargar como términos de búsqueda las partes del expediente.

## Licencia

[MIT](LICENSE) © 2026 Luis Pablo Marmolejo Ramírez.

## Licencias de terceros

PDFium (BSD-3/Apache-2.0), Tesseract y su modelo (Apache-2.0), Leptonica
(BSD-2), SkiaSharp (MIT), PDFsharp 6.2.4 (MIT; la 6.5 cambió de licencia),
WebView2 SDK (BSD-3), React (MIT), pdf.js (Apache-2.0).
