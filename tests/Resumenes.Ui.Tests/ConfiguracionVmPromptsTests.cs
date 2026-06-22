using Resumenes.Infrastructure.Aplicacion;
using Resumenes.Tests.Fakes;
using Resumenes.Ui.ViewModels;
using Xunit;

namespace Resumenes.Ui.Tests;

public class ConfiguracionVmPromptsTests
{
    private sealed class SecretosFake : Resumenes.Core.Interfaces.IAlmacenSecretos
    {
        public string? ObtenerApiKey() => null;
        public void GuardarApiKey(string apiKey) { }
    }

    private static ConfiguracionVm Nuevo(out ServicioPrompts sp)
    {
        sp = new ServicioPrompts(new RepositorioEnMemoria());
        return new ConfiguracionVm(new SecretosFake(), new Configuracion(), sp);
    }

    [Fact]
    public void Carga_LosPromptsEditablesPorDefecto()
    {
        var vm = Nuevo(out _);
        Assert.Equal(Prompts.ResumenEditableDefault, vm.PromptResumen);
        Assert.Contains("#TITULO:", vm.FormatoResumen); // parte fija visible (solo lectura)
    }

    [Fact]
    public void GuardarPrompts_PersisteElOverride()
    {
        var vm = Nuevo(out var sp);
        vm.PromptResumen = "Mi estilo propio";
        vm.GuardarPromptsCommand.Execute(null);
        Assert.Equal("Mi estilo propio", sp.ObtenerEditable(ServicioPrompts.ClaveResumen));
    }

    [Fact]
    public void RestaurarPrompts_VuelveAlDefault()
    {
        var vm = Nuevo(out var sp);
        vm.PromptResumen = "algo";
        vm.GuardarPromptsCommand.Execute(null);
        vm.RestaurarPromptsCommand.Execute(null);
        Assert.Equal(Prompts.ResumenEditableDefault, vm.PromptResumen);
        Assert.Equal(Prompts.ResumenEditableDefault, sp.ObtenerEditable(ServicioPrompts.ClaveResumen));
    }
}
