using System.Text;
using System.Text.Json;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;

namespace Resumenes.Infrastructure.Examenes;

public class GeneradorExamen(IClienteIA ia) : IGeneradorExamen
{
    public const string FormatoJson =
        "Devolvé SOLO un JSON: {\"preguntas\":[{\"tipo\":\"<Tipo>\",\"enunciado\":\"...\",\"puntos\":<n>,\"datos\":{...}}]}. " +
        "Tipos válidos y su 'datos': " +
        "McUna/McVarias → {\"opciones\":[{\"texto\":\"...\",\"correcta\":true|false}]}; " +
        "VfJustificado → {\"afirmacion\":\"...\",\"esVerdadero\":true|false,\"justificacion\":\"por qué es verdadero o falso, breve\"}; " +
        "Desarrollo → {\"criterios\":\"qué debe contener una buena respuesta\",\"respuestaEsperada\":\"respuesta modelo breve (2-4 frases)\"}; " +
        "DesarrolloItems → {\"items\":[{\"enunciado\":\"...\",\"criterios\":\"...\",\"respuestaEsperada\":\"respuesta breve del ítem\"}]}; " +
        "Completar → {\"texto\":\"frase con ___\",\"respuestas\":[\"...\"]}; " +
        "Emparejar → {\"izquierda\":[\"...\"],\"derecha\":[\"...\"],\"pares\":[[0,1]]}. " +
        "Sin texto fuera del JSON. Respetá el idioma del contenido.";

    public async Task<ResultadoGeneracion> GenerarAsync(string examenId, string contenidoFuente, ConfigExamen cfg, string modelo, CancellationToken ct)
    {
        var pedido = new StringBuilder();
        pedido.AppendLine($"Generá un examen de dificultad {cfg.Dificultad} con estas cantidades por tipo:");
        foreach (var t in cfg.Tipos) pedido.AppendLine($"- {t.Cantidad} de tipo {t.Tipo}");
        pedido.AppendLine($"Repartí {cfg.PuntosTotales} puntos en total entre las preguntas.");
        pedido.AppendLine("Basate EXCLUSIVAMENTE en este contenido:");
        pedido.AppendLine(contenidoFuente);

        var sys = "Sos un generador de exámenes de estudio. " + FormatoJson;

        int tokIn = 0, tokOut = 0;
        Exception? ultimo = null;
        for (int intento = 0; intento < 2; intento++)
        {
            var r = await ia.CompletarAsync(new SolicitudIA(sys, pedido.ToString(), 0.4, 8000, "examen-gen-v1", modelo), ct);
            tokIn += r.TokensPrompt; tokOut += r.TokensCompletion;
            try
            {
                var preguntas = Parsear(examenId, r.Texto);
                if (preguntas.Count == 0) throw new InvalidOperationException("El examen generado no tiene preguntas.");
                return new ResultadoGeneracion(preguntas, tokIn, tokOut);
            }
            catch (Exception ex) { ultimo = ex; }
        }
        throw new InvalidOperationException("No se pudo generar un examen válido (JSON inesperado de la IA).", ultimo);
    }

    private static List<PreguntaExamen> Parsear(string examenId, string texto)
    {
        var s = texto.Trim();
        int i = s.IndexOf('{'), j = s.LastIndexOf('}');
        if (i >= 0 && j > i) s = s[i..(j + 1)];

        using var doc = JsonDocument.Parse(s);
        var preguntas = new List<PreguntaExamen>();
        int orden = 1;
        foreach (var p in doc.RootElement.GetProperty("preguntas").EnumerateArray())
        {
            var tipo = Enum.Parse<TipoPregunta>(p.GetProperty("tipo").GetString()!, ignoreCase: true);
            var enunciado = p.GetProperty("enunciado").GetString() ?? "";
            var puntos = p.TryGetProperty("puntos", out var pe) && pe.ValueKind == JsonValueKind.Number ? pe.GetDouble() : 1;
            var datos = p.TryGetProperty("datos", out var d) ? d.GetRawText() : "{}";
            preguntas.Add(new PreguntaExamen {
                Id = Guid.NewGuid().ToString("N"), ExamenId = examenId, Orden = orden++,
                Tipo = tipo, Enunciado = enunciado, Puntos = puntos, DatosJson = datos });
        }
        return preguntas;
    }
}
