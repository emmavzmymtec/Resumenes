using System.Text;
using Resumenes.Core.Apoyos;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;
using Resumenes.Core.Orquestacion;

namespace Resumenes.Infrastructure.Aplicacion;

// Fase 4: el procesamiento por-archivo llega hasta "texto limpio"; los temas (cross-archivo) se
// detectan aparte (DetectorTemas) y luego cada tema se consolida/resume/PDF.
public class ConstructorPipeline(
    IRasterizador rasterizador, IServicioOcr ocr, IClienteIA ia, IGeneradorPdf pdf,
    IConversorOffice conversor, Configuracion cfg, ServicioPrompts prompts, CacheDerivados cache)
{
    // ---- Pasos por-archivo: Captura -> OcrBruto -> LimpiezaIA. Devuelve también la ruta del limpio.txt. ----
    public (IReadOnlyList<PasoPipeline> pasos, string limpioPath) PasosPorArchivo(Analisis an, Archivo arc, string rutaAbs)
    {
        var baseFuentes = Path.Combine(cfg.RutaWorkspace, "analisis", an.Id, "00_fuentes", arc.Id);
        var imagenesDir = Path.Combine(baseFuentes, "imagenes");
        var bruto = Path.Combine(baseFuentes, "texto_bruto", "bruto.txt");
        var limpio = Path.Combine(baseFuentes, "texto_limpio", "limpio.txt");

        bool esOffice = arc.Tipo is TipoArchivo.Doc or TipoArchivo.Docx or TipoArchivo.Ppt or TipoArchivo.Pptx;
        bool esTxt = arc.Tipo == TipoArchivo.Txt;
        IReadOnlyList<string> imagenes = Array.Empty<string>();

        var pasos = new[]
        {
            // Captura: PDF -> rasterizar; Office -> LibreOffice a PDF -> rasterizar; TXT -> nada.
            new PasoPipeline(Etapa.Captura, arc.Id, null, esTxt ? null : imagenesDir,
                _ => Task.FromResult(arc.HashSha256),
                async ctx =>
                {
                    if (esTxt) return;
                    var fuente = rutaAbs;
                    if (esOffice)
                        fuente = await conversor.ConvertirAPdfAsync(rutaAbs, Path.Combine(baseFuentes, "pdf"), ctx.Ct);
                    imagenes = await rasterizador.RasterizarAsync(fuente, imagenesDir, cfg.Dpi, ctx.Ct,
                        new Progress<(int a, int t)>(p => ctx.Reportar($"rasterizando {p.a}/{p.t}", p.a, p.t)));
                }),

            // OcrBruto: TXT -> texto directo (sin OCR); resto -> OCR de las imágenes.
            new PasoPipeline(Etapa.OcrBruto, arc.Id, null, bruto,
                _ => Task.FromResult(arc.HashSha256),
                async ctx =>
                {
                    if (esTxt)
                    {
                        EscrituraAtomica.Escribir(bruto, await File.ReadAllTextAsync(rutaAbs, ctx.Ct));
                        return;
                    }
                    var hitOcr = cache.BuscarOcr(arc.HashSha256, cfg.Dpi);
                    if (hitOcr != null)
                    {
                        ctx.Reportar("OCR reutilizado de caché");
                        CopiarArtefacto(hitOcr, bruto);
                        return;
                    }
                    if (imagenes.Count == 0)
                        imagenes = Directory.GetFiles(imagenesDir, "pagina_*.jpg").OrderBy(x => x).ToArray();
                    EscrituraAtomica.Escribir(bruto, await ocr.OcrAsync(imagenes, ctx.Ct,
                        new Progress<(int a, int t)>(p => ctx.Reportar($"OCR página {p.a}/{p.t}", p.a, p.t))));
                    cache.GuardarOcr(arc.HashSha256, cfg.Dpi, bruto);
                }),

            // LimpiezaIA: corrige OCR sin inventar; chunking si excede MaxCharsIA.
            new PasoPipeline(Etapa.LimpiezaIA, arc.Id, null, limpio,
                _ => Task.FromResult(Hashing.Sha256HexDeTexto(
                    Hashing.Sha256HexDeArchivo(bruto) + "|" +
                    prompts.HashEditable(ServicioPrompts.ClaveLimpieza) + "|" + cfg.Modelo)),
                async ctx =>
                {
                    var hashPrompt = prompts.HashEditable(ServicioPrompts.ClaveLimpieza);
                    var hitLimpieza = cache.BuscarLimpieza(arc.HashSha256, cfg.Dpi, hashPrompt, cfg.Modelo);
                    if (hitLimpieza != null)
                    {
                        ctx.Reportar("limpieza reutilizada de caché");
                        CopiarArtefacto(hitLimpieza, limpio);
                        return;
                    }
                    var entrada = await File.ReadAllTextAsync(bruto, ctx.Ct);
                    var sb = new StringBuilder();
                    foreach (var bloque in Chunking.Dividir(entrada, cfg.MaxCharsIA))
                    {
                        ctx.Reportar("pensando…");
                        var r = await ia.CompletarAsync(new SolicitudIA(
                            prompts.SystemLimpieza(), bloque, 0.2, 8000, "limpieza-v1", cfg.Modelo), ctx.Ct);
                        sb.Append(r.Texto).Append('\n');
                    }
                    EscrituraAtomica.Escribir(limpio, sb.ToString().Trim());
                    cache.GuardarLimpieza(arc.HashSha256, cfg.Dpi, hashPrompt, cfg.Modelo, limpio);
                }, PromptVersion: "limpieza-v1", ModeloIa: cfg.Modelo),
        };
        return (pasos, limpio);
    }

    // ---- Pasos por-tema: ConsolidaciónTemas (junta los limpios asignados) -> ResumenFinal -> GeneraciónPDF. ----
    public IReadOnlyList<PasoPipeline> PasosPorTema(Analisis an, TemaDetectado tema, IReadOnlyList<string> limpiosDelTema, string promptResumen = "")
    {
        var raizAn = Path.Combine(cfg.RutaWorkspace, "analisis", an.Id);
        var consolidado = Path.Combine(raizAn, "consolidado", $"{tema.Id}.txt");
        var resumen = Path.Combine(raizAn, "resumen", $"{tema.Id}.txt");
        var pdfSalida = Path.Combine(raizAn, "final", $"{NombreSeguro(tema.Nombre)}.pdf");

        // hash de entrada del tema = hash de los hashes (ordenados) de los limpios asignados.
        Task<string> HashTema(CancellationToken _) => Task.FromResult(
            Hashing.Sha256HexDeTexto(string.Join("|",
                limpiosDelTema.Where(File.Exists).OrderBy(x => x).Select(Hashing.Sha256HexDeArchivo))));

        return new[]
        {
            new PasoPipeline(Etapa.ConsolidacionTemas, null, tema.Id, consolidado,
                HashTema,
                async ctx =>
                {
                    var sb = new StringBuilder();
                    foreach (var lp in limpiosDelTema.Where(File.Exists).OrderBy(x => x))
                    {
                        sb.AppendLine(await File.ReadAllTextAsync(lp, ctx.Ct));
                        sb.AppendLine();
                    }
                    EscrituraAtomica.Escribir(consolidado, sb.ToString().Trim());
                }),

            new PasoPipeline(Etapa.ResumenFinal, null, tema.Id, resumen,
                // El prompt del alumno forma parte del hash de entrada: un prompt distinto invalida
                // la unidad cacheada y fuerza la regeneración (re-procesar). Mismo prompt + mismo
                // contenido ⇒ mismo hash ⇒ se saltea (idempotente).
                _ => Task.FromResult(Hashing.Sha256HexDeTexto(
                    Hashing.Sha256HexDeArchivo(consolidado) + "|" + (promptResumen ?? "") + "|" +
                    prompts.HashEditable(ServicioPrompts.ClaveResumen))),
                async ctx =>
                {
                    var entrada = await File.ReadAllTextAsync(consolidado, ctx.Ct);
                    var partes = new List<string>();
                    foreach (var bloque in Chunking.Dividir(entrada, cfg.MaxCharsIA))
                    {
                        ctx.Reportar("pensando…");
                        var sys = prompts.SystemResumen(tema.Nombre, promptResumen);
                        var r = await ia.CompletarAsync(new SolicitudIA(
                            sys, bloque, 0.5, 8000, "resumen-v1", cfg.Modelo), ctx.Ct);
                        partes.Add(r.Texto);
                    }
                    EscrituraAtomica.Escribir(resumen, UnirResumenes(partes));
                }, PromptVersion: "resumen-v1", ModeloIa: cfg.Modelo),

            new PasoPipeline(Etapa.GeneracionPDF, null, tema.Id, pdfSalida,
                _ => Task.FromResult(Hashing.Sha256HexDeArchivo(resumen)),
                async ctx =>
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(pdfSalida)!);
                    await pdf.GenerarAsync(resumen, pdfSalida, tema.Nombre, "Guía de estudio", ctx.Ct);
                }),
        };
    }

    private static string UnirResumenes(IReadOnlyList<string> partes)
    {
        if (partes.Count == 1) return partes[0];
        var sb = new StringBuilder();
        bool titulo = false, subtitulo = false;
        foreach (var parte in partes)
            foreach (var linea in parte.Split('\n'))
            {
                var l = linea.TrimStart();
                if (l.StartsWith("#TITULO", StringComparison.OrdinalIgnoreCase)) { if (titulo) continue; titulo = true; }
                else if (l.StartsWith("#SUBTITULO", StringComparison.OrdinalIgnoreCase)) { if (subtitulo) continue; subtitulo = true; }
                sb.Append(linea).Append('\n');
            }
        return sb.ToString().Trim();
    }

    private static void CopiarArtefacto(string origen, string destino)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.Copy(origen, destino, true);
    }

    private static string NombreSeguro(string nombre)
    {
        var sb = new StringBuilder();
        foreach (var c in nombre ?? "")
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_');
        var s = sb.ToString().Trim('_');
        if (s.Length == 0) return "tema";
        return s.Length > 40 ? s[..40] : s;
    }
}

