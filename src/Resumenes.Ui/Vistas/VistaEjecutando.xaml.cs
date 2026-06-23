using System.Windows;
using System.Windows.Controls;
using Resumenes.Ui.Servicios;
using Resumenes.Ui.ViewModels;

namespace Resumenes.Ui.Vistas;

/// <summary>
/// Pantalla de ejecución del análisis con progreso en vivo.
/// El ViewModel es el DataContext; el parámetro de navegación (ParametroEjecucion)
/// se consume en Loaded y arranca el pipeline.
/// </summary>
public partial class VistaEjecutando : Page
{
    private readonly EjecutandoVm _vm;
    private readonly ServicioNavegacion _nav;

    public VistaEjecutando(EjecutandoVm vm, ServicioNavegacion nav)
    {
        _vm = vm;
        _nav = nav;
        InitializeComponent();
        DataContext = vm;
        Loaded += OnCargado;
        Unloaded += OnDescargado;
    }

    private void OnCargado(object sender, RoutedEventArgs e)
    {
        Loaded -= OnCargado; // una sola vez por instancia
        if (_nav.ConsumirParametro() is ParametroEjecucion param)
            _ = _vm.EjecutarAsync(param.An, param.Prompt);
    }

    private void OnDescargado(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnDescargado;
        _vm.AlSalir(); // cancela el pipeline si se abandona la pantalla
    }
}
