# Fase 2 — Caché global de OCR/limpieza — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reutilizar entre análisis distintos los derivados de un mismo contenido (OCR bruto siempre; limpieza IA solo si coinciden prompt y modelo), copiándolos al análisis nuevo, para ahorrar tiempo (OCR) y tokens (limpieza).

**Architecture:** Una caché *content-addressed* en disco (`%LOCALAPPDATA%/ResumenesApp/cache/<hash>/`) con índice en SQLite (`CacheDerivado`). Un servicio `CacheDerivados` (Infrastructure) busca/puebla la caché por `hash_contenido + tipo + clave_variante`. `ConstructorPipeline` consulta la caché al inicio de los pasos `OcrBruto` y `LimpiezaIA`: si hay hit, copia el artefacto y termina el paso; si no, procesa y puebla la caché. Cada análisis conserva su copia local (originales intactos, trazabilidad).

**Tech Stack:** .NET 9, C#, Microsoft.Data.Sqlite, xUnit.

## Global Constraints

- **Plataforma:** Windows-only; datos por usuario en `%LOCALAPPDATA%/ResumenesApp/`.
- **Originales intactos:** la caché nunca modifica el material; copia artefactos derivados.
- **OCR reutilizable** si coincide `hash_contenido + dpi + versión-OCR`. **Limpieza reutilizable** solo si además coincide `hash_prompt_limpieza + modelo_ia`; si no, se rehace solo la limpieza.
- **Resiliencia:** un miss o un archivo de caché corrupto/faltante degrada a "procesar normal" (nunca rompe el análisis).
- **SQLite es índice de estado.** Migración no destructiva: `schema_version`→3, `CREATE TABLE IF NOT EXISTS`, upsert para el bump de versión.
- **Idempotencia previa intacta:** la caché actúa DENTRO del trabajo del paso; no cambia el `hash_entrada` de las unidades ni el mecanismo de reanudación.
- **Build de verificación:** `dotnet build Resumenes.sln -c Debug` debe pasar (incluye `Resumenes.Cli`).
- **Tests:** xUnit + fakes (`RepositorioEnMemoria`). El `hash_contenido` es `Archivo.HashSha256`.

---

## File Structure

- `src/Resumenes.Infrastructure/schema.sql` + `schema.sql` (raíz) — **Modify**: tabla `CacheDerivado`, `schema_version`→3.
- `src/Resumenes.Core/Interfaces/IRepositorioEstado.cs` — **Modify**: 2 métodos de `CacheDerivado`.
- `src/Resumenes.Infrastructure/Persistencia/SqliteRepositorioEstado.cs` — **Modify**: implementación.
- `tests/Resumenes.Tests/Fakes/Fakes.cs` — **Modify**: `RepositorioEnMemoria` implementa los 2 métodos.
- `tests/Resumenes.Ui.Tests/ConfirmarTemasVmTests.cs`, `tests/Resumenes.Ui.Tests/InicioVmTests.cs` — **Modify**: los `RepoFake` locales ganan los 2 stubs no-op (para que la solución compile tras cambiar la interfaz).
- `src/Resumenes.Infrastructure/Aplicacion/Configuracion.cs` — **Modify**: propiedad `RutaCache`.
- `src/Resumenes.Infrastructure/Aplicacion/CacheDerivados.cs` — **Create**: servicio de caché.
- `src/Resumenes.Infrastructure/Aplicacion/ConstructorPipeline.cs` — **Modify**: constructor recibe `CacheDerivados`; integración en `OcrBruto` y `LimpiezaIA`; helper `CopiarArtefacto`.
- `src/Resumenes.Infrastructure/Aplicacion/ServicioAnalisis.cs` — **Modify**: construye `CacheDerivados` y lo pasa al pipeline.
- `src/Resumenes.Ui/App.xaml.cs` — **Modify**: setear `cfg.RutaCache` por defecto a `%LOCALAPPDATA%/ResumenesApp/cache`.
- `src/Resumenes.Cli/Program.cs` — **Modify**: setear `cfg.RutaCache` por defecto (paridad con la UI).
- `tests/Resumenes.Tests/CacheDerivadosTests.cs` — **Create**: tests unit (hit/miss/variante/archivo faltante).
- `tests/Resumenes.Tests/Fakes/Fakes.cs` (`ServicioAnalisisFactory`) — **Modify**: aceptar una `RutaCache` temporal.
- `tests/Resumenes.Tests/CachePipelineIntegracionTests.cs` — **Create**: test de integración (la 2.ª corrida no vuelve a llamar a la IA para limpieza).

