using Resumenes.Core.Interfaces;
using Resumenes.Core.Licencias;

namespace Resumenes.Ui.Servicios.Licencia;

public sealed class ServicioLicencia(
    IServicioHwid hwid,
    IAlmacenLicencia almacen,
    IClienteLicencias cliente,
    ValidadorTokenLicencia validador,
    IRelojUtc reloj)
{
    public string IdEquipo => hwid.ObtenerIdEquipo();

    public async Task<EstadoLicenciaCliente> ObtenerEstadoAsync(CancellationToken ct)
    {
        var datos = almacen.Leer();
        var idEquipo = hwid.ObtenerIdEquipo();
        var validacion = datos is null
            ? ResultadoValidacionToken.Invalido
            : validador.Validar(datos.Token, idEquipo);

        var ahora = reloj.Ahora();
        var estado = EvaluadorEstadoLicencia.Evaluar(
            validacion.Valido, datos?.UltimaValidacionExitosa, ahora);

        if (estado != EstadoLicenciaCliente.RevalidarAhora)
            return estado;

        // Toca revalidar contra el servidor.
        var resp = await cliente.ValidarAsync(validacion.Claims!.LicenciaId, idEquipo, ct);
        switch (resp)
        {
            case EstadoValidacionServidor.Activa:
                almacen.Guardar(datos! with { UltimaValidacionExitosa = ahora });
                return EstadoLicenciaCliente.Activa;
            case EstadoValidacionServidor.Revocada:
                almacen.Borrar();
                return EstadoLicenciaCliente.Revocada;
            default: // SinConexion: seguir con la gracia (ya sabemos que <= 30 días, si no sería BloqueadaPorGracia)
                return EstadoLicenciaCliente.Activa;
        }
    }

    public async Task<ResultadoActivacion> ActivarAsync(string clave, string nombreEquipo, CancellationToken ct)
    {
        var idEquipo = hwid.ObtenerIdEquipo();
        var r = await cliente.ActivarAsync(clave, idEquipo, nombreEquipo, ct);
        if (!r.Exitoso || r.Token is null) return r;

        // El token debe validar contra nuestra clave pública y nuestro hwid.
        if (!validador.Validar(r.Token, idEquipo).Valido)
            return new ResultadoActivacion(false, null, "token_invalido");

        almacen.Guardar(new DatosLicenciaGuardada(r.Token, reloj.Ahora()));
        return r;
    }
}
