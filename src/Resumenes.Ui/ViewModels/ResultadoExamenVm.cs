using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;
using Resumenes.Ui.Servicios;
using Resumenes.Ui.Vistas;

namespace Resumenes.Ui.ViewModels;

public partial class ResultadoExamenVm : VistaModeloBase
{
    private readonly IRepositorioExamenes _repo;
    private readonly IServicioExamenes _svc;
    private readonly ServicioNavegacion _nav;
    private Analisis? _an;
    private Examen? _examen;

    [ObservableProperty] private string _titulo = "";
    [ObservableProperty] private string _notaLegible = "";
    [ObservableProperty] private string _porcentajeLegible = "";
    [ObservableProperty] private bool _aprobado;
    [ObservableProperty] private string _feedbackGeneral = "";
    public ObservableCollection<ItemResultadoVm> Detalle { get; } = new();

    public ResultadoExamenVm(IRepositorioExamenes repo, IServicioExamenes svc, ServicioNavegacion nav)
    { _repo = repo; _svc = svc; _nav = nav; }

    public void Cargar(string examenId, Analisis an)
    {
        _an = an;
        _examen = _repo.ObtenerExamen(examenId);
        Detalle.Clear();
        if (_examen is null) return;
        Titulo = _examen.Titulo;
        NotaLegible = _examen.Nota is null ? "—" : $"Nota: {_examen.Nota:0.#}";
        PorcentajeLegible = _examen.Porcentaje is null ? "" : $"{_examen.Porcentaje:0}% de acierto";
        Aprobado = _examen.Aprobado == true;
        FeedbackGeneral = _examen.FeedbackGeneral ?? "";

        var respuestas = _repo.ListarRespuestas(examenId).ToDictionary(r => r.PreguntaId);
        foreach (var p in _repo.ListarPreguntas(examenId))
        {
            respuestas.TryGetValue(p.Id, out var r);
            Detalle.Add(new ItemResultadoVm(p, r));
        }
    }

    [RelayCommand]
    private async Task Reintentar()
    {
        if (_examen is null || _an is null) return;
        try
        {
            var cfg = System.Text.Json.JsonSerializer.Deserialize<ConfigExamen>(_examen.ConfigJson);
            if (cfg is null) return;
            var nuevo = await _svc.CrearAsync(_an.Id, _examen.Titulo, cfg, System.Threading.CancellationToken.None);
            _nav.Navegar<VistaRendirExamen>(new ParametroRendir(nuevo.Id, _an));
        }
        catch { /* si falla, permanecer en el resultado */ }
    }
}

public sealed class ItemResultadoVm
{
    public ItemResultadoVm(PreguntaExamen p, RespuestaUsuario? r)
    {
        Enunciado = p.Enunciado;
        Puntos = p.Puntos;
        PuntosObtenidos = r?.PuntosObtenidos ?? 0;
        EsCorrecta = r?.Correcta == true;
        Feedback = r?.FeedbackIa ?? "";
        Ambigua = r?.Ambigua == true;
    }
    public string Enunciado { get; }
    public double Puntos { get; }
    public double PuntosObtenidos { get; }
    public bool EsCorrecta { get; }
    public string Feedback { get; }
    public bool Ambigua { get; }
    public string PuntajeLegible => $"{PuntosObtenidos:0.#}/{Puntos:0.#}";
}
