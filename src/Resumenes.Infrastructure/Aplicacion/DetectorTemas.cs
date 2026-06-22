using System.Text;
using System.Text.Json;
using Resumenes.Core.Apoyos;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;

namespace Resumenes.Infrastructure.Aplicacion;

// Detecta temas a partir de los textos limpios de TODOS los archivos. Si ya existe temas.json
// (editado por el usuario), lo respeta. Persiste Tema + TemaArchivo en SQLite.
public class DetectorTemas(IClienteIA ia, IRepositorioEstado repo, Configuracion cfg, ServicioPrompts prompts)
{
    public async Task<IReadOnlyList<TemaDetectado>> DetectarOCargarAsync(
        Analisis an, IReadOnlyList<(Archivo arc, string limpioPath)> archivos, string promptTemas, CancellationToken ct)
    {
        var temasPath = Path.Combine(cfg.RutaWorkspace, "analisis", an.Id, "temas.json");

        var temas = File.Exists(temasPath)
            ? CargarTemas(temasPath)
            : await DetectarConIA(archivos, promptTemas, ct);

        if (!File.Exists(temasPath))
            GuardarTemas(temasPath, temas);

        // Sincronizar a SQLite (la fuente editable es temas.json; D13).
        foreach (var t in temas)
        {
            repo.GuardarTema(new Tema(t.Id, an.Id, t.Nombre, t.Orden, true));
            foreach (var aid in t.Archivos)
                repo.GuardarTemaArchivo(t.Id, aid);
        }
        return temas;
    }

    private async Task<List<TemaDetectado>> DetectarConIA(
        IReadOnlyList<(Archivo arc, string limpioPath)> archivos, string promptTemas, CancellationToken ct)
    {
        // Presupuesto por archivo para que el input entre en una sola llamada.
        int presupuesto = Math.Max(1500, cfg.MaxCharsIA / Math.Max(1, archivos.Count));
        var sb = new StringBuilder();
        foreach (var (arc, limpio) in archivos)
        {
            var txt = File.Exists(limpio) ? await File.ReadAllTextAsync(limpio, ct) : "";
            if (txt.Length > presupuesto) txt = txt[..presupuesto];
            sb.AppendLine($"### ARCHIVO {arc.Id} :: {arc.NombreOriginal}");
            sb.AppendLine(txt);
            sb.AppendLine();
        }
        var idsValidos = archivos.Select(a => a.arc.Id).ToHashSet();

        var sys = prompts.SystemDeteccion(promptTemas);

        var r = await ia.CompletarAsync(new SolicitudIA(sys, sb.ToString(), 0.2, 4000, "deteccion-v1", cfg.Modelo), ct);

        var temas = new List<TemaDetectado>();
        try
        {
            using var doc = JsonDocument.Parse(ExtraerJson(r.Texto));
            int orden = 1;
            foreach (var t in doc.RootElement.GetProperty("temas").EnumerateArray())
            {
                var nombre = t.GetProperty("nombre").GetString() ?? $"Tema {orden}";
                var archivosT = new List<string>();
                if (t.TryGetProperty("archivos", out var arr))
                    foreach (var a in arr.EnumerateArray())
                    {
                        var aid = a.GetString();
                        if (aid != null && idsValidos.Contains(aid)) archivosT.Add(aid);
                    }
                if (archivosT.Count == 0) archivosT.AddRange(idsValidos);
                temas.Add(new TemaDetectado(SlugTema(nombre, orden), nombre, orden, archivosT));
                orden++;
            }
        }
        catch (JsonException) { /* cae al fallback */ }

        if (temas.Count == 0)
            temas.Add(new TemaDetectado("resumen-general-01", "Resumen general", 1, idsValidos.ToList()));
        return temas;
    }

    private static string ExtraerJson(string texto)
    {
        var s = texto.Trim();
        int i = s.IndexOf('{'), j = s.LastIndexOf('}');
        return (i >= 0 && j > i) ? s[i..(j + 1)] : s;
    }

    private static string SlugTema(string nombre, int orden)
    {
        var sb = new StringBuilder();
        foreach (var c in nombre.ToLowerInvariant())
            sb.Append(char.IsLetterOrDigit(c) ? c : '-');
        var slug = sb.ToString().Trim('-');
        if (slug.Length > 30) slug = slug[..30];
        if (slug.Length == 0) slug = "tema";
        return $"{slug}-{orden:00}";
    }

    private static List<TemaDetectado> CargarTemas(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var temas = new List<TemaDetectado>();
        int orden = 1;
        foreach (var t in doc.RootElement.GetProperty("temas").EnumerateArray())
        {
            var nombre = t.GetProperty("nombre").GetString() ?? $"Tema {orden}";
            var id = t.TryGetProperty("id", out var idEl) && idEl.GetString() is { Length: > 0 } s ? s : SlugTema(nombre, orden);
            var ord = t.TryGetProperty("orden", out var oEl) && oEl.ValueKind == JsonValueKind.Number ? oEl.GetInt32() : orden;
            var archivos = new List<string>();
            if (t.TryGetProperty("archivos", out var arr))
                foreach (var a in arr.EnumerateArray()) { var v = a.GetString(); if (v != null) archivos.Add(v); }
            temas.Add(new TemaDetectado(id, nombre, ord, archivos));
            orden++;
        }
        return temas;
    }

    private static void GuardarTemas(string path, List<TemaDetectado> temas)
    {
        var obj = new { temas = temas.Select(t => new { id = t.Id, nombre = t.Nombre, orden = t.Orden, archivos = t.Archivos }) };
        EscrituraAtomica.Escribir(path, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
    }
}
