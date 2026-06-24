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
        Assert.True(resultado.Claims.ContainsKey("iat"));
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

    [Theory]
    [InlineData("pem")]            // PEM bien formado (saltos reales)
    [InlineData("pem-escapado")]   // saltos como \n literales (caso típico de variable de entorno)
    [InlineData("pem-una-linea")]  // saltos colapsados a espacios
    [InlineData("base64-der")]     // solo el base64 del DER, sin headers (a prueba de pegado)
    public async Task Firmar_ToleraDistintosFormatosDeClave(string formato)
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pubPem = ec.ExportSubjectPublicKeyInfoPem();
        var priv = formato switch
        {
            "pem" => ec.ExportECPrivateKeyPem(),
            "pem-escapado" => ec.ExportECPrivateKeyPem().Replace("\n", "\\n"),
            "pem-una-linea" => ec.ExportECPrivateKeyPem().Replace("\r\n", "\n").Replace("\n", " "),
            "base64-der" => Convert.ToBase64String(ec.ExportECPrivateKey()),
            _ => throw new ArgumentOutOfRangeException(nameof(formato)),
        };

        var token = new FirmadorTokens(priv).Firmar("lic-9", "hw-9", "Ana");

        using var ecPub = ECDsa.Create();
        ecPub.ImportFromPem(pubPem);
        var resultado = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            IssuerSigningKey = new ECDsaSecurityKey(ecPub),
            ValidAlgorithms = ["ES256"],
        });

        Assert.True(resultado.IsValid, $"el formato '{formato}' no validó");
        Assert.Equal("lic-9", resultado.Claims["lic"]);
    }

    [Fact]
    public void Firmar_ClaveVacia_Lanza()
    {
        Assert.Throws<ArgumentException>(() => new FirmadorTokens("   ").Firmar("l", "h", "c"));
    }
}
