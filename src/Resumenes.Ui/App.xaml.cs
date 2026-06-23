using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Retry;
using Resumenes.Infrastructure.Aplicacion;
using Resumenes.Infrastructure.Examenes;
using Resumenes.Infrastructure.IA;
using Resumenes.Infrastructure.Ocr;
using Resumenes.Infrastructure.Office;
using Resumenes.Infrastructure.Pdf;
using Resumenes.Infrastructure.Persistencia;
using Resumenes.Infrastructure.Secretos;
using Resumenes.Core.Apoyos;
using Resumenes.Core.Interfaces;
using Resumenes.Ui.Servicios;
using Resumenes.Ui.ViewModels;
using Resumenes.Ui.Vistas;

namespace Resumenes.Ui;

public partial class App : Application
{
    public static IServiceProvider Servicios { get; private set; } = default!;

    protected override void OnStartup(StartupEventArgs e)
    {
        ConfigurarManejadoresDeError();
        ConfigurarScrollGlobalPorRueda();

        var sc = new ServiceCollection();

        // -------- Configuración (igual que el Cli) --------
        var raizApp = AppContext.BaseDirectory;
        var cfgPath = System.IO.Path.Combine(raizApp, "config", "settings.json");
        var cfg = System.IO.File.Exists(cfgPath)
            ? JsonSerializer.Deserialize<Configuracion>(System.IO.File.ReadAllText(cfgPath))!
            : new Configuracion();
        if (string.IsNullOrWhiteSpace(cfg.RutaWorkspace))
            cfg.RutaWorkspace = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), "workspace");
        else if (!System.IO.Path.IsPathRooted(cfg.RutaWorkspace))
        {
            // Resolver una ruta relativa: preferir junto al cwd (raíz del repo en dev),
            // y si no existe, junto al exe. Así "Abrir carpeta"/"Exportar" ven el mismo workspace.
            var juntoCwd = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), cfg.RutaWorkspace);
            var juntoExe = System.IO.Path.Combine(raizApp, cfg.RutaWorkspace);
            cfg.RutaWorkspace = System.IO.Directory.Exists(juntoCwd) ? juntoCwd : juntoExe;
        }
        // Siempre absoluta: evita que Process.Start / File.Copy dependan del directorio actual.
        cfg.RutaWorkspace = System.IO.Path.GetFullPath(cfg.RutaWorkspace);
        System.IO.Directory.CreateDirectory(cfg.RutaWorkspace);

        // Runtime por-usuario (escribible) donde se descomprimen Python/LibreOffice.
        var raizDatos = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResumenesApp");
        if (string.IsNullOrWhiteSpace(cfg.RutaRuntime))
            cfg.RutaRuntime = System.IO.Path.Combine(raizDatos, "runtime");
        cfg.RutaRuntime = Environment.ExpandEnvironmentVariables(cfg.RutaRuntime);
        if (string.IsNullOrWhiteSpace(cfg.RutaCache))
            cfg.RutaCache = System.IO.Path.Combine(raizDatos, "cache");
        cfg.RutaCache = Environment.ExpandEnvironmentVariables(cfg.RutaCache);

        // Expandir variables de entorno en las rutas (settings de instalación las usa).
        cfg.PythonExe       = Environment.ExpandEnvironmentVariables(cfg.PythonExe);
        cfg.LibreOfficeDir  = Environment.ExpandEnvironmentVariables(cfg.LibreOfficeDir);
        cfg.ModelosPaddle   = Environment.ExpandEnvironmentVariables(cfg.ModelosPaddle);
        cfg.ScriptsDir      = Environment.ExpandEnvironmentVariables(cfg.ScriptsDir);
        cfg.FontsDir        = Environment.ExpandEnvironmentVariables(cfg.FontsDir);
        cfg.ManifestUrl     = Environment.ExpandEnvironmentVariables(cfg.ManifestUrl);

        sc.AddSingleton(cfg);

        // -------- Secretos (DPAPI, igual que el Cli) --------
        var secretos = new DpapiAlmacenSecretos(
            System.IO.Path.Combine(raizDatos, "config", "deepseek.key"));
        sc.AddSingleton<IAlmacenSecretos>(secretos);

        // -------- Resolver rutas (igual que el Cli) --------
        string Resolver(string r)
        {
            if (System.IO.Path.IsPathRooted(r)) return r;
            var juntoExe = System.IO.Path.Combine(raizApp, r);
            if (System.IO.Directory.Exists(juntoExe) || System.IO.File.Exists(juntoExe)) return juntoExe;
            var juntoCwd = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(), r);
            if (System.IO.Directory.Exists(juntoCwd) || System.IO.File.Exists(juntoCwd)) return juntoCwd;
            return juntoExe;
        }

        // -------- Adaptadores (igual que el Cli) --------
        var pipelineHttp = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                    .Handle<HttpRequestException>().Handle<TaskCanceledException>()
                    .HandleResult(r => (int)r.StatusCode == 429 || (int)r.StatusCode >= 500),
                MaxRetryAttempts = 3,
                BackoffType = DelayBackoffType.Exponential
            }).Build();
        var http = new HttpClient(new PollyHandler(pipelineHttp)) { Timeout = TimeSpan.FromSeconds(120) };

        sc.AddSingleton<IClienteIA>(new DeepseekClienteIA(http, secretos, cfg.BaseUrlDeepseek));
        sc.AddSingleton<IRasterizador>(new PyMuPdfRasterizador(
            cfg.PythonExe, System.IO.Path.Combine(Resolver(cfg.ScriptsDir), "rasterizar.py")));
        sc.AddSingleton<IServicioOcr>(new PaddleOcrServicio(
            cfg.PythonExe,
            System.IO.Path.Combine(Resolver(cfg.ScriptsDir), "worker_ocr.py"),
            Resolver(cfg.ModelosPaddle)));
        sc.AddSingleton<IGeneradorPdf>(new PythonGeneradorPdf(
            cfg.PythonExe,
            System.IO.Path.Combine(Resolver(cfg.ScriptsDir), "generador_estudio_final.py"),
            Resolver(cfg.FontsDir)));
        var sofficePath = System.IO.Path.Combine(Resolver(cfg.LibreOfficeDir), "program", "soffice.exe");
        // Conversor Office→PDF: LibreOffice portable como primario y, si falla o se cuelga,
        // Microsoft Office (Word/PowerPoint/Excel) vía COM como red de seguridad. El fallo del
        // primario se registra en logs\office-conversion.log para diagnóstico.
        var logsDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ResumenesApp", "logs");
        Action<string> logConv = msg =>
        {
            try
            {
                System.IO.Directory.CreateDirectory(logsDir);
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(logsDir, "office-conversion.log"),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}{Environment.NewLine}");
            }
            catch { /* el log es best-effort */ }
        };
        sc.AddSingleton<IConversorOffice>(new ConversorOfficeConFallback(
            new LibreOfficeConversor(sofficePath),
            new OfficeComConversor(),
            logConv));

        // -------- Repositorio SQLite (singleton) --------
        var dbPath = System.IO.Path.Combine(cfg.RutaWorkspace, "data.sqlite");
        sc.AddSingleton(new SqliteRepositorioEstado($"Data Source={dbPath}"));
        sc.AddSingleton<IRepositorioEstado>(sp => sp.GetRequiredService<SqliteRepositorioEstado>());

        // -------- Prompts --------
        sc.AddSingleton<ServicioPrompts>(sp =>
            new ServicioPrompts(sp.GetRequiredService<SqliteRepositorioEstado>()));

        // -------- Servicio de análisis --------
        sc.AddSingleton<IRelojUtc, RelojUtcSistema>();
        sc.AddSingleton<IServicioAnalisis, ServicioAnalisis>();

        // -------- Simulador de exámenes --------
        sc.AddSingleton<SqliteRepositorioExamenes>(_ => new SqliteRepositorioExamenes($"Data Source={dbPath}"));
        sc.AddSingleton<Resumenes.Core.Interfaces.IRepositorioExamenes>(sp => sp.GetRequiredService<SqliteRepositorioExamenes>());
        sc.AddSingleton<Resumenes.Core.Interfaces.IGeneradorExamen>(sp => new GeneradorExamen(sp.GetRequiredService<IClienteIA>()));
        sc.AddSingleton<Resumenes.Core.Interfaces.ICorrectorExamen>(sp => new CorrectorExamen(sp.GetRequiredService<IClienteIA>()));
        sc.AddSingleton<Resumenes.Core.Interfaces.IServicioExamenes>(sp => new ServicioExamenes(
            sp.GetRequiredService<Resumenes.Core.Interfaces.IRepositorioExamenes>(),
            sp.GetRequiredService<Resumenes.Core.Interfaces.IGeneradorExamen>(),
            sp.GetRequiredService<Resumenes.Core.Interfaces.ICorrectorExamen>(),
            sp.GetRequiredService<Configuracion>(),
            sp.GetRequiredService<IRelojUtc>()));

        // -------- Servicios de UI --------
        sc.AddSingleton<ServicioNavegacion>();
        sc.AddSingleton<Wpf.Ui.IContentDialogService, Wpf.Ui.ContentDialogService>();
        sc.AddSingleton<Resumenes.Core.Interfaces.IDescargadorDependencias>(_ =>
            new Resumenes.Infrastructure.Instalador.DescargadorDependencias(
                new HttpClient { Timeout = TimeSpan.FromMinutes(60) }, cfg.ManifestUrl, cfg.RutaRuntime));

        // -------- Costos --------
        sc.AddSingleton<ServicioCostos>(sp =>
            new ServicioCostos(sp.GetRequiredService<SqliteRepositorioEstado>(), cfg));
        sc.AddSingleton<Resumenes.Core.Interfaces.IClienteSaldo>(
            new ClienteSaldo(http, secretos, cfg.BaseUrlDeepseek));

        // -------- ViewModel base / ViewModels --------
        // (futuros VMs se agregan aquí)

        // -------- ViewModels --------
        sc.AddTransient<EjecutandoVm>(sp => new EjecutandoVm(
            sp.GetRequiredService<IServicioAnalisis>(),
            sp.GetRequiredService<ServicioNavegacion>()));
        sc.AddTransient<InicioVm>(sp => new InicioVm(
            sp.GetRequiredService<IRepositorioEstado>(),
            sp.GetRequiredService<ServicioNavegacion>(),
            sp.GetRequiredService<IServicioAnalisis>(),
            sp.GetRequiredService<Configuracion>(),
            sp.GetRequiredService<Wpf.Ui.IContentDialogService>(),
            sp.GetRequiredService<ServicioCostos>()));
        sc.AddTransient<ConfigurarVm>();
        sc.AddTransient<ConfirmarTemasVm>(sp => new ConfirmarTemasVm(
            sp.GetRequiredService<ServicioNavegacion>(),
            sp.GetRequiredService<Configuracion>().RutaWorkspace));
        sc.AddTransient<GenerandoVm>();
        sc.AddTransient<ResultadosVm>(sp => new ResultadosVm(
            sp.GetRequiredService<Configuracion>().RutaWorkspace,
            sp.GetRequiredService<ServicioNavegacion>(),
            sp.GetRequiredService<IServicioAnalisis>(),
            sp.GetRequiredService<Wpf.Ui.IContentDialogService>(),
            sp.GetRequiredService<ServicioCostos>()));
        sc.AddTransient<ConfiguracionVm>(sp => new ConfiguracionVm(
            sp.GetRequiredService<IAlmacenSecretos>(),
            sp.GetRequiredService<Configuracion>(),
            sp.GetRequiredService<ServicioPrompts>(),
            sp.GetRequiredService<Resumenes.Core.Interfaces.IClienteSaldo>()));
        sc.AddTransient<OnboardingVm>();
        sc.AddTransient<ExamenesVm>(sp => new ExamenesVm(
            sp.GetRequiredService<Resumenes.Core.Interfaces.IServicioExamenes>(),
            sp.GetRequiredService<Resumenes.Core.Interfaces.IRepositorioExamenes>(),
            sp.GetRequiredService<ServicioNavegacion>()));
        sc.AddTransient<CrearExamenVm>(sp => new CrearExamenVm(
            sp.GetRequiredService<Resumenes.Core.Interfaces.IServicioExamenes>(),
            sp.GetRequiredService<ServicioNavegacion>()));
        sc.AddTransient<RendirExamenVm>(sp => new RendirExamenVm(
            sp.GetRequiredService<Resumenes.Core.Interfaces.IRepositorioExamenes>(),
            sp.GetRequiredService<Resumenes.Core.Interfaces.IServicioExamenes>(),
            sp.GetRequiredService<ServicioNavegacion>()));

        // -------- Vistas (páginas) --------
        sc.AddTransient<VistaInicio>();
        sc.AddTransient<VistaConfiguracion>();
        sc.AddTransient<VistaEjecutando>();
        sc.AddTransient<VistaConfigurar>();
        sc.AddTransient<VistaConfirmarTemas>();
        sc.AddTransient<VistaGenerando>();
        sc.AddTransient<VistaResultados>();
        sc.AddTransient<VistaOnboarding>();
        sc.AddTransient<VistaExamenes>();
        sc.AddTransient<VistaCrearExamen>();
        sc.AddTransient<VistaRendirExamen>(sp => new VistaRendirExamen(
            sp.GetRequiredService<RendirExamenVm>(),
            sp.GetRequiredService<ServicioNavegacion>()));
        sc.AddTransient<VistaResultadoExamen>();

        // -------- Ventana principal --------
        sc.AddSingleton<MainWindow>();

        Servicios = sc.BuildServiceProvider();

        // Inicializar esquema SQLite
        Servicios.GetRequiredService<SqliteRepositorioEstado>().InicializarEsquema();

        // Mostrar ventana principal
        Servicios.GetRequiredService<MainWindow>().Show();

        // Modo demo (verificación visual): --demo <carpeta> dispara un análisis directo a Ejecutando.
        var idxDemo = Array.IndexOf(e.Args, "--demo");
        if (idxDemo >= 0 && idxDemo + 1 < e.Args.Length)
        {
            var carpetaDemo = e.Args[idxDemo + 1];
            _ = Dispatcher.InvokeAsync(async () =>
            {
                var servicio = Servicios.GetRequiredService<Resumenes.Core.Interfaces.IServicioAnalisis>();
                var nav = Servicios.GetRequiredService<Resumenes.Ui.Servicios.ServicioNavegacion>();
                var an = await servicio.AbrirOCrearAsync(carpetaDemo, System.Threading.CancellationToken.None);
                nav.Navegar<Resumenes.Ui.Vistas.VistaEjecutando>(new Resumenes.Ui.ViewModels.ParametroEjecucion(an, ""));
            });
        }

        base.OnStartup(e);
    }

    // ── Manejo global de errores (nunca un crash mudo) ──────────────────────
    private static readonly object _bloqueoLog = new();

    /// <summary>
    /// Suscribe los tres canales de excepciones no controladas: las del hilo de UI
    /// (Dispatcher), las de cualquier hilo (AppDomain) y las de Task sin observar.
    /// Las del Dispatcher se marcan como manejadas para evitar el cierre silencioso.
    /// </summary>
    private void ConfigurarManejadoresDeError()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            RegistrarError("UI", args.Exception);
            MostrarDialogoError(args.Exception);
            args.Handled = true; // mantener viva la app: no más cierres mudos
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                RegistrarError("AppDomain", ex);
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            RegistrarError("Task", args.Exception);
            args.SetObserved();
        };
    }

    private static string RutaLogErrores => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ResumenesApp", "logs", "ui-error.log");

    /// <summary>Anexa el detalle técnico completo de la excepción al log de errores.</summary>
    private static void RegistrarError(string origen, Exception ex)
    {
        try
        {
            var archivo = RutaLogErrores;
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(archivo)!);
            var linea = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] ({origen}) {ex}"
                        + Environment.NewLine + Environment.NewLine;
            lock (_bloqueoLog)
                System.IO.File.AppendAllText(archivo, linea);
        }
        catch { /* el registro de errores jamás debe tumbar la app */ }
    }

    /// <summary>Muestra un diálogo amigable apuntando al log para el detalle técnico.</summary>
    private static void MostrarDialogoError(Exception ex)
    {
        MessageBox.Show(
            "Ocurrió un error inesperado y la operación no pudo completarse.\n\n"
            + ex.Message
            + "\n\nEl detalle técnico quedó registrado en:\n" + RutaLogErrores,
            "Resúmenes — Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    // ── Scroll global por rueda del mouse ───────────────────────────────────
    // Por defecto, en WPF la rueda solo desplaza si llega al ScrollViewer correcto;
    // diversos contenedores/plantillas (NavigationView, listas, textbox) pueden
    // consumirla y dejar la página sin scroll salvo sobre la barra. Con un class
    // handler de PreviewMouseWheel re-ruteamos la rueda al ScrollViewer adecuado.
    private void ConfigurarScrollGlobalPorRueda()
    {
        EventManager.RegisterClassHandler(
            typeof(ScrollViewer),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(EnRuedaScrollViewer));
    }

    private static void EnRuedaScrollViewer(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer raiz) return;

        // PreviewMouseWheel tunelea root→leaf: procesamos una sola vez, en el
        // ScrollViewer más externo (el primero en recibir el evento).
        if (ObtenerScrollViewerPadre(raiz) is not null) return;

        // Desde el elemento bajo el cursor hacia afuera, elegir el primer
        // ScrollViewer que pueda desplazarse en la dirección de la rueda.
        var objetivo = ElegirScrollViewer(e.OriginalSource as DependencyObject, raiz, e.Delta);
        if (objetivo is null) return;

        objetivo.ScrollToVerticalOffset(objetivo.VerticalOffset - e.Delta);
        e.Handled = true;
    }

    private static ScrollViewer? ElegirScrollViewer(DependencyObject? origen, ScrollViewer raiz, int delta)
    {
        // Recolectar los ScrollViewers desde el más interno (bajo el cursor) hasta la raíz.
        var cadena = new List<ScrollViewer>();
        var actual = origen;
        while (actual is not null)
        {
            if (actual is ScrollViewer sv) cadena.Add(sv);
            if (ReferenceEquals(actual, raiz)) break;
            actual = VisualTreeHelper.GetParent(actual);
        }
        if (!cadena.Contains(raiz)) cadena.Add(raiz);

        // El primero (más interno) que pueda desplazarse en esta dirección.
        foreach (var sv in cadena)
            if (PuedeDesplazar(sv, delta)) return sv;
        return null;
    }

    private static bool PuedeDesplazar(ScrollViewer sv, int delta) =>
        sv.ScrollableHeight > 0 &&
        !((delta > 0 && sv.VerticalOffset <= 0) ||
          (delta < 0 && sv.VerticalOffset >= sv.ScrollableHeight));

    private static ScrollViewer? ObtenerScrollViewerPadre(DependencyObject hijo)
    {
        var actual = VisualTreeHelper.GetParent(hijo);
        while (actual is not null and not ScrollViewer)
            actual = VisualTreeHelper.GetParent(actual);
        return actual as ScrollViewer;
    }
}

// Handler que aplica la pipeline de Polly al HttpClient (mismo patrón que el Cli).
file sealed class PollyHandler(ResiliencePipeline<HttpResponseMessage> pipeline)
    : DelegatingHandler(new HttpClientHandler())
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => pipeline.ExecuteAsync(async token => await base.SendAsync(request, token), ct).AsTask();
}
