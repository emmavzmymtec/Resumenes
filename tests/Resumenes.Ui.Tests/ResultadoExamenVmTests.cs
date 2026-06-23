using Resumenes.Core.Modelos;
using Resumenes.Tests.Fakes;
using Resumenes.Ui.ViewModels;
using Xunit;

namespace Resumenes.Ui.Tests;

public class ResultadoExamenVmTests
{
    private static Analisis An() => new("an1","n","c","fp",EstadoAnalisis.Completado,DateTime.UtcNow,DateTime.UtcNow);

    [Fact]
    public void Cargar_MuestraNotaYDetalle()
    {
        var repo = new RepositorioExamenesEnMemoria();
        repo.GuardarExamen(new Examen { Id="e1", AnalisisId="an1", Titulo="P", Estado=EstadoExamen.Corregido,
            Nota=7, Porcentaje=70, Aprobado=true, FeedbackGeneral="Bien", CreadoEn=DateTime.UtcNow });
        repo.GuardarPregunta(new PreguntaExamen { Id="p1", ExamenId="e1", Orden=1, Tipo=TipoPregunta.McUna, Enunciado="¿?", Puntos=1, DatosJson="{}" });
        repo.GuardarRespuesta(new RespuestaUsuario { Id="r1", ExamenId="e1", PreguntaId="p1", Correcta=true, PuntosObtenidos=1 });

        var vm = new ResultadoExamenVm(repo, null!, null!);
        vm.Cargar("e1", An());

        Assert.Contains("7", vm.NotaLegible);
        Assert.True(vm.Aprobado);
        Assert.Single(vm.Detalle);
        Assert.True(vm.Detalle[0].EsCorrecta);
    }
}
