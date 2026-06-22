using System.Text;
using System.Text.Json;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;

namespace Resumenes.Infrastructure.Examenes;

public class CorrectorExamen(IClienteIA ia) : ICorrectorExamen
{
    public void CorregirObjetivo(PreguntaExamen p, RespuestaUsuario r)
    {
        using var datos = JsonDocument.Parse(p.DatosJson);
        var root = datos.RootElement;
        // VfJustificado NO entra aquí: es abierto (lo evalúa la IA, que recibe la afirmación
        // y 'esVerdadero' vía DatosJson y puntúa V/F + justificación de forma integral).
        bool correcta = p.Tipo switch
        {
            TipoPregunta.McUna => CorregirMcUna(root, r.RespuestaJson),
            TipoPregunta.McVarias => CorregirMcVarias(root, r.RespuestaJson),
            TipoPregunta.Completar => CorregirCompletar(root, r.RespuestaJson),
            TipoPregunta.Emparejar => CorregirEmparejar(root, r.RespuestaJson),
            _ => false
        };
        r.Correcta = correcta;
        r.PuntosObtenidos = correcta ? p.Puntos : 0;
    }

    // RespuestaJson de McUna: índice de la opción elegida (número).
    private static bool CorregirMcUna(JsonElement datos, string? resp)
    {
        if (string.IsNullOrWhiteSpace(resp) || !int.TryParse(resp.Trim('"', ' '), out var idx)) return false;
        var ops = datos.GetProperty("opciones");
        return idx >= 0 && idx < ops.GetArrayLength() && ops[idx].GetProperty("correcta").GetBoolean();
    }

    // RespuestaJson de McVarias: array de índices elegidos. Correcta = coincide EXACTO con las correctas.
    private static bool CorregirMcVarias(JsonElement datos, string? resp)
    {
        var elegidas = ParseIndices(resp);
        var correctas = new HashSet<int>();
        var ops = datos.GetProperty("opciones");
        for (int k = 0; k < ops.GetArrayLength(); k++)
            if (ops[k].GetProperty("correcta").GetBoolean()) correctas.Add(k);
        return elegidas.SetEquals(correctas);
    }

    // RespuestaJson de Completar: array de strings, una por hueco. Match normalizado.
    private static bool CorregirCompletar(JsonElement datos, string? resp)
    {
        var dadas = ParseStrings(resp);
        var esperadas = datos.GetProperty("respuestas").EnumerateArray().Select(e => Norm(e.GetString())).ToList();
        if (dadas.Count != esperadas.Count) return false;
        for (int k = 0; k < esperadas.Count; k++)
            if (Norm(dadas[k]) != esperadas[k]) return false;
        return true;
    }

    // RespuestaJson de Emparejar: array de pares [i,j]. Correcto = mismo conjunto que datos.pares.
    private static bool CorregirEmparejar(JsonElement datos, string? resp)
    {
        var dados = ParsePares(resp);
        var esperados = new HashSet<(int, int)>();
        foreach (var par in datos.GetProperty("pares").EnumerateArray())
            esperados.Add((par[0].GetInt32(), par[1].GetInt32()));
        return dados.SetEquals(esperados);
    }

    public async Task<(int tokIn, int tokOut)> CorregirAbiertasAsync(
        IReadOnlyList<(PreguntaExamen p, RespuestaUsuario r)> abiertas, string modelo, CancellationToken ct)
    {
        if (abiertas.Count == 0) return (0, 0);

        var sb = new StringBuilder();
        sb.AppendLine("Corregí estas respuestas abiertas. Para cada una devolvé puntos (0..máximo), feedback y si es ambigua.");
        sb.AppendLine("Devolvé SOLO JSON: {\"resultados\":[{\"puntos\":<n>,\"feedback\":\"...\",\"ambigua\":true|false}]} en el MISMO orden.");
        int n = 1;
        foreach (var (p, r) in abiertas)
        {
            sb.AppendLine($"--- Pregunta {n++} (máximo {p.Puntos} puntos) ---");
            sb.AppendLine($"Enunciado: {p.Enunciado}");
            sb.AppendLine($"Criterios/guía: {p.DatosJson}");
            sb.AppendLine($"Respuesta del alumno: {r.RespuestaJson}");
        }

        var sys = "Sos un evaluador de exámenes justo y conciso. Penalizá lo incorrecto y reconocé lo correcto. " +
                  "Marcá 'ambigua' si la respuesta es interpretable de varias formas.";
        var resp = await ia.CompletarAsync(new SolicitudIA(sys, sb.ToString(), 0.3, 8000, "examen-corr-v1", modelo), ct);

        var s = resp.Texto.Trim();
        int i = s.IndexOf('{'), j = s.LastIndexOf('}');
        if (i >= 0 && j > i) s = s[i..(j + 1)];
        using var doc = JsonDocument.Parse(s);
        var arr = doc.RootElement.GetProperty("resultados");
        for (int k = 0; k < abiertas.Count && k < arr.GetArrayLength(); k++)
        {
            var (p, r) = abiertas[k];
            var res = arr[k];
            var pts = res.TryGetProperty("puntos", out var pp) ? pp.GetDouble() : 0;
            r.PuntosObtenidos = Math.Clamp(pts, 0, p.Puntos);
            r.FeedbackIa = res.TryGetProperty("feedback", out var fb) ? fb.GetString() : null;
            r.Ambigua = res.TryGetProperty("ambigua", out var am) && am.GetBoolean();
            r.Correcta = r.PuntosObtenidos >= p.Puntos; // correcta si puntaje completo
        }
        return (resp.TokensPrompt, resp.TokensCompletion);
    }

    public ResultadoCorreccion CalcularResultado(
        IReadOnlyList<(PreguntaExamen p, RespuestaUsuario r)> todo, double escalaMax, double notaAprobacion)
    {
        double total = todo.Sum(x => x.p.Puntos);
        double obtenido = todo.Sum(x => x.r.PuntosObtenidos);
        double pct = total <= 0 ? 0 : Math.Round(obtenido / total * 100, 2);
        double nota = Math.Round(pct / 100 * escalaMax, 2);
        bool aprobado = nota >= notaAprobacion;
        int correctas = todo.Count(x => x.r.Correcta == true);
        var fb = $"Acertaste {correctas} de {todo.Count} preguntas ({pct}%).";
        return new ResultadoCorreccion(nota, pct, aprobado, fb, 0, 0);
    }

    // ── helpers de parseo de RespuestaJson ──
    private static HashSet<int> ParseIndices(string? resp)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(resp)) return set;
        try { foreach (var e in JsonDocument.Parse(resp).RootElement.EnumerateArray()) set.Add(e.GetInt32()); }
        catch { /* respuesta vacía o inválida ⇒ conjunto vacío */ }
        return set;
    }
    private static List<string> ParseStrings(string? resp)
    {
        var lista = new List<string>();
        if (string.IsNullOrWhiteSpace(resp)) return lista;
        try { foreach (var e in JsonDocument.Parse(resp).RootElement.EnumerateArray()) lista.Add(e.GetString() ?? ""); }
        catch { }
        return lista;
    }
    private static HashSet<(int, int)> ParsePares(string? resp)
    {
        var set = new HashSet<(int, int)>();
        if (string.IsNullOrWhiteSpace(resp)) return set;
        try { foreach (var par in JsonDocument.Parse(resp).RootElement.EnumerateArray()) set.Add((par[0].GetInt32(), par[1].GetInt32())); }
        catch { }
        return set;
    }
    private static string Norm(string? s) => (s ?? "").Trim().ToLowerInvariant();
}
