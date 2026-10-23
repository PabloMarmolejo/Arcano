namespace Arcano.Motor.Generacion;

/// <summary>Datos que identifican la versión pública en la leyenda.</summary>
public sealed record DatosLeyenda
{
    /// <summary>Identificación del documento (tipo, expediente, fecha).</summary>
    public string Documento { get; init; } = "";
    public string TextoAdicional { get; init; } = "";
}

public sealed record OpcionesGeneracion
{
    /// <summary>Resolución de las páginas de salida (150 a 400).</summary>
    public int Dpi { get; init; } = 300;
    /// <summary>Calidad JPEG de las páginas (50 a 95).</summary>
    public int CalidadJpeg { get; init; } = 80;
    /// <summary>Color de relleno en formato #RRGGBB.</summary>
    public string Color { get; init; } = "#000000";
    /// <summary>Escribe el número de cada testado dentro de su recuadro.</summary>
    public bool Numerar { get; init; } = true;
    /// <summary>Agrega una capa de texto invisible (OCR) para que el PDF se pueda buscar.</summary>
    public bool CapaTexto { get; init; } = true;
    public bool IncluirLeyenda { get; init; } = true;
    public DatosLeyenda Leyenda { get; init; } = new();
}

public sealed record Hallazgo(string Tipo, string Descripcion, int? Pagina = null);

public sealed record Verificacion(bool Correcta, IReadOnlyList<string> Comprobaciones, IReadOnlyList<Hallazgo> Hallazgos);

public sealed record ResultadoGeneracion(string Ruta, int Paginas, int Testados, long Bytes, Verificacion Verificacion);
