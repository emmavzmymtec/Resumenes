using Resumenes.Core.Interfaces;
using Resumenes.Tests.Fakes;
using Xunit;

namespace Resumenes.Tests;

public class CachePipelineIntegracionTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), $"resu-int-{Guid.NewGuid():N}");

    [Fact]
    public async Task SegundoAnalisis_ConMismoArchivo_ReutilizaLimpiezaSinLlamarIA()
    {
        Directory.CreateDirectory(_base);
        var rutaCache = Path.Combine(_base, "cache");

        // Dos carpetas DISTINTAS, cada una con un archivo DIFERENTE pero MISMO CONTENIDO.
        // Nombres de archivo distintos => fingerprints distintos => análisis distintos.
        // Contenido igual => caché reutilizable. Workspaces distintos => prueba la copia de caché.
        const string contenido = "contenido de estudio";

        var carpeta1 = Path.Combine(_base, "material1");
        Directory.CreateDirectory(carpeta1);
        await File.WriteAllTextAsync(Path.Combine(carpeta1, "apunte1.txt"), contenido);

        var carpeta2 = Path.Combine(_base, "material2");
        Directory.CreateDirectory(carpeta2);
        await File.WriteAllTextAsync(Path.Combine(carpeta2, "apunte2.txt"), contenido);

        // Contador de llamadas a la IA (la limpieza es la única etapa IA por-archivo)
        // Usamos un array para permitir Interlocked.Increment (thread-safe)
        var llamadas = new int[1];
        var ia = new FakeClienteIAContador(() => System.Threading.Interlocked.Increment(ref llamadas[0]));

        // Un repo compartido (como en producción: misma BD SQLite para el índice de caché).
        var repo = new RepositorioEnMemoria();
        var ws1 = Path.Combine(_base, "ws1");
        var ws2 = Path.Combine(_base, "ws2");

        // 1.ª corrida: procesa carpeta1 y puebla la caché
        var svc1 = ServicioAnalisisFactory.ParaTests(repo, ws1, rutaCache, ia);
        var an1 = await svc1.AbrirOCrearAsync(carpeta1, default);
        await svc1.ProcesarArchivosAsync(an1, null, default);
        var llamadasTras1 = System.Threading.Volatile.Read(ref llamadas[0]);
        Assert.True(llamadasTras1 >= 1, "la 1.ª corrida debe llamar a la IA al menos una vez (limpieza)");

        // 2.ª corrida: carpeta2 (distinto fingerprint => distinto an.Id => el orquestador
        // no tiene unidades previas para esta carpeta). MISMO contenido de archivo => cache hit.
        // DISTINTO workspace => prueba que el artefacto cacheado se copia al nuevo ws.
        var svc2 = ServicioAnalisisFactory.ParaTests(repo, ws2, rutaCache, ia);
        var an2 = await svc2.AbrirOCrearAsync(carpeta2, default);
        var resultado2 = await svc2.ProcesarArchivosAsync(an2, null, default);

        Assert.Equal(0, resultado2.Error); // la 2.ª corrida procesó sin errores
        Assert.Equal(llamadasTras1, System.Threading.Volatile.Read(ref llamadas[0])); // no hubo nuevas llamadas a la IA
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_base)) Directory.Delete(_base, true);
    }
}
