namespace Resumenes.Core.Interfaces;

/// <summary>Saldo de la cuenta del proveedor de IA. Null al consultarlo = no disponible.</summary>
public record SaldoCuenta(bool Disponible, string Moneda, string TotalDisponible);

public interface IClienteSaldo
{
    /// <summary>Consulta el saldo; devuelve null si falla (sin romper la app).</summary>
    Task<SaldoCuenta?> ObtenerAsync(CancellationToken ct);
}
