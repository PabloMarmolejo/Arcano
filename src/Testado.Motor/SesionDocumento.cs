using System.Collections.Concurrent;
using System.Security.Cryptography;
using Testado.Motor.Conversion;
using Testado.Motor.Deteccion;
using Testado.Motor.Modelo;
using Testado.Motor.Ocr;
using Testado.Motor.Pdfium;
using Testado.Motor.Texto;

namespace Testado.Motor;

/// <summary>Avance de una tarea larga (OCR, generación).</summary>
public sealed record Progreso(string Etapa, int Actual, int Total, string? Detalle = null);

/// <summary>
/// Documento abierto para testar: el PDF de trabajo (convertido si venía de
/// Word), sus páginas y el texto de cada una, propio o reconocido por OCR.
/// </summary>
public sealed class SesionDocumento : IDisposable
{
    /// <summary>Resolución a la que se hace el OCR de páginas escaneadas.</summary>
    public const int DpiOcr = 300;

    private readonly ConcurrentDictionary<int, TextoPagina> _textos = new();
    private readonly IReconocedorTexto? _ocr;

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Nombre { get; }
    public string RutaOrigen { get; }
    /// <summary>SHA-256 del archivo de origen; liga el proyecto guardado con su documento.</summary>
    public string Sha256 { get; }
    public byte[] Pdf { get; }
    public DocumentoPdf Documento { get; }
    public IReadOnlyList<string> Advertencias { get; }
    public IReadOnlyList<InfoPagina> Paginas => Enumerable.Range(0, Documento.Paginas).Select(InfoDe).ToList();

    private readonly (double Ancho, double Alto)[] _tamanos;

    private SesionDocumento(string rutaOrigen, string sha256, byte[] pdf, IReconocedorTexto? ocr, IReadOnlyList<string> advertencias)
    {
        RutaOrigen = rutaOrigen;
        Nombre = Path.GetFileName(rutaOrigen);
        Sha256 = sha256;
        Pdf = pdf;
        _ocr = ocr;
        Advertencias = advertencias;
        Documento = DocumentoPdf.Abrir(pdf);
        _tamanos = Enumerable.Range(0, Documento.Paginas).Select(Documento.TamanoPagina).ToArray();
    }

