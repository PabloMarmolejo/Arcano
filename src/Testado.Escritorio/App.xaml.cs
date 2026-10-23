using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace Testado.Escritorio;

public partial class App : Application
{
    /// <summary>Carpeta de datos del usuario: catálogo, preferencias, bitácora y caché de WebView2.</summary>
    public static string CarpetaDatos { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Testado");

    /// <summary>Archivos de trabajo de la sesión (documentos de Word convertidos). Se borran al salir.</summary>
    public static string CarpetaTrabajo { get; } =
        Path.Combine(Path.GetTempPath(), "Testado", Environment.ProcessId.ToString());

    /// <summary>Documento pasado por línea de comandos («Abrir con…»); se abre al iniciar.</summary>
    public static string? ArchivoInicial { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        if (e.Args.Length > 0 && File.Exists(e.Args[0])) ArchivoInicial = Path.GetFullPath(e.Args[0]);
        base.OnStartup(e);
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
        MessageBox.Show($"Ocurrió un error inesperado:\n\n{e.Exception.Message}", "Testado",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
