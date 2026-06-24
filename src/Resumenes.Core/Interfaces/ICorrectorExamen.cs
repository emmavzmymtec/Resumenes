using Resumenes.Core.Modelos;

namespace Resumenes.Core.Interfaces;

public record ResultadoCorreccion(double Nota, double Porcentaje, bool Aprobado, string FeedbackGeneral, int TokensEntrada, int TokensSalida);

public interface ICorrectorExamen
{
    /// <summary>Corrige una pregunta objetiva LOCALMENTE; muta r (Correcta, PuntosObtenidos).</summary>
    void CorregirObjetivo(PreguntaExamen p, RespuestaUsuario r);
    /// <summary>Corrige las preguntas abiertas con IA; muta cada r (PuntosObtenidos, FeedbackIa, Ambigua). Acumula tokens en el resultado del Servicio.</summary>
    Task<(int tokIn, int tokOut)> CorregirAbiertasAsync(IReadOnlyList<(PreguntaExamen p, RespuestaUsuario r)> abiertas, string modelo, CancellationToken ct);
    ResultadoCorreccion CalcularResultado(IReadOnlyList<(PreguntaExamen p, RespuestaUsuario r)> todo, double escalaMax, double notaAprobacion);
    /// <summary>Genera una devolución breve y motivadora con IA a partir del desempeño. Best-effort: ante error devuelve un texto de respaldo y (0,0) tokens.</summary>
    Task<(string texto, int tokIn, int tokOut)> GenerarDevolucionAsync(
        IReadOnlyList<(PreguntaExamen p, RespuestaUsuario r)> todo, double pct, string modelo, CancellationToken ct);
}
