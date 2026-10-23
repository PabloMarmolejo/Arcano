using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Win32;
using Testado.Motor;
using Testado.Motor.Catalogo;
using Testado.Motor.Conversion;
using Testado.Motor.Deteccion;
using Testado.Motor.Generacion;
using Testado.Motor.Modelo;
using Testado.Motor.Ocr;
using Testado.Motor.Pdfium;
using Testado.Motor.Proyecto;

namespace Testado.Escritorio;

/// <summary>Opciones que el usuario repite de un documento a otro.</summary>
public sealed record Preferencias
{
    public DatosLeyenda Leyenda { get; init; } = new();
    public OpcionesGeneracion Opciones { get; init; } = new();
    public string? UltimaCarpeta { get; init; }
}

/// <summary>
/// Atiende los mensajes de la interfaz. Cada petición llega como
/// <c>{ id, metodo, args }</c> y se responde con <c>{ id, ok, resultado | error }</c>;
/// el avance de tareas largas se envía como <c>{ evento: "progreso", datos }</c>.
/// </summary>
public sealed class Puente : IDisposable
{
    private static readonly JsonSerializerOptions Json = CatalogoDatos.Json;

    private readonly VentanaPrincipal _ventana;
    private readonly Dictionary<string, SesionDocumento> _sesiones = [];
    private readonly IConvertidorDocumentos _convertidor = new ConvertidorDocumentos();
    private readonly Lazy<(ReconocedorTesseract? Ocr, string? Error)> _ocr = new(CrearOcr);
    private readonly string _rutaCatalogo = Path.Combine(App.CarpetaDatos, "catalogo.json");
    private readonly string _rutaPreferencias = Path.Combine(App.CarpetaDatos, "preferencias.json");
    private CatalogoDatos _catalogo;
    private CancellationTokenSource? _cancelacion;

    public bool HayCambiosSinGuardar { get; private set; }

    public Puente(VentanaPrincipal ventana)
    {
        _ventana = ventana;
        _catalogo = CargarCatalogo();
    }

    public byte[]? PdfDe(string id) => _sesiones.TryGetValue(id, out var s) ? s.Pdf : null;

    public async Task RecibirAsync(string mensaje)
    {
        JsonNode? id = null;
        try
        {
            var nodo = JsonNode.Parse(mensaje)!.AsObject();
            id = nodo["id"]?.DeepClone();
            var metodo = nodo["metodo"]!.GetValue<string>();
            var args = nodo["args"] as JsonObject ?? [];
            var resultado = await DespacharAsync(metodo, args);
            Enviar(new JsonObject { ["id"] = id, ["ok"] = true, ["resultado"] = JsonSerializer.SerializeToNode(resultado, Json) });
        }
        catch (OperationCanceledException)
        {
            Enviar(new JsonObject { ["id"] = id, ["ok"] = false, ["cancelado"] = true, ["error"] = "Operación cancelada." });
        }
        catch (Exception ex)
        {
            if (ex is not (PdfProtegidoException or PdfInvalidoException or NotSupportedException or ArgumentException))
                Bitacora.Error("puente", ex);
            Enviar(new JsonObject { ["id"] = id, ["ok"] = false, ["error"] = ex.Message });
        }
    }

    private void Enviar(JsonObject mensaje) => _ventana.Web.PostWebMessageAsJson(mensaje.ToJsonString(Json));

    private void Progreso(Progreso p) =>
        Enviar(new JsonObject { ["evento"] = "progreso", ["datos"] = JsonSerializer.SerializeToNode(p, Json) });

