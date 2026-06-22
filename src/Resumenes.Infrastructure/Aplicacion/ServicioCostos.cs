using System.Globalization;
using Resumenes.Core.Interfaces;

namespace Resumenes.Infrastructure.Aplicacion;

/// <summary>
/// Calcula el costo estimado en USD de un análisis a partir de los tokens persistidos
/// y las tarifas configurables. Es una ESTIMACIÓN (las tarifas pueden variar).
/// </summary>
public class ServicioCostos(IRepositorioEstado repo, Configuracion cfg)
{
    public decimal CostoDe(string analisisId)
    {
        var (entrada, salida) = repo.SumarTokensAnalisis(analisisId);
        return (entrada * cfg.PrecioInputPorMillonUsd + salida * cfg.PrecioOutputPorMillonUsd) / 1_000_000m;
    }

    /// <summary>Costo formateado, p. ej. "US$ 0.0123".</summary>
    public string CostoLegible(string analisisId)
        => "US$ " + CostoDe(analisisId).ToString("0.####", CultureInfo.InvariantCulture);
}