// Defaults NEUTROS (multi-idioma) y partes FIJAS de cada prompt. La parte fija nunca
// es editable por el usuario (sostiene el parseo del PDF / el JSON de detección).
public static class Prompts
{
    // ── Limpieza de OCR ──
    public const string LimpiezaEditableDefault =
        "Sos un corrector de texto OCR. Corregí errores de OCR, reconstruí palabras partidas " +
        "y quitá ruido. Mantené el idioma original del texto y respetá su ortografía, tildes y signos. " +
        "PROHIBIDO agregar información que no esté en el texto.";
    public const string LimpiezaFijo =
        "Devolvé solo el texto corregido.";

    // ── Detección de temas ──
    public const string DeteccionEditableDefault =
        "Sos un organizador de material de estudio. Agrupá el contenido de los archivos en TEMAS " +
        "coherentes para estudiar (ni demasiados ni demasiado pocos).";
    public const string DeteccionFijo =
        "Devolvé SOLO un JSON con la forma {\"temas\":[{\"nombre\":\"...\",\"archivos\":[\"<archivo_id>\"]}]} " +
        "usando exactamente los <archivo_id> que aparecen como '### ARCHIVO <id>'. Sin nada de texto fuera del JSON.";

    // ── Resumen ──
    public const string ResumenEditableDefault =
        "Sos un asistente de estudio. Resumí en el mismo idioma del material, sin extremos, " +
        "sin eliminar contenido, priorizando el original.";
    public const string ResumenFijo =
        "Devolvé el resultado en este formato de marcadores (uno por línea): " +
        "#TITULO:, #SUBTITULO:, y bloques @seccion:, @texto:, @blt:, @ejemplo:, @dato:, @tip:. " +
        "Usá \\n para saltos de línea dentro de un bloque. Lo que agregues de contexto marcalo con @dato o @tip.";
}