    private Task<object?> DespacharAsync(string metodo, JsonObject a) => metodo switch
    {
        "iniciar" => Task.FromResult<object?>(Iniciar()),
        "abrir" => AbrirAsync(Texto(a, "ruta")),
        "cerrar" => Sincrono(() => Cerrar(Requerido(a, "id"))),
        "palabras" => Sincrono(() => Sesion(a).Palabras(Entero(a, "pagina"))),
        "textoBajo" => Sincrono(() => Sesion(a).TextoBajo(Entero(a, "pagina"), Leer<RectN>(a, "caja"))),
        "buscar" => EnSegundoPlano(() => Buscar(Sesion(a), a)),
        "detectar" => EnSegundoPlano(() => Sesion(a).Detectar(Leer<List<string>>(a, "detectores"), _catalogo.DominiosInstitucionales).ToList()),
        "imagenes" => EnSegundoPlano(() => Sesion(a).Imagenes().ToList()),
        "reconocer" => ReconocerAsync(Sesion(a), Leer<List<int>>(a, "paginas")),
        "repeticiones" => EnSegundoPlano(() => Sesion(a).RepeticionesSinTestar(Leer<List<Region>>(a, "regiones"))),
        "guardarProyecto" => Sincrono(() => GuardarProyecto(Sesion(a), a)),
        "marcarCambios" => Sincrono(() => HayCambiosSinGuardar = a["pendientes"]?.GetValue<bool>() ?? false),
        "generar" => GenerarAsync(Sesion(a), a),
        "cancelar" => Accion(() => _cancelacion?.Cancel()),
        "abrirArchivo" => Accion(() => AbrirConSistema(Requerido(a, "ruta"), carpeta: false)),
        "mostrarEnCarpeta" => Accion(() => AbrirConSistema(Requerido(a, "ruta"), carpeta: true)),
        "guardarCatalogo" => Sincrono(() => GuardarCatalogo(Leer<CatalogoDatos>(a, "catalogo"))),
        "restaurarCatalogo" => Sincrono(() => GuardarCatalogo(CatalogoDatos.PorDefecto())),
        "importarCatalogo" => Sincrono(ImportarCatalogo),
        "exportarCatalogo" => Sincrono(() => ExportarCatalogo(Leer<CatalogoDatos>(a, "catalogo"))),
        "guardarPreferencias" => Sincrono(() => GuardarPreferencias(a)),
        _ => throw new NotSupportedException($"Método desconocido: {metodo}"),
    };

    private object Iniciar()
    {
        var (ocr, errorOcr) = _ocr.Value;
        return new
        {
            Version = typeof(Puente).Assembly.GetName().Version?.ToString(3),
            Catalogo = _catalogo,
            Detectores = Detectores.Todos.Select(d => new { d.Id, d.Nombre, d.Descripcion, d.CategoriaId, d.Sugerida }),
            OcrDisponible = ocr is not null,
            ErrorOcr = errorOcr,
            Preferencias = CargarPreferencias(),
            ArchivoInicial = TomarArchivoInicial(),
        };
    }

    private bool _archivoInicialEntregado;

    private string? TomarArchivoInicial()
    {
        if (_archivoInicialEntregado) return null;
        _archivoInicialEntregado = true;
        return App.ArchivoInicial;
    }

    private async Task<object?> AbrirAsync(string? ruta)
    {
        if (ruta is null)
        {
            var preferencias = CargarPreferencias();
            var dialogo = new OpenFileDialog
            {
                Title = "Abrir documento para testar",
                Filter = "Documentos (*.pdf;*.docx;*.doc;*.rtf;*.odt)|*.pdf;*.docx;*.doc;*.rtf;*.odt|PDF (*.pdf)|*.pdf|Word (*.docx;*.doc)|*.docx;*.doc",
                InitialDirectory = preferencias.UltimaCarpeta ?? "",
            };
            if (dialogo.ShowDialog(_ventana) != true) return null;
            ruta = dialogo.FileName;
            Guardar(_rutaPreferencias, preferencias with { UltimaCarpeta = Path.GetDirectoryName(ruta) });
        }

        var esWord = !ruta.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase);
        if (esWord) Progreso(new Progreso("conversion", 0, 1, "Convirtiendo el documento de Word a PDF"));
        var ocr = _ocr.Value.Ocr;
        var sesion = await Task.Run(() => SesionDocumento.Abrir(ruta, ocr, _convertidor, App.CarpetaTrabajo));
        _sesiones[sesion.Id] = sesion;

