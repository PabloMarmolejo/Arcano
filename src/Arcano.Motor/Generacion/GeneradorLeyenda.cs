using PdfSharp.Drawing;
using PdfSharp.Pdf;
using Arcano.Motor.Catalogo;
using Arcano.Motor.Modelo;

namespace Arcano.Motor.Generacion;

/// <summary>
/// Hoja final con la leyenda de clasificación: área, documento, qué se testó
/// (por categoría, con números de testado y páginas), motivación y fundamento.
/// </summary>
internal static class GeneradorLeyenda
{
    private const double Margen = 72;
    private const double AnchoCarta = 612;
    private const double AltoCarta = 792;

    public static void Agregar(PdfDocument pdf, SesionDocumento sesion, IReadOnlyList<Region> regiones,
        Dictionary<string, int> numeros, CatalogoDatos catalogo, DatosLeyenda datos)
    {
        var redactor = new Redactor(pdf);
        redactor.Titulo("LEYENDA DE CLASIFICACIÓN");
        redactor.Titulo("VERSIÓN PÚBLICA");
        redactor.Espacio(10);

        redactor.Campo("Documento", string.IsNullOrWhiteSpace(datos.Documento) ? Path.GetFileNameWithoutExtension(sesion.Nombre) : datos.Documento);
        redactor.Campo("Fecha de elaboración", DateTime.Now.ToString("d 'de' MMMM 'de' yyyy", new System.Globalization.CultureInfo("es-MX")));
        redactor.Campo("Páginas del documento", sesion.Documento.Paginas.ToString());
        redactor.Espacio(8);

        if (regiones.Count == 0)
        {
            redactor.Parrafo("No se testó información en este documento.");
        }
        else
        {
            redactor.Subtitulo("Partes o secciones clasificadas");
            var porCategoria = regiones
                .GroupBy(r => r.CategoriaId ?? "otro")
                .Select(g => (Categoria: catalogo.Buscar(g.Key) ?? catalogo.Buscar("otro") ?? new Categoria { Id = g.Key, Nombre = g.Key }, Regiones: g.ToList()))
                .OrderBy(x => x.Regiones.Min(r => numeros.GetValueOrDefault(r.Id)));

            foreach (var (categoria, lista) in porCategoria)
            {
                var nums = lista.Select(r => numeros.GetValueOrDefault(r.Id)).Distinct().Order().ToList();
                var paginas = lista.Select(r => r.Pagina + 1).Distinct().Order().ToList();
                var tipo = categoria.Tipo == "reservada" ? "información reservada" : "información confidencial";
                redactor.Parrafo($"{categoria.Nombre} ({tipo}).", negrita: true);
                redactor.Parrafo($"Testados: {Rangos(nums)}. Páginas: {Rangos(paginas)}.", sangria: 14);
                if (!string.IsNullOrWhiteSpace(categoria.Motivacion))
                    redactor.Parrafo($"Motivación: se testa {categoria.Motivacion}.", sangria: 14);
                redactor.Parrafo($"Fundamento: {catalogo.FundamentoDe(categoria)}", sangria: 14);
                redactor.Espacio(6);
            }
        }

        if (!string.IsNullOrWhiteSpace(datos.TextoAdicional))
        {
            redactor.Espacio(6);
            redactor.Parrafo(datos.TextoAdicional);
        }

        redactor.Cerrar();
    }

    /// <summary>1, 2, 3, 5, 7, 8 → «1 a 3, 5, 7 y 8».</summary>
    internal static string Rangos(IReadOnlyList<int> valores)
    {
        var partes = new List<string>();
        for (var i = 0; i < valores.Count;)
        {
            var j = i;
            while (j + 1 < valores.Count && valores[j + 1] == valores[j] + 1) j++;
            if (j - i >= 2)
            {
                partes.Add($"{valores[i]} a {valores[j]}");
                i = j + 1;
            }
            else
            {
                partes.Add(valores[i].ToString());
                i++;
            }
        }
        return partes.Count <= 1 ? string.Join("", partes) : string.Join(", ", partes[..^1]) + " y " + partes[^1];
    }

    /// <summary>Escritura de párrafos con ajuste de renglón y salto de página.</summary>
    private sealed class Redactor
    {
        private readonly PdfDocument _pdf;
        private XGraphics? _gfx;
        private double _y;
        private readonly XFont _normal = new("Arcano", 10.5);
        private readonly XFont _negrita = new("Arcano", 10.5, XFontStyleEx.Bold);
        private readonly XFont _titulo = new("Arcano", 14, XFontStyleEx.Bold);
        private readonly XFont _subtitulo = new("Arcano", 11.5, XFontStyleEx.Bold);

        public Redactor(PdfDocument pdf)
        {
            _pdf = pdf;
            NuevaPagina();
        }

        private XGraphics Gfx => _gfx!;

        private void NuevaPagina()
        {
            _gfx?.Dispose();
            var pagina = _pdf.AddPage();
            pagina.Width = XUnit.FromPoint(AnchoCarta);
            pagina.Height = XUnit.FromPoint(AltoCarta);
            _gfx = XGraphics.FromPdfPage(pagina);
            _y = Margen;
        }

        private void Asegurar(double alto)
        {
            if (_y + alto > AltoCarta - Margen) NuevaPagina();
        }

        public void Espacio(double alto) => _y += alto;

        public void Titulo(string texto)
        {
            Asegurar(20);
            var ancho = Gfx.MeasureString(texto, _titulo).Width;
            Gfx.DrawString(texto, _titulo, XBrushes.Black, (AnchoCarta - ancho) / 2, _y + 14, XStringFormats.BaseLineLeft);
            _y += 20;
        }

        public void Subtitulo(string texto)
        {
            Asegurar(22);
            Gfx.DrawString(texto, _subtitulo, XBrushes.Black, Margen, _y + 12, XStringFormats.BaseLineLeft);
            _y += 20;
        }

        public void Campo(string etiqueta, string valor)
        {
            var prefijo = etiqueta + ": ";
            var anchoPrefijo = Gfx.MeasureString(prefijo, _negrita).Width;
            Asegurar(14);
            Gfx.DrawString(prefijo, _negrita, XBrushes.Black, Margen, _y + 10.5, XStringFormats.BaseLineLeft);
            Escribir(valor, _normal, Margen + anchoPrefijo, Margen);
        }

        public void Parrafo(string texto, bool negrita = false, double sangria = 0) =>
            Escribir(texto, negrita ? _negrita : _normal, Margen + sangria, Margen + sangria);

        /// <summary>Escribe texto ajustado; el primer renglón puede empezar más a la derecha.</summary>
        private void Escribir(string texto, XFont fuente, double xPrimero, double xResto)
        {
            const double interlineado = 14;
            var derecha = AnchoCarta - Margen;
            var x0 = xPrimero;
            var renglon = "";
            foreach (var palabra in texto.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var prueba = renglon.Length == 0 ? palabra : renglon + " " + palabra;
                if (renglon.Length > 0 && x0 + Gfx.MeasureString(prueba, fuente).Width > derecha)
                {
                    Asegurar(interlineado);
                    Gfx.DrawString(renglon, fuente, XBrushes.Black, x0, _y + 10.5, XStringFormats.BaseLineLeft);
                    _y += interlineado;
                    x0 = xResto;
                    renglon = palabra;
                }
                else
                {
                    renglon = prueba;
                }
            }
            Asegurar(interlineado);
            Gfx.DrawString(renglon, fuente, XBrushes.Black, x0, _y + 10.5, XStringFormats.BaseLineLeft);
            _y += interlineado;
        }

        public void Cerrar() => _gfx?.Dispose();
    }
}
