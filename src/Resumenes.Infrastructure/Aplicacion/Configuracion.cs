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
}
