using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;
using Resumenes.Core.Orquestacion;
using Resumenes.Infrastructure.Aplicacion;

namespace Resumenes.Tests.Fakes;

public class RelojFijo : IRelojUtc
{
    public DateTime Valor { get; set; } = new(2026, 6, 17, 12, 0, 0, DateTimeKind.Utc);
    public DateTime Ahora() => Valor;
}

// Repositorio en memoria, suficiente para testear el orquestador.
public class RepositorioEnMemoria : IRepositorioEstado
{
    private readonly Dictionary<string, Unidad> _unidades = new();
    private readonly Dictionary<string, Analisis> _analisis = new();
    public readonly List<Unidad> Guardados = new();

    private static string Clave(string a, string? arc, string? t, Etapa e) => $"{a}|{arc}|{t}|{e}";

    public void InicializarEsquema() { }
    public Analisis? ObtenerAnalisisPorFingerprint(string fingerprint)
        => _analisis.Values.FirstOrDefault(a => a.Fingerprint == fingerprint);
    public void GuardarAnalisis(Analisis a) { _analisis[a.Id] = a; }
    public IReadOnlyList<Analisis> ListarAnalisis()
        => _analisis.Values.OrderByDescending(a => a.ActualizadoEn).ToList();
    public void EliminarAnalisis(string id) => _analisis.Remove(id);
    public Archivo? ObtenerArchivo(string id) => null;
    public void GuardarArchivo(Archivo a) { }
    public Tema? ObtenerTema(string id) => null;
    public void GuardarTema(Tema t) { }
    public void GuardarTemaArchivo(string temaId, string archivoId) { }

    public Unidad? ObtenerUnidad(string analisisId, string? archivoId, string? temaId, Etapa etapa)
        => _unidades.TryGetValue(Clave(analisisId, archivoId, temaId, etapa), out var u) ? u : null;

    public void GuardarUnidad(Unidad u)
    {
        _unidades[Clave(u.AnalisisId, u.ArchivoId, u.TemaId, u.Etapa)] = u;
        Guardados.Add(u);
    }

    private readonly Dictionary<string, string> _ajustes = new();
    public string? ObtenerAjustePrompt(string clave) => _ajustes.TryGetValue(clave, out var v) ? v : null;
    public void GuardarAjustePrompt(string clave, string texto) => _ajustes[clave] = texto;
    public void EliminarAjustePrompt(string clave) => _ajustes.Remove(clave);
}

public class FakeClienteIA : IClienteIA
{
    public Func<SolicitudIA, string>? Responder { get; set; }
    public Task<RespuestaIA> CompletarAsync(SolicitudIA req, CancellationToken ct)
        => Task.FromResult(new RespuestaIA(Responder?.Invoke(req) ?? req.PromptUser, "stop", 1, 1, 2));
}

public class FakeRasterizador : IRasterizador
{
    public Task<IReadOnlyList<string>> RasterizarAsync(string p, string o, int dpi, CancellationToken ct,
        IProgress<(int, int)>? sp = null) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
}

public class FakeOcr : IServicioOcr
{
    public Task<string> OcrAsync(IReadOnlyList<string> img, CancellationToken ct, IProgress<(int, int)>? sp = null)
        => Task.FromResult("");
}

public class FakeGeneradorPdf : IGeneradorPdf
{
    public Task GenerarAsync(string c, string pdf, string t, string s, CancellationToken ct)
    { File.WriteAllText(pdf, "PDF"); return Task.CompletedTask; }
}

public class FakeConversor : IConversorOffice
{
    public Task<string> ConvertirAPdfAsync(string o, string outDir, CancellationToken ct)
        => Task.FromResult(o);
}

public static class ServicioAnalisisFactory
{
    public static ServicioAnalisis ParaTests(IRepositorioEstado repo, string workspace)
    {
        var cfg = new Configuracion { RutaWorkspace = workspace };
        return new ServicioAnalisis(
            new FakeRasterizador(),
            new FakeOcr(),
            new FakeClienteIA(),
            new FakeGeneradorPdf(),
            new FakeConversor(),
            repo,
            new RelojFijo(),
            cfg);
    }
}
