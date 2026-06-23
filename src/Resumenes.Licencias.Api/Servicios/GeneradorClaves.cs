using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Resumenes.Licencias.Api.Servicios;

public static partial class GeneradorClaves
{
    private const string Alfabeto = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford base32

    public static string Generar()
    {
        var grupos = new string[4];
        for (var g = 0; g < 4; g++)
        {
            var chars = new char[5];
            for (var i = 0; i < 5; i++)
                chars[i] = Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];
            grupos[g] = new string(chars);
        }
        return "RESU-" + string.Join("-", grupos);
    }

    public static bool EsFormatoValido(string clave)
        => !string.IsNullOrEmpty(clave) && Patron().IsMatch(clave);

    [GeneratedRegex("^RESU-[0-9A-HJKMNP-TV-Z]{5}(-[0-9A-HJKMNP-TV-Z]{5}){3}$")]
    private static partial Regex Patron();
}
