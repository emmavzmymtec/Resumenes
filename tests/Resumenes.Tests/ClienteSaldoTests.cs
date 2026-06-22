using System.Net;
using System.Text;
using Resumenes.Core.Interfaces;
using Resumenes.Infrastructure.IA;
using Xunit;

namespace Resumenes.Tests;

public class ClienteSaldoTests
{
    private sealed class HandlerFijo(string json, HttpStatusCode code = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(code)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    private sealed class SecretosFake : IAlmacenSecretos
    {
        public string? ObtenerApiKey() => "sk-test";
        public void GuardarApiKey(string apiKey) { }
    }

    [Fact]
    public async Task ObtenerAsync_ParseaSaldoUsd()
    {
        const string json = """
        {"is_available":true,"balance_infos":[{"currency":"USD","total_balance":"12.34"}]}
        """;
        var http = new HttpClient(new HandlerFijo(json));
        var cliente = new ClienteSaldo(http, new SecretosFake(), "https://api.deepseek.com");

        var saldo = await cliente.ObtenerAsync(default);

        Assert.NotNull(saldo);
        Assert.True(saldo!.Disponible);
        Assert.Equal("USD", saldo.Moneda);
        Assert.Equal("12.34", saldo.TotalDisponible);
    }

    [Fact]
    public async Task ObtenerAsync_AnteError_DevuelveNull()
    {
        var http = new HttpClient(new HandlerFijo("nope", HttpStatusCode.Unauthorized));
        var cliente = new ClienteSaldo(http, new SecretosFake(), "https://api.deepseek.com");
        Assert.Null(await cliente.ObtenerAsync(default));
    }
}
