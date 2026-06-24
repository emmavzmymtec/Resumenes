using System.Text.Json;
using Resumenes.Core.Modelos;

namespace Resumenes.Core.Examenes;

/// <summary>Arma, por tipo de pregunta, la respuesta del alumno y la correcta en texto legible.</summary>
public static class DescriptorRespuestas
{
    public static (string usuario, string correcta) Describir(PreguntaExamen p, string? respuestaJson)
    {
        try
        {
            using var datos = JsonDocument.Parse(p.DatosJson);
            var d = datos.RootElement;
            return p.Tipo switch
            {
                TipoPregunta.McUna => McUna(d, respuestaJson),
                TipoPregunta.McVarias => McVarias(d, respuestaJson),
                TipoPregunta.Completar => Completar(d, respuestaJson),
                TipoPregunta.Emparejar => Emparejar(d, respuestaJson),
                TipoPregunta.VfJustificado => Vf(d, respuestaJson),
                TipoPregunta.DesarrolloItems => DesarrolloItems(d, respuestaJson),
                _ => Desarrollo(d, respuestaJson), // Desarrollo
            };
        }
        catch
        {
            return (TextoPlano(respuestaJson), "");
        }
    }

    private static string[] Opciones(JsonElement d) =>
        d.GetProperty("opciones").EnumerateArray().Select(o => o.GetProperty("texto").GetString() ?? "").ToArray();

    private static (string, string) McUna(JsonElement d, string? resp)
    {
        var ops = Opciones(d);
        var correctas = d.GetProperty("opciones").EnumerateArray()
            .Where(o => o.GetProperty("correcta").GetBoolean())
            .Select(o => o.GetProperty("texto").GetString() ?? "");
        var u = int.TryParse((resp ?? "").Trim('"', ' '), out var i) && i >= 0 && i < ops.Length ? ops[i] : "(sin responder)";
        return (u, string.Join(", ", correctas));
    }

    private static (string, string) McVarias(JsonElement d, string? resp)
    {
        var ops = Opciones(d);
        var elegidas = Indices(resp).Where(i => i >= 0 && i < ops.Length).Select(i => ops[i]);
        var correctas = d.GetProperty("opciones").EnumerateArray()
            .Where(o => o.GetProperty("correcta").GetBoolean()).Select(o => o.GetProperty("texto").GetString() ?? "");
        return (Unir(elegidas), string.Join(", ", correctas));
    }

    private static (string, string) Completar(JsonElement d, string? resp)
    {
        var esperadas = d.GetProperty("respuestas").EnumerateArray().Select(e => e.GetString() ?? "");
        return (Unir(Strings(resp)), string.Join(", ", esperadas));
    }

    private static (string, string) Emparejar(JsonElement d, string? resp)
    {
        var izq = d.GetProperty("izquierda").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
        var der = d.GetProperty("derecha").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
        string Par(int i, int j) => $"{(i >= 0 && i < izq.Length ? izq[i] : "?")} → {(j >= 0 && j < der.Length ? der[j] : "?")}";
        var usuario = Pares(resp).Select(par => Par(par.Item1, par.Item2));
        var correcta = d.GetProperty("pares").EnumerateArray().Select(par => Par(par[0].GetInt32(), par[1].GetInt32()));
        return (Unir(usuario), Unir(correcta));
    }

    private static (string, string) Vf(JsonElement d, string? resp)
    {
        var esVerd = d.TryGetProperty("esVerdadero", out var ev) && ev.GetBoolean();
        var just = d.TryGetProperty("justificacion", out var ju) ? ju.GetString() ?? "" : "";
        var u = "(sin responder)";
        try { using var r = JsonDocument.Parse(resp ?? ""); var rr = r.RootElement;
            var vf = rr.TryGetProperty("vf", out var v) && v.GetBoolean();
            var jus = rr.TryGetProperty("justificacion", out var j) ? j.GetString() ?? "" : "";
            u = $"{(vf ? "Verdadero" : "Falso")}{(string.IsNullOrWhiteSpace(jus) ? "" : $" — {jus}")}"; } catch { }
        var c = $"{(esVerd ? "Verdadero" : "Falso")}{(string.IsNullOrWhiteSpace(just) ? "" : $" — {just}")}";
        return (u, c);
    }

    private static (string, string) Desarrollo(JsonElement d, string? resp)
    {
        var esperada = d.TryGetProperty("respuestaEsperada", out var re) ? re.GetString() ?? "" : "";
        return (TextoPlano(resp), esperada);
    }

    private static (string, string) DesarrolloItems(JsonElement d, string? resp)
    {
        var esperadas = d.GetProperty("items").EnumerateArray()
            .Select(it => it.TryGetProperty("respuestaEsperada", out var re) ? re.GetString() ?? "" : "");
        return (Unir(Strings(resp)), Unir(esperadas));
    }

    // helpers de parseo de RespuestaJson
    private static List<int> Indices(string? resp)
    { var l = new List<int>(); try { using var d = JsonDocument.Parse(resp ?? ""); foreach (var e in d.RootElement.EnumerateArray()) l.Add(e.GetInt32()); } catch { } return l; }
    private static List<string> Strings(string? resp)
    { var l = new List<string>(); try { using var d = JsonDocument.Parse(resp ?? ""); foreach (var e in d.RootElement.EnumerateArray()) l.Add(e.GetString() ?? ""); } catch { } return l; }
    private static List<(int, int)> Pares(string? resp)
    { var l = new List<(int, int)>(); try { using var d = JsonDocument.Parse(resp ?? ""); foreach (var par in d.RootElement.EnumerateArray()) l.Add((par[0].GetInt32(), par[1].GetInt32())); } catch { } return l; }
    private static string TextoPlano(string? resp)
    { try { using var d = JsonDocument.Parse(resp ?? ""); return d.RootElement.ValueKind == JsonValueKind.String ? d.RootElement.GetString() ?? "" : (resp ?? ""); } catch { return resp ?? ""; } }
    private static string Unir(IEnumerable<string> xs) { var s = string.Join("  ·  ", xs.Where(x => !string.IsNullOrWhiteSpace(x))); return string.IsNullOrEmpty(s) ? "(sin responder)" : s; }
}
