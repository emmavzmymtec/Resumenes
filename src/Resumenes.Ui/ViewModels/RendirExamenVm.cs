using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;
using Resumenes.Ui.Servicios;

namespace Resumenes.Ui.ViewModels;

public partial class RendirExamenVm : VistaModeloBase
{
    private readonly IRepositorioExamenes _repo;
    private readonly IServicioExamenes _svc;
    private readonly ServicioNavegacion? _nav;
    private readonly DispatcherTimer _timer;
    private Analisis? _an;
    private string _examenId = "";
    private int _segundosRestantes;

    public ObservableCollection<PreguntaRendirVm> Preguntas { get; } = new();
    [ObservableProperty][NotifyPropertyChangedFor(nameof(NumeroPreguntaActual))] private int _indiceActual;
    [ObservableProperty] private PreguntaRendirVm? _actual;
    [ObservableProperty] private string _textoTiempo = "00:00";
    [ObservableProperty] private bool _entregando;

    public int NumeroPreguntaActual => IndiceActual + 1;

    public RendirExamenVm(IRepositorioExamenes repo, IServicioExamenes svc, ServicioNavegacion? nav = null)
    {
        _repo = repo; _svc = svc; _nav = nav;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = System.TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tictac();
    }

    /// <summary>Carga el examen: lee el tiempo límite del ConfigJson del examen.</summary>
    public void Cargar(string examenId, Analisis an)
    {
        var examen = _repo.ObtenerExamen(examenId);
        int tiempo = 0;
        try
        {
            if (examen is not null)
                tiempo = JsonSerializer.Deserialize<ConfigExamen>(examen.ConfigJson)?.TiempoLimiteMin ?? 0;
        }
        catch { }
        CargarInterno(examenId, an, tiempo);
    }

    /// <summary>Overload para tests: permite inyectar el tiempo directamente.</summary>
    public void Cargar(string examenId, Analisis an, int tiempoLimiteMin) => CargarInterno(examenId, an, tiempoLimiteMin);

    private void CargarInterno(string examenId, Analisis an, int tiempoLimiteMin)
    {
        _examenId = examenId; _an = an;
        Preguntas.Clear();
        foreach (var p in _repo.ListarPreguntas(examenId)) Preguntas.Add(new PreguntaRendirVm(p));
        IndiceActual = 0;
        Actual = Preguntas.Count > 0 ? Preguntas[0] : null;
        _segundosRestantes = tiempoLimiteMin * 60;
        ActualizarTiempo();
        if (tiempoLimiteMin > 0) _timer.Start();
    }

    private void Tictac()
    {
        _segundosRestantes--;
        ActualizarTiempo();
        if (_segundosRestantes <= 0) { _timer.Stop(); _ = EntregarAsync(); }
    }

    private void ActualizarTiempo()
    {
        var t = System.TimeSpan.FromSeconds(System.Math.Max(0, _segundosRestantes));
        TextoTiempo = t.ToString(@"mm\:ss");
    }

    [RelayCommand]
    private void Siguiente()
    {
        GuardarActual();
        if (IndiceActual < Preguntas.Count - 1) Actual = Preguntas[++IndiceActual];
    }

    [RelayCommand]
    private void Anterior()
    {
        GuardarActual();
        if (IndiceActual > 0) Actual = Preguntas[--IndiceActual];
    }

    /// <summary>Persiste la respuesta de la pregunta actual (autoguardado, upsert por id determinista).</summary>
    public void GuardarActual()
    {
        if (Actual is null) return;
        _repo.GuardarRespuesta(new RespuestaUsuario
        {
            Id = $"{_examenId}:{Actual.Pregunta.Id}",
            ExamenId = _examenId,
            PreguntaId = Actual.Pregunta.Id,
            RespuestaJson = Actual.ConstruirRespuestaJson()
        });
    }

    [RelayCommand]
    public async Task EntregarAsync()
    {
        if (Entregando) return;
        Entregando = true;
        _timer.Stop();
        // Persistir todas las respuestas (upsert determinista)
        foreach (var pr in Preguntas)
            _repo.GuardarRespuesta(new RespuestaUsuario
            {
                Id = $"{_examenId}:{pr.Pregunta.Id}",
                ExamenId = _examenId,
                PreguntaId = pr.Pregunta.Id,
                RespuestaJson = pr.ConstruirRespuestaJson()
            });
        await _svc.FinalizarYCorregirAsync(_examenId, System.Threading.CancellationToken.None);
        if (_an is not null)
            _nav?.Navegar<Resumenes.Ui.Vistas.VistaResultadoExamen>(new ParametroResultadoExamen(_examenId, _an));
        Entregando = false;
    }
}
