using Resumenes.Core.Modelos;

namespace Resumenes.Core.Interfaces;

public interface IGeneradorExamen
{
    Task<ResultadoGeneracion> GenerarAsync(string examenId, string contenidoFuente, ConfigExamen cfg, string modelo, CancellationToken ct);
}
