using Arcano.Motor.Catalogo;

namespace Arcano.Motor.Pruebas;

public class CatalogoPruebas
{
    [Fact]
    public void El_catalogo_de_fabrica_es_valido_y_esta_pendiente_de_fundamento()
    {
        var catalogo = CatalogoDatos.PorDefecto();
        Assert.Empty(catalogo.Validar());
        Assert.Contains("PENDIENTE", catalogo.FundamentoGeneral);
        Assert.Equal(["gob.mx"], catalogo.DominiosInstitucionales);
    }

    [Fact]
    public void El_fundamento_propio_tiene_prioridad_sobre_el_general()
    {
        var catalogo = CatalogoDatos.PorDefecto() with { FundamentoGeneral = "General" };
        var curp = catalogo.Buscar("curp")! with { Fundamento = "Propio" };
        Assert.Equal("Propio", catalogo.FundamentoDe(curp));
        Assert.Equal("General", catalogo.FundamentoDe(catalogo.Buscar("nombre")!));
    }

    [Fact]
    public void Normaliza_dominios_y_detecta_errores()
    {
        var catalogo = new CatalogoDatos
        {
            DominiosInstitucionales = [" @Tribunal.GOB.mx ", "tribunal.gob.mx", "sin punto"],
            Categorias =
            [
                new Categoria { Id = "a", Nombre = "Nombre" },
                new Categoria { Id = "a", Nombre = "nombre " },
                new Categoria { Id = "c", Nombre = " ", Tipo = "otra cosa" },
            ],
        }.Normalizar();

        Assert.Equal(["tribunal.gob.mx", "sin punto"], catalogo.DominiosInstitucionales);
        Assert.Equal("confidencial", catalogo.Categorias[2].Tipo);
        var errores = catalogo.Validar();
        Assert.Contains(errores, e => e.Contains("«a» está repetida"));
        Assert.Contains(errores, e => e.Contains("llamadas «Nombre»", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errores, e => e.Contains("sin nombre"));
        Assert.Contains(errores, e => e.Contains("«sin punto»"));
    }

    [Fact]
    public void Guardar_e_importar_conservan_los_fundamentos()
    {
        var ruta = Path.Combine(Muestras.CarpetaTemporal(), "catalogo.json");
        var original = CatalogoDatos.PorDefecto() with { FundamentoGeneral = "Artículo 1 de la ley «X»." };
        original.Guardar(ruta);

        var leido = CatalogoDatos.Leer(ruta);
        Assert.Equal("Artículo 1 de la ley «X».", leido.FundamentoGeneral);
        Assert.Equal(original.Categorias.Count, leido.Categorias.Count);
    }

    [Fact]
    public void Importar_un_archivo_que_no_es_catalogo_falla_con_mensaje_claro()
    {
        var ruta = Path.Combine(Muestras.CarpetaTemporal(), "otro.json");
        File.WriteAllText(ruta, "{ \"hola\": 1 }");
        var error = Assert.Throws<ArgumentException>(() => CatalogoDatos.Leer(ruta));
        Assert.Contains("no contiene categorías", error.Message);
    }
}