---

## Task 1: Tabla `CacheDerivado` y métodos de repositorio

**Files:**
- Modify: `src/Resumenes.Infrastructure/schema.sql`, `schema.sql`
- Modify: `src/Resumenes.Core/Interfaces/IRepositorioEstado.cs`
- Modify: `src/Resumenes.Infrastructure/Persistencia/SqliteRepositorioEstado.cs`
- Modify: `tests/Resumenes.Tests/Fakes/Fakes.cs`, `tests/Resumenes.Ui.Tests/ConfirmarTemasVmTests.cs`, `tests/Resumenes.Ui.Tests/InicioVmTests.cs`
- Test: `tests/Resumenes.Tests/SqliteRepositorioEstadoTests.cs`

**Interfaces:**
- Produces:
  - `string? IRepositorioEstado.BuscarCacheDerivado(string hashContenido, string tipo, string claveVariante)`
  - `void IRepositorioEstado.GuardarCacheDerivado(string hashContenido, string tipo, string claveVariante, string ruta)`

- [ ] **Step 1: Escribir el test que falla**

En `tests/Resumenes.Tests/SqliteRepositorioEstadoTests.cs`, agregar (seguir el patrón del test `AjustePrompt_...` para crear el repo con archivo temporal):

```csharp
    [Fact]
    public void CacheDerivado_GuardarBuscarUpsert_Funciona()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"resu-{Guid.NewGuid():N}.db");
        try
        {
            var repo = new SqliteRepositorioEstado($"Data Source={tmp}");
            repo.InicializarEsquema();

            Assert.Null(repo.BuscarCacheDerivado("hashA", "OcrBruto", "dpi=200;ocr=v1"));

            repo.GuardarCacheDerivado("hashA", "OcrBruto", "dpi=200;ocr=v1", @"C:\cache\hashA\ocr.txt");
            Assert.Equal(@"C:\cache\hashA\ocr.txt", repo.BuscarCacheDerivado("hashA", "OcrBruto", "dpi=200;ocr=v1"));

            // distinta variante => miss
            Assert.Null(repo.BuscarCacheDerivado("hashA", "OcrBruto", "dpi=300;ocr=v1"));

            // upsert (misma clave) pisa la ruta
            repo.GuardarCacheDerivado("hashA", "OcrBruto", "dpi=200;ocr=v1", @"C:\cache\hashA\ocr2.txt");
            Assert.Equal(@"C:\cache\hashA\ocr2.txt", repo.BuscarCacheDerivado("hashA", "OcrBruto", "dpi=200;ocr=v1"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter CacheDerivado_GuardarBuscarUpsert_Funciona`
Expected: FAIL de compilación — `IRepositorioEstado` no define `BuscarCacheDerivado`.

- [ ] **Step 3: Agregar la tabla al esquema y subir la versión**

En `src/Resumenes.Infrastructure/schema.sql`, antes del bloque `-- META:`/`SchemaMeta`, insertar:

```sql
-- ----------------------------------------------------------------------------
-- CACHE_DERIVADO: índice de artefactos reutilizables por contenido (OCR/limpieza).
--   El contenido vive en %LOCALAPPDATA%/ResumenesApp/cache/<hash_contenido>/.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS CacheDerivado (
    hash_contenido TEXT NOT NULL,
    tipo           TEXT NOT NULL CHECK (tipo IN ('OcrBruto','Limpieza')),
    clave_variante TEXT NOT NULL,   -- 'dpi=200;ocr=v1' | 'dpi=200;ocr=v1;prompt=<hash>;modelo=<m>'
    ruta           TEXT NOT NULL,
    creado_en      TEXT NOT NULL,
    PRIMARY KEY (hash_contenido, tipo, clave_variante)
);
```

Y cambiar el bump de versión (la línea que hoy dice `'2'`) por:

```sql
INSERT INTO SchemaMeta (clave, valor) VALUES ('schema_version', '3')
ON CONFLICT(clave) DO UPDATE SET valor='3';
```

Replicar ambos cambios en `schema.sql` de la raíz (copia documental).

- [ ] **Step 4: Declarar los métodos en la interfaz**

