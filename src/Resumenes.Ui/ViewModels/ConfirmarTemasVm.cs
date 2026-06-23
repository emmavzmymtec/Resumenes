using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Resumenes.Core.Modelos;
using Resumenes.Ui.Servicios;
using Resumenes.Ui.Vistas;

namespace Resumenes.Ui.ViewModels;

/// <summary>
/// ViewModel de la pantalla de confirmación de temas.
/// Permite renombrar, quitar y fusionar temas antes de iniciar la Fase 3.
/// </summary>
public partial class ConfirmarTemasVm : VistaModeloBase
{
    private readonly ServicioNavegacion _nav;
    private readonly string _rutaWorkspace;
    private Analisis? _analisis;

    /// <summary>Lista editable de temas que el usuario puede modificar.</summary>
    public ObservableCollection<TemaEditableVm> Temas { get; } = new();

    [ObservableProperty]
    private string _mensajeError = string.Empty;

    /// <summary>Indicaciones opcionales del alumno para el estilo/contenido de los resúmenes (Fase 3).</summary>
    [ObservableProperty]
    private string _promptResumen = string.Empty;

    /// <summary>
    /// Constructor para uso en DI (sin temas; cargar via <see cref="CargarTemas"/> o ctor con temas).
    /// </summary>
    public ConfirmarTemasVm(ServicioNavegacion nav, string rutaWorkspace)
    {
        _nav = nav;
        _rutaWorkspace = rutaWorkspace;
        // Reevaluar CanExecute de Generar/Fusionar cuando cambia la lista de temas
        // (Temas es ObservableCollection, no [ObservableProperty]: el toolkit no lo hace solo).
        Temas.CollectionChanged += (_, _) =>
        {
            GenerarCommand.NotifyCanExecuteChanged();
            FusionarCommand.NotifyCanExecuteChanged();
        };
    }

    /// <summary>
    /// Constructor para tests (permite inyectar nav=null y workspace).
    /// </summary>
    public ConfirmarTemasVm(
        Analisis analisis,
        IReadOnlyList<TemaDetectado> temas,
        ServicioNavegacion nav,
        string rutaWorkspace)
        : this(nav, rutaWorkspace)
    {
        CargarTemas(analisis, temas);
    }

    /// <summary>
    /// Carga (o recarga) los temas desde el resultado de la Fase 2.
    /// </summary>
    public void CargarTemas(Analisis analisis, IReadOnlyList<TemaDetectado> temas)
    {
        _analisis = analisis;
        Temas.Clear();
        foreach (var tema in temas)
            Temas.Add(new TemaEditableVm(tema));
        // Pre-cargar el último prompt de resumen usado para este análisis (al continuar/regenerar).
        PromptResumen = LeerPromptResumen(analisis.Id);
    }

    /// <summary>Lee el prompt de resumen persistido para el análisis, o "" si no hay.</summary>
    private string LeerPromptResumen(string analisisId)
    {
        try
        {
            var ruta = System.IO.Path.Combine(_rutaWorkspace, "analisis", analisisId, "resumen-prompt.txt");
            return System.IO.File.Exists(ruta) ? System.IO.File.ReadAllText(ruta) : string.Empty;
        }
        catch { return string.Empty; }
    }

    // ── Comandos ─────────────────────────────────────────────────────────

    /// <summary>Quita un tema de la lista.</summary>
    [RelayCommand]
    private void Quitar(TemaEditableVm? tema)
    {
        if (tema is null) return;
        Temas.Remove(tema);
    }

    /// <summary>Vuelve a Inicio descartando el flujo de confirmación de temas.</summary>
    [RelayCommand]
    private void Volver() => _nav?.Navegar<VistaInicio>();

    /// <summary>
    /// Fusiona los dos temas más recientes que estén seleccionados
    /// (implementación básica: fusiona primero con segundo en la lista).
    /// Para la UI completa se usaría selección múltiple en ListBox.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeFusionar))]
    private void Fusionar()
    {
        if (Temas.Count < 2) return;

        // Fusiona los últimos dos temas seleccionados (primero y segundo de la lista como fallback).
        var primero = Temas[0];
        var segundo = Temas[1];

        var temaFusionado = primero.FusionarCon(segundo, 1);
        var vmFusionado = new TemaEditableVm(temaFusionado);

        Temas.Insert(0, vmFusionado);
        Temas.Remove(primero);
        Temas.Remove(segundo);
    }

    private bool PuedeFusionar() => Temas.Count >= 2;

    /// <summary>
    /// Genera los resúmenes: persiste temas.json y navega a VistaGenerando.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuedeGenerar))]
    private void Generar()
    {
        if (_analisis is null) return;
        MensajeError = string.Empty;
        try
        {
            var temasConfirmados = ObtenerTemasConfirmados();
            EscribirTemasJson(temasConfirmados);
            _nav.Navegar<VistaGenerando>(new ParametroTemas(_analisis, temasConfirmados, PromptResumen));
        }
        catch (Exception ex)
        {
            MensajeError = $"Error al generar: {ex.Message}";
        }
    }

    private bool PuedeGenerar() => Temas.Count > 0 && _analisis is not null;

    // ── Método público para tests ─────────────────────────────────────────

    /// <summary>
    /// Devuelve los temas confirmados con el orden recalculado (1-based, consecutivo).
    /// </summary>
    public IReadOnlyList<TemaDetectado> ObtenerTemasConfirmados()
    {
        return Temas
            .Select((vm, idx) => vm.ObtenerTemaEditado(idx + 1))
            .ToList();
    }

    // ── Helpers privados ──────────────────────────────────────────────────

    private void EscribirTemasJson(IReadOnlyList<TemaDetectado> temas)
    {
        if (_analisis is null) return;
        var dirAnalisis = System.IO.Path.Combine(
            _rutaWorkspace, "analisis", _analisis.Id);
        System.IO.Directory.CreateDirectory(dirAnalisis);
        var rutaTemas = System.IO.Path.Combine(dirAnalisis, "temas.json");

        var dto = new TemasJsonDto(temas.Select(t =>
            new TemaJsonDto(t.Id, t.Nombre, t.Orden, t.Archivos)).ToList());
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(rutaTemas, json, System.Text.Encoding.UTF8);
    }

    // ── DTOs para temas.json ──────────────────────────────────────────────

    private record TemaJsonDto(string id, string nombre, int orden, IReadOnlyList<string> archivos);
    private record TemasJsonDto(IReadOnlyList<TemaJsonDto> temas);
}
