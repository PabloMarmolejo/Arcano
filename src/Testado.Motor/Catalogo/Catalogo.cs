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
        Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
        File.WriteAllText(ruta, JsonSerializer.Serialize(porDefecto, Json));
        return porDefecto;
    }
}
