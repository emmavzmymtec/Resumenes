namespace Resumenes.Infrastructure.Aplicacion;

public class Configuracion
{
    public string RutaWorkspace { get; set; } = "";
    public string PythonExe { get; set; } = "python";
    public string ScriptsDir { get; set; } = "runtime/scripts";
    public string ModelosPaddle { get; set; } = "runtime/modelos";
    public string FontsDir { get; set; } = "runtime/fonts";
    public string LibreOfficeDir { get; set; } = "runtime/libreoffice";
    public int Dpi { get; set; } = 200;
    public int MaxCharsIA { get; set; } = 16000;   // umbral de chunking para las llamadas a la IA
    public string Modelo { get; set; } = "deepseek-v4-flash";
    public string BaseUrlDeepseek { get; set; } = "https://api.deepseek.com";
    /// <summary>URL del manifest.json con los bundles a descargar. Editable según el host.</summary>
    public string ManifestUrl { get; set; } = "https://example.com/resumenes/manifest.json";
    /// <summary>Raíz por-usuario donde se descomprime el runtime. Vacío = App calcula %LOCALAPPDATA%/ResumenesApp/runtime.</summary>
    public string RutaRuntime { get; set; } = "";
    /// <summary>Raíz de la caché de derivados (OCR/limpieza). Vacío = App calcula %LOCALAPPDATA%/ResumenesApp/cache.</summary>
    public string RutaCache { get; set; } = "";
    /// <summary>Tarifa estimada de tokens de entrada (USD por millón). Editable; puede variar.</summary>
    public decimal PrecioInputPorMillonUsd { get; set; } = 0.27m;
    /// <summary>Tarifa estimada de tokens de salida (USD por millón). Editable; puede variar.</summary>
    public decimal PrecioOutputPorMillonUsd { get; set; } = 1.10m;
    /// <summary>Escala máxima de la nota (default 10 ⇒ 0–10).</summary>
    public double EscalaNotaMaxima { get; set; } = 10;
    /// <summary>Nota mínima para aprobar (en la escala configurada).</summary>
    public double NotaAprobacion { get; set; } = 6;
}
