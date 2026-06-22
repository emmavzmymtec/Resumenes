using System.Net.Http.Headers;
using System.Text.Json;
using Resumenes.Core.Interfaces;

namespace Resumenes.Infrastructure.IA;

/// <summary>
/// Consulta el saldo de la cuenta Deepseek (GET /user/balance). Tolera cualquier fallo
/// devolviendo null: el saldo es informativo y nunca debe romper la app.
/// </summary>
public class ClienteSaldo(HttpClient http, IAlmacenSecretos secretos, string baseUrl) : IClienteSaldo
{
    public async Task<SaldoCuenta?> ObtenerAsync(CancellationToken ct)
    {
        try
        {
            var key = secretos.ObtenerApiKey();
            if (string.IsNullOrWhiteSpace(key)) return null;

            using var msg = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/user/balance");
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

            using var resp = await http.SendAsync(msg, ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            bool disponible = root.TryGetProperty("is_available", out var av) && av.GetBoolean();

            if (!root.TryGetProperty("balance_infos", out var infos) || infos.GetArrayLength() == 0)
                return new SaldoCuenta(disponible, "", "");

            // Preferir USD si existe; si no, el primero.
            JsonElement elegido = infos[0];
            foreach (var bi in infos.EnumerateArray())
                if (bi.TryGetProperty("currency", out var c) && c.GetString() == "USD") { elegido = bi; break; }

            var moneda = elegido.TryGetProperty("currency", out var cu) ? cu.GetString() ?? "" : "";
            var total = elegido.TryGetProperty("total_balance", out var tb) ? tb.GetString() ?? "" : "";
            return new SaldoCuenta(disponible, moneda, total);
        }
        catch
        {
            return null; // saldo informativo: nunca rompe
        }
    }
}
