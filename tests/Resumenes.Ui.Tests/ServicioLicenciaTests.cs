using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Licencias;
using Resumenes.Ui.Servicios.Licencia;

namespace Resumenes.Ui.Tests;

public class ServicioLicenciaTests
{
    private const string Hwid = "hw-equipo-1";
    private static readonly DateTime Ahora = new(2026, 6, 24, 12, 0, 0, DateTimeKind.Utc);

    private sealed class HwidFake : IServicioHwid { public string ObtenerIdEquipo() => Hwid; }
    private sealed class RelojFake : IRelojUtc { public DateTime Ahora() => ServicioLicenciaTests.Ahora; }

    private sealed class AlmacenMemoria : IAlmacenLicencia
    {
        public DatosLicenciaGuardada? Datos;
        public DatosLicenciaGuardada? Leer() => Datos;
        public void Guardar(DatosLicenciaGuardada d) => Datos = d;
        public void Borrar() => Datos = null;
    }

    private sealed class ClienteFake(ResultadoActivacion? act = null, EstadoValidacionServidor val = EstadoValidacionServidor.Activa)
        : IClienteLicencias
    {
        public EstadoValidacionServidor Val = val;
        public Task<ResultadoActivacion> ActivarAsync(string c, string h, string n, CancellationToken ct)
            => Task.FromResult(act ?? new ResultadoActivacion(false, null, "clave_invalida"));
        public Task<EstadoValidacionServidor> ValidarAsync(string l, string h, CancellationToken ct)
            => Task.FromResult(Val);
    }

    // Genera (pub, token) reales para el hwid dado, con iat fijo.
    private static (string pub, string token) ParYToken(string hwid)
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var pub = Convert.ToBase64String(ec.ExportSubjectPublicKeyInfo());
        static string B64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var h = B64Url(Encoding.UTF8.GetBytes("{\"alg\":\"ES256\",\"typ\":\"JWT\"}"));
        var p = B64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { lic = "lic-1", hwid, sub = "Juan", iat = 1700000000 })));
        var f = ec.SignData(Encoding.ASCII.GetBytes($"{h}.{p}"), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return (pub, $"{h}.{p}.{B64Url(f)}");
    }

    private static ServicioLicencia Crear(string pub, AlmacenMemoria almacen, IClienteLicencias cliente)
        => new(new HwidFake(), almacen, cliente, new ValidadorTokenLicencia(pub), new RelojFake());

    [Fact]
    public async Task ObtenerEstado_SinDatos_DevuelveSinLicencia()
    {
        var (pub, _) = ParYToken(Hwid);
        var estado = await Crear(pub, new AlmacenMemoria(), new ClienteFake()).ObtenerEstadoAsync(default);
        Assert.Equal(EstadoLicenciaCliente.SinLicencia, estado);
    }

    [Fact]
    public async Task ObtenerEstado_TokenValidoReciente_DevuelveActiva_SinLlamarServidor()
    {
        var (pub, token) = ParYToken(Hwid);
        var almacen = new AlmacenMemoria { Datos = new DatosLicenciaGuardada(token, Ahora.AddDays(-3)) };
        var estado = await Crear(pub, almacen, new ClienteFake()).ObtenerEstadoAsync(default);
        Assert.Equal(EstadoLicenciaCliente.Activa, estado);
    }

    [Fact]
    public async Task ObtenerEstado_TocaRevalidar_ServidorActiva_ActualizaFechaYDevuelveActiva()
    {
        var (pub, token) = ParYToken(Hwid);
        var almacen = new AlmacenMemoria { Datos = new DatosLicenciaGuardada(token, Ahora.AddDays(-20)) };
        var estado = await Crear(pub, almacen, new ClienteFake(val: EstadoValidacionServidor.Activa)).ObtenerEstadoAsync(default);
        Assert.Equal(EstadoLicenciaCliente.Activa, estado);
        Assert.Equal(Ahora, almacen.Datos!.UltimaValidacionExitosa);
    }

    [Fact]
    public async Task ObtenerEstado_TocaRevalidar_ServidorRevoca_BorraYDevuelveRevocada()
    {
        var (pub, token) = ParYToken(Hwid);
        var almacen = new AlmacenMemoria { Datos = new DatosLicenciaGuardada(token, Ahora.AddDays(-20)) };
        var estado = await Crear(pub, almacen, new ClienteFake(val: EstadoValidacionServidor.Revocada)).ObtenerEstadoAsync(default);
        Assert.Equal(EstadoLicenciaCliente.Revocada, estado);
        Assert.Null(almacen.Datos);
    }

    [Fact]
    public async Task ObtenerEstado_TocaRevalidar_SinConexionDentroDeGracia_DevuelveActiva()
    {
        var (pub, token) = ParYToken(Hwid);
        var almacen = new AlmacenMemoria { Datos = new DatosLicenciaGuardada(token, Ahora.AddDays(-20)) };
        var estado = await Crear(pub, almacen, new ClienteFake(val: EstadoValidacionServidor.SinConexion)).ObtenerEstadoAsync(default);
        Assert.Equal(EstadoLicenciaCliente.Activa, estado);
    }

    [Fact]
    public async Task ObtenerEstado_GraciaAgotada_DevuelveBloqueadaPorGracia()
    {
        var (pub, token) = ParYToken(Hwid);
        var almacen = new AlmacenMemoria { Datos = new DatosLicenciaGuardada(token, Ahora.AddDays(-31)) };
        var estado = await Crear(pub, almacen, new ClienteFake(val: EstadoValidacionServidor.SinConexion)).ObtenerEstadoAsync(default);
        Assert.Equal(EstadoLicenciaCliente.BloqueadaPorGracia, estado);
    }

    [Fact]
    public async Task ObtenerEstado_TokenDeOtraMaquina_DevuelveSinLicencia()
    {
        var (pub, tokenOtro) = ParYToken("hw-OTRA-maquina");
        var almacen = new AlmacenMemoria { Datos = new DatosLicenciaGuardada(tokenOtro, Ahora.AddDays(-1)) };
        var estado = await Crear(pub, almacen, new ClienteFake()).ObtenerEstadoAsync(default);
        Assert.Equal(EstadoLicenciaCliente.SinLicencia, estado);
    }

    [Fact]
    public async Task Activar_Exitoso_GuardaTokenConFechaAhora()
    {
        var (pub, token) = ParYToken(Hwid);
        var almacen = new AlmacenMemoria();
        var cliente = new ClienteFake(act: new ResultadoActivacion(true, token, null));
        var r = await Crear(pub, almacen, cliente).ActivarAsync("RESU-AAAAA-BBBBB-CCCCC-DDDDD", "PC", default);
        Assert.True(r.Exitoso);
        Assert.NotNull(almacen.Datos);
        Assert.Equal(token, almacen.Datos!.Token);
        Assert.Equal(Ahora, almacen.Datos.UltimaValidacionExitosa);
    }

    [Fact]
    public async Task Activar_ServidorRechaza_NoGuarda()
    {
        var (pub, _) = ParYToken(Hwid);
        var almacen = new AlmacenMemoria();
        var cliente = new ClienteFake(act: new ResultadoActivacion(false, null, "limite_alcanzado"));
        var r = await Crear(pub, almacen, cliente).ActivarAsync("RESU-AAAAA-BBBBB-CCCCC-DDDDD", "PC", default);
        Assert.False(r.Exitoso);
        Assert.Equal("limite_alcanzado", r.Error);
        Assert.Null(almacen.Datos);
    }
}
