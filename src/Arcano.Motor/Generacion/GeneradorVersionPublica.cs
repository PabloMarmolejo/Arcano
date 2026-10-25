using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SkiaSharp;
using Arcano.Motor.Catalogo;
using Arcano.Motor.Modelo;
using Arcano.Motor.Ocr;

namespace Arcano.Motor.Generacion;

/// <summary>
/// Genera la versión pública. Cada página con testados se convierte en imagen,
/// se pintan los testados sobre los píxeles y solo después se reconoce el texto
/// para la capa de búsqueda: lo testado ya no existe en el archivo de salida, ni
/// como texto ni como imagen, y no hay forma de recuperarlo.
///
/// Las páginas sin testados se copian del original (texto y vectores, mucho más
/// ligeras) sin anotaciones, acciones ni metadatos; del documento original no se
/// lleva nada más (adjuntos, JavaScript, marcadores, formularios). Con
/// <see cref="OpcionesGeneracion.RasterizarTodas"/> se convierten todas.
/// </summary>
public sealed class GeneradorVersionPublica(IReconocedorTexto? ocr)
{
    public ResultadoGeneracion Generar(
        SesionDocumento sesion,
        IReadOnlyList<Region> regiones,
        CatalogoDatos catalogo,
        OpcionesGeneracion opciones,
        string rutaSalida,
        IProgress<Progreso>? progreso = null,
        CancellationToken ct = default)
    {
        ResolutorFuentes.Instalar();
        regiones = Depurar(regiones);
        var dpi = Math.Clamp(opciones.Dpi, 150, 400);
        var calidad = Math.Clamp(opciones.CalidadJpeg, 50, 95);
        var color = SKColor.TryParse(opciones.Color, out var c) ? c : SKColors.Black;
        var numeros = Numerar(regiones);
        var totalPaginas = sesion.Documento.Paginas;

        using var pdf = new PdfDocument();
        pdf.Info.Title = "Versión pública";
        pdf.Info.Author = "";
        pdf.Info.Subject = "";
        pdf.Info.Keywords = "";
        pdf.Info.Creator = "Arcano";

        var avisos = new List<string>();
        var conTestados = regiones.Select(r => r.Pagina).ToHashSet();
        using var original = opciones.RasterizarTodas ? null : AbrirParaCopiar(sesion.Pdf, avisos);
        var convertidas = 0;

        for (var p = 0; p < totalPaginas; p++)
        {
            ct.ThrowIfCancellationRequested();

            if (original is not null && !conTestados.Contains(p))
            {
                progreso?.Report(new Progreso("pagina", p, totalPaginas, $"Página {p + 1} de {totalPaginas} (sin testados, se copia)"));
                CopiarPagina(pdf, original.Pages[p]);
                continue;
            }

            progreso?.Report(new Progreso("pagina", p, totalPaginas, $"Página {p + 1} de {totalPaginas}"));
            convertidas++;
            using var imagen = sesion.Documento.Renderizar(p, dpi);
            Pintar(imagen, regiones.Where(r => r.Pagina == p), numeros, color, dpi, opciones.Numerar);

            var palabras = opciones.CapaTexto && ocr is not null ? ocr.Reconocer(imagen, dpi) : [];
            var (anchoPt, altoPt) = sesion.Tamano(p);
            AgregarPagina(pdf, imagen, palabras, anchoPt, altoPt, calidad);
        }

        if (opciones.IncluirLeyenda)
        {
            progreso?.Report(new Progreso("leyenda", totalPaginas, totalPaginas, "Leyenda de clasificación"));
            GeneradorLeyenda.Agregar(pdf, sesion, regiones, numeros, catalogo, opciones.Leyenda);
        }

        // Se escribe a un temporal en la misma carpeta y se mueve al final, para
        // no dejar un archivo a medias si algo falla.
        var carpeta = Path.GetDirectoryName(Path.GetFullPath(rutaSalida))!;
        var temporal = Path.Combine(carpeta, $".{Path.GetFileName(rutaSalida)}.{Guid.NewGuid():N}.tmp");
        try
        {
            var paginas = pdf.PageCount;
            pdf.Save(temporal);
            progreso?.Report(new Progreso("verificacion", totalPaginas, totalPaginas, "Verificando el archivo"));
            var verificacion = Verificador.Verificar(temporal, regiones, totalPaginas, convertidas);
            File.Move(temporal, rutaSalida, overwrite: true);
            return new ResultadoGeneracion(rutaSalida, paginas, numeros.Values.Distinct().Count(),
                new FileInfo(rutaSalida).Length, verificacion, convertidas, avisos);
        }
        finally
        {
            if (File.Exists(temporal)) File.Delete(temporal);
        }
    }

