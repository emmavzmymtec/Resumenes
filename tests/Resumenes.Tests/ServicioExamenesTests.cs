using Resumenes.Core.Modelos;
using Resumenes.Infrastructure.Aplicacion;
using Resumenes.Infrastructure.Examenes;
using Resumenes.Tests.Fakes;
using Xunit;

namespace Resumenes.Tests;

public class ServicioExamenesTests : IDisposable
{
    private readonly string _ws = Path.Combine(Path.GetTempPath(), $"resu-svcex-{Guid.NewGuid():N}");

    private (ServicioExamenes svc, RepositorioExamenesEnMemoria repo, FakeClienteIA ia) Armar()
    {
        // Resúmenes en disco: <ws>/analisis/an1/resumen/tema1.txt
        var dir = Path.Combine(_ws, "analisis", "an1", "resumen");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "tema1.txt"), "La fotosíntesis convierte luz en energía.");

        var repoEx = new RepositorioExamenesEnMemoria();
        var ia = new FakeClienteIA();
        var cfg = new Configuracion { RutaWorkspace = _ws, EscalaNotaMaxima = 10, NotaAprobacion = 6 };
        var svc = new ServicioExamenes(repoEx,
            new GeneradorExamen(ia), new CorrectorExamen(ia), cfg, new RelojFijo());
        return (svc, repoEx, ia);
    }

    private static ConfigExamen Cfg() => new(
        new[] { new CantidadPorTipo(TipoPregunta.McUna, 1) }, Array.Empty<string>(), "media", 1, 30, "rapido");

    [Fact]
    public async Task Crear_Responder_Finalizar_PersisteResultado()
    {
        var (svc, repo, ia) = Armar();
        ia.Responder = req => req.PromptSystem.Contains("generador")
            ? "{\"preguntas\":[{\"tipo\":\"McUna\",\"enunciado\":\"?\",\"puntos\":1,\"datos\":{\"opciones\":[{\"texto\":\"A\",\"correcta\":true},{\"texto\":\"B\",\"correcta\":false}]}}]}"
            : "{\"resultados\":[]}";

        var examen = await svc.CrearAsync("an1", "Parcial 1", Cfg(), default);
        var preguntas = repo.ListarPreguntas(examen.Id);
        Assert.Single(preguntas);

        // El alumno responde la opción correcta (índice 0)
        repo.GuardarRespuesta(new RespuestaUsuario {
            Id = Guid.NewGuid().ToString("N"), ExamenId = examen.Id, PreguntaId = preguntas[0].Id, RespuestaJson = "0" });

        var corregido = await svc.FinalizarYCorregirAsync(examen.Id, default);
        Assert.Equal(EstadoExamen.Corregido, corregido.Estado);
        Assert.Equal(100, corregido.Porcentaje);
        Assert.Equal(10, corregido.Nota);
        Assert.True(corregido.Aprobado);
    }

    public void Dispose() { if (Directory.Exists(_ws)) Directory.Delete(_ws, true); }
}
