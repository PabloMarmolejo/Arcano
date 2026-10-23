using System.Text.RegularExpressions;
using Arcano.Motor.Modelo;
using Arcano.Motor.Texto;

namespace Arcano.Motor.Deteccion;

/// <summary>
/// Búsqueda de términos (nombres, domicilios, cualquier frase) sin distinguir
/// mayúsculas ni acentos, y tolerante a saltos de renglón entre palabras.
/// </summary>
public static class Buscador
{
    public static Regex Patron(string termino, bool palabraCompleta = true)
    {
        var normalizado = TextoPagina.NormalizarTermino(termino);
        if (normalizado.Length == 0) throw new ArgumentException("El término está vacío.", nameof(termino));
        var cuerpo = string.Join(@"\s+", normalizado.Split(' ').Select(Regex.Escape));
        var patron = palabraCompleta ? $@"(?<![a-z0-9]){cuerpo}(?![a-z0-9])" : cuerpo;
        return new Regex(patron, RegexOptions.CultureInvariant);
    }

    public static IEnumerable<Coincidencia> Buscar(TextoPagina texto, string termino, bool palabraCompleta = true,
        string? categoriaId = null)
    {
        var patron = Patron(termino, palabraCompleta);
        foreach (var (inicio, fin) in texto.Buscar(patron))
        {
            var cajas = texto.Cajas(inicio, fin);
            if (cajas.Count == 0) continue;
            yield return new Coincidencia(texto.Pagina, texto.Extraer(inicio, fin), cajas, "busqueda",
                categoriaId, true, texto.Contexto(inicio, fin));
        }
    }

    /// <summary>
    /// Variantes de un nombre completo que suelen aparecer en el documento:
    /// el nombre entero, nombre(s) + primer apellido y los dos apellidos.
    /// Se asume el orden «nombre(s) apellido apellido».
    /// </summary>
    public static IReadOnlyList<string> VariantesDeNombre(string nombreCompleto)
    {
        var partes = TextoPagina.NormalizarTermino(nombreCompleto).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var variantes = new List<string> { string.Join(' ', partes) };
        if (partes.Length >= 3)
        {
            variantes.Add(string.Join(' ', partes[..^1]));
            variantes.Add(string.Join(' ', partes[^2..]));
        }
        return variantes.Distinct().ToList();
    }
}
