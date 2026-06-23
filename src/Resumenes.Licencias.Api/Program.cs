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

var app = builder.Build();

app.MapGet("/salud", () => Results.Text("ok"));

app.Run();

public partial class Program { }
