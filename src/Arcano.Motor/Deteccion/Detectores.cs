using System.Text.RegularExpressions;
using Arcano.Motor.Modelo;
using Arcano.Motor.Texto;

namespace Arcano.Motor.Deteccion;

/// <summary>
/// Regla que propone zonas a testar. Trabaja sobre el texto normalizado
/// (minúsculas, sin acentos); <see cref="Sugerida"/> indica si la coincidencia
/// se ofrece ya marcada o solo como posible.
/// </summary>
public sealed record Detector(
    string Id,
    string Nombre,
    string Descripcion,
    string? CategoriaId,
    bool Sugerida,
    Func<TextoPagina, IEnumerable<(int Inicio, int Fin)>> Detectar);

public static partial class Detectores
{
    private const RegexOptions Opciones = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    /// <summary>
    /// Dominios de correo institucional que no se proponen como dato personal
    /// cuando no se indican otros (se configuran en el catálogo).
    /// </summary>
    public static readonly string[] DominiosPorDefecto = ["gob.mx"];

    private static readonly Regex Curp = new(
        @"(?<![a-z0-9])[a-z][aeioux][a-z]{2}\d{2}(?:0[1-9]|1[0-2])(?:0[1-9]|[12]\d|3[01])[hmx]" +
        @"(?:as|bc|bs|cc|cl|cm|cs|ch|df|dg|gt|gr|hg|jc|mc|mn|ms|nt|nl|oc|pl|qt|qr|sp|sl|sr|tc|ts|tl|vz|yn|zs|ne)" +
        @"[b-df-hj-np-tv-z]{3}[a-z\d]\d(?![a-z0-9])", Opciones);

    private static readonly Regex RfcFisica = new(
        @"(?<![a-z0-9&])[a-z&]{4}\d{2}(?:0[1-9]|1[0-2])(?:0[1-9]|[12]\d|3[01])[a-z\d]{3}(?![a-z0-9])", Opciones);

    private static readonly Regex RfcMoral = new(
        @"(?<![a-z0-9&])[a-z&]{3}\d{2}(?:0[1-9]|1[0-2])(?:0[1-9]|[12]\d|3[01])[a-z\d]{3}(?![a-z0-9])", Opciones);

    private static readonly Regex ClaveElector = new(
        @"(?<![a-z0-9])[a-z]{6}\d{8}[hm]\d{3}(?![a-z0-9])", Opciones);

    private static readonly Regex Correo = new(
        @"(?<![a-z0-9._%+-])[a-z0-9._%+-]+@(?<dominio>[a-z0-9-]+(?:\.[a-z0-9-]+)*\.[a-z]{2,})(?![a-z0-9])", Opciones);

    private static readonly Regex Telefono = new(
        @"(?<![\d/.,-])(?:\+?\s?52[\s.-]?)?(?:\(\d{2,3}\)\s?|\d{2,3}[\s.-]?)\d{3,4}[\s.-]?\d{4}(?![\d/.,-]?\d)", Opciones);

    private static readonly Regex Clabe = new(
        @"(?<![\d-])\d{3}[\s-]?\d{3}[\s-]?\d{11}[\s-]?\d(?![\d-]?\d)", Opciones);

    private static readonly Regex Tarjeta = new(
        @"(?<![\d-])(?:\d{4}[\s-]?){3}\d{4}(?![\d-]?\d)", Opciones);

    private static readonly Regex CuentaPorPalabra = new(
        @"(?:cuenta|cta\.?)\s+(?:bancaria\s+)?(?:(?:no|num|numero)\.?\s*)?:?\s*(?<v>\d[\d\s-]{7,22}\d)", Opciones);

    private static readonly Regex CodigoPostal = new(
        @"(?<![a-z])c\.?\s?p\.?\s?(?<v>\d{5})(?!\d)", Opciones);