    /// <summary>
    /// Abre el original para copiar páginas. Si PDFsharp no puede leerlo (cifrado,
    /// estructura dañada) se devuelve null y todas las páginas se convierten en imagen.
    /// </summary>
    private static PdfDocument? AbrirParaCopiar(byte[] contenido, List<string> avisos)
    {
        try
        {
            return PdfSharp.Pdf.IO.PdfReader.Open(new MemoryStream(contenido, writable: false), PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
        }
        catch (Exception ex) when (ex is PdfSharp.Pdf.IO.PdfReaderException or InvalidOperationException or NotImplementedException or NotSupportedException or IOException)
        {
            avisos.Add("No se pudieron copiar las páginas del original (el PDF está protegido o tiene una estructura que no se puede leer); todas se convirtieron en imagen.");
            return null;
        }
    }

    /// <summary>Elementos de página que no se conservan al copiarla: pueden llevar datos que no se ven.</summary>
    private static readonly string[] ElementosQueSeQuitan =
        ["/Annots", "/AA", "/Metadata", "/PieceInfo", "/Thumb", "/B", "/StructParents", "/Tabs"];

    /// <summary>
    /// Copia una página sin testados. Se quitan anotaciones (comentarios, campos de
    /// formulario, enlaces), acciones, metadatos XMP, datos privados de la aplicación
    /// que la creó y miniaturas.
    /// </summary>
    private static void CopiarPagina(PdfDocument destino, PdfPage origen)
    {
        var pagina = destino.AddPage(origen);
        foreach (var clave in ElementosQueSeQuitan)
            pagina.Elements.Remove(clave);
    }

    /// <summary>
    /// Quita las zonas que ya cubre otra (por ejemplo, el mismo nombre hallado por
    /// búsqueda y por un detector), para que no cuenten dos veces en la leyenda.
    /// </summary>
    public static IReadOnlyList<Region> Depurar(IReadOnlyList<Region> regiones)
    {
        var resultado = new List<Region>();
        foreach (var r in regiones.OrderByDescending(r => r.Caja.Area))
        {
            var cubierta = resultado.Any(o => o.Pagina == r.Pagina && o.Caja.AreaInterseccion(r.Caja) >= r.Caja.Area * 0.9);
            if (!cubierta) resultado.Add(r);
        }
        return resultado;
    }

    /// <summary>
    /// Numera los testados en orden de lectura. Las cajas de una misma aparición
    /// (mismo <see cref="Region.Grupo"/>) comparten número.
    /// </summary>
    public static Dictionary<string, int> Numerar(IReadOnlyList<Region> regiones)
    {
        var grupos = regiones
            .GroupBy(r => r.Grupo ?? r.Id)
            .OrderBy(g => g.Min(r => r.Pagina))
            .ThenBy(g => g.Where(r => r.Pagina == g.Min(x => x.Pagina)).Min(r => Math.Round(r.Caja.Y, 3)))
            .ThenBy(g => g.Min(r => r.Caja.X))
            .ToList();
        var numeros = new Dictionary<string, int>();
        for (var i = 0; i < grupos.Count; i++)
            foreach (var r in grupos[i])
                numeros[r.Id] = i + 1;
        return numeros;
    }

    private static void Pintar(SKBitmap imagen, IEnumerable<Region> regiones, Dictionary<string, int> numeros,
        SKColor color, int dpi, bool numerar)
    {
        using var lienzo = new SKCanvas(imagen);
        using var relleno = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = false };
        var margen = Math.Max(1, dpi / 100);
        var rotulados = new HashSet<int>();
        var claro = color.Red * 0.299 + color.Green * 0.587 + color.Blue * 0.114 > 140;
        using var tinta = new SKPaint { Color = claro ? SKColors.Black : SKColors.White, IsAntialias = true };

        foreach (var region in regiones.OrderBy(r => r.Caja.Y).ThenBy(r => r.Caja.X))
        {
            var caja = region.Caja.Acotar();
            if (caja.EsVacio) continue;
            var rect = new SKRect(
                (float)Math.Floor(caja.X * imagen.Width) - margen,
                (float)Math.Floor(caja.Y * imagen.Height) - margen,
                (float)Math.Ceiling(caja.Derecha * imagen.Width) + margen,
                (float)Math.Ceiling(caja.Abajo * imagen.Height) + margen);
            lienzo.DrawRect(rect, relleno);

            if (!numerar || !numeros.TryGetValue(region.Id, out var n) || !rotulados.Add(n)) continue;
            var etiqueta = $"({n})";
            using var fuente = new SKFont(SKTypeface.Default, Math.Min(rect.Height * 0.7f, dpi * 0.11f));
            var ancho = fuente.MeasureText(etiqueta);
            if (fuente.Size < dpi * 0.04f || ancho > rect.Width - 2) continue;
            lienzo.DrawText(etiqueta, rect.MidX - ancho / 2, rect.MidY + fuente.Size * 0.35f, SKTextAlign.Left, fuente, tinta);
        }
        lienzo.Flush();
    }

