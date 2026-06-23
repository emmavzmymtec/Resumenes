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
/// ViewModel de la pantalla "Ejecutando": corre Fase 1 (procesar archivos)
/// + Fase 2 (detectar temas) con IProgress, exponiendo progreso en vivo.
/// Al terminar navega a VistaConfirmarTemas pasando el análisis y los temas detectados.
/// </summary>
public partial class EjecutandoVm : VistaModeloBase
{
    private readonly IServicioAnalisis _servicio;
    private readonly ServicioNavegacion? _nav;
    private CancellationTokenSource _cts = new();
    private readonly Stopwatch _cronometro = new();
    private readonly DispatcherTimer _timerTiempo;

    // ── Propiedades de progreso global ────────────────────────────────────

    /// <summary>Texto descriptivo del paso actual.</summary>
    [ObservableProperty]
    private string _textoEstado = string.Empty;

    /// <summary>True si la operación actual es indeterminada (sin % conocido).</summary>
    [ObservableProperty]
    private bool _indeterminado = true;

    /// <summary>Fracción del sub-paso del ítem actual (0.0 – 1.0); solo OCR/rasterizado la reportan.</summary>
    [ObservableProperty]
    private double _fraccionItem;

    /// <summary>Avance macro de la fase (0.0 – 1.0): cuántos archivos/temas se completaron.</summary>
    [ObservableProperty]
    private double _fraccionGlobal;

    /// <summary>Texto del ítem en curso, p. ej. "Archivo 3 de 10".</summary>
    [ObservableProperty]
    private string _textoItem = string.Empty;

    /// <summary>True cuando la Fase 1 (limpieza de archivos) terminó.</summary>
    [ObservableProperty]
    private bool _paso1Hecho;

    /// <summary>True cuando la Fase 2 (detección de temas) terminó.</summary>
    [ObservableProperty]
    private bool _paso2Hecho;

    /// <summary>Fase del pipeline en curso.</summary>
    [ObservableProperty]
    private FaseAnalisis _faseActual = FaseAnalisis.Limpieza;

    /// <summary>Tiempo transcurrido formateado (mm:ss).</summary>
    [ObservableProperty]
    private string _textoTiempo = "00:00";

    // ── Colecciones en vivo ───────────────────────────────────────────────

    /// <summary>Un ítem por archivo/tema procesado; se actualiza con cada evento de progreso.</summary>
    public ObservableCollection<ItemProgresoVm> Items { get; } = new();

    /// <summary>Líneas de log con timestamp.</summary>
    public ObservableCollection<string> Log { get; } = new();

    // ── Resultado al terminar ─────────────────────────────────────────────

    /// <summary>Temas detectados al finalizar Fase 2. Vacío hasta que termine.</summary>
    public IReadOnlyList<TemaDetectado> TemasDetectados { get; private set; } =
        Array.Empty<TemaDetectado>();

    // ── Constructor ───────────────────────────────────────────────────────

    /// <summary>
    /// Constructor principal (DI): recibe el servicio de análisis y el servicio de navegación.
    /// El nav puede ser null en tests.
    /// </summary>
    public EjecutandoVm(IServicioAnalisis servicio, ServicioNavegacion? nav = null)
    {
        _servicio = servicio;
        _nav = nav;

        // Timer para actualizar el texto de tiempo transcurrido cada segundo.
        // Se inicializa pero no se arranca hasta que comience la ejecución.
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
        AgregarLog("⛔ Cancelado por el usuario.");
        _nav?.Navegar<VistaInicio>();
    }

    /// <summary>
    /// Limpieza al abandonar la pantalla: cancela el pipeline en curso para que una ejecución
    /// abandonada no termine navegando sola a la pantalla siguiente.
    /// </summary>
    public void AlSalir() => _cts.Cancel();

    // ── Método principal ──────────────────────────────────────────────────

