using Arcano.Motor.Catalogo;
using Arcano.Motor.Deteccion;
using Arcano.Motor.Generacion;
using Arcano.Motor.Modelo;
using Arcano.Motor.Ocr;
using Arcano.Motor.Pdfium;

namespace Arcano.Motor.Pruebas;

/// <summary>Un solo reconocedor para todas las pruebas: cargar el modelo tarda.</summary>
public sealed class OcrCompartido : IDisposable
{
    public ReconocedorTesseract Ocr { get; } = new();
    public void Dispose() => Ocr.Dispose();
}

public class MotorPruebas(OcrCompartido compartido) : IClassFixture<OcrCompartido>
{
    private readonly ReconocedorTesseract _ocr = compartido.Ocr;

    private SesionDocumento Abrir() => SesionDocumento.DesdeBytes("muestra.pdf", Muestras.Documento(), _ocr);

    [Fact]
    public void Clasifica_las_paginas_por_origen_del_texto()
    {
        using var sesion = Abrir();
        var paginas = sesion.Paginas;
        Assert.Equal(3, paginas.Count);
        Assert.Equal(OrigenTexto.Nativo, paginas[0].Texto);
        Assert.Equal(OrigenTexto.Nativo, paginas[1].Texto);
        Assert.Equal(OrigenTexto.SinTexto, paginas[2].Texto);
        // La página rotada se reporta ya girada: horizontal.
        Assert.True(paginas[1].AnchoPt > paginas[1].AltoPt);
    }

    [Fact]
    public void Busca_sin_acentos_ni_mayusculas_y_ubica_la_caja()
    {
        using var sesion = Abrir();
        var hallados = sesion.Buscar("juan perez lopez", palabraCompleta: true, categoriaId: "nombre").ToList();
        Assert.Equal(2, hallados.Count);

        var digital = hallados.Single(c => c.Pagina == 0);
        Assert.Equal(Muestras.Nombre, digital.Texto);
        var caja = Assert.Single(digital.Cajas);
        Assert.InRange(caja.X, 0.05, 0.6);
        Assert.InRange(caja.Y, 0.08, 0.16);
        Assert.True(caja.Ancho > caja.Alto);

        // En la página rotada el renglón corre en vertical: una sola caja alta.
        var rotada = Assert.Single(hallados.Single(c => c.Pagina == 1).Cajas);
        Assert.True(rotada.Alto > rotada.Ancho * 3);
    }

    [Fact]
    public void Los_detectores_encuentran_datos_personales()
    {
        using var sesion = Abrir();
        var detectado = sesion.Detectar(null).ToList();
        string Valor(string id) => detectado.Single(c => c.Origen == "deteccion:" + id).Texto;

        Assert.Equal(Muestras.Curp, Valor("curp"));
        Assert.Equal("PELJ800101AB1", Valor("rfc"));
        Assert.Equal("55 1234 5678", Valor("telefono"));
        Assert.Equal("002180700512345675", Valor("clabe"));
        Assert.Equal("4111 1111 1111 1111", Valor("tarjeta"));
        Assert.Equal("06000", Valor("codigo-postal"));
        Assert.Equal("calle Hidalgo No. 5, colonia Centro, C.P. 06000", Valor("domicilio"));
        Assert.Contains("Juan Pérez López", detectado.Where(c => c.Origen == "deteccion:nombre-tratamiento").Select(c => c.Texto));

        var correos = detectado.Where(c => c.Origen == "deteccion:correo").ToList();
        Assert.True(correos.Single(c => c.Texto.Contains("gmail")).Sugerida);
        Assert.False(correos.Single(c => c.Texto.Contains("gob.mx")).Sugerida);
        // La CURP no debe confundirse con un RFC.
        Assert.DoesNotContain(detectado, c => c.Origen == "deteccion:rfc" && c.Texto.Length != 13);
    }

    [Fact]
    public void El_ocr_hace_buscable_la_pagina_escaneada()
    {
        using var sesion = Abrir();
        Assert.DoesNotContain(sesion.Buscar("Perez Lopez", true, null), c => c.Pagina == 2);
        sesion.Reconocer([2]);
        Assert.Equal(OrigenTexto.Ocr, sesion.Texto(2).Origen);
        var escaneada = Assert.Single(sesion.Buscar("Perez Lopez", true, null), c => c.Pagina == 2);
        // En OCR se cubre la palabra completa, con la coma pegada a «Lopez,».
        var comaPegada = sesion.Texto(2).Palabras().Single(p => p.Texto == "Lopez,");
        Assert.True(escaneada.Cajas[0].Derecha >= comaPegada.Caja.Derecha - 1e-9);
    }

