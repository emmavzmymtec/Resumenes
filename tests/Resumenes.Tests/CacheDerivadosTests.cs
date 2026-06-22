using Resumenes.Infrastructure.Aplicacion;
using Resumenes.Tests.Fakes;
using Xunit;

namespace Resumenes.Tests;

public class CacheDerivadosTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"resu-cache-{Guid.NewGuid():N}");

    private CacheDerivados Nuevo()
        => new(new RepositorioEnMemoria(), new Configuracion { RutaCache = _dir });

    private string ArchivoConTexto(string texto)
    {
        var p = Path.Combine(_dir, $"src-{Guid.NewGuid():N}.txt");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(p, texto);
        return p;
    }

    [Fact]
    public void GuardarOcr_LuegoBuscarOcr_DevuelveRutaConContenido()
    {
        var cache = Nuevo();
        var origen = ArchivoConTexto("texto ocr");
        cache.GuardarOcr("hashA", 200, origen);

        var hit = cache.BuscarOcr("hashA", 200);
        Assert.NotNull(hit);
        Assert.Equal("texto ocr", File.ReadAllText(hit!));
    }

    [Fact]
    public void BuscarOcr_SinGuardar_DevuelveNull()
        => Assert.Null(Nuevo().BuscarOcr("hashX", 200));

    [Fact]
    public void BuscarOcr_DistintoDpi_EsMiss()
    {
        var cache = Nuevo();
        cache.GuardarOcr("hashA", 200, ArchivoConTexto("x"));
        Assert.Null(cache.BuscarOcr("hashA", 300));
    }

    [Fact]
    public void BuscarLimpieza_DistintoPromptOModelo_EsMiss()
    {
        var cache = Nuevo();
        cache.GuardarLimpieza("hashA", 200, "prompt1", "modeloA", ArchivoConTexto("limpio"));
        Assert.NotNull(cache.BuscarLimpieza("hashA", 200, "prompt1", "modeloA"));
        Assert.Null(cache.BuscarLimpieza("hashA", 200, "prompt2", "modeloA"));
        Assert.Null(cache.BuscarLimpieza("hashA", 200, "prompt1", "modeloB"));
    }

    [Fact]
    public void Buscar_ConRegistroPeroArchivoFaltante_EsMiss()
    {
        var cache = Nuevo();
        var origen = ArchivoConTexto("limpio");
        cache.GuardarLimpieza("hashA", 200, "p", "m", origen);
        var hit = cache.BuscarLimpieza("hashA", 200, "p", "m");
        Assert.NotNull(hit);
        File.Delete(hit!);                      // simula caché corrupta/borrada
        Assert.Null(cache.BuscarLimpieza("hashA", 200, "p", "m"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}
