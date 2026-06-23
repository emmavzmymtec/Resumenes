using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;
using Resumenes.Infrastructure.Aplicacion;
using Resumenes.Ui.Servicios;
using Resumenes.Ui.Vistas;

namespace Resumenes.Ui.ViewModels;

/// <summary>
/// ViewModel de la pantalla de resultados: lista los PDFs generados en final/,
/// permite abrirlos, abrir la carpeta, exportar a otro destino o re-procesar con un nuevo prompt.
/// </summary>
public partial class ResultadosVm : VistaModeloBase
{
    private readonly string _rutaWorkspace;
    private readonly ServicioNavegacion _nav;
    private readonly IServicioAnalisis _servicio;
    private readonly Wpf.Ui.IContentDialogService _dialogos;
    private readonly ServicioCostos _costos;
    private Analisis? _analisis;

    [ObservableProperty]
    private string _rutaSalida = string.Empty;

    [ObservableProperty]
    private string _mensajeEstado = string.Empty;

    [ObservableProperty]
    private string _costoLegible = string.Empty;

    /// <summary>PDFs encontrados en la carpeta final/.</summary>
    public ObservableCollection<ResultadoPdfVm> Pdfs { get; } = new();

    public ResultadosVm(string rutaWorkspace, ServicioNavegacion nav, IServicioAnalisis servicio,
        Wpf.Ui.IContentDialogService dialogos, ServicioCostos costos)
    {
        _rutaWorkspace = rutaWorkspace;
        _nav = nav;
        _servicio = servicio;
        _dialogos = dialogos;
        _costos = costos;
        // Pdfs es ObservableCollection (no [ObservableProperty]): el toolkit no reevalúa
        // CanExecute solo. Lo forzamos cuando cambia la lista para habilitar "Exportar".
        Pdfs.CollectionChanged += (_, _) => ExportarCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Carga los PDFs del análisis indicado desde la carpeta final/.
    /// </summary>
    public void Cargar(Analisis an)
    {
        _analisis = an;
        Pdfs.Clear();
        MensajeEstado = string.Empty;

        RutaSalida = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(_rutaWorkspace, "analisis", an.Id, "final"));

        if (!System.IO.Directory.Exists(RutaSalida))
        {
            MensajeEstado = "No se encontró la carpeta de resultados.";
            return;
        }

        var archivos = System.IO.Directory
            .EnumerateFiles(RutaSalida, "*.pdf")
            .OrderBy(f => f)
            .ToList();

        foreach (var pdf in archivos)
            Pdfs.Add(new ResultadoPdfVm(pdf));

        MensajeEstado = archivos.Count == 0
            ? "No se generaron PDFs."
            : $"{archivos.Count} PDF(s) generado(s).";

        CostoLegible = _costos.CostoLegible(an.Id);
    }

    // ── Comandos ─────────────────────────────────────────────────────────

    /// <summary>Abre un PDF con la aplicación predeterminada del sistema.</summary>
    [RelayCommand]
    private void AbrirPdf(ResultadoPdfVm? pdf)
    {
        if (pdf is null || !System.IO.File.Exists(pdf.RutaCompleta)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = pdf.RutaCompleta,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MensajeEstado = $"No se pudo abrir el PDF: {ex.Message}";
        }
    }

