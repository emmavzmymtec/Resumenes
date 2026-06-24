using System.Net.Http;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;
using Resumenes.Infrastructure.Examenes;

namespace Resumenes.Tests;

public class DevolucionIaTests
{
    private sealed class IaFake(string texto) : IClienteIA
    {
        public string? UltimoUsuario;
        public Task<RespuestaIA> CompletarAsync(SolicitudIA s, CancellationToken ct)
        { UltimoUsuario = s.PromptUser; return Task.FromResult(new RespuestaIA(texto, "stop", 5, 7, 12)); }
    }

    private static (PreguntaExamen, RespuestaUsuario) Par(string enun, double pts, double obt)
        => (new PreguntaExamen { Id = "p", ExamenId = "e", Enunciado = enun, Puntos = pts, Tipo = TipoPregunta.Desarrollo },
            new RespuestaUsuario { Id = "r", ExamenId = "e", PreguntaId = "p", PuntosObtenidos = obt });

    [Fact]
    public async Task GenerarDevolucion_DevuelveTextoDeLaIa_YTokens()
    {
        var ia = new IaFake("Muy bien; reforzá los aranceles.");
        var sut = new CorrectorExamen(ia);
        var (txt, tin, tout) = await sut.GenerarDevolucionAsync(
            new[] { Par("Aranceles", 10, 4) }, 40, "modelo", default);

        Assert.Equal("Muy bien; reforzá los aranceles.", txt);
        Assert.Equal(5, tin);
        Assert.Equal(7, tout);
        Assert.Contains("Aranceles", ia.UltimoUsuario!); // el enunciado viaja a la IA
    }

    [Fact]
    public async Task GenerarDevolucion_SiLaIaFalla_DevuelveRespaldo_SinLanzar()
    {
        var sut = new CorrectorExamen(new IaQueLanza());
        var (txt, tin, tout) = await sut.GenerarDevolucionAsync(
            new[] { Par("X", 10, 10) }, 100, "modelo", default);

        Assert.False(string.IsNullOrWhiteSpace(txt)); // texto de respaldo
        Assert.Equal(0, tin);
        Assert.Equal(0, tout);
    }

    private sealed class IaQueLanza : IClienteIA
    {
        public Task<RespuestaIA> CompletarAsync(SolicitudIA s, CancellationToken ct)
            => throw new HttpRequestException("sin red");
    }
}