    // El domicilio termina en un signo de puntuación fuerte o donde empieza otro dato
    // («, teléfono…», «, correo…»). Los puntos de abreviaturas (No., Col., C.P.) no lo cortan.
    private static readonly Regex Domicilio = new(
        @"(?<![a-z])(?<v>(?:calle|avenida|av\.|boulevard|blvd\.?|privada|callejon|andador|carretera|camino|colonia|fraccionamiento)\s+" +
        @"(?:(?!,?\s*(?:telefono|tel\.|celular|correo|curp|rfc|cuenta|clabe)(?![a-z]))" +
        @"(?:[^;.:()]|(?<=(?<![a-z])(?:no|num|int|ext|col|fracc|av|blvd|priv|mz|lt|depto|edif|esq|edo|c|p|s|n))\.)){3,140})",
        Opciones);

    private static readonly Regex Placa = new(
        @"(?:placas?|matricula)\s+(?:de\s+circulacion\s+)?(?:(?:no|num|numero)\.?\s*)?:?\s*(?<v>[a-z0-9]{2,4}[\s-]?[a-z0-9]{2,4}(?:[\s-]?[a-z0-9]{1,3})?)(?![a-z0-9])", Opciones);

    private static readonly Regex Marcas = new(@"\|[^|]{1,600}\|", Opciones);

    private static readonly Regex NombreConTratamiento = new(
        @"(?<![a-z])(?:c\.|ciudadan[oa]s?|senor[a]?|sr\.|sra\.|promovente|actora?|quejos[oa]|denunciante|tercer[oa]\s+interesad[oa])\s+(?<v>[a-z]+(?:\s+[a-z]+){1,6})",
        Opciones);

    private static readonly HashSet<string> Conectores = ["de", "del", "la", "las", "los", "y", "e", "van", "von", "da", "di"];

    public static IReadOnlyList<Detector> Todos { get; } =
    [
        new("curp", "CURP", "Clave Única de Registro de Población (18 caracteres)", "curp", true,
            t => t.Buscar(Curp)),
        new("rfc", "RFC de persona física", "13 caracteres con fecha válida", "rfc", true,
            t => t.Buscar(RfcFisica)),
        new("rfc-moral", "RFC de persona moral", "12 caracteres; normalmente es público", "otro", false,
            t => t.Buscar(RfcMoral)),
        new("clave-elector", "Clave de elector", "18 caracteres de la credencial para votar", "clave-elector", true,
            t => t.Buscar(ClaveElector)),
        new("correo", "Correo electrónico", "Los dominios institucionales se ofrecen sin marcar", "correo", true,
            t => t.Buscar(Correo)),
        new("telefono", "Teléfono", "10 dígitos, con o sin lada internacional", "telefono", true,
            t => Validar(t, t.Buscar(Telefono), v => Digitos(v) is 10 or 12)),
        new("clabe", "CLABE interbancaria", "18 dígitos con dígito verificador válido", "cuenta-bancaria", true,
            t => Validar(t, t.Buscar(Clabe), ClabeValida)),
        new("tarjeta", "Tarjeta bancaria", "16 dígitos que pasan la validación de Luhn", "tarjeta", true,
            t => Validar(t, t.Buscar(Tarjeta), LuhnValido)),
        new("cuenta", "Número de cuenta", "Cifras precedidas de «cuenta»", "cuenta-bancaria", true,
            t => t.Buscar(CuentaPorPalabra, "v")),
        new("codigo-postal", "Código postal", "C.P. seguido de 5 dígitos", "domicilio", true,
            t => t.Buscar(CodigoPostal, "v")),
        new("domicilio", "Posible domicilio", "Desde «calle», «avenida», «colonia»… hasta el signo de puntuación; revisar el alcance", "domicilio", false,
            t => t.Buscar(Domicilio, "v")),
        new("placa", "Placas de vehículo", "Texto que sigue a «placas»", "placa", false,
            t => t.Buscar(Placa, "v")),
        new("nombre-tratamiento", "Posible nombre de persona", "Palabras con mayúscula tras «C.», «ciudadano», «actor», «promovente»…", "nombre", false,
            NombresConTratamiento),
        new("marcas", "Marcas |…| de Word", "Texto marcado con las macros de barras verticales", null, true,
            t => t.Buscar(Marcas)),
    ];

    public static Detector? Buscar(string id) => Todos.FirstOrDefault(d => d.Id == id);

