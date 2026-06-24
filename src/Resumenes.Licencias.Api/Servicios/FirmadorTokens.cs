using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Resumenes.Licencias.Api.Servicios;

public sealed partial class FirmadorTokens
{
    private readonly string _claveEntrada;

    public FirmadorTokens(string clavePrivada) => _claveEntrada = clavePrivada;

    public string Firmar(string licenciaId, string hwid, string comprador)
    {
        var ec = CargarClavePrivada(_claveEntrada);
        try
        {
            var key = new ECDsaSecurityKey(ec)
            {
                CryptoProviderFactory = new CryptoProviderFactory { CacheSignatureProviders = false }
            };
            var credenciales = new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256);

            var descriptor = new SecurityTokenDescriptor
            {
                Claims = new Dictionary<string, object>
                {
                    ["lic"] = licenciaId,
                    ["hwid"] = hwid,
                    ["sub"] = comprador,
                },
                IssuedAt = DateTime.UtcNow,
                SigningCredentials = credenciales,
            };

            return new JsonWebTokenHandler().CreateToken(descriptor);
        }
        finally
        {
            ec.Dispose();
        }
    }

    /// <summary>
    /// Carga una clave privada EC tolerando cómo se haya pegado en una variable de
    /// entorno: PEM con saltos reales, PEM con saltos escapados (\n literales) o
    /// aplastado en una línea, o el base64 del DER (SEC1 o PKCS8) sin headers.
    /// </summary>
    internal static ECDsa CargarClavePrivada(string entrada)
    {
        if (string.IsNullOrWhiteSpace(entrada))
            throw new ArgumentException("La clave privada está vacía.", nameof(entrada));

        var ec = ECDsa.Create();
        var texto = entrada.Trim();

        // 1) PEM tal cual (bien formado, con saltos reales).
        if (texto.Contains("-----BEGIN") && IntentarImportarPem(ec, texto))
            return ec;

        // 2) PEM con saltos escapados (\n / \r\n literales) en vez de reales.
        if (texto.Contains("-----BEGIN"))
        {
            var normalizado = texto
                .Replace("\\r\\n", "\n")
                .Replace("\\n", "\n")
                .Replace("\\r", "\n");
            if (!ReferenceEquals(normalizado, texto) && IntentarImportarPem(ec, normalizado))
                return ec;
        }

        // 3) Extraer el base64 (entre headers si los hay, o el texto puro) y cargar el DER.
        if (IntentarImportarDer(ec, Convert.FromBase64String(ExtraerBase64(texto))))
            return ec;

        ec.Dispose();
        throw new ArgumentException(
            "No se pudo cargar la clave privada EC: formato no reconocido " +
            "(se acepta PEM —con o sin saltos— o el base64 del DER de la clave EC).",
            nameof(entrada));
    }

    private static bool IntentarImportarPem(ECDsa ec, string pem)
    {
        try { ec.ImportFromPem(pem); return true; }
        catch (ArgumentException) { return false; }
        catch (CryptographicException) { return false; }
    }

    private static bool IntentarImportarDer(ECDsa ec, byte[] der)
    {
        try { ec.ImportECPrivateKey(der, out _); return true; }
        catch (CryptographicException) { /* probar PKCS8 */ }
        try { ec.ImportPkcs8PrivateKey(der, out _); return true; }
        catch (CryptographicException) { return false; }
    }

    /// <summary>Quita separadores escapados, headers/footers PEM y todo whitespace,
    /// dejando solo el base64 del cuerpo de la clave.</summary>
    private static string ExtraerBase64(string texto)
    {
        var s = texto
            .Replace("\\r\\n", "")
            .Replace("\\n", "")
            .Replace("\\r", "")
            .Replace("\\t", "");
        s = HeaderPem().Replace(s, "");
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
            if (char.IsLetterOrDigit(c) || c is '+' or '/' or '=')
                sb.Append(c);
        return sb.ToString();
    }

    [GeneratedRegex("-----[^-]*-----")]
    private static partial Regex HeaderPem();
}
