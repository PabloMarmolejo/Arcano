using System.IO;
using System.Windows;
using System.Windows.Threading;
using Arcano.Motor.Proyecto;

namespace Arcano.Escritorio;

public partial class App : Application
{
    /// <summary>Carpeta de datos del usuario: catálogo, preferencias, bitácora y caché de WebView2.</summary>
    public static string CarpetaDatos { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Arcano");

    /// <summary>Archivos de trabajo de la sesión (documentos de Word convertidos). Se borran al salir.</summary>
    public static string CarpetaTrabajo { get; } =
        Path.Combine(Path.GetTempPath(), "Arcano", Environment.ProcessId.ToString());

    /// <summary>Documento pasado por línea de comandos («Abrir con…»); se abre al iniciar.</summary>
    public static string? ArchivoInicial { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        MigrarDatosDeTestado();
        if (e.Args.Length > 0 && File.Exists(e.Args[0]))
        {
            // Un trabajo «documento.pdf.arcano» abre su documento, que a su vez carga el trabajo.
            var ruta = Path.GetFullPath(e.Args[0]);
            ruta = ProyectoArcano.DocumentoDe(ruta) ?? ruta;
            if (File.Exists(ruta)) ArchivoInicial = ruta;
        }
        base.OnStartup(e);
    }

    /// <summary>
    /// El programa se llamó Testado hasta la versión 0.1: se copian el catálogo
    /// (con los fundamentos capturados) y las preferencias a la carpeta nueva.
    /// </summary>
    private static void MigrarDatosDeTestado()
    {
        var anterior = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Testado");
        foreach (var archivo in new[] { "catalogo.json", "preferencias.json" })
        {
            var origen = Path.Combine(anterior, archivo);
            var destino = Path.Combine(CarpetaDatos, archivo);
            if (!File.Exists(origen) || File.Exists(destino)) continue;
            try
            {
                Directory.CreateDirectory(CarpetaDatos);
                File.Copy(origen, destino);
            }
            catch (IOException ex) { Bitacora.Error("migración", ex); }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { if (Directory.Exists(CarpetaTrabajo)) Directory.Delete(CarpetaTrabajo, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        base.OnExit(e);
    }

    private void ErrorNoControlado(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Bitacora.Error("No controlado", e.Exception);
        MessageBox.Show($"Ocurrió un error inesperado:\n\n{e.Exception.Message}", "Arcano",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