        var paginas = await Task.Run(() => sesion.Paginas);
        var proyecto = ProyectoTestado.Cargar(ProyectoTestado.RutaPara(ruta));
        var proyectoDistinto = proyecto is not null && proyecto.Sha256 != sesion.Sha256;
        return new
        {
            sesion.Id,
            sesion.Nombre,
            Ruta = ruta,
            Url = $"{VentanaPrincipal.Origen}/doc/{sesion.Id}.pdf",
            Paginas = paginas,
            sesion.Advertencias,
            Proyecto = proyectoDistinto ? null : proyecto,
            ProyectoDistinto = proyectoDistinto,
        };
    }

    private object? Cerrar(string id)
    {
        if (_sesiones.Remove(id, out var sesion)) sesion.Dispose();
        HayCambiosSinGuardar = false;
        return null;
    }

    private static List<Coincidencia> Buscar(SesionDocumento sesion, JsonObject a)
    {
        var termino = Requerido(a, "termino");
        var palabraCompleta = a["palabraCompleta"]?.GetValue<bool>() ?? true;
        var variantes = a["variantes"]?.GetValue<bool>() ?? false;
        var categoria = Texto(a, "categoriaId");
        var terminos = variantes ? Buscador.VariantesDeNombre(termino) : [termino];
        var resultado = new List<Coincidencia>();
        foreach (var t in terminos)
            foreach (var c in sesion.Buscar(t, palabraCompleta, categoria))
                if (!resultado.Any(r => r.Pagina == c.Pagina && r.Cajas[0].AreaInterseccion(c.Cajas[0]) > c.Cajas[0].Area * 0.5))
                    resultado.Add(c);
        return resultado.OrderBy(c => c.Pagina).ThenBy(c => c.Cajas[0].Y).ToList();
    }

    private async Task<object?> ReconocerAsync(SesionDocumento sesion, List<int> paginas)
    {
        if (!sesion.HayOcr) throw new InvalidOperationException(_ocr.Value.Error ?? "El OCR no está disponible.");
        using var cts = NuevaCancelacion();
        var progreso = new Progress<Progreso>(Progreso);
        await Task.Run(() => sesion.Reconocer(paginas, progreso, cts.Token));
        return sesion.Paginas;
    }

    private string GuardarProyecto(SesionDocumento sesion, JsonObject a)
    {
        var proyecto = new ProyectoTestado
        {
            Documento = sesion.Nombre,
            Sha256 = sesion.Sha256,
            Regiones = Leer<List<Region>>(a, "regiones"),
            Terminos = Leer<List<string>?>(a, "terminos") ?? [],
            Leyenda = Leer<DatosLeyenda?>(a, "leyenda"),
        };
        var ruta = ProyectoTestado.RutaPara(sesion.RutaOrigen);
        proyecto.Guardar(ruta);
        HayCambiosSinGuardar = false;
        return ruta;
    }

    private async Task<object?> GenerarAsync(SesionDocumento sesion, JsonObject a)
    {
        var regiones = Leer<List<Region>>(a, "regiones");
        var opciones = Leer<OpcionesGeneracion>(a, "opciones");
        var carpeta = Path.GetDirectoryName(sesion.RutaOrigen)!;
        var nombre = Path.GetFileNameWithoutExtension(sesion.RutaOrigen);
        var dialogo = new SaveFileDialog
        {
            Title = "Guardar versión pública",
            Filter = "PDF (*.pdf)|*.pdf",
            InitialDirectory = carpeta,
            FileName = $"{nombre} - versión pública.pdf",
            OverwritePrompt = true,
        };
        if (dialogo.ShowDialog(_ventana) != true) return null;
        var salida = dialogo.FileName;
        if (string.Equals(Path.GetFullPath(salida), Path.GetFullPath(sesion.RutaOrigen), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("La versión pública no puede sustituir al documento original. Elija otro nombre.");

        using var cts = NuevaCancelacion();
        var progreso = new Progress<Progreso>(Progreso);
        var generador = new GeneradorVersionPublica(opciones.CapaTexto ? _ocr.Value.Ocr : null);
        return await Task.Run(() => generador.Generar(sesion, regiones, _catalogo, opciones, salida, progreso, cts.Token));
    }

    private object? GuardarPreferencias(JsonObject a)
    {
        var actuales = CargarPreferencias();
        Guardar(_rutaPreferencias, actuales with
        {
            Leyenda = Leer<DatosLeyenda?>(a, "leyenda") ?? actuales.Leyenda,
            Opciones = Leer<OpcionesGeneracion?>(a, "opciones") ?? actuales.Opciones,
        });
        return null;
    }

    private CancellationTokenSource NuevaCancelacion()
    {
        _cancelacion = new CancellationTokenSource();
        return _cancelacion;
    }

    private static void AbrirConSistema(string ruta, bool carpeta)
    {
        if (carpeta)
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{ruta}\"") { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
    }

    private CatalogoDatos GuardarCatalogo(CatalogoDatos catalogo)
    {
        var limpio = catalogo.Normalizar();
        var errores = limpio.Validar();
        if (errores.Count > 0) throw new ArgumentException(string.Join(" ", errores));
        limpio.Guardar(_rutaCatalogo);
        _catalogo = limpio;
        return limpio;
    }

    /// <summary>Lee un catálogo de otro archivo (por ejemplo, el que comparte el área) sin guardarlo todavía.</summary>
    private CatalogoDatos? ImportarCatalogo()
    {
        var dialogo = new OpenFileDialog
        {
            Title = "Importar catálogo",
            Filter = "Catálogo de Testado (*.json)|*.json",
        };
        return dialogo.ShowDialog(_ventana) == true ? CatalogoDatos.Leer(dialogo.FileName) : null;
    }

    private string? ExportarCatalogo(CatalogoDatos catalogo)
    {
        var limpio = catalogo.Normalizar();
        var errores = limpio.Validar();
        if (errores.Count > 0) throw new ArgumentException(string.Join(" ", errores));
        var dialogo = new SaveFileDialog
        {
            Title = "Exportar catálogo",
            Filter = "Catálogo de Testado (*.json)|*.json",
            FileName = "catalogo-testado.json",
            OverwritePrompt = true,
        };
        if (dialogo.ShowDialog(_ventana) != true) return null;
        limpio.Guardar(dialogo.FileName);
        return dialogo.FileName;
    }

    private CatalogoDatos CargarCatalogo()
    {
        try { return CatalogoDatos.CargarODefecto(_rutaCatalogo); }
        catch (JsonException ex)
        {
            MessageBox.Show($"El catálogo ({_rutaCatalogo}) tiene un error y se usará el de fábrica:\n\n{ex.Message}",
                "Testado", MessageBoxButton.OK, MessageBoxImage.Warning);
            return CatalogoDatos.PorDefecto();
        }
    }

    private Preferencias CargarPreferencias()
    {
        try
        {
            return File.Exists(_rutaPreferencias)
                ? JsonSerializer.Deserialize<Preferencias>(File.ReadAllText(_rutaPreferencias), Json) ?? new()
                : new();
        }
        catch (JsonException) { return new(); }
    }

    private static void Guardar<T>(string ruta, T valor)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, JsonSerializer.Serialize(valor, Json));
    }

    private static (ReconocedorTesseract?, string?) CrearOcr()
    {
        try { return (new ReconocedorTesseract(), null); }
        catch (Exception ex)
        {
            Bitacora.Error("ocr", ex);
            return (null, $"El OCR no está disponible: {ex.Message}");
        }
    }

    private SesionDocumento Sesion(JsonObject a) =>
        _sesiones.TryGetValue(Requerido(a, "id"), out var s) ? s : throw new ArgumentException("El documento ya no está abierto.");

    private static Task<object?> Sincrono(Func<object?> f) => Task.FromResult(f());
    private static Task<object?> Accion(Action f)
    {
        f();
        return Task.FromResult<object?>(null);
    }
    private static Task<object?> EnSegundoPlano(Func<object?> f) => Task.Run(f);
    private static string? Texto(JsonObject a, string clave) => a[clave]?.GetValue<string>();
    private static string Requerido(JsonObject a, string clave) =>
        Texto(a, clave) is { Length: > 0 } v ? v : throw new ArgumentException($"Falta el dato «{clave}».");
    private static int Entero(JsonObject a, string clave) => a[clave]?.GetValue<int>() ?? throw new ArgumentException($"Falta el dato «{clave}».");
    private static T Leer<T>(JsonObject a, string clave) => a[clave].Deserialize<T>(Json)!;

    public void Dispose()
    {
        foreach (var s in _sesiones.Values) s.Dispose();
        _sesiones.Clear();
        if (_ocr.IsValueCreated) _ocr.Value.Ocr?.Dispose();
    }
}