    [Fact]
    public void Avisa_de_repeticiones_sin_testar()
    {
        using var sesion = Abrir();
        var primera = sesion.Buscar(Muestras.Nombre, true, "nombre").First(c => c.Pagina == 0);
        var region = new Region { Id = "r1", Pagina = 0, Caja = primera.Cajas[0], CategoriaId = "nombre", Texto = primera.Texto };

        var repeticiones = sesion.RepeticionesSinTestar([region]);
        var pendiente = Assert.Single(repeticiones);
        Assert.Equal(1, pendiente.Pagina);
    }

    [Fact]
    public void Genera_version_publica_sin_el_texto_testado()
    {
        using var sesion = Abrir();
        sesion.Reconocer([2]);
        var regiones = new List<Region>();
        var n = 0;
        foreach (var c in sesion.Buscar("Juan Pérez López", true, "nombre")
                     .Concat(sesion.Buscar("Perez Lopez", true, "nombre"))
                     .Concat(sesion.Detectar(["curp", "rfc", "telefono", "correo"]).Where(c => c.Sugerida)))
        {
            var grupo = $"g{++n}";
            regiones.AddRange(c.Cajas.Select((caja, i) => new Region
            {
                Id = $"{grupo}-{i}", Grupo = grupo, Pagina = c.Pagina, Caja = caja,
                CategoriaId = c.CategoriaId ?? "nombre", Texto = c.Texto, Origen = c.Origen,
            }));
        }

        var carpeta = Muestras.CarpetaTemporal();
        var salida = Path.Combine(carpeta, "version-publica.pdf");
        var generador = new GeneradorVersionPublica(_ocr);
        var resultado = generador.Generar(sesion, regiones, CatalogoDatos.PorDefecto(),
            new OpcionesGeneracion { Dpi = 200, Leyenda = new DatosLeyenda { Documento = "Sentencia de prueba" } }, salida);

        Assert.True(resultado.Verificacion.Correcta, string.Join("\n", resultado.Verificacion.Hallazgos.Select(h => h.Descripcion)));
        Assert.True(resultado.Paginas >= 4);

        using var generado = DocumentoPdf.Abrir(salida);
        var texto = string.Join("\n", Enumerable.Range(0, 3).Select(p => generado.ExtraerTexto(p).Normalizado));
        Assert.DoesNotContain("perez lopez", texto);
        Assert.DoesNotContain("pelj800101", texto);
        Assert.DoesNotContain("gmail", texto);
        // La capa de texto conserva lo no testado.
        Assert.Contains("ciudad de mexico", texto);
        Assert.Contains("magistrada", texto);
        Assert.Equal("", generado.Metadato("Author"));
        Assert.Equal(0, generado.ContarAnotaciones(0));
    }

    [Fact]
    public void La_verificacion_detecta_un_nombre_que_quedo_sin_testar()
    {
        using var sesion = Abrir();
        var primera = sesion.Buscar(Muestras.Nombre, true, "nombre").First(c => c.Pagina == 0);
        var regiones = new List<Region>
        {
            new() { Id = "r1", Pagina = 0, Caja = primera.Cajas[0], CategoriaId = "nombre", Texto = primera.Texto },
        };
        var salida = Path.Combine(Muestras.CarpetaTemporal(), "vp.pdf");
        var resultado = new GeneradorVersionPublica(_ocr).Generar(sesion, regiones, CatalogoDatos.PorDefecto(),
            new OpcionesGeneracion { Dpi = 200, IncluirLeyenda = false }, salida);

        Assert.False(resultado.Verificacion.Correcta);
        Assert.Contains(resultado.Verificacion.Hallazgos, h => h.Tipo == "texto" && h.Pagina == 1);
        // La página 3 es un escaneo sin testados: se copia sin capa de texto, pero se revisa con OCR.
        var enEscaneo = Assert.Single(resultado.Verificacion.Hallazgos, h => h.Tipo == "texto" && h.Pagina == 2);
        Assert.Equal("juan perez lopez", enEscaneo.Texto);
        Assert.Equal(1, resultado.PaginasConvertidas);
    }

