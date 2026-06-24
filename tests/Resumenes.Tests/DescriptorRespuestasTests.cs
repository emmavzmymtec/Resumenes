using Resumenes.Core.Examenes;
using Resumenes.Core.Modelos;

namespace Resumenes.Tests;

public class DescriptorRespuestasTests
{
    private static PreguntaExamen P(TipoPregunta t, string datos)
        => new() { Id = "p", ExamenId = "e", Enunciado = "?", Puntos = 1, Tipo = t, DatosJson = datos };

    [Fact]
    public void McUna_MuestraOpcionElegidaYCorrecta()
    {
        var p = P(TipoPregunta.McUna, "{\"opciones\":[{\"texto\":\"Roma\",\"correcta\":false},{\"texto\":\"París\",\"correcta\":true}]}");
        var (u, c) = DescriptorRespuestas.Describir(p, "0"); // eligió Roma
        Assert.Equal("Roma", u);
        Assert.Equal("París", c);
    }

    [Fact]
    public void McVarias_MuestraVariasYCorrectas()
    {
        var p = P(TipoPregunta.McVarias, "{\"opciones\":[{\"texto\":\"A\",\"correcta\":true},{\"texto\":\"B\",\"correcta\":false},{\"texto\":\"C\",\"correcta\":true}]}");
        var (u, c) = DescriptorRespuestas.Describir(p, "[0,1]"); // eligió A y B
        Assert.Contains("A", u); Assert.Contains("B", u);
        Assert.Contains("A", c); Assert.Contains("C", c);
    }

    [Fact]
    public void Completar_MuestraDadasYEsperadas()
    {
        var p = P(TipoPregunta.Completar, "{\"texto\":\"__ y __\",\"respuestas\":[\"sol\",\"luna\"]}");
        var (u, c) = DescriptorRespuestas.Describir(p, "[\"sol\",\"X\"]");
        Assert.Contains("sol", u); Assert.Contains("X", u);
        Assert.Contains("sol", c); Assert.Contains("luna", c);
    }

    [Fact]
    public void Emparejar_MuestraParesLegibles()
    {
        var p = P(TipoPregunta.Emparejar, "{\"izquierda\":[\"Mitocondria\"],\"derecha\":[\"Respiración\",\"Fotosíntesis\"],\"pares\":[[0,0]]}");
        var (u, c) = DescriptorRespuestas.Describir(p, "[[0,1]]"); // emparejó mal
        Assert.Contains("Mitocondria", u); Assert.Contains("Fotosíntesis", u);
        Assert.Contains("Mitocondria", c); Assert.Contains("Respiración", c);
    }

    [Fact]
    public void VfJustificado_MuestraVfYJustificacionEsperada()
    {
        var p = P(TipoPregunta.VfJustificado, "{\"afirmacion\":\"La tierra es plana\",\"esVerdadero\":false,\"justificacion\":\"Es un esferoide\"}");
        var (u, c) = DescriptorRespuestas.Describir(p, "{\"vf\":true,\"justificacion\":\"creo que sí\"}");
        Assert.Contains("Verdadero", u);
        Assert.Contains("Falso", c);
        Assert.Contains("esferoide", c);
    }

    [Fact]
    public void Desarrollo_MuestraTextoYRespuestaEsperada()
    {
        var p = P(TipoPregunta.Desarrollo, "{\"criterios\":\"x\",\"respuestaEsperada\":\"Lo esperado es Y\"}");
        var (u, c) = DescriptorRespuestas.Describir(p, "\"mi respuesta\"");
        Assert.Equal("mi respuesta", u);
        Assert.Equal("Lo esperado es Y", c);
    }

    [Fact]
    public void DatosOResp_Invalidos_NoLanza()
    {
        var p = P(TipoPregunta.McUna, "no es json");
        var (u, c) = DescriptorRespuestas.Describir(p, "tampoco");
        Assert.NotNull(u); Assert.NotNull(c);
    }
}
