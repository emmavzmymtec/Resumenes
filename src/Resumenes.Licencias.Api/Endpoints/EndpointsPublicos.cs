using Resumenes.Licencias.Api.Contratos;
using Resumenes.Licencias.Api.Servicios;

namespace Resumenes.Licencias.Api.Endpoints;

public static class EndpointsPublicos
{
    public static void Mapear(WebApplication app)
    {
        app.MapPost("/activar", async (ActivarRequest req, ServicioActivacion svc) =>
        {
            if (!GeneradorClaves.EsFormatoValido(req.Clave))
                return Results.NotFound(new { error = "clave_invalida" });

            var r = await svc.ActivarAsync(req.Clave, req.Hwid, req.NombreEquipo);
            return r.Codigo switch
            {
                CodigoActivacion.Ok => Results.Ok(new ActivarResponse(r.Token!)),
                CodigoActivacion.ClaveInvalida => Results.NotFound(new { error = "clave_invalida" }),
                CodigoActivacion.Revocada => Results.Json(new { error = "revocada" }, statusCode: 403),
                CodigoActivacion.LimiteAlcanzado => Results.Json(new { error = "limite_alcanzado" }, statusCode: 409),
                _ => Results.StatusCode(500),
            };
        });

        app.MapPost("/validar", async (ValidarRequest req, ServicioActivacion svc) =>
        {
            if (!Guid.TryParse(req.LicenciaId, out var id))
                return Results.Json(new ValidarResponse("revocada"), statusCode: 403);

            var estado = await svc.ValidarAsync(id, req.Hwid);
            return estado == EstadoValidacion.Activa
                ? Results.Ok(new ValidarResponse("activa"))
                : Results.Json(new ValidarResponse("revocada"), statusCode: 403);
        });
    }
}
