using Microsoft.EntityFrameworkCore;
using Resumenes.Licencias.Api.Datos;
using Resumenes.Licencias.Api.Endpoints;
using Resumenes.Licencias.Api.Servicios;
using System.Security.Cryptography;

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

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<LicenciasDbContext>();
    db.Database.EnsureCreated();
}

app.MapGet("/salud", () => Results.Text("ok"));

EndpointsPublicos.Mapear(app);

app.Run();

public partial class Program { }