    /// <summary>
    /// Ejecuta Fase 1 (ProcesarArchivosAsync) y Fase 2 (DetectarTemasAsync).
    /// Debe llamarse desde el hilo de UI para que Progress&lt;T&gt; marshalee correctamente.
    /// </summary>
    public async Task EjecutarAsync(Analisis an, string promptTemas)
    {
        // Ceder el control para que el cuerpo (y la navegación final) NO corra de forma
        // re-entrante dentro de la navegación que está creando esta página. Sin esto, cuando
        // todo el pipeline resuelve sincrónicamente (caché), la navegación a la siguiente
        // pantalla ocurre dentro de la navegación en curso y el NavigationView la descarta.
        await Task.Yield();

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        // Arrancar cronómetro
        _cronometro.Restart();
        TextoTiempo = "00:00";
        _timerTiempo.Start();

        // Reiniciar estado de progreso (por si el VM se reutiliza).
        FraccionGlobal = 0;
        TextoItem = string.Empty;
        Paso1Hecho = false;
        Paso2Hecho = false;

        // Progress<T> captura el SynchronizationContext del hilo de UI al construirse,
        // por lo que sus callbacks se ejecutan en el hilo correcto.
        var progreso = new Progress<ProgresoPaso>(ManejarProgreso);

        try
        {
            // ── Fase 1: procesar archivos ──────────────────────────────
            FaseActual = FaseAnalisis.Limpieza;
            AgregarLog("▶ Fase 1 — Procesando archivos…");

            await _servicio.ProcesarArchivosAsync(an, progreso, ct);

            AgregarLog("✔ Fase 1 completada.");
            Paso1Hecho = true;

            // ── Fase 2: detectar temas ─────────────────────────────────
            FaseActual = FaseAnalisis.Deteccion;
            TextoEstado = "Detectando temas…";
            TextoItem = "Detectando temas…";
            Indeterminado = true;
            AgregarLog("▶ Fase 2 — Detectando temas…");

            var temas = await _servicio.DetectarTemasAsync(an, promptTemas, ct);
            TemasDetectados = temas;
            Paso2Hecho = true;

            TextoEstado = $"Temas detectados: {temas.Count}";
            Indeterminado = false;
            AgregarLog($"✔ Fase 2 completada — {temas.Count} tema(s) detectado(s).");

            // Navegar a VistaConfirmarTemas pasando el análisis y los temas detectados.
            _nav?.Navegar<VistaConfirmarTemas>(new ParametroTemas(an, temas));
        }
        catch (OperationCanceledException)
        {
            TextoEstado = "Cancelado.";
            AgregarLog("⛔ Operación cancelada.");
        }
        catch (Exception ex)
        {
            TextoEstado = $"Error: {ex.Message}";
            AgregarLog($"✘ Error: {ex.Message}");
        }
        finally
        {
            // Detener cronómetro
            _cronometro.Stop();
            _timerTiempo.Stop();
            // Actualizar una última vez con el tiempo real
            var e = _cronometro.Elapsed;
            TextoTiempo = e.TotalHours >= 1 ? e.ToString(@"h\:mm\:ss") : e.ToString(@"mm\:ss");
        }
    }

    // ── Helpers privados ──────────────────────────────────────────────────

    private void ManejarProgreso(ProgresoPaso e)
    {
        // Actualizar estado global
        var estado = MapeoProgreso.AEstado(e);
        TextoEstado = estado.Texto;
        Indeterminado = estado.Indeterminado;
        FraccionItem = estado.FraccionItem;
        FaseActual = estado.Fase;

        bool itemCerrado = e.Estado is EstadoEvento.Completado or EstadoEvento.Salteado;

        // Avance macro por archivo (monótono: al cerrar un ítem cuenta como 1.0,
        // así la barra global no retrocede entre etapas y se percibe progreso real).
        if (e.ItemTotal > 0)
        {
            double fracEnItem = itemCerrado ? 1.0 : estado.FraccionItem;
            FraccionGlobal = ((e.ItemIndice - 1) + fracEnItem) / e.ItemTotal;
            TextoItem = $"Archivo {e.ItemIndice} de {e.ItemTotal}";
        }

        // Crear o actualizar el ItemProgresoVm correspondiente al ítem
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

        // Agregar línea al log
        AgregarLog($"[{e.Fase}/{e.Etapa}] {estado.Texto} ({e.Estado})");
    }

    private void AgregarLog(string mensaje)
    {
        var linea = $"{DateTime.Now:HH:mm:ss.fff}  {mensaje}";
        Log.Add(linea);
    }
}
