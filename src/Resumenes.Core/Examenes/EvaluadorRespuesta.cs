using Resumenes.Core.Modelos;

namespace Resumenes.Core.Examenes;

public static class EvaluadorRespuesta
{
    public static EstadoRespuesta Estado(double obtenido, double puntos)
    {
        if (puntos <= 0) return EstadoRespuesta.Incorrecta;
        var frac = obtenido / puntos;
        if (frac >= 0.85) return EstadoRespuesta.Correcta;
        if (frac >= 0.40) return EstadoRespuesta.Parcial;
        return EstadoRespuesta.Incorrecta;
    }
}
