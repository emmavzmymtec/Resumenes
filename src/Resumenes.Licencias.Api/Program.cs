using Microsoft.EntityFrameworkCore;
using Resumenes.Licencias.Api.Datos;
using Resumenes.Licencias.Api.Endpoints;
using Resumenes.Licencias.Api.Servicios;
using System.Security.Cryptography;
using System.Threading.RateLimiting;

if (args.Length > 0 && args[0] == "gen-keys")
{
    using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    Console.WriteLine("=== FIRMA_PRIVADA_PEM (variable de entorno en Railway, NO commitear) ===");
    Console.WriteLine(ec.ExportECPrivateKeyPem());
    Console.WriteLine("=== Clave PUBLICA (se embebe en el cliente, fase futura) ===");
    Console.WriteLine(ec.ExportSubjectPublicKeyInfoPem());
    return;
}

var builder = WebApplication.CreateBuilder(args);

var puerto = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(puerto))
    builder.WebHost.UseUrls($"http://0.0.0.0:{puerto}");

var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
builder.Services.AddDbContext<LicenciasDbContext>(opt =>
{
    if (ConfiguracionBd.EsPostgres(databaseUrl))
        opt.UseNpgsql(ConfiguracionBd.ConnectionStringDesde(databaseUrl!));
    else
        opt.UseSqlite(builder.Configuration.GetConnectionString("Sqlite")
                      ?? "Data Source=licencias.db");
});

builder.Services.AddScoped<ServicioActivacion>();
builder.Services.AddScoped(_ => new FirmadorTokens(
    Environment.GetEnvironmentVariable("FIRMA_PRIVADA_PEM")
    ?? throw new InvalidOperationException("Falta FIRMA_PRIVADA_PEM")));

builder.Services.AddRateLimiter(opt =>
{
    opt.RejectionStatusCode = 429;
    opt.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
    {
        if (ctx.Request.Path.StartsWithSegments("/salud"))
            return RateLimitPartition.GetNoLimiter("salud");

        return RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "global",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            });
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LicenciasDbContext>();
    db.Database.EnsureCreated();
}

app.UseRateLimiter();

app.MapGet("/salud", () => Results.Text("ok"));

EndpointsPublicos.Mapear(app);
EndpointsAdmin.Mapear(app);

app.Run();

public partial class Program { }
