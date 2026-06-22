using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;
using Resumenes.Core.Orquestacion;
using Resumenes.Ui.Servicios;
using Resumenes.Ui.ViewModels;
using Xunit;

namespace Resumenes.Ui.Tests;

public class InicioVmTests
{
    private class RepoFake : IRepositorioEstado
    {
        private readonly List<Analisis> _lista;
        public RepoFake(IEnumerable<Analisis> lista) => _lista = lista.ToList();
        public void InicializarEsquema() { }
        public Analisis? ObtenerAnalisisPorFingerprint(string fp) => null;
        public void GuardarAnalisis(Analisis a) { }
        public IReadOnlyList<Analisis> ListarAnalisis() =>
            _lista.OrderByDescending(a => a.ActualizadoEn).ToList();
        public void EliminarAnalisis(string id) => _lista.RemoveAll(a => a.Id == id);
        public Archivo? ObtenerArchivo(string id) => null;
        public void GuardarArchivo(Archivo a) { }
        public Tema? ObtenerTema(string id) => null;
        public void GuardarTema(Tema t) { }
        public void GuardarTemaArchivo(string temaId, string archivoId) { }
        public Unidad? ObtenerUnidad(string analisisId, string? archivoId, string? temaId, Etapa etapa) => null;
        public void GuardarUnidad(Unidad u) { }
        public string? ObtenerAjustePrompt(string clave) => null;
        public void GuardarAjustePrompt(string clave, string texto) { }
        public void EliminarAjustePrompt(string clave) { }
    }

    [Fact]
    public void Cargar_devuelve_dos_analisis_en_orden_desc()
    {
        var a1 = new Analisis("id1", "Primero", "c1", "fp1", EstadoAnalisis.Completado,
            new DateTime(2026, 1, 1), new DateTime(2026, 1, 1));
        var a2 = new Analisis("id2", "Segundo", "c2", "fp2", EstadoAnalisis.EnProceso,
            new DateTime(2026, 1, 2), new DateTime(2026, 1, 2));

        var repo = new RepoFake(new[] { a1, a2 });
        // ServicioNavegacion sin NavigationView real (no se usa en Cargar)
        var nav = new ServicioNavegacion();
        // Cargar() no usa el servicio, la configuración ni los diálogos: se pasan null! para este test.
        var vm = new InicioVm(repo, nav, null!, null!, null!);
        vm.Cargar();

        Assert.Equal(2, vm.Analisis.Count);
        Assert.Equal("id2", vm.Analisis[0].Id); // más reciente primero
        Assert.Equal("id1", vm.Analisis[1].Id);
    }
}
