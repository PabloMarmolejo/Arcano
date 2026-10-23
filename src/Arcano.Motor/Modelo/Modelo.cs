using System.Text.Json.Serialization;

namespace Arcano.Motor.Modelo;

/// <summary>
/// Rectángulo normalizado sobre la página tal como se ve (ya rotada):
/// origen arriba a la izquierda, valores entre 0 y 1.
/// </summary>
public readonly record struct RectN(double X, double Y, double Ancho, double Alto)
{
    [JsonIgnore] public double Derecha => X + Ancho;
    [JsonIgnore] public double Abajo => Y + Alto;
    [JsonIgnore] public bool EsVacio => Ancho <= 0 || Alto <= 0;

    public static RectN DesdeBordes(double izquierda, double arriba, double derecha, double abajo) =>
        new(Math.Min(izquierda, derecha), Math.Min(arriba, abajo),
            Math.Abs(derecha - izquierda), Math.Abs(abajo - arriba));

    public RectN Union(RectN otro) => EsVacio ? otro : otro.EsVacio ? this
        : DesdeBordes(Math.Min(X, otro.X), Math.Min(Y, otro.Y),
            Math.Max(Derecha, otro.Derecha), Math.Max(Abajo, otro.Abajo));

    public bool Intersecta(RectN otro) =>
        X < otro.Derecha && otro.X < Derecha && Y < otro.Abajo && otro.Y < Abajo;

    public double AreaInterseccion(RectN otro)
    {
        var ancho = Math.Min(Derecha, otro.Derecha) - Math.Max(X, otro.X);
        var alto = Math.Min(Abajo, otro.Abajo) - Math.Max(Y, otro.Y);
        return ancho > 0 && alto > 0 ? ancho * alto : 0;
    }

    [JsonIgnore] public double Area => Ancho * Alto;

    /// <summary>Recorta al rango [0,1] de la página.</summary>
    public RectN Acotar()
    {
        var izquierda = Math.Clamp(X, 0, 1);
        var arriba = Math.Clamp(Y, 0, 1);
        return DesdeBordes(izquierda, arriba, Math.Clamp(Derecha, 0, 1), Math.Clamp(Abajo, 0, 1));
    }
}

/// <summary>Tamaño de una página en puntos PDF (1/72 pulgada), ya rotada.</summary>
public sealed record InfoPagina(int Indice, double AnchoPt, double AltoPt, OrigenTexto Texto);

[JsonConverter(typeof(JsonStringEnumConverter<OrigenTexto>))]
public enum OrigenTexto
{
    /// <summary>La página trae texto propio (PDF nacido digital).</summary>
    Nativo,
    /// <summary>No trae texto; se obtuvo por OCR.</summary>
    Ocr,
    /// <summary>No trae texto y todavía no se reconoce (escaneo).</summary>
    SinTexto,
}

/// <summary>Palabra con su caja, para seleccionar texto en la interfaz.</summary>
public sealed record Palabra(string Texto, RectN Caja, int Inicio, int Fin);

/// <summary>Imagen incrustada en una página (firma, fotografía, sello, logotipo).</summary>
public sealed record ImagenPagina(int Pagina, RectN Caja);

/// <summary>Una aparición encontrada por búsqueda o por un detector.</summary>
public sealed record Coincidencia(
    int Pagina,
    string Texto,
    IReadOnlyList<RectN> Cajas,
    string Origen,
    string? CategoriaId,
    bool Sugerida,
    string Contexto);

/// <summary>Zona a testar. Es lo que la interfaz edita y lo que se guarda en el proyecto.</summary>
public sealed record Region
{
    public required string Id { get; init; }
    public required int Pagina { get; init; }
    public required RectN Caja { get; init; }
    public string? CategoriaId { get; init; }
    /// <summary>manual, texto, busqueda, deteccion:&lt;id&gt;, imagen.</summary>
    public string Origen { get; init; } = "manual";
    /// <summary>Texto que había debajo al marcarla; sirve para la revisión y la verificación.</summary>
    public string? Texto { get; init; }
    /// <summary>Agrupa las cajas de una misma aparición que ocupa varios renglones.</summary>
    public string? Grupo { get; init; }
}
