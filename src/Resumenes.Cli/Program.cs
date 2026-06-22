using System.Text;
using Polly;
using Polly.Retry;
using Resumenes.Core.Apoyos;
using Resumenes.Core.Modelos;
using Resumenes.Core.Orquestacion;
using Resumenes.Infrastructure.Aplicacion;
using Resumenes.Infrastructure.IA;
using Resumenes.Infrastructure.Ocr;
using Resumenes.Infrastructure.Office;
using Resumenes.Infrastructure.Pdf;
using Resumenes.Infrastructure.Persistencia;
using Resumenes.Infrastructure.Secretos;
using Serilog;
using System.Text.Json;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length < 1)
{
    Console.WriteLine("Uso: Resumenes.Cli <carpeta-con-material> [--temas \"t1, t2\"] [--set-key <APIKEY>]");
    return 1;
}

// Configuración
var raizApp = AppContext.BaseDirectory;
var cfgPath = Path.Combine(raizApp, "config", "settings.json");
var cfg = File.Exists(cfgPath)
    ? JsonSerializer.Deserialize<Configuracion>(File.ReadAllText(cfgPath))!
    : new Configuracion();
if (string.IsNullOrWhiteSpace(cfg.RutaWorkspace))
    cfg.RutaWorkspace = Path.Combine(Directory.GetCurrentDirectory(), "workspace");

string Resolver(string r)
{
    if (Path.IsPathRooted(r)) return r;
    var juntoExe = Path.Combine(raizApp, r);
    if (Directory.Exists(juntoExe) || File.Exists(juntoExe)) return juntoExe;
    var juntoCwd = Path.Combine(Directory.GetCurrentDirectory(), r);
    if (Directory.Exists(juntoCwd) || File.Exists(juntoCwd)) return juntoCwd;
    return juntoExe;
}

Directory.CreateDirectory(cfg.RutaWorkspace);

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(cfg.RutaWorkspace, "logs", "app.log"), rollingInterval: RollingInterval.Day)
    .CreateLogger();

var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResumenesApp");
if (string.IsNullOrWhiteSpace(cfg.RutaCache))
    cfg.RutaCache = Path.Combine(appDataDir, "cache");
var secretos = new DpapiAlmacenSecretos(Path.Combine(appDataDir, "config", "deepseek.key"));

var idxKey = Array.IndexOf(args, "--set-key");
if (idxKey >= 0 && idxKey + 1 < args.Length)
{
    secretos.GuardarApiKey(args[idxKey + 1]);
    Log.Information("API key guardada (cifrada con DPAPI).");
    return 0;
}

var idxTemas = Array.IndexOf(args, "--temas");
var promptTemas = (idxTemas >= 0 && idxTemas + 1 < args.Length) ? args[idxTemas + 1] : "";

var carpeta = args[0];
if (!Directory.Exists(carpeta)) { Log.Error("La carpeta no existe: {Carpeta}", carpeta); return 1; }

