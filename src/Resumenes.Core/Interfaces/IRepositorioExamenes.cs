using Resumenes.Core.Modelos;

namespace Resumenes.Core.Interfaces;

public interface IRepositorioExamenes
{
    void GuardarExamen(Examen e);
    Examen? ObtenerExamen(string id);
    IReadOnlyList<Examen> ListarExamenes(string analisisId);
    void EliminarExamen(string id);

    void GuardarPregunta(PreguntaExamen p);
    IReadOnlyList<PreguntaExamen> ListarPreguntas(string examenId);

    void GuardarRespuesta(RespuestaUsuario r);
    IReadOnlyList<RespuestaUsuario> ListarRespuestas(string examenId);
}
