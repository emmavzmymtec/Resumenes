using Resumenes.Ui.Servicios.Licencia;

namespace Resumenes.Ui.Tests;

public class ServicioHwidWindowsTests
{
    [Fact]
    public void ObtenerIdEquipo_EsHex64_YDeterministaParaLaMismaSemilla()
    {
        var a = new ServicioHwidWindows("semilla-fija").ObtenerIdEquipo();
        var b = new ServicioHwidWindows("semilla-fija").ObtenerIdEquipo();

        Assert.Equal(a, b);
        Assert.Equal(64, a.Length); // SHA-256 en hex
        Assert.Matches("^[0-9a-f]{64}$", a);
    }

    [Fact]
    public void ObtenerIdEquipo_SemillasDistintas_DanIdsDistintos()
    {
        var a = new ServicioHwidWindows("equipo-A").ObtenerIdEquipo();
        var b = new ServicioHwidWindows("equipo-B").ObtenerIdEquipo();
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void ObtenerIdEquipo_RealDelRegistro_NoVacio()
    {
        // En Windows real lee el MachineGuid; debe devolver un hex de 64.
        var id = new ServicioHwidWindows().ObtenerIdEquipo();
        Assert.Matches("^[0-9a-f]{64}$", id);
    }
}
