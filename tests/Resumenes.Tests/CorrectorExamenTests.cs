using System.Text.Json;
using Resumenes.Core.Modelos;
using Resumenes.Infrastructure.Examenes;
using Resumenes.Tests.Fakes;
using Xunit;

namespace Resumenes.Tests;

public class CorrectorExamenTests
{
    private static PreguntaExamen McUna() => new() {
        Id="p", ExamenId="e", Tipo=TipoPregunta.McUna, Enunciado="?", Puntos=1,
        DatosJson="{\"opciones\":[{\"texto\":\"A\",\"correcta\":true},{\"texto\":\"B\",\"correcta\":false}]}" };

    [Fact]
    public void CorregirObjetivo_McUna_CorrectaEIncorrecta()
    {
        var c = new CorrectorExamen(new FakeClienteIA());
        var p = McUna();

        var ok = new RespuestaUsuario { Id="r1", ExamenId="e", PreguntaId="p", RespuestaJson="0" }; // índice 0 = A
        c.CorregirObjetivo(p, ok);
        Assert.True(ok.Correcta);
        Assert.Equal(1, ok.PuntosObtenidos);

        var mal = new RespuestaUsuario { Id="r2", ExamenId="e", PreguntaId="p", RespuestaJson="1" };
        c.CorregirObjetivo(p, mal);
        Assert.False(mal.Correcta);
        Assert.Equal(0, mal.PuntosObtenidos);
    }

    [Fact]
    public void CalcularResultado_NotaYPorcentajeYAprobado()
    {
        var c = new CorrectorExamen(new FakeClienteIA());
        var p1 = McUna(); var p2 = McUna();
        var r1 = new RespuestaUsuario { Id="r1", ExamenId="e", PreguntaId=p1.Id, PuntosObtenidos=1 };
        var r2 = new RespuestaUsuario { Id="r2", ExamenId="e", PreguntaId=p2.Id, PuntosObtenidos=0 };

        var res = c.CalcularResultado(new[] { (p1, r1), (p2, r2) }, escalaMax: 10, notaAprobacion: 6);
        Assert.Equal(50, res.Porcentaje);   // 1 de 2 puntos
        Assert.Equal(5, res.Nota);          // 50% de 10
        Assert.False(res.Aprobado);         // 5 < 6
    }

    [Fact]
    public async Task CorregirAbiertasAsync_AplicaVeredictoIa()
    {
        const string veredicto = """
        {"resultados":[{"puntos":1.5,"feedback":"bien pero incompleto","ambigua":false}]}
        """;
        var ia = new FakeClienteIA { Responder = _ => veredicto };
        var c = new CorrectorExamen(ia);

        var p = new PreguntaExamen { Id="p", ExamenId="e", Tipo=TipoPregunta.Desarrollo, Enunciado="Explicá X", Puntos=2,
            DatosJson="{\"criterios\":\"...\"}" };
        var r = new RespuestaUsuario { Id="r", ExamenId="e", PreguntaId="p", RespuestaJson="\"mi respuesta\"" };

        await c.CorregirAbiertasAsync(new[] { (p, r) }, "modelo", default);

        Assert.Equal(1.5, r.PuntosObtenidos);
        Assert.Equal("bien pero incompleto", r.FeedbackIa);
    }
}