var exts = new[] { ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".txt" };
var archivosPreVuelo = Directory.GetFiles(carpeta)
    .Where(f => exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
    .OrderBy(p => p).ToArray();
if (archivosPreVuelo.Length == 0) { Log.Error("No hay material soportado (pdf/doc/docx/ppt/pptx/txt) en {Carpeta}", carpeta); return 1; }

// Pre-vuelo de entorno
if (secretos.ObtenerApiKey() is null)
{
    Log.Error("No hay API key configurada. Cargala con: Resumenes.Cli --set-key <APIKEY>");
    return 1;
}
var sofficePath = Path.Combine(Resolver(cfg.LibreOfficeDir), "program", "soffice.exe");
foreach (var (etiqueta, ruta) in new[]
{
    ("rasterizar.py", Path.Combine(Resolver(cfg.ScriptsDir), "rasterizar.py")),
    ("worker_ocr.py", Path.Combine(Resolver(cfg.ScriptsDir), "worker_ocr.py")),
    ("generador_estudio_final.py", Path.Combine(Resolver(cfg.ScriptsDir), "generador_estudio_final.py")),
    ("DejaVuSans.ttf", Path.Combine(Resolver(cfg.FontsDir), "DejaVuSans.ttf")),
})
{
    if (!File.Exists(ruta)) { Log.Error("Pre-vuelo: falta {Etiqueta} ({Ruta})", etiqueta, ruta); return 1; }
}
bool hayOffice = archivosPreVuelo.Any(a => Ids.TipoDe(a) is TipoArchivo.Doc or TipoArchivo.Docx or TipoArchivo.Ppt or TipoArchivo.Pptx);
if (hayOffice && !File.Exists(sofficePath))
{
    Log.Error("Pre-vuelo: hay archivos Office pero falta LibreOffice ({Ruta})", sofficePath);
    return 1;
}

// Adaptadores (una vez para todo el lote)
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

var ia = new DeepseekClienteIA(http, secretos, cfg.BaseUrlDeepseek);
var rasterizador = new PyMuPdfRasterizador(cfg.PythonExe, Path.Combine(Resolver(cfg.ScriptsDir), "rasterizar.py"));
var ocr = new PaddleOcrServicio(cfg.PythonExe, Path.Combine(Resolver(cfg.ScriptsDir), "worker_ocr.py"), Resolver(cfg.ModelosPaddle));
var generadorPdf = new PythonGeneradorPdf(
    cfg.PythonExe, Path.Combine(Resolver(cfg.ScriptsDir), "generador_estudio_final.py"), Resolver(cfg.FontsDir));
var conversor = new LibreOfficeConversor(sofficePath);

var repo = new SqliteRepositorioEstado($"Data Source={Path.Combine(cfg.RutaWorkspace, "data.sqlite")}");
repo.InicializarEsquema();
var reloj = new RelojUtcSistema();

// Construir el ServicioAnalisis que orquesta las 3 fases
var servicio = new ServicioAnalisis(rasterizador, ocr, ia, generadorPdf, conversor, repo, reloj, cfg);

// Progress<ProgresoPaso> que escribe al log
var progresoLog = new Progress<ProgresoPaso>(e =>
{
    if (e.Estado == EstadoEvento.Iniciado)
        Log.Debug("[{Fase}] Iniciado {Etapa} para {Item}", e.Fase, e.Etapa, e.Item);
    else if (e.Estado == EstadoEvento.Avance && !string.IsNullOrEmpty(e.Detalle))
        Log.Debug("[{Fase}] {Detalle}", e.Fase, e.Detalle);
    else if (e.Estado == EstadoEvento.Completado)
        Log.Debug("[{Fase}] Completado {Etapa} para {Item}", e.Fase, e.Etapa, e.Item);
    else if (e.Estado == EstadoEvento.Error)
        Log.Warning("[{Fase}] Error en {Etapa} para {Item}: {Detalle}", e.Fase, e.Etapa, e.Item, e.Detalle);
});

// ===== FASE 1: por archivo, hasta texto limpio =====
var an = await servicio.AbrirOCrearAsync(carpeta, CancellationToken.None);
Log.Information("Análisis: {Nombre} ({Id})", an.Nombre, an.Id);

var r1 = await servicio.ProcesarArchivosAsync(an, progresoLog, CancellationToken.None);
Log.Information("Fase 1: {Ok} archivo(s) limpios / {Err} con error", r1.Ok, r1.Error);
foreach (var f in r1.Fallos) Log.Error("  - {Fallo}", f);

if (r1.Ok == 0)
{
    Log.Error("Ningún archivo llegó a texto limpio; no hay nada para consolidar.");
    Log.CloseAndFlush(); return 2;
}

// ===== FASE 2: detección de temas =====
Log.Information("Detectando temas...");
IReadOnlyList<TemaDetectado> temas;
try
{
    temas = await servicio.DetectarTemasAsync(an, promptTemas, CancellationToken.None);
}
catch (Exception ex)
{
    Log.Error("Falló la detección de temas: {Msg}", ex.Message);
    Log.CloseAndFlush(); return 2;
}
Log.Information("Temas detectados: {N} -> {Lista}", temas.Count, string.Join(" | ", temas.Select(t => t.Nombre)));

// ===== FASE 3: por tema (consolidar -> resumir -> PDF) =====
var r3 = await servicio.GenerarPorTemasAsync(an, temas, "", progresoLog, CancellationToken.None);
Log.Information("Resultado: {AOk} archivo(s) / {TOk}/{TT} tema(s) con PDF",
    r1.Ok, r3.Ok, temas.Count);
foreach (var f in r3.Fallos) Log.Error("  - {Fallo}", f);

Log.Information("Salida: {Ruta}", Path.Combine(cfg.RutaWorkspace, "analisis", an.Id, "final"));
Log.CloseAndFlush();
return (r1.Error + r3.Error) == 0 ? 0 : 2;

// Handler que aplica la pipeline de Polly al HttpClient.
sealed class PollyHandler(ResiliencePipeline<HttpResponseMessage> pipeline) : DelegatingHandler(new HttpClientHandler())
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        => pipeline.ExecuteAsync(async token => await base.SendAsync(request, token), ct).AsTask();
}