    [Fact]
    public void Las_paginas_sin_testados_se_copian_sin_anotaciones()
    {
        using var sesion = Abrir();
        var curp = sesion.Detectar(["curp"]).Single();
        var regiones = new List<Region>
        {
            new() { Id = "r1", Pagina = 0, Caja = curp.Cajas[0], CategoriaId = "curp", Texto = curp.Texto },
        };
        var carpeta = Muestras.CarpetaTemporal();
        var generador = new GeneradorVersionPublica(_ocr);
        var opciones = new OpcionesGeneracion { Dpi = 200, IncluirLeyenda = false };

        var mixta = generador.Generar(sesion, regiones, CatalogoDatos.PorDefecto(), opciones, Path.Combine(carpeta, "mixta.pdf"));
        var todas = generador.Generar(sesion, regiones, CatalogoDatos.PorDefecto(),
            opciones with { RasterizarTodas = true }, Path.Combine(carpeta, "todas.pdf"));

        Assert.Equal(1, mixta.PaginasConvertidas);
        Assert.Equal(3, todas.PaginasConvertidas);

        using var generado = DocumentoPdf.Abrir(mixta.Ruta);
        Assert.Equal(3, generado.Paginas);
        // La página 2 (rotada) se copió: conserva su texto propio, su giro y nada de la anotación.
        Assert.Equal(OrigenTexto.Nativo, generado.ExtraerTexto(1).Origen);
        Assert.Contains("notifiquese personalmente", generado.ExtraerTexto(1).Normalizado);
        Assert.True(generado.TamanoPagina(1).AnchoPt > generado.TamanoPagina(1).AltoPt);
        Assert.Equal(0, generado.ContarAnotaciones(1));
        Assert.Equal("", generado.Metadato("Author"));
        // La CURP no aparece en ningún lado.
        Assert.DoesNotContain("pelj800101hdfrpn09", string.Concat(Enumerable.Range(0, 3).Select(p => generado.ExtraerTexto(p).Normalizado)));
        Assert.True(mixta.Verificacion.Correcta, string.Join("\n", mixta.Verificacion.Hallazgos.Select(h => h.Descripcion)));
    }

    [Fact]
    public void En_un_documento_de_texto_copiar_las_paginas_sin_testados_reduce_mucho_el_tamano()
    {
        using var sesion = SesionDocumento.DesdeBytes("largo.pdf", Muestras.DocumentoDeTexto(20), _ocr);
        var nombre = sesion.Buscar(Muestras.Nombre, true, "nombre").First();
        var regiones = new List<Region>
        {
            new() { Id = "r1", Pagina = nombre.Pagina, Caja = nombre.Cajas[0], CategoriaId = "nombre", Texto = nombre.Texto },
        };
        var carpeta = Muestras.CarpetaTemporal();
        var generador = new GeneradorVersionPublica(_ocr);
        var opciones = new OpcionesGeneracion { Dpi = 300, IncluirLeyenda = false, CapaTexto = false };

        var mixta = generador.Generar(sesion, regiones, CatalogoDatos.PorDefecto(), opciones, Path.Combine(carpeta, "mixta.pdf"));
        var todas = generador.Generar(sesion, regiones, CatalogoDatos.PorDefecto(),
            opciones with { RasterizarTodas = true }, Path.Combine(carpeta, "todas.pdf"));

        Assert.Equal(1, mixta.PaginasConvertidas);
        Assert.True(mixta.Bytes * 5 < todas.Bytes, $"mixta {mixta.Bytes} B, todas {todas.Bytes} B");
    }

    [Theory]
    [InlineData(new[] { 1, 2, 3, 5, 7, 8 }, "1 a 3, 5, 7 y 8")]
    [InlineData(new[] { 4 }, "4")]
    [InlineData(new[] { 1, 3 }, "1 y 3")]
    public void Rangos_de_numeros(int[] valores, string esperado) =>
        Assert.Equal(esperado, GeneradorLeyenda.Rangos(valores));

    [Fact]
    public void El_domicilio_se_detiene_antes_de_otro_dato()
    {
        var texto = Texto("con domicilio en calle Juárez 120, colonia Centro, C.P. 06000, teléfono 5512345678. Se resuelve");
        var domicilio = Assert.Single(Detectores.Ejecutar(texto, ["domicilio"]));
        Assert.Equal("calle Juárez 120, colonia Centro, C.P. 06000", domicilio.Texto);
    }

    [Fact]
    public void Marcas_de_barras_verticales()
    {
        var texto = Texto("promovido por |María Guadalupe Torres Ruiz|, con domicilio");
        Assert.Equal("|María Guadalupe Torres Ruiz|", Assert.Single(Detectores.Ejecutar(texto, ["marcas"])).Texto);
    }

    /// <summary>Texto de una sola línea con una caja por carácter, para probar detectores.</summary>
    private static Texto.TextoPagina Texto(string valor) =>
        new(0, OrigenTexto.Nativo, valor.Select((c, i) =>
            new Texto.Caracter(c.ToString(), c == ' ' ? null : new RectN(i * 0.005, 0.1, 0.005, 0.01))).ToList());

    [Fact]
    public void Variantes_de_nombre() =>
        Assert.Equal(["juan perez lopez", "juan perez", "perez lopez"], Buscador.VariantesDeNombre("Juan Pérez López"));
}