En `src/Resumenes.Core/Interfaces/IRepositorioEstado.cs`, antes del cierre:

```csharp
    /// <summary>Ruta del artefacto cacheado para (hash, tipo, variante); null si no hay registro.</summary>
    string? BuscarCacheDerivado(string hashContenido, string tipo, string claveVariante);
    void GuardarCacheDerivado(string hashContenido, string tipo, string claveVariante, string ruta);
```

- [ ] **Step 5: Implementar en SqliteRepositorioEstado**

En `src/Resumenes.Infrastructure/Persistencia/SqliteRepositorioEstado.cs`, antes del cierre de la clase:

```csharp
    public string? BuscarCacheDerivado(string hashContenido, string tipo, string claveVariante)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT ruta FROM CacheDerivado WHERE hash_contenido=$h AND tipo=$t AND clave_variante=$v;";
        cmd.Parameters.AddWithValue("$h", hashContenido);
        cmd.Parameters.AddWithValue("$t", tipo);
        cmd.Parameters.AddWithValue("$v", claveVariante);
        var r = cmd.ExecuteScalar();
        return r is string s ? s : null;
    }

    public void GuardarCacheDerivado(string hashContenido, string tipo, string claveVariante, string ruta)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO CacheDerivado (hash_contenido, tipo, clave_variante, ruta, creado_en)
                            VALUES ($h,$t,$v,$r,$c)
                            ON CONFLICT(hash_contenido, tipo, clave_variante) DO UPDATE SET ruta=$r, creado_en=$c;";
        cmd.Parameters.AddWithValue("$h", hashContenido);
        cmd.Parameters.AddWithValue("$t", tipo);
        cmd.Parameters.AddWithValue("$v", claveVariante);
        cmd.Parameters.AddWithValue("$r", ruta);
        cmd.Parameters.AddWithValue("$c", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }
```

- [ ] **Step 6: Actualizar los fakes / impls de `IRepositorioEstado`**

En `tests/Resumenes.Tests/Fakes/Fakes.cs`, dentro de `RepositorioEnMemoria`, agregar:

```csharp
    private readonly Dictionary<string, string> _cache = new();
    private static string CacheKey(string h, string t, string v) => $"{h}|{t}|{v}";
    public string? BuscarCacheDerivado(string hashContenido, string tipo, string claveVariante)
        => _cache.TryGetValue(CacheKey(hashContenido, tipo, claveVariante), out var r) ? r : null;
    public void GuardarCacheDerivado(string hashContenido, string tipo, string claveVariante, string ruta)
        => _cache[CacheKey(hashContenido, tipo, claveVariante)] = ruta;
```

En `tests/Resumenes.Ui.Tests/ConfirmarTemasVmTests.cs` y `tests/Resumenes.Ui.Tests/InicioVmTests.cs`, en cada `RepoFake` local que implementa `IRepositorioEstado`, agregar los stubs no-op:

```csharp
    public string? BuscarCacheDerivado(string hashContenido, string tipo, string claveVariante) => null;
    public void GuardarCacheDerivado(string hashContenido, string tipo, string claveVariante, string ruta) { }
```

- [ ] **Step 7: Correr el test y verificar que pasa**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter CacheDerivado_GuardarBuscarUpsert_Funciona`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Resumenes.Infrastructure/schema.sql schema.sql \
        src/Resumenes.Core/Interfaces/IRepositorioEstado.cs \
        src/Resumenes.Infrastructure/Persistencia/SqliteRepositorioEstado.cs \
        tests/Resumenes.Tests/Fakes/Fakes.cs \
        tests/Resumenes.Ui.Tests/ConfirmarTemasVmTests.cs tests/Resumenes.Ui.Tests/InicioVmTests.cs \
        tests/Resumenes.Tests/SqliteRepositorioEstadoTests.cs
git commit -m "feat(cache): tabla CacheDerivado y metodos de repositorio"
```

---

## Task 2: `Configuracion.RutaCache` y servicio `CacheDerivados`

**Files:**
- Modify: `src/Resumenes.Infrastructure/Aplicacion/Configuracion.cs`
- Create: `src/Resumenes.Infrastructure/Aplicacion/CacheDerivados.cs`
- Modify: `src/Resumenes.Ui/App.xaml.cs` (setear `cfg.RutaCache`)
- Modify: `src/Resumenes.Cli/Program.cs` (setear `cfg.RutaCache`)
- Test: `tests/Resumenes.Tests/CacheDerivadosTests.cs`

