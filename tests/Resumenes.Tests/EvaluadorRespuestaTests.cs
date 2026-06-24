using Resumenes.Core.Examenes;
using Resumenes.Core.Modelos;

namespace Resumenes.Tests;

public class EvaluadorRespuestaTests
{
    [Theory]
    [InlineData(10, 10, EstadoRespuesta.Correcta)]   // 100%
    [InlineData(8.5, 10, EstadoRespuesta.Correcta)]  // 85% exacto
    [InlineData(8.4, 10, EstadoRespuesta.Parcial)]   // 84%
    [InlineData(4, 10, EstadoRespuesta.Parcial)]     // 40% exacto
    [InlineData(3.9, 10, EstadoRespuesta.Incorrecta)]// 39%
    [InlineData(0, 10, EstadoRespuesta.Incorrecta)]  // 0%
    public void Estado_SegunUmbral(double obtenido, double puntos, EstadoRespuesta esperado)
        => Assert.Equal(esperado, EvaluadorRespuesta.Estado(obtenido, puntos));

    [Fact]
    public void Estado_PuntosCero_EsIncorrecta()
        => Assert.Equal(EstadoRespuesta.Incorrecta, EvaluadorRespuesta.Estado(0, 0));
}
