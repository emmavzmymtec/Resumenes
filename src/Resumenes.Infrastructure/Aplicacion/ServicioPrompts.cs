using Resumenes.Core.Apoyos;
using Resumenes.Core.Interfaces;

namespace Resumenes.Infrastructure.Aplicacion;

/// <summary>
/// Resuelve el prompt efectivo de cada etapa con IA: parte EDITABLE (override del usuario en
/// SQLite, o el default neutro del código) + parte FIJA (formato protegido). Expone el hash del
/// texto editable para sumarlo al hash_entrada de las unidades (idempotencia: editar el prompt
/// invalida y reprocesa).
/// </summary>
public class ServicioPrompts(IRepositorioEstado repo)
{
    public const string ClaveLimpieza  = "limpieza";
    public const string ClaveDeteccion = "deteccion";
    public const string ClaveResumen   = "resumen";

    public string DefaultEditable(string clave) => clave switch
    {
        ClaveLimpieza  => Prompts.LimpiezaEditableDefault,
        ClaveDeteccion => Prompts.DeteccionEditableDefault,
        ClaveResumen   => Prompts.ResumenEditableDefault,
        _ => throw new ArgumentException($"Clave de prompt desconocida: {clave}")
    };

    public string TextoFijo(string clave) => clave switch
    {
        ClaveLimpieza  => Prompts.LimpiezaFijo,
        ClaveDeteccion => Prompts.DeteccionFijo,
        ClaveResumen   => Prompts.ResumenFijo,
        _ => throw new ArgumentException($"Clave de prompt desconocida: {clave}")
    };

    public string ObtenerEditable(string clave)
    {
        var ov = repo.ObtenerAjustePrompt(clave);
        return string.IsNullOrWhiteSpace(ov) ? DefaultEditable(clave) : ov;
    }

    public void GuardarEditable(string clave, string texto) => repo.GuardarAjustePrompt(clave, texto);

    public void RestaurarDefault(string clave) => repo.EliminarAjustePrompt(clave);

    public string HashEditable(string clave) => Hashing.Sha256HexDeTexto(ObtenerEditable(clave));

    public string SystemLimpieza() =>
        $"{ObtenerEditable(ClaveLimpieza)} {Prompts.LimpiezaFijo}";

    public string SystemDeteccion(string promptTemas) =>
        ObtenerEditable(ClaveDeteccion) + " " +
        (string.IsNullOrWhiteSpace(promptTemas) ? "" : $"Priorizá estos temas indicados por el alumno: {promptTemas}. ") +
        Prompts.DeteccionFijo;

    public string SystemResumen(string nombreTema, string? promptAlumno)
    {
        var estilo = string.IsNullOrWhiteSpace(promptAlumno)
            ? ObtenerEditable(ClaveResumen)
            : "Seguí ESTRICTAMENTE estas indicaciones del alumno para el contenido y el estilo del resumen " +
              "(tienen prioridad sobre cualquier estilo por defecto): " + promptAlumno.Trim();
        return $"{estilo} {Prompts.ResumenFijo} El tema de este resumen es: \"{nombreTema}\".";
    }
}
