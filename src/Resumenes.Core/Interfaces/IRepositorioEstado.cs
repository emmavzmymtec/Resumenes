using Resumenes.Core.Modelos;

namespace Resumenes.Core.Interfaces;

public interface IRepositorioEstado
{
    void InicializarEsquema();

    Analisis? ObtenerAnalisisPorFingerprint(string fingerprint);
    void GuardarAnalisis(Analisis a);

    Archivo? ObtenerArchivo(string id);
    void GuardarArchivo(Archivo a);

    Tema? ObtenerTema(string id);
    void GuardarTema(Tema t);
    void GuardarTemaArchivo(string temaId, string archivoId);

    IReadOnlyList<Analisis> ListarAnalisis();

    /// <summary>Elimina un análisis y, en cascada (ON DELETE CASCADE), sus archivos, temas y unidades.</summary>
    void EliminarAnalisis(string id);

    Unidad? ObtenerUnidad(string analisisId, string? archivoId, string? temaId, Etapa etapa);
    void GuardarUnidad(Unidad u);

    /// <summary>Texto editable (rol/estilo) override de un prompt; null si no hay override.</summary>
    string? ObtenerAjustePrompt(string clave);
    void GuardarAjustePrompt(string clave, string texto);
    /// <summary>Borra el override (vuelve al default del código).</summary>
    void EliminarAjustePrompt(string clave);

    /// <summary>Ruta del artefacto cacheado para (hash, tipo, variante); null si no hay registro.</summary>
    string? BuscarCacheDerivado(string hashContenido, string tipo, string claveVariante);
    void GuardarCacheDerivado(string hashContenido, string tipo, string claveVariante, string ruta);
}
