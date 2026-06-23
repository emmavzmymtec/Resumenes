using System.Windows;
using System.Windows.Controls;
using Resumenes.Ui.Servicios;
using Resumenes.Ui.ViewModels;

namespace Resumenes.Ui.Vistas;

/// <summary>
/// Pantalla de generación de resúmenes (Fase 3).
/// El ViewModel es el DataContext; el parámetro (ParametroTemas) se consume en Loaded y arranca la generación.
/// </summary>
public partial class VistaGenerando : Page
{
    private readonly GenerandoVm _vm;
    private readonly ServicioNavegacion _nav;

    public VistaGenerando(GenerandoVm vm, ServicioNavegacion nav)
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
        Loaded -= OnCargado;
        if (_nav.ConsumirParametro() is ParametroTemas param)
            _ = _vm.GenerarAsync(param.An, param.TemasDetectados, param.PromptResumen);
    }

    private void OnDescargado(object sender, RoutedEventArgs e)
    {
        Unloaded -= OnDescargado;
        _vm.AlSalir(); // cancela la generación si se abandona la pantalla
    }
}
