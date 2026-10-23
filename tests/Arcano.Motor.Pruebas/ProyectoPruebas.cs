using Arcano.Motor.Modelo;
using Arcano.Motor.Proyecto;

namespace Arcano.Motor.Pruebas;

public class ProyectoPruebas
{
    [Fact]
    public void Lee_el_trabajo_guardado_con_la_extension_anterior()
    {
        var documento = Path.Combine(Muestras.CarpetaTemporal(), "sentencia.pdf");
        var anterior = new ProyectoArcano
        {
            Documento = "sentencia.pdf",
            Sha256 = "abc",
            Regiones = [new Region { Id = "r1", Pagina = 0, Caja = new RectN(0.1, 0.1, 0.2, 0.02) }],
        };
        anterior.Guardar(documento + ProyectoArcano.ExtensionAnterior);

        var leido = ProyectoArcano.CargarDe(documento);
        Assert.NotNull(leido);
        Assert.Single(leido.Regiones);

        // Si existen los dos, manda el nuevo.
        (anterior with { Regiones = [] }).Guardar(ProyectoArcano.RutaPara(documento));
        Assert.Empty(ProyectoArcano.CargarDe(documento)!.Regiones);
    }

    [Theory]
    [InlineData(@"C:\docs\sentencia.pdf.arcano", @"C:\docs\sentencia.pdf")]
    [InlineData(@"C:\docs\sentencia.pdf.testado", @"C:\docs\sentencia.pdf")]
    [InlineData(@"C:\docs\sentencia.pdf", null)]
    public void Documento_al_que_pertenece_un_trabajo(string ruta, string? esperado) =>
        Assert.Equal(esperado, ProyectoArcano.DocumentoDe(ruta));
}
