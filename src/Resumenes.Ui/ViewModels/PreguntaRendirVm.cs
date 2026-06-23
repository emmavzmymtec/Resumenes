using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Resumenes.Core.Modelos;

namespace Resumenes.Ui.ViewModels;

public partial class OpcionRendirVm : ObservableObject
{
    public required string Texto { get; init; }
    [ObservableProperty] private bool _seleccionada;
}

public partial class HuecoVm : ObservableObject
{
    [ObservableProperty] private string _valor = "";
}

public partial class ItemDesarrolloVm : ObservableObject
{
    public required string Enunciado { get; init; }
    [ObservableProperty] private string _texto = "";
}

/// <summary>Par Izquierda–Derecha para Emparejar; sincroniza con SeleccionEmparejar del padre.</summary>
public partial class EmparejamientoItemVm : ObservableObject
{
    private readonly ObservableCollection<int> _selecciones;
    private readonly int _indice;

    public required string TextoIzquierda { get; init; }
    public required ObservableCollection<string> Derecha { get; init; }

    public int SeleccionIndice
    {
        get => _selecciones[_indice];
        set { if (_selecciones[_indice] != value) { _selecciones[_indice] = value; OnPropertyChanged(); } }
    }

    public EmparejamientoItemVm(ObservableCollection<int> selecciones, int indice)
    {
        _selecciones = selecciones;
        _indice = indice;
    }
}

public partial class PreguntaRendirVm : ObservableObject
{
    public PreguntaExamen Pregunta { get; }
    public TipoPregunta Tipo => Pregunta.Tipo;
    public string Enunciado => Pregunta.Enunciado;
    public double Puntos => Pregunta.Puntos;

    public ObservableCollection<OpcionRendirVm> Opciones { get; } = new();
    public ObservableCollection<HuecoVm> Huecos { get; } = new();
    public ObservableCollection<ItemDesarrolloVm> Items { get; } = new();
    public ObservableCollection<string> Izquierda { get; } = new();   // Emparejar
    public ObservableCollection<string> Derecha { get; } = new();
    public string? Afirmacion { get; }

    [ObservableProperty] private bool? _vf;                          // VfJustificado (parte V/F)
    [ObservableProperty] private string _textoRespuesta = "";        // Desarrollo / justificación
    [ObservableProperty] private bool _marcadaParaRevisar;
    // Para Emparejar: índice de la derecha elegido por cada izquierda (paralelo a Izquierda)
    public ObservableCollection<int> SeleccionEmparejar { get; } = new();
    // Para binding XAML de Emparejar: cada item lleva el texto izquierda + ComboBox de Derecha
    public ObservableCollection<EmparejamientoItemVm> EmparejamientoItems { get; } = new();

    public PreguntaRendirVm(PreguntaExamen p)
    {
        Pregunta = p;
        using var d = JsonDocument.Parse(p.DatosJson);
        var root = d.RootElement;
        switch (p.Tipo)
        {
            case TipoPregunta.McUna:
            case TipoPregunta.McVarias:
                foreach (var o in root.GetProperty("opciones").EnumerateArray())
                    Opciones.Add(new OpcionRendirVm { Texto = o.GetProperty("texto").GetString() ?? "" });
                break;
            case TipoPregunta.Completar:
                foreach (var _ in root.GetProperty("respuestas").EnumerateArray()) Huecos.Add(new HuecoVm());
                break;
            case TipoPregunta.DesarrolloItems:
                foreach (var it in root.GetProperty("items").EnumerateArray())
                    Items.Add(new ItemDesarrolloVm { Enunciado = it.GetProperty("enunciado").GetString() ?? "" });
                break;
            case TipoPregunta.Emparejar:
                foreach (var x in root.GetProperty("izquierda").EnumerateArray()) { Izquierda.Add(x.GetString() ?? ""); SeleccionEmparejar.Add(-1); }
                foreach (var y in root.GetProperty("derecha").EnumerateArray()) Derecha.Add(y.GetString() ?? "");
                for (int i = 0; i < Izquierda.Count; i++)
                    EmparejamientoItems.Add(new EmparejamientoItemVm(SeleccionEmparejar, i)
                        { TextoIzquierda = Izquierda[i], Derecha = Derecha });
                break;
            case TipoPregunta.VfJustificado:
                Afirmacion = root.TryGetProperty("afirmacion", out var af) ? af.GetString() : p.Enunciado;
                break;
        }
    }

    public string ConstruirRespuestaJson() => Tipo switch
    {
        TipoPregunta.McUna         => SerializarMcUna(),
        TipoPregunta.McVarias      => JsonSerializer.Serialize(Indices()),
        TipoPregunta.Completar     => JsonSerializer.Serialize(Huecos.Select(h => h.Valor)),
        TipoPregunta.Emparejar     => JsonSerializer.Serialize(
            SeleccionEmparejar.Select((j, i) => new[] { i, j }).Where(par => par[1] >= 0)),
        TipoPregunta.DesarrolloItems => JsonSerializer.Serialize(Items.Select(i => i.Texto)),
        TipoPregunta.VfJustificado => JsonSerializer.Serialize(new { vf = Vf ?? false, justificacion = TextoRespuesta }),
        _                          => JsonSerializer.Serialize(TextoRespuesta),   // Desarrollo
    };

    private List<int> Indices() => Opciones.Select((o, i) => (o, i)).Where(x => x.o.Seleccionada).Select(x => x.i).ToList();

    private string SerializarMcUna()
    {
        var idx = Indices();
        return idx.Count > 0 ? idx[0].ToString() : "null";
    }
}
