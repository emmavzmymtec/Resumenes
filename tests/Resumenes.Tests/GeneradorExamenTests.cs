using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;
using Resumenes.Infrastructure.Examenes;
using Resumenes.Tests.Fakes;
using Xunit;

namespace Resumenes.Tests;

public class GeneradorExamenTests
{
    private const string JsonOk = """
    {"preguntas":[
      {"tipo":"McUna","enunciado":"¿Capital de Francia?","puntos":1,
       "datos":{"opciones":[{"texto":"París","correcta":true},{"texto":"Roma","correcta":false}]}},
      {"tipo":"Desarrollo","enunciado":"Explicá la fotosíntesis.","puntos":2,
       "datos":{"criterios":"menciona luz, clorofila, CO2"}}
    ]}
    """;

    private static ConfigExamen Cfg() => new(
        new[] { new CantidadPorTipo(TipoPregunta.McUna, 1), new CantidadPorTipo(TipoPregunta.Desarrollo, 1) },
        Array.Empty<string>(), "media", 3, 30, "rapido");

    [Fact]
    public async Task GenerarAsync_ParseaPreguntasYDatos()
    {
        var ia = new FakeClienteIA { Responder = _ => JsonOk };
        var gen = new GeneradorExamen(ia);

        var r = await gen.GenerarAsync("ex1", "contenido de estudio", Cfg(), "modelo-x", default);

        Assert.Equal(2, r.Preguntas.Count);
        Assert.Equal(TipoPregunta.McUna, r.Preguntas[0].Tipo);
        Assert.Equal("ex1", r.Preguntas[0].ExamenId);
        Assert.Equal(1, r.Preguntas[0].Orden);
        Assert.Contains("París", r.Preguntas[0].DatosJson);
        Assert.True(r.TokensEntrada > 0 || r.TokensSalida > 0);
    }

    [Fact]
    public async Task GenerarAsync_JsonInvalido_ReintentaYLanza()
    {
        int llamadas = 0;
        var ia = new FakeClienteIA { Responder = _ => { llamadas++; return "esto no es json"; } };
        var gen = new GeneradorExamen(ia);

        await Assert.ThrowsAnyAsync<Exception>(() => gen.GenerarAsync("ex1", "x", Cfg(), "m", default));
        Assert.True(llamadas >= 2, "debe reintentar al menos una vez");
    }
}
