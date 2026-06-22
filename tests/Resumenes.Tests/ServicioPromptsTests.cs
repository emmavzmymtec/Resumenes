using Resumenes.Infrastructure.Aplicacion;
using Resumenes.Tests.Fakes;
using Xunit;

namespace Resumenes.Tests;

public class ServicioPromptsTests
{
    private static ServicioPrompts Nuevo() => new(new RepositorioEnMemoria());

    [Fact]
    public void Editable_SinOverride_DevuelveDefault()
    {
        var sp = Nuevo();
        Assert.Equal(Prompts.ResumenEditableDefault, sp.ObtenerEditable(ServicioPrompts.ClaveResumen));
    }

    [Fact]
    public void Editable_ConOverride_DevuelveOverride_yRestaurarVuelveAlDefault()
    {
        var sp = Nuevo();
        sp.GuardarEditable(ServicioPrompts.ClaveResumen, "Mi estilo");
        Assert.Equal("Mi estilo", sp.ObtenerEditable(ServicioPrompts.ClaveResumen));

        sp.RestaurarDefault(ServicioPrompts.ClaveResumen);
        Assert.Equal(Prompts.ResumenEditableDefault, sp.ObtenerEditable(ServicioPrompts.ClaveResumen));
    }

    [Fact]
    public void SystemResumen_IncluyeFormatoFijo_yNombreTema()
    {
        var sp = Nuevo();
        var s = sp.SystemResumen("Aduanas", null);
        Assert.Contains("#TITULO:", s);          // formato fijo siempre presente
        Assert.Contains("Aduanas", s);
    }

    [Fact]
    public void SystemResumen_ConPromptAlumno_PrioridadAlAlumno()
    {
        var sp = Nuevo();
        var s = sp.SystemResumen("Aduanas", "solo multiple choice");
        Assert.Contains("solo multiple choice", s);
        Assert.Contains("#TITULO:", s);          // formato sigue protegido
    }

    [Fact]
    public void SystemDeteccion_IncluyeFormatoJson()
    {
        var sp = Nuevo();
        var s = sp.SystemDeteccion("");
        Assert.Contains("\"temas\"", s);
    }

    [Fact]
    public void HashEditable_CambiaAlEditar()
    {
        var sp = Nuevo();
        var antes = sp.HashEditable(ServicioPrompts.ClaveLimpieza);
        sp.GuardarEditable(ServicioPrompts.ClaveLimpieza, "Otro corrector");
        var despues = sp.HashEditable(ServicioPrompts.ClaveLimpieza);
        Assert.NotEqual(antes, despues);
    }

    [Fact]
    public void SystemLimpieza_ConOverride_UsaElOverride_yMantieneFijo()
    {
        var sp = Nuevo();
        sp.GuardarEditable(ServicioPrompts.ClaveLimpieza, "CORRECTOR PERSONALIZADO");
        var s = sp.SystemLimpieza();
        Assert.Contains("CORRECTOR PERSONALIZADO", s);
        Assert.Contains(Prompts.LimpiezaFijo, s);
    }

    [Fact]
    public void SystemDeteccion_ConOverride_UsaElOverride_yMantieneFijo()
    {
        var sp = Nuevo();
        sp.GuardarEditable(ServicioPrompts.ClaveDeteccion, "ORGANIZADOR PERSONALIZADO");
        var s = sp.SystemDeteccion("");
        Assert.Contains("ORGANIZADOR PERSONALIZADO", s);
        Assert.Contains("\"temas\"", s); // parte fija (formato JSON)
    }
}
