using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Arcano.Motor.Modelo;

namespace Arcano.Motor.Texto;

/// <summary>
/// Un carácter de la página con su caja (nula en espacios y saltos generados).
/// <paramref name="Aproximada"/> indica que la caja se estimó repartiendo la de la
/// palabra (OCR); en ese caso se testa siempre la palabra completa.
/// </summary>
public readonly record struct Caracter(string Valor, RectN? Caja, bool Aproximada = false);

/// <summary>
/// Texto de una página con la posición de cada carácter. Mantiene además una
/// versión normalizada (minúsculas, sin acentos, espacios colapsados) con su
/// mapa al texto original, sobre la que se hacen búsquedas y detecciones.
/// </summary>
public sealed class TextoPagina
{
    private readonly List<Caracter> _caracteres;
    private readonly int[] _mapaNormalizado;

    public int Pagina { get; }
    public OrigenTexto Origen { get; }
    public string Texto { get; }
    public string Normalizado { get; }
    public IReadOnlyList<Caracter> Caracteres => _caracteres;

    public TextoPagina(int pagina, OrigenTexto origen, List<Caracter> caracteres)
    {
        Pagina = pagina;
        Origen = origen;
        _caracteres = caracteres;
        Texto = string.Concat(caracteres.Select(c => c.Valor));
        (Normalizado, _mapaNormalizado) = Normalizar(caracteres);
    }

    public static TextoPagina Vacio(int pagina, OrigenTexto origen) => new(pagina, origen, []);

    /// <summary>Cuántos caracteres visibles (con caja) tiene la página.</summary>
    public int CaracteresConCaja => _caracteres.Count(c => c.Caja is not null && !string.IsNullOrWhiteSpace(c.Valor));

    /// <summary>Busca un patrón sobre el texto normalizado y devuelve rangos del texto original.</summary>
    public IEnumerable<(int Inicio, int Fin)> Buscar(Regex patron)
    {
        foreach (Match m in patron.Matches(Normalizado))
        {
            if (m.Length == 0) continue;
            var (inicio, fin) = RangoOriginal(m.Index, m.Length);
            if (fin > inicio) yield return (inicio, fin);
        }
    }

    /// <summary>Busca un patrón sobre un grupo con nombre del match (el resto es contexto).</summary>
    public IEnumerable<(int Inicio, int Fin)> Buscar(Regex patron, string grupo)
    {
        foreach (Match m in patron.Matches(Normalizado))
        {
            var g = m.Groups[grupo];
            if (!g.Success || g.Length == 0) continue;
            var (inicio, fin) = RangoOriginal(g.Index, g.Length);
            if (fin > inicio) yield return (inicio, fin);
        }
    }

    public (int Inicio, int Fin) RangoOriginal(int indiceNormalizado, int largo)
    {
        var inicio = _mapaNormalizado[indiceNormalizado];
        var fin = _mapaNormalizado[indiceNormalizado + largo - 1] + 1;
        return (inicio, fin);
    }

    public string Extraer(int inicio, int fin) =>
        ColapsarEspacios(string.Concat(_caracteres.Skip(inicio).Take(fin - inicio).Select(c => c.Valor)));

    public string Contexto(int inicio, int fin, int margen = 40)
    {
        var desde = Math.Max(0, inicio - margen);
        var hasta = Math.Min(_caracteres.Count, fin + margen);
        var antes = Extraer(desde, inicio);
        var medio = Extraer(inicio, fin);
        var despues = Extraer(fin, hasta);
        // Extraer recorta los extremos: se repone el espacio que separa la coincidencia.
        if (antes.Length > 0 && inicio > 0 && EsEspacio(inicio - 1)) antes += " ";
        if (despues.Length > 0 && fin < _caracteres.Count && EsEspacio(fin)) despues = " " + despues;
        return $"{(desde > 0 ? "…" : "")}{antes}[{medio}]{despues}{(hasta < _caracteres.Count ? "…" : "")}";
    }

    private bool EsEspacio(int i) => string.IsNullOrWhiteSpace(_caracteres[i].Valor);

    /// <summary>
    /// Cajas que cubren un rango, una por renglón: los caracteres que se
    /// enciman verticalmente se unen; al cambiar de renglón se abre otra caja.
    /// </summary>
    public IReadOnlyList<RectN> Cajas(int inicio, int fin)
    {
        (inicio, fin) = AmpliarAproximadas(inicio, fin);
        var cajas = new List<RectN>();
        RectN? actual = null;
        for (var i = inicio; i < fin && i < _caracteres.Count; i++)
        {
            if (_caracteres[i].Caja is not { } caja || caja.EsVacio) continue;
            if (actual is { } a && MismoRenglon(a, caja))
            {
                actual = a.Union(caja);
            }
            else
            {
                if (actual is { } previo) cajas.Add(previo);
                actual = caja;
            }
        }
        if (actual is { } ultimo) cajas.Add(ultimo);
        return cajas;
    }

