using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;
using Resumenes.Ui.Servicios;
using Resumenes.Ui.Vistas;

namespace Resumenes.Ui.ViewModels;

public partial class ExamenesVm : VistaModeloBase
{
    private readonly IServicioExamenes _svc;
    private readonly IRepositorioExamenes _repo;
    private readonly ServicioNavegacion _nav;
    private Analisis? _an;

    [ObservableProperty] private ObservableCollection<ExamenItemVm> _examenes = new();

    public ExamenesVm(IServicioExamenes svc, IRepositorioExamenes repo, ServicioNavegacion nav)
    {
        _svc = svc; _repo = repo; _nav = nav;
    }

    public void Cargar(Analisis an)
    {
        _an = an;
        Examenes = new ObservableCollection<ExamenItemVm>(
            _svc.Historial(an.Id).Select(e => new ExamenItemVm(e)));
    }

    [RelayCommand]
    private void NuevoExamen()
    {
        if (_an is not null) _nav.Navegar<VistaCrearExamen>(new ParametroCrearExamen(_an));
    }

    /// <summary>Vuelve a la pantalla de Resultados del análisis.</summary>
    [RelayCommand]
    private void Volver()
    {
        if (_an is not null) _nav?.Navegar<VistaResultados>(new ParametroResultados(_an));
    }

    [RelayCommand]
    private void Rendir(ExamenItemVm? item)
    {
        if (item is null || _an is null) return;
        _nav.Navegar<VistaRendirExamen>(new ParametroRendir(item.Id, _an));
    }

    [RelayCommand]
    private void VerResultado(ExamenItemVm? item)
    {
        if (item is null || _an is null) return;
        _nav.Navegar<VistaResultadoExamen>(new ParametroResultadoExamen(item.Id, _an));
    }

    [RelayCommand]
    private void Eliminar(ExamenItemVm? item)
    {
        if (item is null) return;
        _repo.EliminarExamen(item.Id);
        Examenes.Remove(item);
    }
}

public sealed class ExamenItemVm
{
    private readonly Examen _e;
    public ExamenItemVm(Examen e) => _e = e;
    public string Id => _e.Id;
    public string Titulo => _e.Titulo;
    public string FechaLegible => _e.CreadoEn.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
    public bool EstaCorregido => _e.Estado == EstadoExamen.Corregido;
    public bool EnCurso => _e.Estado is EstadoExamen.Borrador or EstadoExamen.EnCurso;
    public string NotaLegible => _e.Nota is null ? "—"
        : $"Nota {_e.Nota:0.#} ({_e.Porcentaje:0}%)" + (_e.Aprobado == true ? " ✓" : "");
}
