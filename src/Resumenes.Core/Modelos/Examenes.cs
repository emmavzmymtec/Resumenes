namespace Resumenes.Core.Modelos;

public enum TipoPregunta { McUna, McVarias, VfJustificado, Desarrollo, DesarrolloItems, Completar, Emparejar }
public enum EstadoExamen { Borrador, EnCurso, Finalizado, Corregido }

public class Examen
{
    public required string Id { get; set; }
    public required string AnalisisId { get; set; }
    public required string Titulo { get; set; }
    public string ConfigJson { get; set; } = "{}";
    public EstadoExamen Estado { get; set; } = EstadoExamen.Borrador;
    public double? Nota { get; set; }
    public double? Porcentaje { get; set; }
    public bool? Aprobado { get; set; }
    public string? FeedbackGeneral { get; set; }
    public int Tokens { get; set; }
    public double CostoEstimado { get; set; }
    public DateTime CreadoEn { get; set; }
    public DateTime? IniciadoEn { get; set; }
    public DateTime? FinalizadoEn { get; set; }
}

public class PreguntaExamen
{
    public required string Id { get; set; }
    public required string ExamenId { get; set; }
    public int Orden { get; set; }
    public TipoPregunta Tipo { get; set; }
    public required string Enunciado { get; set; }
    public double Puntos { get; set; }
    public string DatosJson { get; set; } = "{}";
    public string? TemaId { get; set; }
}

public class RespuestaUsuario
{
    public required string Id { get; set; }
    public required string ExamenId { get; set; }
    public required string PreguntaId { get; set; }
    public string? RespuestaJson { get; set; }
    public bool? Correcta { get; set; }
    public double PuntosObtenidos { get; set; }
    public string? FeedbackIa { get; set; }
    public bool Ambigua { get; set; }
}

public record CantidadPorTipo(TipoPregunta Tipo, int Cantidad);
public record ConfigExamen(
    IReadOnlyList<CantidadPorTipo> Tipos,
    IReadOnlyList<string> TemasIncluidos,
    string Dificultad,
    double PuntosTotales,
    int TiempoLimiteMin,
    string Fuente);  // "rapido" | "completo"
public record ResultadoGeneracion(IReadOnlyList<PreguntaExamen> Preguntas, int TokensEntrada, int TokensSalida);