    /// <summary>
    /// Si el rango empieza o termina dentro de una palabra con cajas aproximadas,
    /// lo extiende a la palabra completa para no dejar letras a medio cubrir.
    /// </summary>
    private (int, int) AmpliarAproximadas(int inicio, int fin)
    {
        bool Letra(int i) => i >= 0 && i < _caracteres.Count && _caracteres[i].Caja is not null
                             && !string.IsNullOrWhiteSpace(_caracteres[i].Valor);
        if (inicio < _caracteres.Count && _caracteres[inicio].Aproximada)
            while (Letra(inicio - 1) && _caracteres[inicio - 1].Aproximada) inicio--;
        if (fin > 0 && _caracteres[fin - 1].Aproximada)
            while (Letra(fin) && _caracteres[fin].Aproximada) fin++;
        return (inicio, fin);
    }

    /// <summary>Palabras (separadas por espacios) con su caja; una palabra partida en dos renglones da dos.</summary>
    public IReadOnlyList<Palabra> Palabras()
    {
        var palabras = new List<Palabra>();
        var inicio = -1;
        for (var i = 0; i <= _caracteres.Count; i++)
        {
            var corte = i == _caracteres.Count || string.IsNullOrWhiteSpace(_caracteres[i].Valor) || _caracteres[i].Caja is null;
            if (!corte && inicio < 0) inicio = i;
            if (corte && inicio >= 0)
            {
                // Una "palabra" que cambia de renglón se reparte por caja.
                var desde = inicio;
                for (var j = inicio + 1; j <= i; j++)
                {
                    var cambia = j == i || !MismoRenglon(_caracteres[j - 1].Caja!.Value, _caracteres[j].Caja!.Value);
                    if (!cambia) continue;
                    var cajas = Cajas(desde, j);
                    if (cajas.Count > 0)
                        palabras.Add(new Palabra(Extraer(desde, j), cajas[0], desde, j));
                    desde = j;
                }
                inicio = -1;
            }
        }
        return palabras;
    }

    /// <summary>
    /// Dos caracteres seguidos están en el mismo renglón si se enciman en vertical
    /// (texto horizontal) o, en páginas giradas, si se enciman en horizontal y están
    /// pegados uno debajo del otro (texto vertical).
    /// </summary>
    private static bool MismoRenglon(RectN a, RectN b)
    {
        var traslapeY = Math.Min(a.Abajo, b.Abajo) - Math.Max(a.Y, b.Y);
        var menorAlto = Math.Min(a.Alto, b.Alto);
        if (menorAlto > 0 && traslapeY >= menorAlto * 0.5) return true;

        var traslapeX = Math.Min(a.Derecha, b.Derecha) - Math.Max(a.X, b.X);
        var menorAncho = Math.Min(a.Ancho, b.Ancho);
        var separacion = Math.Max(a.Y, b.Y) - Math.Min(a.Abajo, b.Abajo);
        var tamano = Math.Max(Math.Max(a.Alto, b.Alto), Math.Max(a.Ancho, b.Ancho));
        return menorAncho > 0 && traslapeX >= menorAncho * 0.5 && separacion <= tamano * 1.5;
    }

    private static (string, int[]) Normalizar(List<Caracter> caracteres)
    {
        var sb = new StringBuilder(caracteres.Count);
        var mapa = new List<int>(caracteres.Count);
        var ultimoEspacio = true;
        for (var i = 0; i < caracteres.Count; i++)
        {
            var valor = caracteres[i].Valor;
            if (string.IsNullOrWhiteSpace(valor) || valor == "­")
            {
                if (ultimoEspacio || valor == "­") continue;
                sb.Append(' ');
                mapa.Add(i);
                ultimoEspacio = true;
                continue;
            }
            foreach (var c in QuitarAcentos(valor).ToLowerInvariant())
            {
                sb.Append(c);
                mapa.Add(i);
            }
            ultimoEspacio = false;
        }
        return (sb.ToString(), mapa.ToArray());
    }

    public static string NormalizarTermino(string texto)
    {
        var sinAcentos = QuitarAcentos(texto).ToLowerInvariant();
        return ColapsarEspacios(sinAcentos);
    }

    private static string QuitarAcentos(string texto)
    {
        // FormKD además separa ligaduras tipográficas (ﬁ → fi), comunes en PDF.
        var descompuesto = texto.Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString();
    }

    private static string ColapsarEspacios(string texto) => Regex.Replace(texto, @"\s+", " ").Trim();
}
