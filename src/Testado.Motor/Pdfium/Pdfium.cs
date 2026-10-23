namespace Testado.Motor.Pdfium;

/// <summary>Inicialización única de PDFium y candado global de acceso.</summary>
internal static class Pdfium
{
    public static readonly Lock Candado = new();
    private static bool _iniciado;

    public static void Asegurar()
    {
        lock (Candado)
        {
            if (_iniciado) return;
            PdfiumNativo.FPDF_InitLibrary();
            _iniciado = true;
        }
    }
}