**Interfaces:**
- Consumes: `IRepositorioEstado.BuscarCacheDerivado/GuardarCacheDerivado` (Task 1).
- Produces (en `Resumenes.Infrastructure.Aplicacion.CacheDerivados`):
  - `CacheDerivados(IRepositorioEstado repo, Configuracion cfg)`
  - `string? BuscarOcr(string hashContenido, int dpi)`
  - `string? BuscarLimpieza(string hashContenido, int dpi, string hashPrompt, string modelo)`
  - `void GuardarOcr(string hashContenido, int dpi, string rutaOrigen)`
  - `void GuardarLimpieza(string hashContenido, int dpi, string hashPrompt, string modelo, string rutaOrigen)`
- Produces: `Configuracion.RutaCache` (string).

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/Resumenes.Tests/CacheDerivadosTests.cs`:

```csharp
using Resumenes.Infrastructure.Aplicacion;
using Resumenes.Tests.Fakes;
using Xunit;

namespace Resumenes.Tests;

public class CacheDerivadosTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"resu-cache-{Guid.NewGuid():N}");

    private CacheDerivados Nuevo()
        => new(new RepositorioEnMemoria(), new Configuracion { RutaCache = _dir });

    private string ArchivoConTexto(string texto)
    {
        var p = Path.Combine(_dir, $"src-{Guid.NewGuid():N}.txt");
        Directory.CreateDirectory(_dir);
        File.WriteAllText(p, texto);
        return p;
    }

    [Fact]
    public void GuardarOcr_LuegoBuscarOcr_DevuelveRutaConContenido()
    {
        var cache = Nuevo();
        var origen = ArchivoConTexto("texto ocr");
        cache.GuardarOcr("hashA", 200, origen);

        var hit = cache.BuscarOcr("hashA", 200);
        Assert.NotNull(hit);
        Assert.Equal("texto ocr", File.ReadAllText(hit!));
    }

    [Fact]
    public void BuscarOcr_SinGuardar_DevuelveNull()
        => Assert.Null(Nuevo().BuscarOcr("hashX", 200));

    [Fact]
    public void BuscarOcr_DistintoDpi_EsMiss()
    {
        var cache = Nuevo();
        cache.GuardarOcr("hashA", 200, ArchivoConTexto("x"));
        Assert.Null(cache.BuscarOcr("hashA", 300));
    }

    [Fact]
    public void BuscarLimpieza_DistintoPromptOModelo_EsMiss()
    {
        var cache = Nuevo();
        cache.GuardarLimpieza("hashA", 200, "prompt1", "modeloA", ArchivoConTexto("limpio"));
        Assert.NotNull(cache.BuscarLimpieza("hashA", 200, "prompt1", "modeloA"));
        Assert.Null(cache.BuscarLimpieza("hashA", 200, "prompt2", "modeloA"));
        Assert.Null(cache.BuscarLimpieza("hashA", 200, "prompt1", "modeloB"));
    }

    [Fact]
    public void Buscar_ConRegistroPeroArchivoFaltante_EsMiss()
    {
        var cache = Nuevo();
        var origen = ArchivoConTexto("limpio");
        cache.GuardarLimpieza("hashA", 200, "p", "m", origen);
        var hit = cache.BuscarLimpieza("hashA", 200, "p", "m");
        Assert.NotNull(hit);
        File.Delete(hit!);                      // simula caché corrupta/borrada
        Assert.Null(cache.BuscarLimpieza("hashA", 200, "p", "m"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }
}
```

- [ ] **Step 2: Correr y verificar que fallan**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter CacheDerivadosTests`
Expected: FAIL de compilación (`CacheDerivados` y `Configuracion.RutaCache` no existen).

- [ ] **Step 3: Agregar `RutaCache` a `Configuracion`**

En `src/Resumenes.Infrastructure/Aplicacion/Configuracion.cs`, agregar una propiedad (junto a `RutaRuntime`):

```csharp
    /// <summary>Raíz de la caché de derivados (OCR/limpieza). Vacío = App calcula %LOCALAPPDATA%/ResumenesApp/cache.</summary>
    public string RutaCache { get; set; } = "";
```

- [ ] **Step 4: Crear `CacheDerivados`**

Crear `src/Resumenes.Infrastructure/Aplicacion/CacheDerivados.cs`:

```csharp
using Resumenes.Core.Interfaces;

namespace Resumenes.Infrastructure.Aplicacion;

/// <summary>
/// Caché content-addressed de artefactos derivados (OCR bruto y texto limpio) reutilizables
/// entre análisis distintos. Índice en SQLite (CacheDerivado), contenido en disco bajo
/// RutaCache/&lt;hash_contenido&gt;/. Un registro sin archivo en disco se trata como miss
/// (degradación segura ante caché corrupta).
/// </summary>
public class CacheDerivados(IRepositorioEstado repo, Configuracion cfg)
{
    private const string TipoOcr = "OcrBruto";
    private const string TipoLimpieza = "Limpieza";

    private string RaizCache() => string.IsNullOrWhiteSpace(cfg.RutaCache)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResumenesApp", "cache")
        : cfg.RutaCache;

    private static string VarianteOcr(int dpi) => $"dpi={dpi};ocr=v1";
    private static string VarianteLimpieza(int dpi, string hashPrompt, string modelo)
        => $"dpi={dpi};ocr=v1;prompt={hashPrompt};modelo={modelo}";

    public string? BuscarOcr(string hashContenido, int dpi)
        => BuscarValido(hashContenido, TipoOcr, VarianteOcr(dpi));

    public string? BuscarLimpieza(string hashContenido, int dpi, string hashPrompt, string modelo)
        => BuscarValido(hashContenido, TipoLimpieza, VarianteLimpieza(dpi, hashPrompt, modelo));

    public void GuardarOcr(string hashContenido, int dpi, string rutaOrigen)
        => Guardar(hashContenido, TipoOcr, VarianteOcr(dpi), "ocr.txt", rutaOrigen);

    public void GuardarLimpieza(string hashContenido, int dpi, string hashPrompt, string modelo, string rutaOrigen)
        => Guardar(hashContenido, TipoLimpieza, VarianteLimpieza(dpi, hashPrompt, modelo),
                   $"limpio__{Recorte(hashPrompt)}__{Sanear(modelo)}.txt", rutaOrigen);

    private string? BuscarValido(string hash, string tipo, string variante)
    {
        var ruta = repo.BuscarCacheDerivado(hash, tipo, variante);
        return (ruta != null && File.Exists(ruta)) ? ruta : null;
    }

    private void Guardar(string hash, string tipo, string variante, string nombreArchivo, string rutaOrigen)
    {
        var dir = Path.Combine(RaizCache(), hash);
        Directory.CreateDirectory(dir);
        var destino = Path.Combine(dir, nombreArchivo);
        File.Copy(rutaOrigen, destino, true);
        repo.GuardarCacheDerivado(hash, tipo, variante, destino);
    }

    private static string Recorte(string s) => s.Length > 8 ? s[..8] : s;
    private static string Sanear(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }
}
```

- [ ] **Step 5: Setear `RutaCache` en la composición (UI y Cli)**

En `src/Resumenes.Ui/App.xaml.cs`, después del bloque que setea `cfg.RutaRuntime` (≈línea 61), agregar:

```csharp
        if (string.IsNullOrWhiteSpace(cfg.RutaCache))
            cfg.RutaCache = System.IO.Path.Combine(raizDatos, "cache");
        cfg.RutaCache = Environment.ExpandEnvironmentVariables(cfg.RutaCache);
```

En `src/Resumenes.Cli/Program.cs`, localizar dónde se construye/ajusta `cfg` (la línea `: new Configuracion();` y alrededores). Tras resolverse `cfg`, agregar — usando la misma raíz de datos por usuario que usa el resto de la app:

```csharp
if (string.IsNullOrWhiteSpace(cfg.RutaCache))
    cfg.RutaCache = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ResumenesApp", "cache");
```

(Si el Cli no tiene `using System.IO;` global, usar `System.IO.Path`. Verificar que compile.)

- [ ] **Step 6: Correr los tests y verificar que pasan**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter CacheDerivadosTests`
Expected: PASS (5 tests).

- [ ] **Step 7: Commit**

```bash
git add src/Resumenes.Infrastructure/Aplicacion/Configuracion.cs \
        src/Resumenes.Infrastructure/Aplicacion/CacheDerivados.cs \
        src/Resumenes.Ui/App.xaml.cs src/Resumenes.Cli/Program.cs \
        tests/Resumenes.Tests/CacheDerivadosTests.cs
git commit -m "feat(cache): servicio CacheDerivados y RutaCache en configuracion"
```

---

## Task 3: Integrar la caché en el pipeline

**Files:**
- Modify: `src/Resumenes.Infrastructure/Aplicacion/ConstructorPipeline.cs` (constructor, `OcrBruto`, `LimpiezaIA`, helper)
- Modify: `src/Resumenes.Infrastructure/Aplicacion/ServicioAnalisis.cs`
- Modify: `tests/Resumenes.Tests/Fakes/Fakes.cs` (`ServicioAnalisisFactory.ParaTests`)
- Test: `tests/Resumenes.Tests/CachePipelineIntegracionTests.cs`

**Interfaces:**
- Consumes: `CacheDerivados.BuscarOcr/BuscarLimpieza/GuardarOcr/GuardarLimpieza` (Task 2); `ServicioPrompts.HashEditable` (Fase 1).
- Produces: `ConstructorPipeline(IRasterizador, IServicioOcr, IClienteIA, IGeneradorPdf, IConversorOffice, Configuracion, ServicioPrompts, CacheDerivados)`; `ServicioAnalisisFactory.ParaTests(IRepositorioEstado repo, string workspace, string? rutaCache = null)`.

- [ ] **Step 1: Escribir el test de integración que falla**

Crear `tests/Resumenes.Tests/CachePipelineIntegracionTests.cs`. Procesa el MISMO archivo TXT en dos análisis distintos; la segunda corrida debe reutilizar la limpieza desde la caché y, por lo tanto, NO volver a invocar a la IA.

```csharp
using Resumenes.Core.Interfaces;
using Resumenes.Tests.Fakes;
using Xunit;

namespace Resumenes.Tests;

public class CachePipelineIntegracionTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), $"resu-int-{Guid.NewGuid():N}");

    [Fact]
    public async Task SegundoAnalisis_ConMismoArchivo_ReutilizaLimpiezaSinLlamarIA()
    {
        Directory.CreateDirectory(_base);
        var rutaCache = Path.Combine(_base, "cache");

        // Carpeta con un TXT
        var carpeta = Path.Combine(_base, "material");
        Directory.CreateDirectory(carpeta);
        await File.WriteAllTextAsync(Path.Combine(carpeta, "apunte.txt"), "contenido de estudio");

        // Contador de llamadas a la IA (la limpieza es la única etapa IA por-archivo)
        int llamadasIA = 0;
        var ia = new FakeClienteIAContador(() => llamadasIA++);
        var repo = new RepositorioEnMemoria();

        // 1.ª corrida: procesa y puebla la caché
        var ws1 = Path.Combine(_base, "ws1");
        var svc1 = ServicioAnalisisFactory.ParaTests(repo, ws1, rutaCache, ia);
        var an1 = await svc1.AbrirOCrearAsync(carpeta, default);
        await svc1.ProcesarArchivosAsync(an1, null, default);
        var llamadasTras1 = llamadasIA;
        Assert.True(llamadasTras1 >= 1, "la 1.ª corrida debe llamar a la IA al menos una vez (limpieza)");

        // 2.ª corrida: workspace nuevo, MISMO archivo => limpieza desde caché, sin IA
        var ws2 = Path.Combine(_base, "ws2");
        var svc2 = ServicioAnalisisFactory.ParaTests(repo, ws2, rutaCache, ia);
        var an2 = await svc2.AbrirOCrearAsync(carpeta, default);
        await svc2.ProcesarArchivosAsync(an2, null, default);

        Assert.Equal(llamadasTras1, llamadasIA); // no hubo nuevas llamadas a la IA
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_base)) Directory.Delete(_base, true);
    }
}
```

Y agregar al final de `tests/Resumenes.Tests/Fakes/Fakes.cs` un fake de IA con contador:

```csharp
public class FakeClienteIAContador(Action alLlamar) : IClienteIA
{
    public Task<RespuestaIA> CompletarAsync(SolicitudIA req, CancellationToken ct)
    {
        alLlamar();
        return Task.FromResult(new RespuestaIA(req.PromptUser, "stop", 1, 1, 2));
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter CachePipelineIntegracionTests`
Expected: FAIL de compilación — `ServicioAnalisisFactory.ParaTests` no acepta `rutaCache`/`ia`.

- [ ] **Step 3: Extender `ConstructorPipeline` para recibir la caché**

En `ConstructorPipeline.cs`, cambiar la declaración primaria a:

```csharp
public class ConstructorPipeline(
    IRasterizador rasterizador, IServicioOcr ocr, IClienteIA ia, IGeneradorPdf pdf,
    IConversorOffice conversor, Configuracion cfg, ServicioPrompts prompts, CacheDerivados cache)
{
```

Agregar un helper privado dentro de la clase (cerca de `NombreSeguro`):

```csharp
    private static void CopiarArtefacto(string origen, string destino)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.Copy(origen, destino, true);
    }
```

- [ ] **Step 4: Integrar la caché en `OcrBruto`**

Reemplazar el cuerpo del trabajo del paso `OcrBruto` (el `async ctx => { ... }`, líneas ≈45-56) por:

```csharp
                async ctx =>
                {
                    if (esTxt)
                    {
                        EscrituraAtomica.Escribir(bruto, await File.ReadAllTextAsync(rutaAbs, ctx.Ct));
                        return;
                    }
                    var hitOcr = cache.BuscarOcr(arc.HashSha256, cfg.Dpi);
                    if (hitOcr != null)
                    {
                        ctx.Reportar("OCR reutilizado de caché");
                        CopiarArtefacto(hitOcr, bruto);
                        return;
                    }
                    if (imagenes.Count == 0)
                        imagenes = Directory.GetFiles(imagenesDir, "pagina_*.jpg").OrderBy(x => x).ToArray();
                    EscrituraAtomica.Escribir(bruto, await ocr.OcrAsync(imagenes, ctx.Ct,
                        new Progress<(int a, int t)>(p => ctx.Reportar($"OCR página {p.a}/{p.t}", p.a, p.t))));
                    cache.GuardarOcr(arc.HashSha256, cfg.Dpi, bruto);
                }),
```

(Los TXT no se cachean a nivel OCR: la lectura directa es trivial; su limpieza sí se cachea en el paso siguiente.)

- [ ] **Step 5: Integrar la caché en `LimpiezaIA`**

Reemplazar el cuerpo del trabajo del paso `LimpiezaIA` (el `async ctx => { ... }`, líneas ≈63-74) por:

```csharp
                async ctx =>
                {
                    var hashPrompt = prompts.HashEditable(ServicioPrompts.ClaveLimpieza);
                    var hitLimpieza = cache.BuscarLimpieza(arc.HashSha256, cfg.Dpi, hashPrompt, cfg.Modelo);
                    if (hitLimpieza != null)
                    {
                        ctx.Reportar("limpieza reutilizada de caché");
                        CopiarArtefacto(hitLimpieza, limpio);
                        return;
                    }
                    var entrada = await File.ReadAllTextAsync(bruto, ctx.Ct);
                    var sb = new StringBuilder();
                    foreach (var bloque in Chunking.Dividir(entrada, cfg.MaxCharsIA))
                    {
                        ctx.Reportar("pensando…");
                        var r = await ia.CompletarAsync(new SolicitudIA(
                            prompts.SystemLimpieza(), bloque, 0.2, 8000, "limpieza-v1", cfg.Modelo), ctx.Ct);
                        sb.Append(r.Texto).Append('\n');
                    }
                    EscrituraAtomica.Escribir(limpio, sb.ToString().Trim());
                    cache.GuardarLimpieza(arc.HashSha256, cfg.Dpi, hashPrompt, cfg.Modelo, limpio);
                }, PromptVersion: "limpieza-v1", ModeloIa: cfg.Modelo),
