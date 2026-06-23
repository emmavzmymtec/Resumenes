using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;
using Resumenes.Core.Orquestacion;
using Resumenes.Ui.Servicios;
using Resumenes.Ui.Vistas;

namespace Resumenes.Ui.ViewModels;

/// <summary>
/// ViewModel de la pantalla "Generando resúmenes": corre Fase 3 (GenerarPorTemasAsync)
/// con el mismo patrón de progreso que EjecutandoVm.
/// </summary>
public partial class GenerandoVm : VistaModeloBase
{
    private readonly IServicioAnalisis _servicio;
    private readonly ServicioNavegacion _nav;
    private CancellationTokenSource _cts = new();
    private readonly Stopwatch _cronometro = new();
    private readonly DispatcherTimer _timerTiempo;

    // ── Propiedades de progreso ───────────────────────────────────────────

    [ObservableProperty]
    private string _textoEstado = string.Empty;

    [ObservableProperty]
    private bool _indeterminado = true;

    [ObservableProperty]
    private double _fraccionItem;

    /// <summary>Avance macro (0.0 – 1.0): cuántos temas se completaron.</summary>
    [ObservableProperty]
    private double _fraccionGlobal;

    /// <summary>Texto del tema en curso, p. ej. "Tema 2 de 5".</summary>
    [ObservableProperty]
    private string _textoItem = string.Empty;

    [ObservableProperty]
    private string _textoTiempo = "00:00";

    [ObservableProperty]
    private bool _completado;

    [ObservableProperty]
    private string _mensajeError = string.Empty;

    // ── Colecciones en vivo ───────────────────────────────────────────────

    public ObservableCollection<ItemProgresoVm> Items { get; } = new();
    public ObservableCollection<string> Log { get; } = new();

    // ── Constructor ───────────────────────────────────────────────────────

    public GenerandoVm(IServicioAnalisis servicio, ServicioNavegacion nav)
    {
        _servicio = servicio;
        _nav = nav;

        _timerTiempo = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timerTiempo.Tick += (_, _) =>
        {
            var elapsed = _cronometro.Elapsed;
            TextoTiempo = elapsed.TotalHours >= 1
                ? elapsed.ToString(@"h\:mm\:ss")
                : elapsed.ToString(@"mm\:ss");
        };
    }

    // ── Comando Cancelar ──────────────────────────────────────────────────

    [RelayCommand]
    private void Cancelar()
    {
        _cts.Cancel();
        AgregarLog("Cancelado por el usuario.");
        _nav?.Navegar<VistaInicio>();
    }

    /// <summary>
    /// Limpieza al abandonar la pantalla: cancela la generación en curso para que una
    /// generación abandonada no termine navegando sola a Resultados.
    /// </summary>
    public void AlSalir() => _cts.Cancel();

    // ── Método principal ──────────────────────────────────────────────────

    /// <summary>
    /// Ejecuta Fase 3 (GenerarPorTemasAsync).
    /// Debe llamarse desde el hilo de UI para que Progress&lt;T&gt; marshalee correctamente.
    /// Al terminar, navega a VistaResultados pasando el Analisis.
    /// </summary>
    public async Task GenerarAsync(Analisis an, IReadOnlyList<TemaDetectado> temas, string promptResumen = "")
    {
        // Ceder el control para no navegar de forma re-entrante dentro de la navegación
        // que está creando esta página (ver nota en EjecutandoVm.EjecutarAsync).
        await Task.Yield();

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _cronometro.Restart();
        TextoTiempo = "00:00";
        _timerTiempo.Start();
        Completado = false;
        MensajeError = string.Empty;
        FraccionGlobal = 0;
        TextoItem = string.Empty;

        var progreso = new Progress<ProgresoPaso>(ManejarProgreso);

        try
        {
            TextoEstado = "Generando resúmenes por tema…";
            Indeterminado = true;
            AgregarLog($"Fase 3 — Generando {temas.Count} resumen(es)…");

            var resultado = await _servicio.GenerarPorTemasAsync(an, temas, promptResumen, progreso, ct);

            TextoEstado = resultado.Error == 0
                ? $"Completado: {resultado.Ok} resumen(es) generado(s)."
                : $"Completado con {resultado.Error} error(es). OK: {resultado.Ok}.";
            Indeterminado = false;
            Completado = true;

            AgregarLog($"Fase 3 completada — OK: {resultado.Ok}, Errores: {resultado.Error}");
            foreach (var fallo in resultado.Fallos)
                AgregarLog($"  Fallo: {fallo}");

            // Navegar a resultados
            _nav.Navegar<VistaResultados>(new ParametroResultados(an));
        }
        catch (OperationCanceledException)
        {
            TextoEstado = "Cancelado.";
            Indeterminado = false;
            AgregarLog("Operación cancelada.");
        }
        catch (Exception ex)
        {
            TextoEstado = $"Error: {ex.Message}";
            MensajeError = ex.Message;
            Indeterminado = false;
            AgregarLog($"Error: {ex.Message}");
        }
        finally
        {
            _cronometro.Stop();
            _timerTiempo.Stop();
            var e = _cronometro.Elapsed;
            TextoTiempo = e.TotalHours >= 1 ? e.ToString(@"h\:mm\:ss") : e.ToString(@"mm\:ss");
        }
    }

    // ── Helpers privados ──────────────────────────────────────────────────

    private void ManejarProgreso(ProgresoPaso e)
    {
        var estado = MapeoProgreso.AEstado(e);
        TextoEstado = estado.Texto;
        Indeterminado = estado.Indeterminado;
        FraccionItem = estado.FraccionItem;

        bool itemCerrado = e.Estado is EstadoEvento.Completado or EstadoEvento.Salteado;

        // Avance macro por tema (monótono: al cerrar un tema cuenta como 1.0).
        if (e.ItemTotal > 0)
        {
            double fracEnItem = itemCerrado ? 1.0 : estado.FraccionItem;
            FraccionGlobal = ((e.ItemIndice - 1) + fracEnItem) / e.ItemTotal;
            TextoItem = $"Tema {e.ItemIndice} de {e.ItemTotal}";
        }

        if (!string.IsNullOrEmpty(e.Item))
        {
            var item = Items.FirstOrDefault(i => i.Nombre == e.Item);
            if (item is null)
            {
                item = new ItemProgresoVm { Nombre = e.Item };
                Items.Add(item);
            }
            item.Estado = e.Estado.ToString();
            item.Fraccion = itemCerrado ? 1.0 : estado.FraccionItem;
        }

        AgregarLog($"[{e.Fase}/{e.Etapa}] {estado.Texto} ({e.Estado})");
    }

    private void AgregarLog(string mensaje)
    {
        Log.Add($"{DateTime.Now:HH:mm:ss.fff}  {mensaje}");
    }
}
