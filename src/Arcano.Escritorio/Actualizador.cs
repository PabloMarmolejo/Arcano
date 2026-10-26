using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Arcano.Motor;
using Arcano.Motor.Catalogo;

namespace Arcano.Escritorio;

/// <summary>
/// Versión más nueva encontrada en la fuente de actualizaciones. <c>Instalable</c>:
/// si el programa se instaló con el instalador se actualiza en un paso; si es la
/// versión portátil, solo se ofrece la página de descarga.
/// </summary>
public sealed record ActualizacionDisponible(
    string Version,
    string? Notas,
    bool Instalable,
    string? Pagina,
    [property: JsonIgnore] string? Instalador,
    [property: JsonIgnore] string? Sha256);

/// <summary>
/// Busca e instala versiones nuevas. La fuente se configura con un archivo
/// <c>actualizaciones.json</c> junto al ejecutable:
/// <code>{ "manifiesto": "https://…/version.json" }</code> o <code>{ "desactivadas": true }</code>.
/// Sin ese archivo se consultan las Releases públicas de GitHub.
///
/// El manifiesto es <c>{ "version", "instalador", "sha256", "notas", "pagina" }</c>.
/// </summary>
public sealed class Actualizador
{
    private const string RepositorioPublico = "PabloMarmolejo/Arcano";
    private static readonly HttpClient Http = CrearHttp();

    private readonly Configuracion _config;
    private readonly string _carpetaPrograma = AppContext.BaseDirectory;
    private ActualizacionDisponible? _encontrada;

    public Actualizador()
    {
        var ruta = Path.Combine(_carpetaPrograma, "actualizaciones.json");
        try
        {
            _config = File.Exists(ruta)
                ? JsonSerializer.Deserialize<Configuracion>(File.ReadAllText(ruta), CatalogoDatos.Json) ?? new()
                : new();
        }
        catch (JsonException ex)
        {
            Bitacora.Error("actualizaciones.json", ex);
            _config = new() { Desactivadas = true };
        }
    }

    public static Version VersionActual => typeof(Actualizador).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>Instalado con el instalador (no la versión portátil del zip ni una compilación de desarrollo).</summary>
    private bool Instalado => File.Exists(Path.Combine(_carpetaPrograma, "unins000.exe"));

