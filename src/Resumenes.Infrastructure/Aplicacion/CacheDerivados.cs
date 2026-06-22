using Resumenes.Core.Interfaces;

namespace Resumenes.Infrastructure.Aplicacion;

/// <summary>
/// Caché content-addressed de artefactos derivados (OCR bruto y texto limpio) reutilizables
/// entre análisis distintos. Índice en SQLite (CacheDerivado), contenido en disco bajo
/// RutaCache/&lt;hash_contenido&gt;/. Un registro sin archivo en disco se trata como miss
/// (degradación segura ante caché corrupta).
/// </summary>
public class CacheDerivados(IRepositorioEstado repo, Configuracion cfg)
{
    private const string TipoOcr = "OcrBruto";
    private const string TipoLimpieza = "Limpieza";

    private string RaizCache() => string.IsNullOrWhiteSpace(cfg.RutaCache)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResumenesApp", "cache")
        : cfg.RutaCache;

    private static string VarianteOcr(int dpi) => $"dpi={dpi};ocr=v1";
    private static string VarianteLimpieza(int dpi, string hashPrompt, string modelo)
        => $"dpi={dpi};ocr=v1;prompt={hashPrompt};modelo={modelo}";

    public string? BuscarOcr(string hashContenido, int dpi)
        => BuscarValido(hashContenido, TipoOcr, VarianteOcr(dpi));

    public string? BuscarLimpieza(string hashContenido, int dpi, string hashPrompt, string modelo)
        => BuscarValido(hashContenido, TipoLimpieza, VarianteLimpieza(dpi, hashPrompt, modelo));

    public void GuardarOcr(string hashContenido, int dpi, string rutaOrigen)
        => Guardar(hashContenido, TipoOcr, VarianteOcr(dpi), "ocr.txt", rutaOrigen);

    public void GuardarLimpieza(string hashContenido, int dpi, string hashPrompt, string modelo, string rutaOrigen)
        => Guardar(hashContenido, TipoLimpieza, VarianteLimpieza(dpi, hashPrompt, modelo),
                   $"limpio__{Recorte(hashPrompt)}__{Sanear(modelo)}.txt", rutaOrigen);

    private string? BuscarValido(string hash, string tipo, string variante)
    {
        var ruta = repo.BuscarCacheDerivado(hash, tipo, variante);
        return (ruta != null && File.Exists(ruta)) ? ruta : null;
    }

    private void Guardar(string hash, string tipo, string variante, string nombreArchivo, string rutaOrigen)
    {
        try
        {
            var dir = Path.Combine(RaizCache(), hash);
            Directory.CreateDirectory(dir);
            var destino = Path.Combine(dir, nombreArchivo);
            File.Copy(rutaOrigen, destino, true);
            repo.GuardarCacheDerivado(hash, tipo, variante, destino);
        }
        catch
        {
            // Poblar la caché es best-effort: el artefacto local del análisis ya existe.
            // Un fallo al cachear (p. ej. carrera de File.Copy) no debe romper el análisis.
        }
    }

    private static string Recorte(string s) => s.Length > 8 ? s[..8] : s;
    private static string Sanear(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }
}