```

- [ ] **Step 6: Cablear la caché en `ServicioAnalisis`**

En `ServicioAnalisis.cs`, donde están `_prompts` y la propiedad lazy `_ctor` (≈líneas 18-20 tras Fase 1), agregar el campo de caché y pasarlo al pipeline:

```csharp
    private readonly ServicioPrompts _prompts = new(repo);
    private readonly CacheDerivados _cache = new(repo, cfg);
    private ConstructorPipeline? _ctorLazy;
    private ConstructorPipeline _ctor => _ctorLazy ??= new(rasterizador, ocr, ia, generadorPdf, conversor, cfg, _prompts, _cache);
```

- [ ] **Step 7: Extender `ServicioAnalisisFactory.ParaTests`**

En `tests/Resumenes.Tests/Fakes/Fakes.cs`, cambiar `ParaTests` para aceptar una ruta de caché y un cliente IA opcionales:

```csharp
    public static ServicioAnalisis ParaTests(IRepositorioEstado repo, string workspace,
        string? rutaCache = null, IClienteIA? ia = null)
    {
        var cfg = new Configuracion { RutaWorkspace = workspace, RutaCache = rutaCache ?? "" };
        return new ServicioAnalisis(
            new FakeRasterizador(),
            new FakeOcr(),
            ia ?? new FakeClienteIA(),
            new FakeGeneradorPdf(),
            new FakeConversor(),
            repo,
            new RelojFijo(),
            cfg);
    }
