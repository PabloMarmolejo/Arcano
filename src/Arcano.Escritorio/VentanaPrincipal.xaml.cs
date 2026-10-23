using System.ComponentModel;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace Arcano.Escritorio;

/// <summary>
/// Ventana que hospeda la interfaz web. Todo se sirve desde el origen
/// https://app.arcano, que no sale a la red: los archivos de la interfaz se
/// leen de wwwroot y los documentos abiertos se entregan desde memoria.
/// </summary>
public partial class VentanaPrincipal : Window
{
    public const string Origen = "https://app.arcano";

    private readonly Puente _puente;
    private readonly string _carpetaUi = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    private readonly string? _uiDesarrollo = Environment.GetEnvironmentVariable("ARCANO_UI");

    public VentanaPrincipal()
    {
        InitializeComponent();
        _puente = new Puente(this);
        Loaded += async (_, _) => await IniciarAsync();
    }

    public CoreWebView2 Web => Vista.CoreWebView2;

    private async Task IniciarAsync()
    {
        try
        {
            var entorno = await CoreWebView2Environment.CreateAsync(null, Path.Combine(App.CarpetaDatos, "WebView2"));
            await Vista.EnsureCoreWebView2Async(entorno);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            MessageBox.Show("Este equipo no tiene instalado Microsoft Edge WebView2 Runtime, necesario para la interfaz.\n" +
                            "Pida a Informática que lo instale y vuelva a abrir Arcano.", "Arcano",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
            return;
        }

        var ajustes = Web.Settings;
        var desarrollo = _uiDesarrollo is not null;
        ajustes.AreDevToolsEnabled = desarrollo;
        ajustes.AreDefaultContextMenusEnabled = desarrollo;
        ajustes.IsStatusBarEnabled = false;
        ajustes.IsZoomControlEnabled = false;
        ajustes.AreBrowserAcceleratorKeysEnabled = desarrollo;
        ajustes.IsPasswordAutosaveEnabled = false;
        ajustes.IsGeneralAutofillEnabled = false;

        Web.AddWebResourceRequestedFilter(Origen + "/*", CoreWebView2WebResourceContext.All);
        Web.WebResourceRequested += ServirRecurso;
        Web.WebMessageReceived += (_, e) => _ = _puente.RecibirAsync(e.WebMessageAsJson);
        Web.NewWindowRequested += (_, e) => e.Handled = true;
        Web.NavigationStarting += (_, e) =>
        {
            // La interfaz no navega fuera de su origen.
            var permitido = e.Uri.StartsWith(Origen, StringComparison.OrdinalIgnoreCase)
                            || (desarrollo && e.Uri.StartsWith(_uiDesarrollo!, StringComparison.OrdinalIgnoreCase));
            if (!permitido) e.Cancel = true;
        };
        Web.NavigationCompleted += (_, _) => Cargando.Visibility = Visibility.Collapsed;

        Web.Navigate(desarrollo ? _uiDesarrollo! : Origen + "/index.html");
    }

    private void ServirRecurso(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = new Uri(e.Request.Uri);
        var ruta = Uri.UnescapeDataString(uri.AbsolutePath);
        var cors = _uiDesarrollo is not null ? "Access-Control-Allow-Origin: *\r\n" : "";

        if (ruta.StartsWith("/doc/", StringComparison.Ordinal))
        {
            var id = Path.GetFileNameWithoutExtension(ruta);
            var pdf = _puente.PdfDe(id);
            e.Response = pdf is null
                ? Web.Environment.CreateWebResourceResponse(null, 404, "No encontrado", cors)
                : Web.Environment.CreateWebResourceResponse(new MemoryStream(pdf, writable: false), 200, "OK",
                    $"Content-Type: application/pdf\r\nCache-Control: no-store\r\n{cors}");
            return;
        }

        var archivo = Path.GetFullPath(Path.Combine(_carpetaUi, ruta.TrimStart('/')));
        if (!archivo.StartsWith(_carpetaUi, StringComparison.OrdinalIgnoreCase) || !File.Exists(archivo))
            archivo = Path.Combine(_carpetaUi, "index.html");
        if (!File.Exists(archivo))
        {
            e.Response = Web.Environment.CreateWebResourceResponse(null, 404, "No encontrado", "");
            return;
        }
        var flujo = new MemoryStream(File.ReadAllBytes(archivo), writable: false);
        e.Response = Web.Environment.CreateWebResourceResponse(flujo, 200, "OK",
            $"Content-Type: {TipoContenido(archivo)}\r\nCache-Control: no-cache\r\n");
    }

    private static string TipoContenido(string archivo) => Path.GetExtension(archivo).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".js" or ".mjs" => "text/javascript; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".json" => "application/json",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".pfb" => "application/octet-stream",
        ".bcmap" => "application/octet-stream",
        ".wasm" => "application/wasm",
        _ => "application/octet-stream",
    };

    private void AlCerrar(object? sender, CancelEventArgs e)
    {
        if (!_puente.HayCambiosSinGuardar) return;
        var respuesta = MessageBox.Show(this,
            "Hay zonas marcadas que no se han guardado en el proyecto.\n\n¿Salir de todos modos?",
            "Arcano", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (respuesta != MessageBoxResult.Yes) e.Cancel = true;
    }

    protected override void OnClosed(EventArgs e)
    {
        _puente.Dispose();
        base.OnClosed(e);
    }
}