    private bool EnDesarrollo =>
        Environment.GetEnvironmentVariable("ARCANO_UI") is not null
        || _carpetaPrograma.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);

    /// <summary>Consulta la fuente; devuelve null si no hay nada nuevo o si no se pudo consultar.</summary>
    public async Task<ActualizacionDisponible?> BuscarAsync(CancellationToken ct = default)
    {
        if (_config.Desactivadas || EnDesarrollo) return null;
        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromSeconds(15));
            var encontrada = string.IsNullOrWhiteSpace(_config.Manifiesto)
                ? await DesdeGitHubAsync(limite.Token)
                : await DesdeManifiestoAsync(_config.Manifiesto, limite.Token);
            if (encontrada is null || !Version.TryParse(encontrada.Version.TrimStart('v', 'V'), out var nueva) || nueva <= VersionActual)
                return null;
            _encontrada = encontrada with { Instalable = Instalado && encontrada.Instalador is not null };
            return _encontrada;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            // Sin red o fuente caída: simplemente no se avisa.
            return null;
        }
    }

    /// <summary>
    /// Descarga el instalador, comprueba su huella y lo ejecuta en silencio con
    /// /REABRIR, para que vuelva a abrir Arcano al terminar. Quien llama debe
    /// cerrar el programa enseguida.
    /// </summary>
    public async Task InstalarAsync(IProgress<Progreso>? progreso, CancellationToken ct)
    {
        var a = _encontrada ?? throw new InvalidOperationException("No hay una actualización pendiente.");
        if (!a.Instalable || a.Instalador is null) throw new InvalidOperationException("Esta copia de Arcano no se instaló con el instalador; descargue la versión nueva desde su página.");
        if (!a.Instalador.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El instalador debe descargarse por https.");

        var carpeta = Path.Combine(Path.GetTempPath(), "Arcano-actualizacion");
        Directory.CreateDirectory(carpeta);
        var destino = Path.Combine(carpeta, $"Arcano-{a.Version}-instalador.exe");

        using (var respuesta = await Http.GetAsync(a.Instalador, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            respuesta.EnsureSuccessStatusCode();
            var total = respuesta.Content.Headers.ContentLength ?? 0;
            await using var origen = await respuesta.Content.ReadAsStreamAsync(ct);
            await using var archivo = File.Create(destino);
            var buffer = new byte[1 << 16];
            long leidos = 0;
            int n;
            while ((n = await origen.ReadAsync(buffer, ct)) > 0)
            {
                await archivo.WriteAsync(buffer.AsMemory(0, n), ct);
                leidos += n;
                if (total > 0)
                    progreso?.Report(new Progreso("actualizacion", (int)(leidos / 1024), (int)(total / 1024),
                        $"Descargando Arcano {a.Version} ({leidos / 1048576} de {total / 1048576} MB)"));
            }
        }

        if (a.Sha256 is { Length: > 0 } esperado)
        {
            progreso?.Report(new Progreso("actualizacion", 1, 1, "Comprobando el archivo descargado"));
            await using var f = File.OpenRead(destino);
            var real = Convert.ToHexStringLower(await SHA256.HashDataAsync(f, ct));
            if (!string.Equals(real, esperado, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(destino);
                throw new InvalidOperationException("El instalador descargado no coincide con su huella SHA-256; no se instaló. Intente más tarde.");
            }
        }

        var alcance = _carpetaPrograma.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), StringComparison.OrdinalIgnoreCase)
            ? "/CURRENTUSER"
            : "/ALLUSERS";
        progreso?.Report(new Progreso("actualizacion", 1, 1, "Instalando la versión nueva; Arcano se volverá a abrir"));
        Process.Start(new ProcessStartInfo(destino)
        {
            UseShellExecute = true,
            Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART {alcance} /REABRIR",
        });
    }

    private static async Task<ActualizacionDisponible?> DesdeManifiestoAsync(string url, CancellationToken ct)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("El manifiesto de actualizaciones debe estar en https.");
        // Se pide sin caché para no recibir un manifiesto viejo.
        var m = await Http.GetFromJsonSinCacheAsync<Manifiesto>(url, ct);
        if (m is null || string.IsNullOrWhiteSpace(m.Version)) return null;
        return new ActualizacionDisponible(m.Version, m.Notas, Instalable: true, m.Pagina, m.Instalador, m.Sha256);
    }

    private static async Task<ActualizacionDisponible?> DesdeGitHubAsync(CancellationToken ct)
    {
        var r = await Http.GetFromJsonSinCacheAsync<ReleaseGitHub>($"https://api.github.com/repos/{RepositorioPublico}/releases/latest", ct);
        if (r is null || string.IsNullOrWhiteSpace(r.TagName)) return null;
        var instalador = r.Assets?.FirstOrDefault(x => x.Name.EndsWith("-instalador.exe", StringComparison.OrdinalIgnoreCase));
        var sha = instalador?.Digest is { } d && d.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ? d["sha256:".Length..] : null;
        return new ActualizacionDisponible(r.TagName, r.Body, Instalable: true, r.HtmlUrl, instalador?.BrowserDownloadUrl, sha);
    }

    private static HttpClient CrearHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Arcano/{VersionActual.ToString(3)}");
        return http;
    }

    private sealed record Configuracion
    {
        public string? Manifiesto { get; init; }
        public bool Desactivadas { get; init; }
    }

    private sealed record Manifiesto
    {
        public string Version { get; init; } = "";
        public string? Instalador { get; init; }
        public string? Sha256 { get; init; }
        public string? Notas { get; init; }
        public string? Pagina { get; init; }
    }

    private sealed record ReleaseGitHub
    {
        [JsonPropertyName("tag_name")] public string TagName { get; init; } = "";
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; init; }
        [JsonPropertyName("body")] public string? Body { get; init; }
        [JsonPropertyName("assets")] public List<AssetGitHub>? Assets { get; init; }
    }

    private sealed record AssetGitHub
    {
        [JsonPropertyName("name")] public string Name { get; init; } = "";
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; init; }
        [JsonPropertyName("digest")] public string? Digest { get; init; }
    }
}

internal static class HttpExtensiones
{
    public static async Task<T?> GetFromJsonSinCacheAsync<T>(this HttpClient http, string url, CancellationToken ct)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, url);
        solicitud.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
        using var respuesta = await http.SendAsync(solicitud, ct);
        respuesta.EnsureSuccessStatusCode();
        await using var flujo = await respuesta.Content.ReadAsStreamAsync(ct);
        return await JsonSerializer.DeserializeAsync<T>(flujo, CatalogoDatos.Json, ct);
    }
}
