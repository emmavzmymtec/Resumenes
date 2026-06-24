using System.Text;
using System.Text.Json;
using Resumenes.Core.Apoyos;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;
using Resumenes.Infrastructure.Aplicacion;

namespace Resumenes.Infrastructure.Examenes;

public class ServicioExamenes(
    IRepositorioExamenes repo, IGeneradorExamen generador,
    ICorrectorExamen corrector, Configuracion cfg, IRelojUtc reloj) : IServicioExamenes
{
    private static readonly TipoPregunta[] Abiertos =
        { TipoPregunta.Desarrollo, TipoPregunta.DesarrolloItems, TipoPregunta.VfJustificado };

    public async Task<Examen> CrearAsync(string analisisId, string titulo, ConfigExamen cfgExamen, CancellationToken ct)
    {
        var contenido = EnsamblarContenido(analisisId, cfgExamen);
        if (string.IsNullOrWhiteSpace(contenido))
            throw new InvalidOperationException("No hay contenido para generar el examen (procesá el análisis primero).");

        var examenId = Guid.NewGuid().ToString("N");
        var r = await generador.GenerarAsync(examenId, contenido, cfgExamen, cfg.Modelo, ct);

        var examen = new Examen {
            Id = examenId, AnalisisId = analisisId, Titulo = titulo,
            ConfigJson = JsonSerializer.Serialize(cfgExamen), Estado = EstadoExamen.EnCurso,
            Tokens = r.TokensEntrada + r.TokensSalida,
            CostoEstimado = Costo(r.TokensEntrada, r.TokensSalida),
            CreadoEn = reloj.Ahora(), IniciadoEn = reloj.Ahora() };
        repo.GuardarExamen(examen);
        foreach (var p in r.Preguntas) repo.GuardarPregunta(p);
        return examen;
    }

    public async Task<Examen> FinalizarYCorregirAsync(string examenId, CancellationToken ct)
    {
        var examen = repo.ObtenerExamen(examenId) ?? throw new InvalidOperationException("Examen no encontrado.");
        var preguntas = repo.ListarPreguntas(examenId);
        var respuestas = repo.ListarRespuestas(examenId).ToDictionary(x => x.PreguntaId);

        var pares = new List<(PreguntaExamen p, RespuestaUsuario r)>();
        foreach (var p in preguntas)
        {
            var r = respuestas.TryGetValue(p.Id, out var ru) ? ru
                : new RespuestaUsuario { Id = Guid.NewGuid().ToString("N"), ExamenId = examenId, PreguntaId = p.Id };
            pares.Add((p, r));
        }

        // Objetivo local
        foreach (var (p, r) in pares.Where(x => !Abiertos.Contains(x.p.Tipo)))
            corrector.CorregirObjetivo(p, r);

        // Abierto con IA
        var abiertas = pares.Where(x => Abiertos.Contains(x.p.Tipo)).ToList();
        var (tokIn, tokOut) = await corrector.CorregirAbiertasAsync(abiertas, cfg.Modelo, ct);

        var res = corrector.CalcularResultado(pares, cfg.EscalaNotaMaxima, cfg.NotaAprobacion);
        var (devolucion, dIn, dOut) = await corrector.GenerarDevolucionAsync(pares, res.Porcentaje, cfg.Modelo, ct);

        foreach (var (_, r) in pares) repo.GuardarRespuesta(r);

        examen.Estado = EstadoExamen.Corregido;
        examen.Nota = res.Nota; examen.Porcentaje = res.Porcentaje; examen.Aprobado = res.Aprobado;
        examen.FeedbackGeneral = devolucion;
        examen.Tokens += tokIn + tokOut + dIn + dOut;
        examen.CostoEstimado += Costo(tokIn + dIn, tokOut + dOut);
        examen.FinalizadoEn = reloj.Ahora();
        repo.GuardarExamen(examen);
        return examen;
    }

    public IReadOnlyList<Examen> Historial(string analisisId) => repo.ListarExamenes(analisisId);

    // Lee los .txt de resumen/ (rápido) o consolidado/ (completo); filtra por TemasIncluidos (nombres sin extensión).
    private string EnsamblarContenido(string analisisId, ConfigExamen cfgExamen)
    {
        var sub = cfgExamen.Fuente == "completo" ? "consolidado" : "resumen";
        var dir = Path.Combine(cfg.RutaWorkspace, "analisis", analisisId, sub);
        if (!Directory.Exists(dir)) return "";

        var incluir = cfgExamen.TemasIncluidos.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        foreach (var f in Directory.GetFiles(dir, "*.txt").OrderBy(x => x))
        {
            var nombre = Path.GetFileNameWithoutExtension(f);
            if (incluir.Count > 0 && !incluir.Contains(nombre)) continue;
            sb.AppendLine(File.ReadAllText(f));
            sb.AppendLine();
        }
        return sb.ToString().Trim();
    }

    private double Costo(int tokIn, int tokOut)
        => (double)((tokIn * cfg.PrecioInputPorMillonUsd + tokOut * cfg.PrecioOutputPorMillonUsd) / 1_000_000m);
}
