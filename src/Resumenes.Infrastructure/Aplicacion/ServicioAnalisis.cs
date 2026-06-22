using Resumenes.Core.Apoyos;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;
using Resumenes.Core.Orquestacion;

namespace Resumenes.Infrastructure.Aplicacion;

public class ServicioAnalisis(
    IRasterizador rasterizador,
    IServicioOcr ocr,
    IClienteIA ia,
    IGeneradorPdf generadorPdf,
    IConversorOffice conversor,
    IRepositorioEstado repo,
    IRelojUtc reloj,
    Configuracion cfg) : IServicioAnalisis
{
    private readonly ServicioPrompts _prompts = new(repo);
    private readonly CacheDerivados _cache = new(repo, cfg);
    private ConstructorPipeline? _ctorLazy;
    private ConstructorPipeline _ctor => _ctorLazy ??= new(rasterizador, ocr, ia, generadorPdf, conversor, cfg, _prompts, _cache);
    private readonly PipelineOrquestador _orq = new(repo, reloj);
    private static readonly string[] _exts = { ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".txt" };

    // Archivos e hashes cargados en AbrirOCrearAsync para ser reutilizados en ProcesarArchivosAsync.
    // Se guarda como estado de instancia (un ciclo de vida por análisis en curso).
    private string[] _archivosActuales = Array.Empty<string>();
    private Dictionary<string, string> _hashesActuales = new();

    public Task<Analisis> AbrirOCrearAsync(string carpeta, CancellationToken ct)
    {
        var archivos = Directory.GetFiles(carpeta)
            .Where(f => _exts.Contains(Path.GetExtension(f).ToLowerInvariant()))
            .OrderBy(p => p).ToArray();

        var hashes = archivos.ToDictionary(p => p, Hashing.Sha256HexDeArchivo);
        var fingerprint = Hashing.Sha256HexDeTexto(string.Join("|",
            archivos.Select(p => Path.GetFileName(p) + ":" + hashes[p])));
        var nombreAnalisis = Path.GetFileName(carpeta.TrimEnd('\\', '/'));

        var an = repo.ObtenerAnalisisPorFingerprint(fingerprint)
            ?? new Analisis(Ids.SlugId(nombreAnalisis), nombreAnalisis, Path.GetFullPath(carpeta), fingerprint,
                EstadoAnalisis.EnProceso, reloj.Ahora(), reloj.Ahora());
        repo.GuardarAnalisis(an);

        _archivosActuales = archivos;
        _hashesActuales = hashes;

        return Task.FromResult(an);
    }

    public async Task<ResultadoLote> ProcesarArchivosAsync(Analisis an, IProgress<ProgresoPaso>? progreso, CancellationToken ct)
    {
        var archivos = _archivosActuales;
        var hashes = _hashesActuales;
        var fallos = new List<string>();
        int archOk = 0, archErr = 0;

        for (int i = 0; i < archivos.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            var rutaArch = archivos[i];
            var nombre = Path.GetFileName(rutaArch);
            try
            {
                var info = new FileInfo(rutaArch);
                if (info.Length == 0) throw new InvalidOperationException("archivo vacío (0 bytes)");
                var hash = hashes[rutaArch];
                var arc = new Archivo(Hashing.ArchivoIdDesdeHash(hash), an.Id, nombre, nombre, hash,
                    info.Length, Ids.TipoDe(rutaArch), null, reloj.Ahora());
                repo.GuardarArchivo(arc);
                var (pasos, limpioPath) = _ctor.PasosPorArchivo(an, arc, Path.GetFullPath(rutaArch));
                var r = await _orq.EjecutarAsync(an.Id, pasos, ct,
                    FaseAnalisis.Limpieza, nombre, i + 1, archivos.Length, progreso);
                if (r.Errores == 0 && File.Exists(limpioPath))
                    archOk++;
                else
                {
                    archErr++;
                    fallos.Add($"{nombre}: {string.Join("; ", r.MensajesError)}");
                }
            }
            catch (Exception ex)
            {
                archErr++;
                fallos.Add($"{nombre}: {ex.Message}");
            }
        }

        return new ResultadoLote(archOk, archErr, fallos);
    }

    public async Task<IReadOnlyList<TemaDetectado>> DetectarTemasAsync(Analisis an, string promptTemas, CancellationToken ct)
    {
        // Reconstruir el dict de limpios desde el repo
        var archivos = _archivosActuales;
        var hashes = _hashesActuales;
        var limpios = new Dictionary<string, string>();

        for (int i = 0; i < archivos.Length; i++)
        {
            var rutaArch = archivos[i];
            var hash = hashes[rutaArch];
            var arcId = Hashing.ArchivoIdDesdeHash(hash);
            var limpio = Path.Combine(cfg.RutaWorkspace, "analisis", an.Id, "00_fuentes", arcId, "texto_limpio", "limpio.txt");
            if (File.Exists(limpio))
                limpios[arcId] = limpio;
        }

        var archivosLimpios = limpios
            .Select(kv => (repo.ObtenerArchivo(kv.Key)!, kv.Value))
            .Where(t => t.Item1 != null)
            .ToList();

        var detector = new DetectorTemas(ia, repo, cfg, _prompts);
        return await detector.DetectarOCargarAsync(an, archivosLimpios, promptTemas, ct);
    }

    public async Task<ResultadoLote> GenerarPorTemasAsync(
        Analisis an, IReadOnlyList<TemaDetectado> temas, string promptResumen, IProgress<ProgresoPaso>? progreso, CancellationToken ct)
    {
        // Persistir el prompt de resumen usado, para recordarlo al continuar / re-procesar.
        try
        {
            var rutaPrompt = Path.Combine(cfg.RutaWorkspace, "analisis", an.Id, "resumen-prompt.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(rutaPrompt)!);
            File.WriteAllText(rutaPrompt, promptResumen ?? "");
        }
        catch { /* persistir el prompt no es crítico para la generación */ }

        // Reconstruir limpios
        var archivos = _archivosActuales;
        var hashes = _hashesActuales;
        var limpios = new Dictionary<string, string>();

        for (int i = 0; i < archivos.Length; i++)
        {
            var rutaArch = archivos[i];
            var hash = hashes[rutaArch];
            var arcId = Hashing.ArchivoIdDesdeHash(hash);
            var limpio = Path.Combine(cfg.RutaWorkspace, "analisis", an.Id, "00_fuentes", arcId, "texto_limpio", "limpio.txt");
            if (File.Exists(limpio))
                limpios[arcId] = limpio;
        }

        var fallos = new List<string>();
        int temaOk = 0, temaErr = 0;
        int temaIdx = 0;

        foreach (var tema in temas)
        {
            ct.ThrowIfCancellationRequested();
            temaIdx++;
            try
            {
                var limpiosDelTema = tema.Archivos.Where(limpios.ContainsKey).Select(a => limpios[a]).ToList();
                if (limpiosDelTema.Count == 0) limpiosDelTema = limpios.Values.ToList();
                var pasos = _ctor.PasosPorTema(an, tema, limpiosDelTema, promptResumen);
                var r = await _orq.EjecutarAsync(an.Id, pasos, ct,
                    FaseAnalisis.Generacion, tema.Nombre, temaIdx, temas.Count, progreso);
                if (r.Errores == 0)
                    temaOk++;
                else
                {
                    temaErr++;
                    fallos.Add($"tema '{tema.Nombre}': {string.Join("; ", r.MensajesError)}");
                }
            }
            catch (Exception ex)
            {
                temaErr++;
                fallos.Add($"tema '{tema.Nombre}': {ex.Message}");
            }
        }

        // Actualizar estado del análisis
        var estadoFinal = (temaErr == 0) ? EstadoAnalisis.Completado : EstadoAnalisis.ConErrores;
        var anActualizado = an with { Estado = estadoFinal, ActualizadoEn = reloj.Ahora() };
        repo.GuardarAnalisis(anActualizado);

        return new ResultadoLote(temaOk, temaErr, fallos);
    }
}
