using Resumenes.Core.Modelos;
using Resumenes.Infrastructure.Persistencia;
using Xunit;

namespace Resumenes.Tests;

public class RepositorioExamenesTests
{
    private static (SqliteRepositorioEstado estado, SqliteRepositorioExamenes ex, string tmp) Nuevo()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"resu-ex-{Guid.NewGuid():N}.db");
        var cs = $"Data Source={tmp}";
        var estado = new SqliteRepositorioEstado(cs);
        estado.InicializarEsquema();
        return (estado, new SqliteRepositorioExamenes(cs), tmp);
    }

    [Fact]
    public void Examen_Preguntas_Respuestas_RoundTrip()
    {
        var (estado, repo, tmp) = Nuevo();
        try
        {
            estado.GuardarAnalisis(new Analisis("an1","n","c","fp",EstadoAnalisis.Completado,DateTime.UtcNow,DateTime.UtcNow));

            repo.GuardarExamen(new Examen {
                Id="ex1", AnalisisId="an1", Titulo="Parcial", ConfigJson="{}",
                Estado=EstadoExamen.Borrador, Tokens=0, CostoEstimado=0, CreadoEn=DateTime.UtcNow });
            repo.GuardarPregunta(new PreguntaExamen {
                Id="p1", ExamenId="ex1", Orden=1, Tipo=TipoPregunta.McUna,
                Enunciado="¿2+2?", Puntos=1, DatosJson="{}" });
            repo.GuardarRespuesta(new RespuestaUsuario {
                Id="r1", ExamenId="ex1", PreguntaId="p1", RespuestaJson="\"4\"",
                Correcta=true, PuntosObtenidos=1, Ambigua=false });

            Assert.Equal("Parcial", repo.ObtenerExamen("ex1")!.Titulo);
            Assert.Single(repo.ListarExamenes("an1"));
            Assert.Single(repo.ListarPreguntas("ex1"));
            Assert.True(repo.ListarRespuestas("ex1")[0].Correcta);

            // Actualizar estado/nota (upsert)
            var e = repo.ObtenerExamen("ex1")!;
            e.Estado = EstadoExamen.Corregido; e.Nota = 8.5; e.Porcentaje = 85; e.Aprobado = true;
            repo.GuardarExamen(e);
            var e2 = repo.ObtenerExamen("ex1")!;
            Assert.Equal(EstadoExamen.Corregido, e2.Estado);
            Assert.Equal(8.5, e2.Nota);

            repo.EliminarExamen("ex1");
            Assert.Null(repo.ObtenerExamen("ex1"));
            Assert.Empty(repo.ListarPreguntas("ex1"));   // cascada
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