    /// <summary>
    /// Corre los detectores pedidos sobre una página. Los correos de los dominios
    /// institucionales se ofrecen sin marcar.
    /// </summary>
    public static IEnumerable<Coincidencia> Ejecutar(TextoPagina texto, IEnumerable<string>? ids = null,
        IReadOnlyCollection<string>? dominiosInstitucionales = null)
    {
        var dominios = dominiosInstitucionales is { Count: > 0 } ? dominiosInstitucionales : DominiosPorDefecto;
        var seleccion = ids is null ? Todos : Todos.Where(d => ids.Contains(d.Id));
        foreach (var detector in seleccion)
        {
            foreach (var (inicio, fin) in detector.Detectar(texto).Distinct())
            {
                var cajas = texto.Cajas(inicio, fin);
                if (cajas.Count == 0) continue;
                var valor = texto.Extraer(inicio, fin);
                var sugerida = detector.Sugerida;
                if (detector.Id == "correo" && EsInstitucional(valor, dominios)) sugerida = false;
                yield return new Coincidencia(texto.Pagina, valor, cajas, "deteccion:" + detector.Id,
                    detector.CategoriaId, sugerida, texto.Contexto(inicio, fin));
            }
        }
    }

    private static bool EsInstitucional(string correo, IEnumerable<string> dominiosInstitucionales)
    {
        var dominio = correo[(correo.IndexOf('@') + 1)..].ToLowerInvariant();
        return dominiosInstitucionales.Select(d => d.Trim().TrimStart('@').ToLowerInvariant()).Any(d => dominio == d || dominio.EndsWith("." + d, StringComparison.Ordinal));
    }

    private static IEnumerable<(int, int)> Validar(TextoPagina t, IEnumerable<(int Inicio, int Fin)> rangos, Func<string, bool> valido) =>
        rangos.Where(r => valido(t.Extraer(r.Inicio, r.Fin)));

    private static int Digitos(string valor) => valor.Count(char.IsAsciiDigit);

    private static bool ClabeValida(string valor)
    {
        var d = valor.Where(char.IsAsciiDigit).Select(c => c - '0').ToArray();
        if (d.Length != 18) return false;
        int[] pesos = [3, 7, 1];
        var suma = 0;
        for (var i = 0; i < 17; i++) suma += d[i] * pesos[i % 3] % 10;
        return (10 - suma % 10) % 10 == d[17];
    }

    private static bool LuhnValido(string valor)
    {
        var d = valor.Where(char.IsAsciiDigit).Select(c => c - '0').ToArray();
        if (d.Length != 16 || d.All(x => x == d[0])) return false;
        var suma = 0;
        for (var i = 0; i < d.Length; i++)
        {
            var x = d[d.Length - 1 - i];
            if (i % 2 == 1) { x *= 2; if (x > 9) x -= 9; }
            suma += x;
        }
        return suma % 10 == 0;
    }

    /// <summary>
    /// Tras un tratamiento («C.», «actor»…) toma las palabras que empiezan con
    /// mayúscula en el texto original, admitiendo conectores (de, del, la…).
    /// </summary>
    private static IEnumerable<(int, int)> NombresConTratamiento(TextoPagina t)
    {
        foreach (var (inicio, fin) in t.Buscar(NombreConTratamiento, "v"))
        {
            // Las palabras pueden traer puntuación pegada («López,»): se recortan al match.
            var palabras = t.Palabras().Where(p => p.Inicio >= inicio && p.Inicio < fin).ToList();
            var finNombre = -1;
            var mayusculas = 0;
            foreach (var p in palabras)
            {
                var primera = p.Texto.FirstOrDefault(char.IsLetter);
                if (primera != default && char.IsUpper(primera))
                {
                    finNombre = Math.Min(p.Fin, fin);
                    mayusculas++;
                }
                else if (!Conectores.Contains(p.Texto.ToLowerInvariant()) || finNombre < 0)
                {
                    break;
                }
            }
            if (mayusculas >= 2 && finNombre > inicio) yield return (inicio, finNombre);
        }
    }
}
