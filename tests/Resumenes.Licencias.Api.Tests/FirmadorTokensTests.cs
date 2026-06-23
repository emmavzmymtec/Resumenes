using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Resumenes.Licencias.Api.Servicios;

namespace Resumenes.Licencias.Api.Tests;

public class FirmadorTokensTests
{
    private static (string priv, string pub) ParDeClaves()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (ec.ExportECPrivateKeyPem(), ec.ExportSubjectPublicKeyInfoPem());
    }

    [Fact]
    public async Task Firmar_ProduceTokenVerificableConLaPublica()
    {
        var (priv, pub) = ParDeClaves();
        var firmador = new FirmadorTokens(priv);

        var token = firmador.Firmar("lic-123", "hw-abc", "Juan Perez");

        using var ecPub = ECDsa.Create();
        ecPub.ImportFromPem(pub);
        var handler = new JsonWebTokenHandler();
        var resultado = await handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            IssuerSigningKey = new ECDsaSecurityKey(ecPub),
            ValidAlgorithms = ["ES256"],
        });

        Assert.True(resultado.IsValid);
        Assert.Equal("lic-123", resultado.Claims["lic"]);
        Assert.Equal("hw-abc", resultado.Claims["hwid"]);
        Assert.Equal("Juan Perez", resultado.Claims["sub"]);
    }

    [Fact]
    public async Task Firmar_FirmaAlteradaNoValida()
    {
        var (priv, _) = ParDeClaves();
        var (_, otraPub) = ParDeClaves(); // pública que NO corresponde
        var token = new FirmadorTokens(priv).Firmar("lic-1", "hw-1", "X");

        using var ecOtra = ECDsa.Create();
        ecOtra.ImportFromPem(otraPub);
        var resultado = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            IssuerSigningKey = new ECDsaSecurityKey(ecOtra),
            ValidAlgorithms = ["ES256"],
        });

        Assert.False(resultado.IsValid);
    }
}
