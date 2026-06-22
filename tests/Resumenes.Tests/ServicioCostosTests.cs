using Resumenes.Core.Modelos;
using Resumenes.Infrastructure.Aplicacion;
using Resumenes.Tests.Fakes;
using Xunit;

namespace Resumenes.Tests;

public class ServicioCostosTests
{
    [Fact]
    public void CostoDe_AplicaTarifaPorMillon()
    {
        var repo = new RepositorioEnMemoria();
        repo.GuardarAnalisis(new Analisis("an1","n","c","fp",EstadoAnalisis.Completado,DateTime.UtcNow,DateTime.UtcNow));
        repo.GuardarUnidad(new Unidad {
            AnalisisId="an1", ArchivoId="a", Etapa=Etapa.LimpiezaIA, Estado=EstadoUnidad.Completado,
            TokensEntrada=1_000_000, TokensSalida=1_000_000, ActualizadoEn=DateTime.UtcNow });

        var cfg = new Configuracion { PrecioInputPorMillonUsd = 0.27m, PrecioOutputPorMillonUsd = 1.10m };
        var svc = new ServicioCostos(repo, cfg);

        Assert.Equal(1.37m, svc.CostoDe("an1"));          // 0.27 + 1.10
        Assert.Contains("US$", svc.CostoLegible("an1"));
    }

    [Fact]
    public void CostoDe_SinTokens_EsCero()
    {
        var repo = new RepositorioEnMemoria();
        var svc = new ServicioCostos(repo, new Configuracion());
        Assert.Equal(0m, svc.CostoDe("inexistente"));
    }
}
