using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;
using Resumenes.Ui.Servicios;
using Resumenes.Ui.Vistas;

namespace Resumenes.Ui.ViewModels;

public partial class CrearExamenVm : VistaModeloBase
{
    private readonly IServicioExamenes _svc;
    private readonly ServicioNavegacion _nav;
    private Analisis? _an;

    [ObservableProperty] private string _titulo = "Examen";
    [ObservableProperty] private int _cantidadMcUna = 5;
    [ObservableProperty] private int _cantidadMcVarias;
    [ObservableProperty] private int _cantidadVf;
    [ObservableProperty] private int _cantidadDesarrollo;
    [ObservableProperty] private int _cantidadDesarrolloItems;
    [ObservableProperty] private int _cantidadCompletar;
    [ObservableProperty] private int _cantidadEmparejar;
    [ObservableProperty] private string _dificultad = "media";   // facil|media|dificil
    [ObservableProperty] private double _puntosTotales = 10;
    [ObservableProperty] private int _tiempoLimiteMin = 30;
    [ObservableProperty] private bool _fuenteRapida = true;      // true=rapido(resúmenes) / false=completo
    [ObservableProperty] private bool _generando;
    [ObservableProperty] private string _mensajeError = string.Empty;

    public CrearExamenVm(IServicioExamenes svc, ServicioNavegacion nav) { _svc = svc; _nav = nav; }

    public void Cargar(Analisis an) => _an = an;

    [RelayCommand]
    private async Task Crear()
    {
        if (_an is null) return;
        var tipos = new List<CantidadPorTipo>
        {
            new(TipoPregunta.McUna, CantidadMcUna),
            new(TipoPregunta.McVarias, CantidadMcVarias),
            new(TipoPregunta.VfJustificado, CantidadVf),
            new(TipoPregunta.Desarrollo, CantidadDesarrollo),
            new(TipoPregunta.DesarrolloItems, CantidadDesarrolloItems),
            new(TipoPregunta.Completar, CantidadCompletar),
            new(TipoPregunta.Emparejar, CantidadEmparejar),
        }.Where(t => t.Cantidad > 0).ToList();

        if (tipos.Count == 0) { MensajeError = "Elegí al menos una pregunta."; return; }

        Generando = true; MensajeError = string.Empty;
        try
        {
            var cfg = new ConfigExamen(tipos, System.Array.Empty<string>(), Dificultad,
                PuntosTotales, TiempoLimiteMin, FuenteRapida ? "rapido" : "completo");
            var examen = await _svc.CrearAsync(_an.Id, Titulo.Trim(), cfg, System.Threading.CancellationToken.None);
            _nav.Navegar<VistaRendirExamen>(new ParametroRendir(examen.Id, _an));
        }
        catch (System.Exception ex) { MensajeError = $"No se pudo crear el examen: {ex.Message}"; }
        finally { Generando = false; }
    }

    /// <summary>Vuelve al historial de exámenes del análisis sin crear nada.</summary>
    [RelayCommand]
    private void Volver()
    {
        if (_an is not null) _nav?.Navegar<VistaExamenes>(new ParametroExamenes(_an));
    }

    internal Task CrearParaTestAsync() => Crear();
}