```

(Las llamadas existentes `ParaTests(repo, ws)` siguen compilando porque los nuevos parámetros son opcionales.)

- [ ] **Step 8: Correr el test de integración y la suite completa**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter CachePipelineIntegracionTests`
Expected: PASS.

Run: `dotnet test -c Debug`
Expected: toda la suite verde (los tests previos + Task 1/2/3 de esta fase). Los `ServicioAnalisisTests` existentes siguen pasando: con caché vacía, el comportamiento es idéntico al previo (procesa y puebla).

- [ ] **Step 9: Compilar la solución completa**

Run: `dotnet build Resumenes.sln -c Debug`
Expected: BUILD succeeded (incluye `Resumenes.Cli`, que ahora setea `cfg.RutaCache`).

- [ ] **Step 10: Verificación manual (el usuario prueba la UI)**

Cerrar `Resumenes.Ui` si está abierto. Ejecutar la app y:
1. Procesar una carpeta con un PDF/imagen (genera OCR + limpieza; se puebla la caché).
2. Procesar una carpeta distinta que contenga **el mismo archivo** (mismo contenido): el procesamiento debe ser notablemente más rápido y, en el progreso, aparecer "OCR reutilizado de caché" / "limpieza reutilizada de caché".
3. Confirmar que el PDF/resumen resultante es correcto.

