using Resumenes.Licencias.Api.Servicios;

namespace Resumenes.Licencias.Api.Tests;

public class GeneradorClavesTests
{
    [Fact]
    public void Generar_ProduceFormatoEsperado()
    {
        var clave = GeneradorClaves.Generar();

        Assert.StartsWith("RESU-", clave);
        Assert.True(GeneradorClaves.EsFormatoValido(clave), $"clave invalida: {clave}");
        // RESU + 4 grupos de 5 = 4 + 4*(1+5) = 28 chars
        Assert.Equal(28, clave.Length);
    }

    [Fact]
    public void Generar_NoUsaCaracteresAmbiguos()
    {
        for (var i = 0; i < 50; i++)
        {
            var cuerpo = GeneradorClaves.Generar().Replace("RESU-", "").Replace("-", "");
            Assert.DoesNotContain('I', cuerpo);
            Assert.DoesNotContain('L', cuerpo);
            Assert.DoesNotContain('O', cuerpo);
            Assert.DoesNotContain('U', cuerpo);
        }
    }

    [Fact]
    public void Generar_ProduceClavesDistintas()
    {
        var a = GeneradorClaves.Generar();
        var b = GeneradorClaves.Generar();
        Assert.NotEqual(a, b);
    }

    [Theory]
    [InlineData("")]
    [InlineData("RESU-12345")]
    [InlineData("XXXX-ABCDE-ABCDE-ABCDE-ABCDE")]
    [InlineData("RESU-ABCDE-ABCDE-ABCDE-ABCDI")] // I no permitida
    [InlineData("resu-abcde-abcde-abcde-abcde")] // minúsculas
    public void EsFormatoValido_RechazaInvalidas(string clave)
    {
        Assert.False(GeneradorClaves.EsFormatoValido(clave));
    }
}
