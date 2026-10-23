using System.Text.Json;
using System.Text.Json.Serialization;

namespace Testado.Motor.Catalogo;

/// <summary>Tipo de dato que se testa, con su motivación y fundamento para la leyenda.</summary>
public sealed record Categoria
{
    public required string Id { get; init; }
    public required string Nombre { get; init; }
    public string Grupo { get; init; } = "Otros";
    /// <summary>confidencial o reservada.</summary>
    public string Tipo { get; init; } = "confidencial";
    public string Motivacion { get; init; } = "";
    /// <summary>Si está vacío se usa el fundamento general del catálogo.</summary>
    public string Fundamento { get; init; } = "";
}

/// <summary>
/// Catálogo de categorías. Trae uno por defecto incrustado y el usuario puede
/// sustituirlo con un archivo propio (ver <see cref="CargarODefecto"/>).
/// </summary>
public sealed record CatalogoDatos
{
    public int Version { get; init; } = 1;
    public string Nota { get; init; } = "";
    public string FundamentoGeneral { get; init; } = "";
    /// <summary>Dominios de correo de la institución y de otras autoridades: sus correos no se proponen para testar.</summary>
    public IReadOnlyList<string> DominiosInstitucionales { get; init; } = [];
    public IReadOnlyList<Categoria> Categorias { get; init; } = [];

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public Categoria? Buscar(string? id) => id is null ? null : Categorias.FirstOrDefault(c => c.Id == id);

    public string FundamentoDe(Categoria categoria) =>
        string.IsNullOrWhiteSpace(categoria.Fundamento) ? FundamentoGeneral : categoria.Fundamento;

    public static CatalogoDatos PorDefecto()
    {
        using var flujo = typeof(CatalogoDatos).Assembly.GetManifestResourceStream("Testado.Motor.catalogo.json")
            ?? throw new InvalidOperationException("Falta el catálogo incrustado.");
        return JsonSerializer.Deserialize<CatalogoDatos>(flujo, Json)!;
    }

    /// <summary>Lee el catálogo del archivo; si no existe, lo crea con el de por defecto.</summary>
    public static CatalogoDatos CargarODefecto(string ruta)
    {
        if (File.Exists(ruta))
            return JsonSerializer.Deserialize<CatalogoDatos>(File.ReadAllText(ruta), Json) ?? PorDefecto();
        var porDefecto = PorDefecto();
        porDefecto.Guardar(ruta);
        return porDefecto;
    }

    /// <summary>Lee un catálogo de un archivo (por ejemplo, uno compartido por otra persona) y lo valida.</summary>
    public static CatalogoDatos Leer(string ruta)
    {
        CatalogoDatos? catalogo;
        try { catalogo = JsonSerializer.Deserialize<CatalogoDatos>(File.ReadAllText(ruta), Json); }
        catch (JsonException ex) { throw new ArgumentException($"El archivo no es un catálogo válido: {ex.Message}", ex); }
        if (catalogo is null || catalogo.Categorias.Count == 0)
            throw new ArgumentException("El archivo no contiene categorías.");
        var limpio = catalogo.Normalizar();
        var errores = limpio.Validar();
        if (errores.Count > 0) throw new ArgumentException(string.Join(" ", errores));
        return limpio;
    }

    /// <summary>Quita espacios sobrantes y deja los dominios en minúsculas, sin «@» ni repetidos.</summary>
    public CatalogoDatos Normalizar() => this with
    {
        FundamentoGeneral = FundamentoGeneral.Trim(),
        DominiosInstitucionales = DominiosInstitucionales
            .Select(d => d.Trim().TrimStart('@').ToLowerInvariant())
            .Where(d => d.Length > 0)
            .Distinct()
            .ToList(),
        Categorias = Categorias.Select(c => c with
        {
            Id = c.Id.Trim(),
            Nombre = c.Nombre.Trim(),
            Grupo = string.IsNullOrWhiteSpace(c.Grupo) ? "Otros" : c.Grupo.Trim(),
            Tipo = c.Tipo == "reservada" ? "reservada" : "confidencial",
            Motivacion = c.Motivacion.Trim(),
            Fundamento = c.Fundamento.Trim(),
        }).ToList(),
    };

    /// <summary>Errores que impiden guardar el catálogo (vacío si es válido).</summary>
    public IReadOnlyList<string> Validar()
    {
        var errores = new List<string>();
        if (Categorias.Count == 0) errores.Add("El catálogo debe tener al menos una categoría.");
        foreach (var c in Categorias.Where(c => string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Nombre)))
            errores.Add($"Hay una categoría sin {(string.IsNullOrWhiteSpace(c.Nombre) ? "nombre" : "clave")}.");
        foreach (var repetida in Categorias.GroupBy(c => c.Id).Where(g => g.Count() > 1 && g.Key.Length > 0))
            errores.Add($"La clave «{repetida.Key}» está repetida.");
        foreach (var repetido in Categorias.GroupBy(c => c.Nombre.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1 && g.Key.Length > 0))
            errores.Add($"Hay dos categorías llamadas «{repetido.Key}».");
        foreach (var d in DominiosInstitucionales.Where(d => !d.Contains('.') || d.Contains(' ')))
            errores.Add($"«{d}» no parece un dominio de correo (ejemplo: tribunal.gob.mx).");
        return errores;
    }

    public void Guardar(string ruta)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(ruta))!);
        var temporal = ruta + ".tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(this, Json));
        File.Move(temporal, ruta, overwrite: true);
    }
}
