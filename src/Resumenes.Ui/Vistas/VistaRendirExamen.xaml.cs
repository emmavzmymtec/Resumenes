using System.Windows;
using System.Windows.Controls;
using Resumenes.Ui.Servicios;
using Resumenes.Ui.ViewModels;

namespace Resumenes.Ui.Vistas;

public partial class VistaRendirExamen : Page
{
    private readonly RendirExamenVm _vm;
    private readonly ServicioNavegacion _nav;

    public VistaRendirExamen(RendirExamenVm vm, ServicioNavegacion nav)
    {
        _vm = vm; _nav = nav;
        InitializeComponent();
        DataContext = vm;
        Loaded += OnCargado;
        Unloaded += OnDescargado;
    }

    private void OnCargado(object sender, RoutedEventArgs e)
    {
        Loaded -= OnCargado;
        if (_nav.ConsumirParametro() is ParametroRendir p) _vm.Cargar(p.ExamenId, p.An);
    }

    private void OnDescargado(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnDescargado;
        _vm.AlSalir(); // detiene el timer y guarda el borrador al abandonar la pantalla
    }
}
