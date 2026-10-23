using SkiaSharp;
using Tesseract;
using Arcano.Motor.Modelo;
using Arcano.Motor.Texto;

namespace Arcano.Motor.Ocr;

/// <summary>Palabra reconocida en una imagen, en píxeles.</summary>
public sealed record PalabraOcr(string Texto, int X, int Y, int Ancho, int Alto, bool IniciaRenglon);

public interface IReconocedorTexto
{
    IReadOnlyList<PalabraOcr> Reconocer(SKBitmap imagen, int dpi);
}

/// <summary>
/// OCR con Tesseract y el modelo en español. El motor no es seguro entre
/// hilos, así que se reutiliza una sola instancia bajo candado.
/// </summary>
public sealed class ReconocedorTesseract : IReconocedorTexto, IDisposable
{
    private readonly Lock _candado = new();
    private readonly TesseractEngine _motor;

    public ReconocedorTesseract(string? rutaTessdata = null, string idioma = "spa")
    {
        var ruta = rutaTessdata ?? Path.Combine(AppContext.BaseDirectory, "tessdata");
        if (!File.Exists(Path.Combine(ruta, idioma + ".traineddata")))
            throw new FileNotFoundException($"Falta el modelo de OCR «{idioma}.traineddata» en {ruta}.");
        _motor = new TesseractEngine(ruta, idioma, EngineMode.LstmOnly);
        // Conserva los espacios entre palabras tal como están en la hoja.
        _motor.SetVariable("preserve_interword_spaces", "1");
    }

    public IReadOnlyList<PalabraOcr> Reconocer(SKBitmap imagen, int dpi)
    {
        using var datos = imagen.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = datos.ToArray();
        lock (_candado)
        {
            _motor.SetVariable("user_defined_dpi", dpi.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var pix = Pix.LoadFromMemory(bytes);
            using var pagina = _motor.Process(pix, PageSegMode.Auto);
            var palabras = new List<PalabraOcr>();
            using var it = pagina.GetIterator();
            it.Begin();
            do
            {
                var texto = it.GetText(PageIteratorLevel.Word);
                if (string.IsNullOrWhiteSpace(texto)) continue;
                if (!it.TryGetBoundingBox(PageIteratorLevel.Word, out var caja)) continue;
                var inicia = it.IsAtBeginningOf(PageIteratorLevel.TextLine);
                palabras.Add(new PalabraOcr(texto.Trim(), caja.X1, caja.Y1, caja.Width, caja.Height, inicia));
            } while (it.Next(PageIteratorLevel.Word));
            return palabras;
        }
    }

    /// <summary>
    /// Arma un <see cref="TextoPagina"/> a partir de palabras OCR. Cada letra recibe
    /// una parte proporcional de la caja de su palabra.
    /// </summary>
    public static TextoPagina ATexto(int pagina, IReadOnlyList<PalabraOcr> palabras, int anchoPx, int altoPx)
    {
        var caracteres = new List<Caracter>();
        foreach (var p in palabras)
        {
            if (caracteres.Count > 0)
                caracteres.Add(new Caracter(p.IniciaRenglon ? "\n" : " ", null));
            var paso = (double)p.Ancho / Math.Max(1, p.Texto.Length);
            for (var i = 0; i < p.Texto.Length; i++)
            {
                var caja = new RectN(
                    (p.X + paso * i) / anchoPx,
                    (double)p.Y / altoPx,
                    paso / anchoPx,
                    (double)p.Alto / altoPx);
                caracteres.Add(new Caracter(p.Texto[i].ToString(), caja, Aproximada: true));
            }
        }
        return new TextoPagina(pagina, OrigenTexto.Ocr, caracteres);
    }

    public void Dispose() => _motor.Dispose();
}
