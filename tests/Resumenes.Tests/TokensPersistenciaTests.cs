using Resumenes.Core.Modelos;
using Resumenes.Tests.Fakes;
using Xunit;

namespace Resumenes.Tests;

public class TokensPersistenciaTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), $"resu-tok-{Guid.NewGuid():N}");

    [Fact]
    public async Task ProcesarArchivos_PersisteTokensDeLimpieza()
    {
        var carpeta = Path.Combine(_base, "material");
        Directory.CreateDirectory(carpeta);
        await File.WriteAllTextAsync(Path.Combine(carpeta, "apunte.txt"), "contenido de estudio");

        var repo = new RepositorioEnMemoria();
        var svc = ServicioAnalisisFactory.ParaTests(repo, Path.Combine(_base, "ws"));
        var an = await svc.AbrirOCrearAsync(carpeta, default);
        await svc.ProcesarArchivosAsync(an, null, default);

        var (entrada, salida) = repo.SumarTokensAnalisis(an.Id);
        Assert.True(entrada > 0, "deben persistirse tokens de entrada de la limpieza");
        Assert.True(salida > 0, "deben persistirse tokens de salida de la limpieza");
    }

    public void Dispose()
    {
        if (Directory.Exists(_base)) Directory.Delete(_base, true);
    }
}