    /// <summary>Abre la carpeta final/ en el Explorador de Windows.</summary>
    [RelayCommand]
    private void AbrirCarpeta()
    {
        if (!System.IO.Directory.Exists(RutaSalida))
        {
            MensajeEstado = "La carpeta de resultados no existe todavía.";
            return;
        }
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = RutaSalida,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MensajeEstado = $"No se pudo abrir la carpeta: {ex.Message}";
        }
    }

    /// <summary>
    /// Abre un diálogo para seleccionar la carpeta destino y copia todos los PDFs allí.
    /// </summary>
    [RelayCommand(CanExecute = nameof(HayPdfs))]
    private void Exportar()
    {
        var dialogo = new OpenFolderDialog
        {
            Title = "Seleccionar carpeta destino para exportar los PDFs"
        };
        if (dialogo.ShowDialog() != true) return;

        var destino = dialogo.FolderName;
        int copiados = 0;
        var errores = new List<string>();

        foreach (var pdf in Pdfs)
        {
            if (!System.IO.File.Exists(pdf.RutaCompleta)) continue;
            try
            {
                var nombreDestino = System.IO.Path.Combine(destino, pdf.NombreArchivo);
                System.IO.File.Copy(pdf.RutaCompleta, nombreDestino, overwrite: true);
                copiados++;
            }
            catch (Exception ex)
            {
                errores.Add($"{pdf.NombreArchivo}: {ex.Message}");
            }
        }

        MensajeEstado = errores.Count == 0
            ? $"Exportados {copiados} PDF(s) a {destino}."
            : $"Exportados {copiados} PDF(s). Errores: {string.Join(", ", errores)}";
    }

    private bool HayPdfs() => Pdfs.Count > 0;

    /// <summary>Navega a la pantalla de historial de exámenes para este análisis.</summary>
    [RelayCommand]
    private void IrAExamenes()
    {
        if (_analisis is not null) _nav.Navegar<VistaExamenes>(new ParametroExamenes(_analisis));
    }

    /// <summary>Vuelve a la pantalla de Inicio (historial de análisis).</summary>
    [RelayCommand]
    private void Volver() => _nav?.Navegar<VistaInicio>();

    /// <summary>
    /// Re-genera TODOS los resúmenes/PDFs con un nuevo prompt escrito por el usuario, reutilizando
    /// el texto ya procesado (sin re-OCR). Pide el prompt con un diálogo temático y navega a "Generando".
    /// </summary>
    [RelayCommand]
    private async Task ReProcesar()
    {
        if (_analisis is null) return;
        if (!System.IO.Directory.Exists(_analisis.CarpetaOrigen))
        {
            MensajeEstado = "La carpeta de origen ya no existe; no se puede re-procesar.";
            return;
        }

        // Diálogo (ContentDialog temático) con textbox multilínea para el nuevo prompt.
        var caja = new Wpf.Ui.Controls.TextBox
        {
            PlaceholderText = "Ej.: conceptos cortitos y concretos, multiple choice, ejemplos con casos concretos…",
            Text = LeerPromptResumen(_analisis.Id),   // pre-cargar el último prompt usado
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 110,
            MaxHeight = 240
        };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Escribí cómo querés los resúmenes. Se regeneran TODOS los temas reutilizando el texto ya " +
                   "procesado (sin OCR). El formato del PDF se respeta siempre.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });
        panel.Children.Add(caja);

        var dialogo = new Wpf.Ui.Controls.ContentDialog
        {
            Title = "Re-procesar con nuevo prompt",
            Content = panel,
            PrimaryButtonText = "Re-procesar",
            PrimaryButtonAppearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            CloseButtonText = "Cancelar",
            BorderThickness = new Thickness(1)
        };
        if (Application.Current?.TryFindResource("SystemAccentColorBrush") is Brush b)
            dialogo.BorderBrush = b;

        if (await _dialogos.ShowAsync(dialogo, CancellationToken.None) != Wpf.Ui.Controls.ContentDialogResult.Primary)
            return;

        var nuevoPrompt = (caja.Text ?? "").Trim();
        try
        {
            // Recargar el estado del servicio (lista de archivos) y los temas ya detectados (temas.json),
            // luego navegar a "Generando" forzando la regeneración con el nuevo prompt.
            await _servicio.AbrirOCrearAsync(_analisis.CarpetaOrigen, CancellationToken.None);
            var temas = await _servicio.DetectarTemasAsync(_analisis, "", CancellationToken.None);
            _nav.Navegar<VistaGenerando>(new ParametroTemas(_analisis, temas, nuevoPrompt));
        }
        catch (Exception ex)
        {
            MensajeEstado = $"No se pudo re-procesar: {ex.Message}";
        }
    }

    /// <summary>Lee el prompt de resumen persistido para el análisis, o "" si no hay.</summary>
    private string LeerPromptResumen(string analisisId)
    {
        try
        {
            var ruta = System.IO.Path.Combine(_rutaWorkspace, "analisis", analisisId, "resumen-prompt.txt");
            return System.IO.File.Exists(ruta) ? System.IO.File.ReadAllText(ruta) : string.Empty;
        }
        catch { return string.Empty; }
    }
}

/// <summary>Ítem de la lista de PDFs generados.</summary>
public sealed class ResultadoPdfVm
{
    public ResultadoPdfVm(string rutaCompleta)
    {
        RutaCompleta = rutaCompleta;
        NombreArchivo = System.IO.Path.GetFileName(rutaCompleta);
        NombreSinExtension = System.IO.Path.GetFileNameWithoutExtension(rutaCompleta);
        TamanoLegible = ObtenerTamano(rutaCompleta);
    }

    public string RutaCompleta { get; }
    public string NombreArchivo { get; }
    public string NombreSinExtension { get; }
    public string TamanoLegible { get; }

    private static string ObtenerTamano(string ruta)
    {
        try
        {
            var bytes = new System.IO.FileInfo(ruta).Length;
            return bytes switch
            {
                < 1024 => $"{bytes} B",
                < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
                _ => $"{bytes / (1024.0 * 1024):F1} MB"
            };
        }
        catch { return ""; }
    }
}