    private static void AgregarPagina(PdfDocument pdf, SKBitmap imagen, IReadOnlyList<PalabraOcr> palabras,
        double anchoPt, double altoPt, int calidad)
    {
        var pagina = pdf.AddPage();
        pagina.Width = XUnit.FromPoint(anchoPt);
        pagina.Height = XUnit.FromPoint(altoPt);

        using var jpeg = Codificar(imagen, calidad);
        using var flujo = new MemoryStream(jpeg.ToArray());
        using var gfx = XGraphics.FromPdfPage(pagina);
        using (var ximg = XImage.FromStream(flujo))
            gfx.DrawImage(ximg, 0, 0, anchoPt, altoPt);

        if (palabras.Count == 0) return;
        var escalaX = anchoPt / imagen.Width;
        var escalaY = altoPt / imagen.Height;
        var invisible = new XSolidBrush(XColor.FromArgb(0, 0, 0, 0));
        foreach (var palabra in palabras)
        {
            var ancho = palabra.Ancho * escalaX;
            var alto = palabra.Alto * escalaY;
            if (ancho <= 0 || alto <= 0) continue;
            var fuente = new XFont("Arcano", Math.Max(1, alto * 0.9));
            var medida = gfx.MeasureString(palabra.Texto, fuente).Width;
            if (medida <= 0) continue;
            var estado = gfx.Save();
            gfx.TranslateTransform(palabra.X * escalaX, palabra.Y * escalaY + alto * 0.8);
            gfx.ScaleTransform(ancho / medida, 1);
            gfx.DrawString(palabra.Texto, fuente, invisible, 0, 0, XStringFormats.BaseLineLeft);
            gfx.Restore(estado);
        }
    }

    /// <summary>JPEG en escala de grises si la página no tiene color; reduce mucho el tamaño.</summary>
    private static SKData Codificar(SKBitmap imagen, int calidad)
    {
        if (!EsGris(imagen)) return imagen.Encode(SKEncodedImageFormat.Jpeg, calidad);
        using var gris = new SKBitmap(new SKImageInfo(imagen.Width, imagen.Height, SKColorType.Gray8, SKAlphaType.Opaque));
        using (var lienzo = new SKCanvas(gris))
            lienzo.DrawBitmap(imagen, 0, 0);
        return gris.Encode(SKEncodedImageFormat.Jpeg, calidad);
    }

    private static bool EsGris(SKBitmap imagen)
    {
        var paso = Math.Max(1, Math.Min(imagen.Width, imagen.Height) / 400);
        for (var y = 0; y < imagen.Height; y += paso)
        for (var x = 0; x < imagen.Width; x += paso)
        {
            var c = imagen.GetPixel(x, y);
            if (Math.Abs(c.Red - c.Green) > 16 || Math.Abs(c.Green - c.Blue) > 16 || Math.Abs(c.Red - c.Blue) > 16)
                return false;
        }
        return true;
    }
}
