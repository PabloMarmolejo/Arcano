using System.Text.Json;
using Arcano.Motor.Catalogo;
using Arcano.Motor.Generacion;
using Arcano.Motor.Modelo;

namespace Arcano.Motor.Proyecto;

/// <summary>
/// Trabajo en curso sobre un documento: zonas marcadas, términos buscados y
/// datos de la leyenda. Se guarda junto al documento como «nombre.arcano» y
/// se liga a él por el SHA-256, para no aplicarlo a otra versión del archivo.
/// </summary>
public sealed record ProyectoArcano
{
    public const string Extension = ".arcano";
    /// <summary>Extensión que usaba el programa cuando se llamaba Testado; se sigue leyendo.</summary>
    public const string ExtensionAnterior = ".testado";

    public int Version { get; init; } = 1;
    public required string Documento { get; init; }
    public required string Sha256 { get; init; }
    public IReadOnlyList<Region> Regiones { get; init; } = [];
    public IReadOnlyList<string> Terminos { get; init; } = [];
    public DatosLeyenda? Leyenda { get; init; }
    public DateTimeOffset Modificado { get; init; } = DateTimeOffset.UtcNow;

    public static string RutaPara(string rutaDocumento) => rutaDocumento + Extension;

    public void Guardar(string ruta)
    {
        var temporal = ruta + ".tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(this, CatalogoDatos.Json));
        File.Move(temporal, ruta, overwrite: true);
    }

    /// <summary>Trabajo guardado de un documento, con su extensión actual o la anterior.</summary>
    public static ProyectoArcano? CargarDe(string rutaDocumento) =>
        Cargar(RutaPara(rutaDocumento)) ?? Cargar(rutaDocumento + ExtensionAnterior);

    /// <summary>Ruta del documento al que pertenece un archivo de trabajo, o null si no lo es.</summary>
    public static string? DocumentoDe(string rutaProyecto)
    {
        foreach (var extension in new[] { Extension, ExtensionAnterior })
            if (rutaProyecto.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return rutaProyecto[..^extension.Length];
        return null;
    }

    public static ProyectoArcano? Cargar(string ruta) =>
        File.Exists(ruta) ? JsonSerializer.Deserialize<ProyectoArcano>(File.ReadAllText(ruta), CatalogoDatos.Json) : null;
}
