using System.Text.Json;
using Testado.Motor.Catalogo;
using Testado.Motor.Generacion;
using Testado.Motor.Modelo;

namespace Testado.Motor.Proyecto;

/// <summary>
/// Trabajo en curso sobre un documento: zonas marcadas, términos buscados y
/// datos de la leyenda. Se guarda junto al documento como «nombre.testado» y
/// se liga a él por el SHA-256, para no aplicarlo a otra versión del archivo.
/// </summary>
public sealed record ProyectoTestado
{
    public const string Extension = ".testado";

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

    public static ProyectoTestado? Cargar(string ruta) =>
        File.Exists(ruta) ? JsonSerializer.Deserialize<ProyectoTestado>(File.ReadAllText(ruta), CatalogoDatos.Json) : null;
}
