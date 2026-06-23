using System.Windows;
using System.Windows.Controls;
using Resumenes.Ui.Servicios;
using Resumenes.Ui.ViewModels;

namespace Resumenes.Ui.Vistas;

public partial class VistaResultadoExamen : Page
{
    private readonly ResultadoExamenVm _vm;
    private readonly ServicioNavegacion _nav;

    public VistaResultadoExamen(ResultadoExamenVm vm, ServicioNavegacion nav)
    {
        _vm = vm; _nav = nav;
        InitializeComponent();
        DataContext = vm;
        Loaded += OnCargado;
    }

    private void OnCargado(object sender, RoutedEventArgs e)
    {
        Loaded -= OnCargado;
        if (_nav.ConsumirParametro() is ParametroResultadoExamen p) _vm.Cargar(p.ExamenId, p.An);
    }
}