    public static SesionDocumento Abrir(string ruta, IReconocedorTexto? ocr, IConvertidorDocumentos? convertidor, string carpetaTrabajo)
    {
        var original = File.ReadAllBytes(ruta);
        var sha = Convert.ToHexStringLower(SHA256.HashData(original));
        if (Path.GetExtension(ruta).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return new SesionDocumento(ruta, sha, original, ocr, []);

        if (convertidor is null || !convertidor.Admite(ruta))
            throw new NotSupportedException("Solo se pueden abrir archivos PDF o documentos de Word (.docx, .doc, .rtf, .odt).");
        var conversion = convertidor.ConvertirAPdf(ruta, Path.Combine(carpetaTrabajo, Guid.NewGuid().ToString("N")));
        return new SesionDocumento(ruta, sha, File.ReadAllBytes(conversion.RutaPdf), ocr, conversion.Advertencias);
    }

    public static SesionDocumento DesdeBytes(string nombre, byte[] pdf, IReconocedorTexto? ocr) =>
        new(nombre, Convert.ToHexStringLower(SHA256.HashData(pdf)), pdf, ocr, []);

    public (double Ancho, double Alto) Tamano(int pagina) => _tamanos[pagina];

    private InfoPagina InfoDe(int pagina) => new(pagina, _tamanos[pagina].Ancho, _tamanos[pagina].Alto, Texto(pagina).Origen);

    public bool HayOcr => _ocr is not null;

    /// <summary>Texto de la página: el propio del PDF o, si ya se reconoció, el del OCR.</summary>
    public TextoPagina Texto(int pagina) => _textos.GetOrAdd(pagina, p =>
    {
        var texto = Documento.ExtraerTexto(p);
        return texto.CaracteresConCaja < 3 ? TextoPagina.Vacio(p, OrigenTexto.SinTexto) : texto;
    });

    public IReadOnlyList<int> PaginasSinTexto() =>
        Enumerable.Range(0, Documento.Paginas).Where(p => Texto(p).Origen == OrigenTexto.SinTexto).ToList();

    /// <summary>
    /// Reconoce el texto de las páginas indicadas. En páginas que ya tienen texto
    /// propio solo se agregan las palabras que caen fuera de él (por ejemplo, un
    /// anexo escaneado pegado como imagen).
    /// </summary>
    public void Reconocer(IEnumerable<int> paginas, IProgress<Progreso>? progreso = null, CancellationToken ct = default)
    {
        if (_ocr is null) throw new InvalidOperationException("El OCR no está disponible.");
        var lista = paginas.Distinct().Order().ToList();
        for (var i = 0; i < lista.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var p = lista[i];
            progreso?.Report(new Progreso("ocr", i, lista.Count, $"Página {p + 1}"));
            using var imagen = Documento.Renderizar(p, DpiOcr);
            var palabras = _ocr.Reconocer(imagen, DpiOcr);
            var ocr = ReconocedorTesseract.ATexto(p, palabras, imagen.Width, imagen.Height);
            var actual = Texto(p);
            _textos[p] = actual.Origen == OrigenTexto.Nativo ? Combinar(actual, ocr) : ocr;
        }
        progreso?.Report(new Progreso("ocr", lista.Count, lista.Count));
    }

    private static TextoPagina Combinar(TextoPagina nativo, TextoPagina ocr)
    {
        var cajasNativas = nativo.Caracteres.Where(c => c.Caja is not null).Select(c => c.Caja!.Value).ToList();
        var caracteres = nativo.Caracteres.ToList();
        foreach (var palabra in ocr.Palabras())
        {
            if (cajasNativas.Any(c => c.Intersecta(palabra.Caja))) continue;
            caracteres.Add(new Caracter("\n", null));
            caracteres.AddRange(ocr.Caracteres.Skip(palabra.Inicio).Take(palabra.Fin - palabra.Inicio));
        }
        return new TextoPagina(nativo.Pagina, OrigenTexto.Nativo, caracteres);
    }

    public IReadOnlyList<Palabra> Palabras(int pagina) => Texto(pagina).Palabras();

    public IEnumerable<Coincidencia> Buscar(string termino, bool palabraCompleta, string? categoriaId) =>
        Enumerable.Range(0, Documento.Paginas).SelectMany(p => Buscador.Buscar(Texto(p), termino, palabraCompleta, categoriaId));

    public IEnumerable<Coincidencia> Detectar(IEnumerable<string>? detectores, IReadOnlyCollection<string>? dominiosInstitucionales = null)
    {
        var ids = detectores?.ToList();
        return Enumerable.Range(0, Documento.Paginas).SelectMany(p => Detectores.Ejecutar(Texto(p), ids, dominiosInstitucionales));
    }

    public IEnumerable<ImagenPagina> Imagenes() =>
        Enumerable.Range(0, Documento.Paginas).SelectMany(Documento.ExtraerImagenes);

    /// <summary>Texto que queda debajo de una caja (para guardar en la región).</summary>
    public string TextoBajo(int pagina, RectN caja)
    {
        var texto = Texto(pagina);
        var dentro = texto.Caracteres
            .Select((c, i) => (c, i))
            .Where(x => x.c.Caja is { } cc && cc.AreaInterseccion(caja) >= cc.Area * 0.5)
            .Select(x => x.i)
            .ToList();
        return dentro.Count == 0 ? "" : texto.Extraer(dentro.Min(), dentro.Max() + 1);
    }

    /// <summary>
    /// Apariciones, en cualquier página, de lo que ya se testó en alguna región
    /// y que no quedaron cubiertas por otra región. Es el error más común: testar
    /// un nombre en la primera hoja y olvidarlo en la quinta.
    /// </summary>
    public IReadOnlyList<Coincidencia> RepeticionesSinTestar(IReadOnlyList<Region> regiones)
    {
        // Cada término conserva la categoría con la que se testó la primera vez.
        var terminos = regiones
            .Where(r => !string.IsNullOrWhiteSpace(r.Texto))
            .Select(r => (Termino: TextoPagina.NormalizarTermino(r.Texto!), r.CategoriaId))
            .Where(x => x.Termino.Count(char.IsLetterOrDigit) >= 4)
            .DistinctBy(x => x.Termino)
            .ToList();

        var resultado = new List<Coincidencia>();
        foreach (var (termino, categoria) in terminos)
        {
            foreach (var c in Buscar(termino, palabraCompleta: true, categoriaId: categoria))
            {
                if (Cubierta(c, regiones)) continue;
                resultado.Add(c with { Origen = "repeticion", Sugerida = true });
            }
        }
        return resultado
            .GroupBy(c => (c.Pagina, c.Cajas[0]))
            .Select(g => g.First())
            .OrderBy(c => c.Pagina).ThenBy(c => c.Cajas[0].Y)
            .ToList();
    }

    public static bool Cubierta(Coincidencia c, IReadOnlyList<Region> regiones) =>
        c.Cajas.All(caja => regiones
            .Where(r => r.Pagina == c.Pagina)
            .Sum(r => r.Caja.AreaInterseccion(caja)) >= caja.Area * 0.6);

    public void Dispose() => Documento.Dispose();
}