- [ ] **Step 11: Commit**

```bash
git add src/Resumenes.Infrastructure/Aplicacion/ConstructorPipeline.cs \
        src/Resumenes.Infrastructure/Aplicacion/ServicioAnalisis.cs \
        tests/Resumenes.Tests/Fakes/Fakes.cs \
        tests/Resumenes.Tests/CachePipelineIntegracionTests.cs
git commit -m "feat(cache): el pipeline reutiliza OCR y limpieza desde la cache global"
```

---

## Self-Review (cobertura de la spec, Fase 2)

- **Reutilizar OCR siempre que coincida contenido+dpi:** Task 3 Step 4. ✅
- **Reutilizar limpieza solo si coincide prompt+modelo; si no, rehace solo la limpieza:** Task 3 Step 5 (clave de variante incluye `prompt` y `modelo`). ✅
- **Copiar el derivado al análisis nuevo (originales/local intactos):** `CopiarArtefacto` copia al artefacto del análisis. ✅
- **Almacenamiento content-addressed + índice SQLite:** Task 1 (tabla), Task 2 (`CacheDerivados`). ✅
- **Degradación segura (miss / archivo faltante → procesar normal):** `BuscarValido` valida `File.Exists`; test en Task 2. ✅
- **Migración no destructiva (`schema_version`→3, upsert):** Task 1 Step 3. ✅
- **Idempotencia/reanudación previa intacta:** la caché actúa dentro del trabajo del paso; no toca `hash_entrada`. ✅
- **Paridad UI/Cli en `RutaCache`:** Task 2 Step 5. ✅

## Notas de diseño

- **El rasterizado (paso `Captura`) sigue ejecutándose** aunque el OCR venga de caché: la optimización de caché es a nivel `OcrBruto` (el paso lento y/o el que llama servicios). Saltar también el rasterizado sería una mejora futura (backlog), no necesaria para el ahorro principal.
- **Purga/límite de tamaño de la caché:** fuera de alcance (backlog, como indica la spec).
- **TXT:** no se cachea el OCR (lectura directa trivial); su limpieza IA sí se cachea.
- La clave de variante de limpieza incluye `ocr=v1`/`dpi` además de `prompt`+`modelo`, de modo que un cambio de DPI (que cambia el bruto) no reutilice una limpieza basada en otro bruto.
