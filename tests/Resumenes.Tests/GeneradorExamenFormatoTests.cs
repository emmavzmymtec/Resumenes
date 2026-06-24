using System.Text.Json;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;
using Resumenes.Infrastructure.Examenes;

namespace Resumenes.Tests;

public class GeneradorExamenFormatoTests
{
    // IA fake que devuelve un examen con una pregunta de Desarrollo que incluye respuestaEsperada.
    private sealed class IaFake : IClienteIA
    {
        public Task<RespuestaIA> CompletarAsync(SolicitudIA s, CancellationToken ct)
            => Task.FromResult(new RespuestaIA(
                "{\"preguntas\":[{\"tipo\":\"Desarrollo\",\"enunciado\":\"Explicá X\",\"puntos\":10," +
                "\"datos\":{\"criterios\":\"mencionar A y B\",\"respuestaEsperada\":\"A y B en breve\"}}]}",
                "stop", 10, 20, 30));
    }

    [Fact]
    public void FormatoJson_PideRespuestaEsperadaYJustificacion()
    {
        // El system prompt (constante) debe instruir a la IA a devolver estos campos.
        var fmt = GeneradorExamen.FormatoJson;
        Assert.Contains("respuestaEsperada", fmt);
        Assert.Contains("justificacion", fmt);
    }

    [Fact]
    public async Task Generar_PreservaRespuestaEsperadaEnDatosJson()
    {
        var gen = new GeneradorExamen(new IaFake());
        var cfg = new ConfigExamen(new[] { new CantidadPorTipo(TipoPregunta.Desarrollo, 1) },
            Array.Empty<string>(), "media", 10, 0, "rapido");

        var r = await gen.GenerarAsync("ex1", "contenido", cfg, "modelo", default);

        var datos = JsonDocument.Parse(r.Preguntas[0].DatosJson).RootElement;
        Assert.Equal("A y B en breve", datos.GetProperty("respuestaEsperada").GetString());
    }
}
