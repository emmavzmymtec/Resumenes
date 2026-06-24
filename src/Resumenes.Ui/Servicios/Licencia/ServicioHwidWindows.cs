using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using Resumenes.Core.Licencias;

namespace Resumenes.Ui.Servicios.Licencia;

public sealed class ServicioHwidWindows : IServicioHwid
{
    private readonly string _semilla;

    public ServicioHwidWindows() => _semilla = LeerMachineGuid();

    // Para tests deterministas sin tocar el registro.
    internal ServicioHwidWindows(string semilla) => _semilla = semilla;

    public string ObtenerIdEquipo()
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("ResumenesApp|" + _semilla));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string LeerMachineGuid()
    {
        // HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid: estable, no requiere admin.
        var valor = Registry.GetValue(
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) as string;
        return string.IsNullOrWhiteSpace(valor) ? "maquina-desconocida" : valor;
    }
}
