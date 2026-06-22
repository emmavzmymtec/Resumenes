using System.IO;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;
using Resumenes.Core.Orquestacion;
using Resumenes.Ui.Servicios;
using Resumenes.Ui.ViewModels;
using Xunit;

namespace Resumenes.Ui.Tests;

public class ConfirmarTemasVmTests
{
    // ── Fake mínimo del repositorio de estado para evitar I/O ────────────
    private class RepoFake : IRepositorioEstado
    {
        public void InicializarEsquema() { }
        public Analisis? ObtenerAnalisisPorFingerprint(string fp) => null;
        public void GuardarAnalisis(Analisis a) { }
        public Resumenes.Core.Modelos.Archivo? ObtenerArchivo(string id) => null;
        public void GuardarArchivo(Resumenes.Core.Modelos.Archivo archivo) { }
        public Resumenes.Core.Modelos.Tema? ObtenerTema(string id) => null;
        public void GuardarTema(Resumenes.Core.Modelos.Tema t) { }
        public void GuardarTemaArchivo(string temaId, string archivoId) { }
        public IReadOnlyList<Analisis> ListarAnalisis() => Array.Empty<Analisis>();
        public void EliminarAnalisis(string id) { }
        public Resumenes.Core.Modelos.Unidad? ObtenerUnidad(string analisisId, string? archivoId, string? temaId, Resumenes.Core.Modelos.Etapa etapa) => null;
        public void GuardarUnidad(Resumenes.Core.Modelos.Unidad u) { }
        public string? ObtenerAjustePrompt(string clave) => null;
        public void GuardarAjustePrompt(string clave, string texto) { }
        public void EliminarAjustePrompt(string clave) { }
    }

    // ── Fake de ServicioNavegacion que no hace nada ──────────────────────
    private class NavFake : ServicioNavegacion
    {
        public Type? UltimoTipo { get; private set; }
        public object? UltimoParametro { get; private set; }

        public new void Navegar<TVista>(object? parametro = null) where TVista : class
        {
            UltimoTipo = typeof(TVista);
            UltimoParametro = parametro;
        }

        public void CapturarNavegacion(Type tipo, object? param)
        {
            UltimoTipo = tipo;
            UltimoParametro = param;
        }
    }

    private static Analisis CrearAnalisis() =>
        new("an1", "Test", "c:/orig", "fp1", EstadoAnalisis.EnProceso,
            DateTime.UtcNow, DateTime.UtcNow);

    private static IReadOnlyList<TemaDetectado> CrearTemas() =>
        new List<TemaDetectado>
        {
            new("t1", "Tema Uno", 1, new List<string> { "a.txt" }),
            new("t2", "Tema Dos", 2, new List<string> { "b.txt" }),
            new("t3", "Tema Tres", 3, new List<string> { "c.txt" }),
        };

    [Fact]
    public void Carga_temas_iniciales_correctamente()
    {
        var an = CrearAnalisis();
        var temas = CrearTemas();
        // Usar directorio temporal para que no toque el sistema de archivos real
        var dir = Path.Combine(Path.GetTempPath(), $"ct_{Guid.NewGuid():N}");
        var vm = new ConfirmarTemasVm(an with { Id = Path.GetFileName(dir) }, temas, null!, dir);
        try
        {
            Assert.Equal(3, vm.Temas.Count);
            Assert.Equal("Tema Uno", vm.Temas[0].Nombre);
            Assert.Equal("Tema Dos", vm.Temas[1].Nombre);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Editar_nombre_refleja_en_ObtenerTemasConfirmados()
    {
        var an = CrearAnalisis();
        var temas = CrearTemas();
        var dir = Path.Combine(Path.GetTempPath(), $"ct_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var vm = new ConfirmarTemasVm(an with { Id = Path.GetFileName(dir) }, temas, null!, dir);
        try
        {
            // Renombrar el primer tema
            vm.Temas[0].Nombre = "Tema Renombrado";

            var confirmados = vm.ObtenerTemasConfirmados();

            Assert.Equal(3, confirmados.Count);
            Assert.Equal("Tema Renombrado", confirmados[0].Nombre);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Quitar_tema_refleja_en_ObtenerTemasConfirmados()
    {
        var an = CrearAnalisis();
        var temas = CrearTemas();
        var dir = Path.Combine(Path.GetTempPath(), $"ct_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var vm = new ConfirmarTemasVm(an with { Id = Path.GetFileName(dir) }, temas, null!, dir);
        try
        {
            // Quitar el segundo tema
            var temaAQuitar = vm.Temas[1];
            vm.QuitarCommand.Execute(temaAQuitar);

            var confirmados = vm.ObtenerTemasConfirmados();

            Assert.Equal(2, confirmados.Count);
            // El orden se recalcula: 1, 2
            Assert.Equal(1, confirmados[0].Orden);
            Assert.Equal(2, confirmados[1].Orden);
            // No debe contener "Tema Dos"
            Assert.DoesNotContain(confirmados, t => t.Nombre == "Tema Dos");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Editar_nombre_y_quitar_combinados()
    {
        var an = CrearAnalisis();
        var temas = CrearTemas();
        var dir = Path.Combine(Path.GetTempPath(), $"ct_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var vm = new ConfirmarTemasVm(an with { Id = Path.GetFileName(dir) }, temas, null!, dir);
        try
        {
            vm.Temas[0].Nombre = "Nuevo Nombre";
            vm.QuitarCommand.Execute(vm.Temas[1]); // quitar "Tema Dos"

            var confirmados = vm.ObtenerTemasConfirmados();

            Assert.Equal(2, confirmados.Count);
            Assert.Equal("Nuevo Nombre", confirmados[0].Nombre);
            Assert.Equal("Tema Tres", confirmados[1].Nombre);
            Assert.Equal(1, confirmados[0].Orden);
            Assert.Equal(2, confirmados[1].Orden);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
