using PdfSharp.Fonts;

namespace Arcano.Motor.Generacion;

/// <summary>
/// Fuentes para PDFsharp: una sans serif del sistema (Arial en Windows,
/// Liberation o DejaVu en Linux). Se usa en la capa de texto y en la leyenda.
/// </summary>
internal sealed class ResolutorFuentes : IFontResolver
{
    private static readonly Lock Candado = new();
    private static bool _instalado;

    private static readonly string[] Regular =
    [
        @"C:\Windows\Fonts\arial.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
    ];

    private static readonly string[] Negrita =
    [
        @"C:\Windows\Fonts\arialbd.ttf",
        "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
    ];

    public static void Instalar()
    {
        lock (Candado)
        {
            if (_instalado) return;
            GlobalFontSettings.FontResolver = new ResolutorFuentes();
            _instalado = true;
        }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(bold ? "arcano-negrita" : "arcano-regular");

    public byte[]? GetFont(string faceName)
    {
        var candidatas = faceName == "arcano-negrita" ? Negrita : Regular;
        var ruta = candidatas.FirstOrDefault(File.Exists)
                   ?? throw new FileNotFoundException("No se encontró una fuente sans serif en el sistema (Arial, Liberation Sans o DejaVu Sans).");
        return File.ReadAllBytes(ruta);
    }
}
