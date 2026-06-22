using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;

namespace Resumenes.Core.Orquestacion;

public class PipelineOrquestador(IRepositorioEstado repo, IRelojUtc reloj)
{
    public async Task<ResultadoEjecucion> EjecutarAsync(
        string analisisId, IReadOnlyList<PasoPipeline> pasos, CancellationToken ct,
        FaseAnalisis fase = FaseAnalisis.Limpieza, string item = "", int itemIndice = 0, int itemTotal = 0,
        IProgress<ProgresoPaso>? progreso = null)
    {
        int ok = 0, salteados = 0, errores = 0;
        var mensajes = new List<string>();

        void Emitir(Etapa etapa, EstadoEvento estado, string detalle) =>
            progreso?.Report(new ProgresoPaso(fase, item, itemIndice, itemTotal, etapa, detalle, null, null, estado));

        foreach (var paso in pasos)
        {
            ct.ThrowIfCancellationRequested();
            var existente = repo.ObtenerUnidad(analisisId, paso.ArchivoId, paso.TemaId, paso.Etapa);
            var hash = await paso.CalcularHashEntrada(ct);

            // Reutilización: completado + hash igual, o fijado por el usuario.
            if (existente is { Estado: EstadoUnidad.Completado } &&
                (existente.HashEntrada == hash || existente.FijadoPorUsuario))
            {
                salteados++;
                Emitir(paso.Etapa, EstadoEvento.Salteado, "reutilizado");
                continue;
            }

            var unidad = existente ?? new Unidad
            {
                AnalisisId = analisisId, ArchivoId = paso.ArchivoId, TemaId = paso.TemaId, Etapa = paso.Etapa
            };

            try
            {
                unidad.Estado = EstadoUnidad.EnProceso;
                unidad.ActualizadoEn = reloj.Ahora();
                repo.GuardarUnidad(unidad);
                Emitir(paso.Etapa, EstadoEvento.Iniciado, "");

                var ctx = new ContextoPaso(progreso, fase, item, itemIndice, itemTotal, paso.Etapa) { Ct = ct };
                await paso.Ejecutar(ctx);

                unidad.Estado = EstadoUnidad.Completado;
                unidad.HashEntrada = hash;
                unidad.RutaArtefacto = paso.RutaArtefacto;
                unidad.PromptVersion = paso.PromptVersion;
                unidad.ModeloIa = paso.ModeloIa;
                unidad.ErrorMsg = null;
                unidad.TokensEntrada = ctx.TokensEntrada;
                unidad.TokensSalida = ctx.TokensSalida;
                unidad.Tokens = ctx.TokensEntrada + ctx.TokensSalida;
                unidad.ActualizadoEn = reloj.Ahora();
                repo.GuardarUnidad(unidad);
                ok++;
                Emitir(paso.Etapa, EstadoEvento.Completado, "");
            }
            catch (Exception ex)
            {
                errores++;
                mensajes.Add($"{paso.Etapa}: {ex.Message}");
                try
                {
                    unidad.Estado = EstadoUnidad.Error;
                    unidad.ErrorMsg = ex.Message;
                    unidad.ActualizadoEn = reloj.Ahora();
                    repo.GuardarUnidad(unidad);
                }
                catch { /* si ni siquiera se puede persistir el error, igual no crasheamos */ }
                Emitir(paso.Etapa, EstadoEvento.Error, ex.Message);
                break; // la cadena es dependiente: no seguir tras un error
            }
        }

        return new ResultadoEjecucion(ok, salteados, errores, mensajes);
    }
}
