using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SkiaSharp;
using Arcano.Motor.Generacion;

namespace Arcano.Motor.Pruebas;

/// <summary>PDF de prueba armados al vuelo: página digital, página rotada y página escaneada.</summary>
internal static class Muestras
{
    public const string Nombre = "Juan Pérez López";
    public const string Curp = "PELJ800101HDFRPN09";

    public static readonly string[] Parrafo =
    [
        "En la ciudad de México, el C. Juan Pérez López, con CURP PELJ800101HDFRPN09",
        "y RFC PELJ800101AB1, señaló el teléfono 55 1234 5678 y el correo juan.perez@gmail.com,",
        "con domicilio en calle Hidalgo No. 5, colonia Centro, C.P. 06000.",
        "La cuenta CLABE 002180700512345675 y la tarjeta 4111 1111 1111 1111 son de prueba.",
        "Comuníquese a la oficialía: oficialia@tribunal.gob.mx. Magistrada ponente.",
    ];

    public static byte[] Documento()
    {
        ResolutorFuentes.Instalar();
        using var pdf = new PdfDocument();
        pdf.Info.Author = "Autor original";
        pdf.Info.Title = "Sentencia con datos";

        // Página 1: digital.
        var p1 = pdf.AddPage();
        p1.Width = XUnit.FromPoint(612);
        p1.Height = XUnit.FromPoint(792);
        using (var gfx = XGraphics.FromPdfPage(p1))
        {
            var fuente = new XFont("Arcano", 11);
            for (var i = 0; i < Parrafo.Length; i++)
                gfx.DrawString(Parrafo[i], fuente, XBrushes.Black, 60, 100 + i * 18, XStringFormats.BaseLineLeft);
        }

        // Página 2: rotada 90°, vuelve a mencionar el nombre.
        var p2 = pdf.AddPage();
        p2.Width = XUnit.FromPoint(612);
        p2.Height = XUnit.FromPoint(792);
        p2.Rotate = 90;
        using (var gfx = XGraphics.FromPdfPage(p2))
        {
            gfx.DrawString("Notifíquese personalmente a Juan Pérez López en su domicilio.",
                new XFont("Arcano", 12), XBrushes.Black, 60, 120, XStringFormats.BaseLineLeft);
        }

        // Página 3: escaneada (solo imagen).
        var p3 = pdf.AddPage();
        p3.Width = XUnit.FromPoint(612);
        p3.Height = XUnit.FromPoint(792);
        using (var gfx = XGraphics.FromPdfPage(p3))
        using (var flujo = new MemoryStream(ImagenEscaneo()))
        using (var img = XImage.FromStream(flujo))
            gfx.DrawImage(img, 0, 0, 612, 792);

        using var salida = new MemoryStream();
        pdf.Save(salida);
        return salida.ToArray();
    }

    private static byte[] ImagenEscaneo()
    {
        using var bmp = new SKBitmap(2550, 3300);
        using var lienzo = new SKCanvas(bmp);
        lienzo.Clear(SKColors.White);
        using var fuente = new SKFont(SKTypeface.FromFamilyName("Arial"), 64);
        using var tinta = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        lienzo.DrawText("ESCRITO DE PRESENTACION", 250, 500, SKTextAlign.Left, fuente, tinta);
        lienzo.DrawText("Quien suscribe, Juan Perez Lopez, promuevo", 250, 700, SKTextAlign.Left, fuente, tinta);
        lienzo.DrawText("juicio en contra del acto impugnado.", 250, 800, SKTextAlign.Left, fuente, tinta);
        lienzo.Flush();
        using var datos = bmp.Encode(SKEncodedImageFormat.Png, 100);
        return datos.ToArray();
    }

    public static string CarpetaTemporal()
    {
        var ruta = Path.Combine(Path.GetTempPath(), "arcano-pruebas", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ruta);
        return ruta;
    }
}
