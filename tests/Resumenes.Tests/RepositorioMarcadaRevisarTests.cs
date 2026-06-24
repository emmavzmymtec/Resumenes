using Resumenes.Core.Modelos;
using Resumenes.Infrastructure.Persistencia;

namespace Resumenes.Tests;

public class RepositorioMarcadaRevisarTests : IDisposable
{
    private readonly string _ruta = Path.Combine(Path.GetTempPath(), $"exdb-{Guid.NewGuid():N}.sqlite");
    private readonly SqliteRepositorioEstado _estado;
    private readonly SqliteRepositorioExamenes _ex;

    public RepositorioMarcadaRevisarTests()
    {
        var cs = $"Data Source={_ruta}";
        _estado = new SqliteRepositorioEstado(cs);
        _estado.InicializarEsquema();
        _ex = new SqliteRepositorioExamenes(cs);
        // Sembrar respetando FK: Analisis -> Examen -> PreguntaExamen -> RespuestaUsuario.
        _estado.GuardarAnalisis(new Analisis("a", "Análisis test", "C:\\origen", "fp-mr", EstadoAnalisis.Completado, DateTime.UtcNow, DateTime.UtcNow));
        _ex.GuardarExamen(new Examen { Id = "e", AnalisisId = "a", Titulo = "t", ConfigJson = "{}", CreadoEn = DateTime.UtcNow });
        _ex.GuardarPregunta(new PreguntaExamen { Id = "p", ExamenId = "e", Orden = 1, Tipo = TipoPregunta.McUna, Enunciado = "¿?", Puntos = 1, DatosJson = "{}" });
        _ex.GuardarPregunta(new PreguntaExamen { Id = "p2", ExamenId = "e", Orden = 2, Tipo = TipoPregunta.McUna, Enunciado = "¿?2", Puntos = 1, DatosJson = "{}" });
    }

    [Fact]
    public void GuardarYLeer_PreservaMarcadaRevisar()
    {
        _ex.GuardarRespuesta(new RespuestaUsuario { Id = "e:p", ExamenId = "e", PreguntaId = "p", MarcadaRevisar = true });
        var leida = _ex.ListarRespuestas("e").Single();
        Assert.True(leida.MarcadaRevisar);
    }

    [Fact]
    public void PorDefecto_EsFalse()
    {
        _ex.GuardarRespuesta(new RespuestaUsuario { Id = "e:p2", ExamenId = "e", PreguntaId = "p2" });
        Assert.False(_ex.ListarRespuestas("e").Single(r => r.Id == "e:p2").MarcadaRevisar);
    }

    public void Dispose()
    {
        try { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); } catch { }
        try { File.Delete(_ruta); } catch { }
    }
}
