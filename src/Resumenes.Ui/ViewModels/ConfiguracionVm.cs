using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Resumenes.Core.Interfaces;
using Resumenes.Infrastructure.Aplicacion;
using Wpf.Ui.Appearance;

namespace Resumenes.Ui.ViewModels;

/// <summary>
/// ViewModel de la pantalla de Configuración de la aplicación.
/// Permite editar la API key (DPAPI), carpeta de salida, DPI, modelo y tema oscuro/claro.
/// </summary>
public partial class ConfiguracionVm : VistaModeloBase
{
    private readonly IAlmacenSecretos _secretos;
    private readonly Configuracion _cfg;
    private readonly ServicioPrompts _prompts;

    // ── API key ──────────────────────────────────────────────────────────
    /// <summary>Texto ingresado para la nueva API key (nunca se muestra la guardada).</summary>
    [ObservableProperty]
    private string _apiKey = string.Empty;

    /// <summary>True si ya existe una key guardada en el almacén DPAPI.</summary>
    [ObservableProperty]
    private bool _tieneKey;

    // ── Configuración de análisis ────────────────────────────────────────
    [ObservableProperty]
    private string _carpetaSalida = string.Empty;

    [ObservableProperty]
    private int _dpi;

    [ObservableProperty]
    private string _modelo = string.Empty;

    // ── Tema ─────────────────────────────────────────────────────────────
    [ObservableProperty]
    private bool _temaOscuro;

    // ── Prompts editables (rol/estilo) ──
    [ObservableProperty] private string _promptLimpieza = string.Empty;
    [ObservableProperty] private string _promptDeteccion = string.Empty;
    [ObservableProperty] private string _promptResumen = string.Empty;

    // Partes fijas (solo lectura, para mostrar contexto en la UI)
    public string FormatoLimpieza => Prompts.LimpiezaFijo;
    public string FormatoDeteccion => Prompts.DeteccionFijo;
    public string FormatoResumen => Prompts.ResumenFijo;

    // ── Feedback ─────────────────────────────────────────────────────────
    [ObservableProperty]
    private string _mensajeEstado = string.Empty;

    public ConfiguracionVm(IAlmacenSecretos secretos, Configuracion cfg, ServicioPrompts prompts)
    {
        _secretos = secretos;
        _cfg = cfg;
        _prompts = prompts;
        Cargar();
    }

    /// <summary>Carga los valores actuales de la configuración.</summary>
    public void Cargar()
    {
        TieneKey = _secretos.ObtenerApiKey() is not null;
        ApiKey = string.Empty; // nunca mostrar la key en claro

        CarpetaSalida = _cfg.RutaWorkspace;
        Dpi = _cfg.Dpi;
        Modelo = _cfg.Modelo;

        // Detectar tema actual
        TemaOscuro = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;

        PromptLimpieza = _prompts.ObtenerEditable(ServicioPrompts.ClaveLimpieza);
        PromptDeteccion = _prompts.ObtenerEditable(ServicioPrompts.ClaveDeteccion);
        PromptResumen = _prompts.ObtenerEditable(ServicioPrompts.ClaveResumen);
    }

    // ── Comandos ────────────────────────────────────────────────────────

    /// <summary>Guarda la API key ingresada en el almacén DPAPI.</summary>
    [RelayCommand(CanExecute = nameof(PuedeGuardarKey))]
    private void GuardarApiKey()
    {
        if (string.IsNullOrWhiteSpace(ApiKey)) return;
        try
        {
            _secretos.GuardarApiKey(ApiKey.Trim());
            TieneKey = true;
            ApiKey = string.Empty;
            MensajeEstado = "API key guardada correctamente.";
        }
        catch (Exception ex)
        {
            MensajeEstado = $"Error al guardar la API key: {ex.Message}";
        }
    }

    private bool PuedeGuardarKey() => !string.IsNullOrWhiteSpace(ApiKey);

    partial void OnApiKeyChanged(string value) => GuardarApiKeyCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// Serializa la configuración actual a &lt;exe&gt;/config/settings.json.
    /// </summary>
    [RelayCommand]
    private void GuardarConfig()
    {
        try
        {
            _cfg.RutaWorkspace = CarpetaSalida.Trim();
            _cfg.Dpi = Dpi;
            _cfg.Modelo = Modelo.Trim();

            var raizApp = AppContext.BaseDirectory;
            var cfgDir = Path.Combine(raizApp, "config");
            Directory.CreateDirectory(cfgDir);
            var cfgPath = Path.Combine(cfgDir, "settings.json");

            var json = JsonSerializer.Serialize(_cfg, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(cfgPath, json);
            MensajeEstado = "Configuración guardada correctamente.";
        }
        catch (Exception ex)
        {
            MensajeEstado = $"Error al guardar configuración: {ex.Message}";
        }
    }

    /// <summary>Aplica el tema oscuro o claro usando ApplicationThemeManager de WPF-UI 4.x.</summary>
    [RelayCommand]
    private void AplicarTema()
    {
        var tema = TemaOscuro ? ApplicationTheme.Dark : ApplicationTheme.Light;
        ApplicationThemeManager.Apply(tema);
        MensajeEstado = $"Tema {(TemaOscuro ? "oscuro" : "claro")} aplicado.";
    }

    [RelayCommand]
    private void GuardarPrompts()
    {
        try
        {
            _prompts.GuardarEditable(ServicioPrompts.ClaveLimpieza, PromptLimpieza.Trim());
            _prompts.GuardarEditable(ServicioPrompts.ClaveDeteccion, PromptDeteccion.Trim());
            _prompts.GuardarEditable(ServicioPrompts.ClaveResumen, PromptResumen.Trim());
            MensajeEstado = "Prompts guardados. Los próximos análisis (o regeneraciones) los usarán.";
        }
        catch (Exception ex)
        {
            MensajeEstado = $"Error al guardar los prompts: {ex.Message}";
        }
    }

    [RelayCommand]
    private void RestaurarPrompts()
    {
        try
        {
            _prompts.RestaurarDefault(ServicioPrompts.ClaveLimpieza);
            _prompts.RestaurarDefault(ServicioPrompts.ClaveDeteccion);
            _prompts.RestaurarDefault(ServicioPrompts.ClaveResumen);
            PromptLimpieza = _prompts.ObtenerEditable(ServicioPrompts.ClaveLimpieza);
            PromptDeteccion = _prompts.ObtenerEditable(ServicioPrompts.ClaveDeteccion);
            PromptResumen = _prompts.ObtenerEditable(ServicioPrompts.ClaveResumen);
            MensajeEstado = "Prompts restaurados a los valores por defecto.";
        }
        catch (Exception ex)
        {
            MensajeEstado = $"Error al restaurar los prompts: {ex.Message}";
        }
    }
}
