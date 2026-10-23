using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Arcano.Motor.Conversion;

public sealed record ResultadoConversion(string RutaPdf, IReadOnlyList<string> Advertencias);

public interface IConvertidorDocumentos
{
    bool Admite(string ruta);
    ResultadoConversion ConvertirAPdf(string ruta, string carpetaDestino);
}

/// <summary>
/// Convierte documentos de Word a PDF. Usa Microsoft Word por automatización
/// si está instalado y, si no, LibreOffice en modo sin interfaz. El PDF que
/// resulta es la versión final: sin marcas de revisión ni comentarios.
/// </summary>
public sealed class ConvertidorDocumentos : IConvertidorDocumentos
{
    private static readonly string[] Extensiones = [".docx", ".doc", ".rtf", ".odt"];

    public bool Admite(string ruta) => Extensiones.Contains(Path.GetExtension(ruta).ToLowerInvariant());

    public ResultadoConversion ConvertirAPdf(string ruta, string carpetaDestino)
    {
        Directory.CreateDirectory(carpetaDestino);
        var destino = Path.Combine(carpetaDestino, Path.GetFileNameWithoutExtension(ruta) + ".pdf");

        if (OperatingSystem.IsWindows() && Type.GetTypeFromProgID("Word.Application") is not null)
            return ConWord(ruta, destino);

        var soffice = BuscarLibreOffice()
                      ?? throw new InvalidOperationException(
                          "Para abrir documentos de Word se necesita Microsoft Word o LibreOffice instalado. Guarde el documento como PDF y ábralo de nuevo.");
        return ConLibreOffice(soffice, ruta, carpetaDestino, destino);
    }

    [SupportedOSPlatform("windows")]
    private static ResultadoConversion ConWord(string ruta, string destino)
    {
        ResultadoConversion? resultado = null;
        Exception? error = null;
        // La automatización de Office se hace en un hilo STA propio.
        var hilo = new Thread(() =>
        {
            try { resultado = ConWordSta(ruta, destino); }
            catch (Exception ex) { error = ex; }
        });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        hilo.Join();
        if (error is not null)
            throw new InvalidOperationException($"Word no pudo convertir el documento: {error.Message}", error);
        return resultado!;
    }

    [SupportedOSPlatform("windows")]
    private static ResultadoConversion ConWordSta(string ruta, string destino)
    {
        const int formatoPdf = 17;          // wdExportFormatPDF
        const int optimizarImpresion = 0;   // wdExportOptimizeForPrint
        const int todoElDocumento = 0;      // wdExportAllDocument
        const int soloContenido = 0;        // wdExportDocumentContent (sin comentarios ni marcas)
        const int sinMarcadores = 0;        // wdExportCreateNoBookmarks
        const int sinGuardar = 0;           // wdDoNotSaveChanges

        var advertencias = new List<string>();
        dynamic? word = null;
        dynamic? doc = null;
        try
        {
            word = Activator.CreateInstance(Type.GetTypeFromProgID("Word.Application")!)!;
            word.Visible = false;
            word.DisplayAlerts = 0;
            doc = word.Documents.Open(Path.GetFullPath(ruta), false, true, false);

            int revisiones = doc.Revisions.Count;
            int comentarios = doc.Comments.Count;
            if (revisiones > 0)
                advertencias.Add($"El documento tiene {revisiones} cambio(s) con control de cambios sin aceptar. Se tomó la versión final, como si se hubieran aceptado.");
            if (comentarios > 0)
                advertencias.Add($"El documento tiene {comentarios} comentario(s). No se incluyen en la versión pública.");
            try
            {
                doc.ActiveWindow.View.RevisionsFilter.Markup = 0; // wdRevisionsMarkupNone
                doc.ActiveWindow.View.RevisionsFilter.View = 0;   // wdRevisionsViewFinal
            }
            catch (COMException)
            {
                // Versiones antiguas de Word no exponen RevisionsFilter; el formato de exportación ya omite marcas.
            }

            doc.ExportAsFixedFormat(destino, formatoPdf, false, optimizarImpresion, todoElDocumento, 1, 1,
                soloContenido, false, true, sinMarcadores, false, true, false);
            return new ResultadoConversion(destino, advertencias);
        }
        finally
        {
            if (doc is not null)
            {
                try { doc.Close(sinGuardar); } catch (COMException) { }
                Marshal.FinalReleaseComObject(doc);
            }
            if (word is not null)
            {
                try { word.Quit(sinGuardar); } catch (COMException) { }
                Marshal.FinalReleaseComObject(word);
            }
        }
    }

    private static ResultadoConversion ConLibreOffice(string soffice, string ruta, string carpeta, string destino)
    {
        var inicio = new ProcessStartInfo(soffice)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        foreach (var arg in new[] { "--headless", "--norestore", "--convert-to", "pdf", "--outdir", carpeta, Path.GetFullPath(ruta) })
            inicio.ArgumentList.Add(arg);

        using var proceso = Process.Start(inicio) ?? throw new InvalidOperationException("No se pudo iniciar LibreOffice.");
        if (!proceso.WaitForExit(TimeSpan.FromMinutes(3)))
        {
            proceso.Kill(true);
            throw new TimeoutException("LibreOffice tardó demasiado en convertir el documento.");
        }
        if (!File.Exists(destino))
            throw new InvalidOperationException($"LibreOffice no generó el PDF: {proceso.StandardError.ReadToEnd()}");
        return new ResultadoConversion(destino,
            ["Convertido con LibreOffice: revise que el formato coincida con el original y que no haya cambios sin aceptar."]);
    }

    private static string? BuscarLibreOffice()
    {
        string[] candidatas =
        [
            @"C:\Program Files\LibreOffice\program\soffice.exe",
            @"C:\Program Files (x86)\LibreOffice\program\soffice.exe",
            "/usr/bin/soffice",
            "/usr/bin/libreoffice",
        ];
        return candidatas.FirstOrDefault(File.Exists);
    }
}
