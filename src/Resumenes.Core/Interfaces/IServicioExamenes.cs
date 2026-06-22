using Resumenes.Core.Modelos;

namespace Resumenes.Core.Interfaces;

public interface IServicioExamenes
{
    Task<Examen> CrearAsync(string analisisId, string titulo, ConfigExamen cfg, CancellationToken ct);
    Task<Examen> FinalizarYCorregirAsync(string examenId, CancellationToken ct);
    IReadOnlyList<Examen> Historial(string analisisId);
}
