using Arcano.Motor.Deteccion;
using Arcano.Motor.Modelo;
using Arcano.Motor.Pdfium;
using Arcano.Motor.Texto;

namespace Arcano.Motor.Generacion;

/// <summary>
/// Revisa el PDF generado antes de entregarlo: que no traiga anotaciones,
/// adjuntos ni metadatos del original, y que ningún texto testado aparezca en
/// la capa de texto (lo que indicaría otra aparición sin testar).
/// </summary>
public static class Verificador
{
    /// <param name="textoDeCopias">
    /// Texto reconocido por OCR de páginas escaneadas que se copiaron sin convertir:
    /// en el archivo no tienen capa de texto, así que se revisan con este.
    /// </param>
    public static Verificacion Verificar(string ruta, IReadOnlyList<Region> regiones, int paginasOriginales, int paginasConvertidas,
        IReadOnlyDictionary<int, TextoPagina>? textoDeCopias = null)
    {
        var comprobaciones = new List<string>();
        var hallazgos = new List<Hallazgo>();

        using var doc = DocumentoPdf.Abrir(ruta);
        comprobaciones.Add("El archivo generado se abre correctamente.");

        if (doc.Paginas < paginasOriginales)
            hallazgos.Add(new Hallazgo("paginas", $"El archivo tiene {doc.Paginas} páginas y el original {paginasOriginales}."));
        else
            comprobaciones.Add($"Conserva las {paginasOriginales} páginas del original.");

        var anotaciones = Enumerable.Range(0, doc.Paginas).Sum(doc.ContarAnotaciones);
        if (anotaciones > 0)
            hallazgos.Add(new Hallazgo("anotaciones", $"El archivo contiene {anotaciones} anotación(es)."));
        else
            comprobaciones.Add("Sin anotaciones, comentarios ni campos de formulario.");

        if (doc.ContarAdjuntos() > 0)
            hallazgos.Add(new Hallazgo("adjuntos", "El archivo contiene archivos adjuntos."));
        else
            comprobaciones.Add("Sin archivos adjuntos.");

        var autor = doc.Metadato("Author");
        if (autor.Length > 0)
            hallazgos.Add(new Hallazgo("metadatos", $"Los metadatos conservan un autor: «{autor}»."));
        else
            comprobaciones.Add("Sin metadatos del documento original (autor, título, palabras clave).");

        var textos = Enumerable.Range(0, doc.Paginas)
            .Select(p => textoDeCopias is not null && textoDeCopias.TryGetValue(p, out var ocr) ? ocr : doc.ExtraerTexto(p))
            .ToList();
        var terminos = regiones
            .Select(r => r.Texto)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => TextoPagina.NormalizarTermino(t!))
            .Where(t => t.Count(char.IsLetterOrDigit) >= 4)
            .Distinct()
            .ToList();

        var fugas = 0;
        foreach (var termino in terminos)
        {
            var patron = Buscador.Patron(termino);
            foreach (var texto in textos)
            {
                if (!patron.IsMatch(texto.Normalizado)) continue;
                fugas++;
                var donde = texto.Pagina < paginasOriginales ? $"página {texto.Pagina + 1}" : "la leyenda";
                hallazgos.Add(new Hallazgo("texto", $"«{termino}» se puede leer en {donde}.", texto.Pagina, termino));
            }
        }
        if (fugas == 0)
            comprobaciones.Add(terminos.Count == 0
                ? "No hay texto testado que comparar (zonas sin texto o solo imágenes)."
                : $"Ninguno de los {terminos.Count} texto(s) testado(s) aparece en el archivo generado.");

        var copiadas = paginasOriginales - paginasConvertidas;
        comprobaciones.Add(copiadas <= 0
            ? "Cada página es una imagen nueva: lo testado no existe debajo del recuadro."
            : $"Las páginas con testados ({paginasConvertidas}) son imágenes nuevas: lo testado no existe debajo del recuadro. " +
              (copiadas == 1
                  ? "La otra página se copió del original sin anotaciones ni metadatos."
                  : $"Las otras {copiadas} se copiaron del original sin anotaciones ni metadatos."));
        return new Verificacion(hallazgos.Count == 0, comprobaciones, hallazgos);
    }
}
