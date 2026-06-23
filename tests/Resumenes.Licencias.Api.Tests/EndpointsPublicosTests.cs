using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using Resumenes.Licencias.Api.Contratos;
using Resumenes.Licencias.Api.Datos;

namespace Resumenes.Licencias.Api.Tests;

public class EndpointsPublicosTests
{
    private static FabricaApiPruebas CrearFabrica()
    {
        var f = new FabricaApiPruebas();
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        f.EstablecerFirma(ec.ExportECPrivateKeyPem());
        return f;
    }

    private static async Task<Licencia> Sembrar(FabricaApiPruebas f, int max = 2, string estado = "activa")
    {
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LicenciasDbContext>();
        var lic = new Licencia
        {
            Id = Guid.NewGuid(),
            Clave = "RESU-AAAAA-BBBBB-CCCCC-DDDDD",
            Comprador = "Juan", Email = "j@x.com",
            MaxMaquinas = max, Estado = estado, CreadaEn = DateTimeOffset.UtcNow,
        };
        db.Licencias.Add(lic);
        await db.SaveChangesAsync();
        return lic;
    }

    [Fact]
    public async Task Activar_ClaveValida_200ConToken()
    {
        await using var f = CrearFabrica();
        var lic = await Sembrar(f);
        var cliente = f.CreateClient();

        var resp = await cliente.PostAsJsonAsync("/activar",
            new ActivarRequest(lic.Clave, "hw-1", "PC-Oficina"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var cuerpo = await resp.Content.ReadFromJsonAsync<ActivarResponse>();
        Assert.False(string.IsNullOrEmpty(cuerpo!.Token));
    }

    [Fact]
    public async Task Activar_ClaveInexistente_404ClaveInvalida()
    {
        await using var f = CrearFabrica();
        var cliente = f.CreateClient();

        var resp = await cliente.PostAsJsonAsync("/activar",
            new ActivarRequest("RESU-ZZZZZ-ZZZZZ-ZZZZZ-ZZZZZ", "hw-1", "PC"));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        Assert.Contains("clave_invalida", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Activar_SuperaLimite_409LimiteAlcanzado()
    {
        await using var f = CrearFabrica();
        var lic = await Sembrar(f, max: 1);
        var cliente = f.CreateClient();
        await cliente.PostAsJsonAsync("/activar", new ActivarRequest(lic.Clave, "hw-1", "PC1"));

        var resp = await cliente.PostAsJsonAsync("/activar",
            new ActivarRequest(lic.Clave, "hw-2", "PC2"));

        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Contains("limite_alcanzado", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Validar_HwidActivo_200Activa()
    {
        await using var f = CrearFabrica();
        var lic = await Sembrar(f);
        var cliente = f.CreateClient();
        await cliente.PostAsJsonAsync("/activar", new ActivarRequest(lic.Clave, "hw-1", "PC"));

        var resp = await cliente.PostAsJsonAsync("/validar",
            new ValidarRequest(lic.Id.ToString(), "hw-1"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var cuerpo = await resp.Content.ReadFromJsonAsync<ValidarResponse>();
        Assert.Equal("activa", cuerpo!.Estado);
    }

    [Fact]
    public async Task Validar_Revocada_403()
    {
        await using var f = CrearFabrica();
        var lic = await Sembrar(f);
        var cliente = f.CreateClient();

        var resp = await cliente.PostAsJsonAsync("/validar",
            new ValidarRequest(lic.Id.ToString(), "hw-desconocido"));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }
}
