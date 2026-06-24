using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Resumenes.Ui.ViewModels;
using Wpf.Ui.Controls;

namespace Resumenes.Ui.Vistas;

public partial class VentanaActivacion : FluentWindow
{
    public VentanaActivacion(ActivacionVm vm)
    {
        InitializeComponent();
        DataContext = vm;
        // Al activar con éxito, abrir la app y cerrar esta ventana.
        vm.ActivacionExitosa = () =>
        {
            var main = App.Servicios.GetRequiredService<MainWindow>();
            Application.Current.MainWindow = main;
            main.Show();
            Close();
        };
    }

    // Abre los enlaces del disclaimer (mailto / wa.me) en la app por defecto del sistema.
    private void AbrirEnlace(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // Si no hay handler para mailto/https, no rompemos la ventana de activación.
        }
        e.Handled = true;
    }
}
