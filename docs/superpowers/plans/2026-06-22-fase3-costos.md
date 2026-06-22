# Fase 3 — Saldo USD + costo por análisis — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mostrar el saldo en dólares de la cuenta Deepseek y el costo estimado de cada análisis, calculado con los tokens reales (que hoy se descartan) por una tarifa configurable.

**Architecture:** Se persisten los tokens de entrada/salida por `Unidad`. El paso reporta tokens vía `ContextoPaso`; el orquestador los guarda. `ServicioCostos` suma los tokens de un análisis y aplica la tarifa de `Configuracion`. `ClienteSaldo` consulta `GET /user/balance` de Deepseek. La UI muestra el saldo y las tarifas en Configuración, y el costo en el historial y en Resultados.

**Tech Stack:** .NET 9, C#, Microsoft.Data.Sqlite, HttpClient/System.Text.Json, WPF / WPF-UI 4.x, CommunityToolkit.Mvvm, xUnit.

## Global Constraints

- **Plataforma:** Windows-only; datos por usuario en `%LOCALAPPDATA%/ResumenesApp/`.
- **SQLite es índice de estado.** Migración no destructiva: `schema_version`→4; columnas nuevas vía `ALTER TABLE ADD COLUMN` con guarda "si no existe" (programática, no en el script). Bump de versión con upsert.
- **Tokens reales:** `RespuestaIA` ya trae `TokensPrompt`/`TokensCompletion`; hoy se descartan. Se persisten por `Unidad` (`tokens_entrada`, `tokens_salida`) y se mantiene el `tokens` total existente.
- **Costo** = `(Σ tokens_entrada × precioInput + Σ tokens_salida × precioOutput) / 1_000_000`, en USD. Tarifas **configurables**, con default y **aviso en UI de que son una estimación y pueden variar**.
- **Saldo:** `GET {BaseUrlDeepseek}/user/balance` con Bearer de la API key (DPAPI). Tolerar fallos: mostrar "no disponible" sin romper la app. **La API key nunca se expone.**
- **Idempotencia/reanudación intactas:** persistir tokens no cambia el `hash_entrada` ni el flujo de reutilización.
- **Build de verificación:** `dotnet build Resumenes.sln -c Debug` debe pasar (incluye `Resumenes.Cli`).
- **Tests:** xUnit + fakes (`RepositorioEnMemoria`, `FakeClienteIA`).

## Alcance acotado (decisiones de esta fase)

- El **saldo** se muestra en **Configuración** (sección nueva), no en el TitleBar (menos invasivo). El header queda como mejora futura.
- El **costo** se muestra en el **historial** (tarjeta de cada análisis) y en **Resultados**.
- El **costo NO incluye la detección de temas** (esa llamada IA no es una `Unidad` persistida; es una sola llamada chica). Se documenta; cubre limpieza + resumen, que son el grueso.

---

## File Structure

- `src/Resumenes.Infrastructure/schema.sql` + `schema.sql` — **Modify**: columnas `tokens_entrada`/`tokens_salida` en `Unidad`, `schema_version`→4.
- `src/Resumenes.Infrastructure/Persistencia/SqliteRepositorioEstado.cs` — **Modify**: migración de columnas en `InicializarEsquema`; persistir/leer los tokens en `GuardarUnidad`/`ObtenerUnidad`; nuevo `SumarTokensAnalisis`.
- `src/Resumenes.Core/Interfaces/IRepositorioEstado.cs` — **Modify**: `SumarTokensAnalisis`.
- `src/Resumenes.Core/Modelos/Entidades.cs` — **Modify**: `Unidad.TokensEntrada`/`TokensSalida`.
- `tests/Resumenes.Tests/Fakes/Fakes.cs` + los 2 `RepoFake` de `Resumenes.Ui.Tests` — **Modify**: implementar `SumarTokensAnalisis`.
- `src/Resumenes.Core/Orquestacion/ProgresoPaso.cs` — **Modify**: `ContextoPaso` acumula tokens.
- `src/Resumenes.Core/Orquestacion/PipelineOrquestador.cs` — **Modify**: persistir tokens del `ctx` en la unidad.
- `src/Resumenes.Infrastructure/Aplicacion/ConstructorPipeline.cs` — **Modify**: los pasos IA reportan tokens al `ctx`.
- `src/Resumenes.Infrastructure/Aplicacion/Configuracion.cs` — **Modify**: `PrecioInputPorMillonUsd`/`PrecioOutputPorMillonUsd`.
- `src/Resumenes.Infrastructure/Aplicacion/ServicioCostos.cs` — **Create**.
- `src/Resumenes.Infrastructure/IA/ClienteSaldo.cs` — **Create**.
- `src/Resumenes.Core/Interfaces/IClienteSaldo.cs` — **Create** (interfaz + record `SaldoCuenta`).
- `src/Resumenes.Ui/App.xaml.cs` — **Modify**: registrar `ServicioCostos`, `IClienteSaldo`; factories de VMs.
- `src/Resumenes.Ui/ViewModels/ConfiguracionVm.cs` — **Modify**: saldo + tarifas.
- `src/Resumenes.Ui/Vistas/VistaConfiguracion.xaml` — **Modify**: sección "Cuenta y costos".
- `src/Resumenes.Ui/ViewModels/AnalisisHistorialVm.cs` — **Modify**: `CostoLegible`.
- `src/Resumenes.Ui/ViewModels/InicioVm.cs` — **Modify**: calcular costo al cargar el historial.
- `src/Resumenes.Ui/Vistas/VistaInicio.xaml` — **Modify**: mostrar costo en la tarjeta.
- `src/Resumenes.Ui/ViewModels/ResultadosVm.cs` — **Modify**: `CostoLegible`.
- `src/Resumenes.Ui/Vistas/VistaResultados.xaml` — **Modify**: mostrar costo.
- Tests: `tests/Resumenes.Tests/SqliteRepositorioEstadoTests.cs`, `tests/Resumenes.Tests/TokensPersistenciaTests.cs`, `tests/Resumenes.Tests/ServicioCostosTests.cs`, `tests/Resumenes.Tests/ClienteSaldoTests.cs`.

---

## Task 1: Persistir tokens por `Unidad`

**Files:**
- Modify: `src/Resumenes.Infrastructure/schema.sql`, `schema.sql`
- Modify: `src/Resumenes.Core/Modelos/Entidades.cs`
- Modify: `src/Resumenes.Core/Interfaces/IRepositorioEstado.cs`
- Modify: `src/Resumenes.Infrastructure/Persistencia/SqliteRepositorioEstado.cs`
- Modify: `tests/Resumenes.Tests/Fakes/Fakes.cs`, `tests/Resumenes.Ui.Tests/ConfirmarTemasVmTests.cs`, `tests/Resumenes.Ui.Tests/InicioVmTests.cs`
- Test: `tests/Resumenes.Tests/SqliteRepositorioEstadoTests.cs`

**Interfaces:**
- Produces:
  - `Unidad.TokensEntrada` (int?), `Unidad.TokensSalida` (int?)
  - `(int entrada, int salida) IRepositorioEstado.SumarTokensAnalisis(string analisisId)`

- [ ] **Step 1: Escribir el test que falla**

En `tests/Resumenes.Tests/SqliteRepositorioEstadoTests.cs`, agregar (seguir el patrón de los tests con archivo temporal). Verifica que los tokens se persisten y se suman:

```csharp
    [Fact]
    public void Unidad_PersisteTokens_ySumaPorAnalisis()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"resu-{Guid.NewGuid():N}.db");
        try
        {
            var repo = new SqliteRepositorioEstado($"Data Source={tmp}");
            repo.InicializarEsquema();

            // Necesita un Analisis padre (FK)
            repo.GuardarAnalisis(new Resumenes.Core.Modelos.Analisis(
                "an1", "n", "c", "fp", Resumenes.Core.Modelos.EstadoAnalisis.EnProceso,
                DateTime.UtcNow, DateTime.UtcNow));

            repo.GuardarUnidad(new Resumenes.Core.Modelos.Unidad
            {
                AnalisisId = "an1", ArchivoId = "arc1", Etapa = Resumenes.Core.Modelos.Etapa.LimpiezaIA,
                Estado = Resumenes.Core.Modelos.EstadoUnidad.Completado,
                TokensEntrada = 100, TokensSalida = 30, Tokens = 130, ActualizadoEn = DateTime.UtcNow
            });
            repo.GuardarUnidad(new Resumenes.Core.Modelos.Unidad
            {
                AnalisisId = "an1", TemaId = "tema1", Etapa = Resumenes.Core.Modelos.Etapa.ResumenFinal,
                Estado = Resumenes.Core.Modelos.EstadoUnidad.Completado,
                TokensEntrada = 200, TokensSalida = 70, Tokens = 270, ActualizadoEn = DateTime.UtcNow
            });

            // round-trip de una unidad
            var u = repo.ObtenerUnidad("an1", "arc1", null, Resumenes.Core.Modelos.Etapa.LimpiezaIA);
            Assert.Equal(100, u!.TokensEntrada);
            Assert.Equal(30, u.TokensSalida);

            var (entrada, salida) = repo.SumarTokensAnalisis("an1");
            Assert.Equal(300, entrada);
            Assert.Equal(100, salida);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter Unidad_PersisteTokens_ySumaPorAnalisis`
Expected: FAIL de compilación — `Unidad.TokensEntrada` y `SumarTokensAnalisis` no existen.

- [ ] **Step 3: Agregar las columnas al esquema y subir la versión**

En `src/Resumenes.Infrastructure/schema.sql`, en la definición `CREATE TABLE IF NOT EXISTS Unidad (...)`, agregar dos columnas después de `tokens INTEGER,`:

```sql
    tokens_entrada     INTEGER,
    tokens_salida      INTEGER,
```

Y subir la versión (la línea que hoy dice `'3'`):

```sql
INSERT INTO SchemaMeta (clave, valor) VALUES ('schema_version', '4')
ON CONFLICT(clave) DO UPDATE SET valor='4';
```

Replicar ambos cambios en `schema.sql` de la raíz.

(Nota: el `CREATE TABLE IF NOT EXISTS` solo afecta bases nuevas; las bases existentes se migran en el Step 5 vía `ALTER TABLE`.)

- [ ] **Step 4: Agregar los campos a `Unidad`**

En `src/Resumenes.Core/Modelos/Entidades.cs`, en la clase `Unidad`, agregar (junto a `Tokens`):

```csharp
    public int? TokensEntrada { get; set; }
    public int? TokensSalida { get; set; }
```

- [ ] **Step 5: Migración de columnas + persistencia + suma en el repo**

En `src/Resumenes.Infrastructure/Persistencia/SqliteRepositorioEstado.cs`:

(a) Agregar un helper estático y llamarlo desde `InicializarEsquema` justo **antes** de `SqliteConnection.ClearPool(con);` (reutiliza la conexión `con` abierta):

```csharp
        AsegurarColumna(con, "Unidad", "tokens_entrada", "INTEGER");
        AsegurarColumna(con, "Unidad", "tokens_salida", "INTEGER");
```

y el helper (agregarlo como método privado de la clase):

```csharp
    // Agrega una columna si no existe (SQLite no soporta ADD COLUMN IF NOT EXISTS).
    // tabla/columna provienen de literales del código (no de entrada de usuario).
    private static void AsegurarColumna(SqliteConnection con, string tabla, string columna, string tipoSql)
    {
        using (var check = con.CreateCommand())
        {
            check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{tabla}') WHERE name = $c;";
            check.Parameters.AddWithValue("$c", columna);
            if (Convert.ToInt64(check.ExecuteScalar()) > 0) return;
        }
        using var alter = con.CreateCommand();
        alter.CommandText = $"ALTER TABLE {tabla} ADD COLUMN {columna} {tipoSql};";
        alter.ExecuteNonQuery();
    }
```

(b) En `GuardarUnidad`, agregar las dos columnas tanto al `UPDATE` como al `INSERT`. En el `UPDATE` SET, añadir `tokens_entrada=$te, tokens_salida=$ts`; en el `INSERT`, añadir las columnas y los valores `$te,$ts`. En ambos comandos, agregar los parámetros:

```csharp
        upd.Parameters.AddWithValue("$te", (object?)u.TokensEntrada ?? DBNull.Value);
        upd.Parameters.AddWithValue("$ts", (object?)u.TokensSalida ?? DBNull.Value);
```
```csharp
        ins.Parameters.AddWithValue("$te", (object?)u.TokensEntrada ?? DBNull.Value);
        ins.Parameters.AddWithValue("$ts", (object?)u.TokensSalida ?? DBNull.Value);
```

El `UPDATE` queda (mostrando el SET completo):
```csharp
        upd.CommandText = @"UPDATE Unidad
                            SET estado=$est, ruta_artefacto=$ruta, hash_entrada=$he, prompt_version=$pv,
                                modelo_ia=$mi, tokens=$tok, tokens_entrada=$te, tokens_salida=$ts,
                                fijado_por_usuario=$fij, error_msg=$err, actualizado_en=$ac
                            WHERE analisis_id=$an AND COALESCE(archivo_id,'')=$arc
                              AND COALESCE(tema_id,'')=$t AND etapa=$e;";
```
El `INSERT` queda:
```csharp
            ins.CommandText = @"INSERT INTO Unidad (analisis_id, archivo_id, tema_id, etapa, estado, ruta_artefacto,
                                    hash_entrada, prompt_version, modelo_ia, tokens, tokens_entrada, tokens_salida,
                                    fijado_por_usuario, error_msg, actualizado_en)
                                VALUES ($an,$arc,$t,$e,$est,$ruta,$he,$pv,$mi,$tok,$te,$ts,$fij,$err,$ac);";
```

(c) En `ObtenerUnidad`, agregar las columnas al SELECT y al mapeo. Cambiar el SELECT para incluir `tokens_entrada, tokens_salida` (p. ej. tras `tokens`), y en la construcción de `Unidad` agregar:

```csharp
            TokensEntrada = r.IsDBNull(r.GetOrdinal("tokens_entrada")) ? null : r.GetInt32(r.GetOrdinal("tokens_entrada")),
            TokensSalida = r.IsDBNull(r.GetOrdinal("tokens_salida")) ? null : r.GetInt32(r.GetOrdinal("tokens_salida")),
```

(usar `GetOrdinal` evita tener que renumerar los índices posicionales existentes; si preferís, agregá las dos columnas al FINAL del SELECT y usá los índices 14 y 15.)

(d) Implementar `SumarTokensAnalisis` (agregar antes del cierre de la clase):

```csharp
    public (int entrada, int salida) SumarTokensAnalisis(string analisisId)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT COALESCE(SUM(tokens_entrada),0), COALESCE(SUM(tokens_salida),0)
                            FROM Unidad WHERE analisis_id=$an;";
        cmd.Parameters.AddWithValue("$an", analisisId);
        using var r = cmd.ExecuteReader();
        r.Read();
        return (r.GetInt32(0), r.GetInt32(1));
    }
```

- [ ] **Step 6: Declarar `SumarTokensAnalisis` en la interfaz y en los fakes**

En `src/Resumenes.Core/Interfaces/IRepositorioEstado.cs`, antes del cierre:

```csharp
    /// <summary>Suma de tokens de entrada y salida de todas las unidades del análisis.</summary>
    (int entrada, int salida) SumarTokensAnalisis(string analisisId);
```

En `tests/Resumenes.Tests/Fakes/Fakes.cs` (`RepositorioEnMemoria`), agregar:

```csharp
    public (int entrada, int salida) SumarTokensAnalisis(string analisisId)
    {
        var us = _unidades.Values.Where(u => u.AnalisisId == analisisId);
        return (us.Sum(u => u.TokensEntrada ?? 0), us.Sum(u => u.TokensSalida ?? 0));
    }
```

En los `RepoFake` locales de `tests/Resumenes.Ui.Tests/ConfirmarTemasVmTests.cs` e `InicioVmTests.cs`, agregar el stub:

```csharp
    public (int entrada, int salida) SumarTokensAnalisis(string analisisId) => (0, 0);
```

- [ ] **Step 7: Correr el test y verificar que pasa**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter Unidad_PersisteTokens_ySumaPorAnalisis`
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add src/Resumenes.Infrastructure/schema.sql schema.sql \
        src/Resumenes.Core/Modelos/Entidades.cs src/Resumenes.Core/Interfaces/IRepositorioEstado.cs \
        src/Resumenes.Infrastructure/Persistencia/SqliteRepositorioEstado.cs \
        tests/Resumenes.Tests/Fakes/Fakes.cs \
        tests/Resumenes.Ui.Tests/ConfirmarTemasVmTests.cs tests/Resumenes.Ui.Tests/InicioVmTests.cs \
        tests/Resumenes.Tests/SqliteRepositorioEstadoTests.cs
git commit -m "feat(costos): persistir tokens_entrada/salida por unidad y sumarlos por analisis"
```

---

## Task 2: Capturar los tokens en el pipeline

**Files:**
- Modify: `src/Resumenes.Core/Orquestacion/ProgresoPaso.cs` (`ContextoPaso`)
- Modify: `src/Resumenes.Core/Orquestacion/PipelineOrquestador.cs`
- Modify: `src/Resumenes.Infrastructure/Aplicacion/ConstructorPipeline.cs`
- Test: `tests/Resumenes.Tests/TokensPersistenciaTests.cs`

**Interfaces:**
- Consumes: `Unidad.TokensEntrada/TokensSalida` (Task 1).
- Produces:
  - `ContextoPaso.TokensEntrada` (int), `ContextoPaso.TokensSalida` (int), `void ContextoPaso.AcumularTokens(int entrada, int salida)`

- [ ] **Step 1: Escribir el test de integración que falla**

Crear `tests/Resumenes.Tests/TokensPersistenciaTests.cs`. Procesa un TXT con `FakeClienteIA` (que devuelve tokens 1/1 por llamada) y verifica que tras procesar quedan tokens persistidos (>0) en el análisis:

```csharp
using Resumenes.Core.Modelos;
using Resumenes.Tests.Fakes;
using Xunit;

namespace Resumenes.Tests;

public class TokensPersistenciaTests : IDisposable
{
    private readonly string _base = Path.Combine(Path.GetTempPath(), $"resu-tok-{Guid.NewGuid():N}");

    [Fact]
    public async Task ProcesarArchivos_PersisteTokensDeLimpieza()
    {
        var carpeta = Path.Combine(_base, "material");
        Directory.CreateDirectory(carpeta);
        await File.WriteAllTextAsync(Path.Combine(carpeta, "apunte.txt"), "contenido de estudio");

        var repo = new RepositorioEnMemoria();
        var svc = ServicioAnalisisFactory.ParaTests(repo, Path.Combine(_base, "ws"));
        var an = await svc.AbrirOCrearAsync(carpeta, default);
        await svc.ProcesarArchivosAsync(an, null, default);

        var (entrada, salida) = repo.SumarTokensAnalisis(an.Id);
        Assert.True(entrada > 0, "deben persistirse tokens de entrada de la limpieza");
        Assert.True(salida > 0, "deben persistirse tokens de salida de la limpieza");
    }

    public void Dispose()
    {
        if (Directory.Exists(_base)) Directory.Delete(_base, true);
    }
}
```

(El `FakeClienteIA` ya devuelve `RespuestaIA(..., 1, 1, 2)` → 1 token de entrada y 1 de salida por llamada.)

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter ProcesarArchivos_PersisteTokensDeLimpieza`
Expected: FAIL — `SumarTokensAnalisis` devuelve (0,0) porque los tokens aún no se capturan.

- [ ] **Step 3: `ContextoPaso` acumula tokens**

En `src/Resumenes.Core/Orquestacion/ProgresoPaso.cs`, en la clase `ContextoPaso`, agregar:

```csharp
    public int TokensEntrada { get; private set; }
    public int TokensSalida { get; private set; }
    /// <summary>Acumula los tokens de una llamada a la IA dentro de este paso.</summary>
    public void AcumularTokens(int entrada, int salida)
    {
        TokensEntrada += entrada;
        TokensSalida += salida;
    }
```

- [ ] **Step 4: El orquestador persiste los tokens del `ctx`**

En `src/Resumenes.Core/Orquestacion/PipelineOrquestador.cs`, en el bloque `try` exitoso, después de `await paso.Ejecutar(ctx);` y antes de `repo.GuardarUnidad(unidad);` (el segundo, el del éxito), agregar:

```csharp
                unidad.TokensEntrada = ctx.TokensEntrada;
                unidad.TokensSalida = ctx.TokensSalida;
                unidad.Tokens = ctx.TokensEntrada + ctx.TokensSalida;
```

(Quedan junto a las asignaciones de `unidad.Estado = Completado`, `unidad.HashEntrada = hash`, etc.)

- [ ] **Step 5: Los pasos IA reportan tokens**

En `src/Resumenes.Infrastructure/Aplicacion/ConstructorPipeline.cs`:

En el paso `LimpiezaIA`, después de `var r = await ia.CompletarAsync(...)` y antes de `sb.Append(r.Texto)...`, agregar:

```csharp
                        ctx.AcumularTokens(r.TokensPrompt, r.TokensCompletion);
```

En el paso `ResumenFinal`, después de `var r = await ia.CompletarAsync(...)` y antes de `partes.Add(r.Texto);`, agregar:

```csharp
                        ctx.AcumularTokens(r.TokensPrompt, r.TokensCompletion);
```

- [ ] **Step 6: Correr el test de integración y la suite**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter ProcesarArchivos_PersisteTokensDeLimpieza`
Expected: PASS.

Run: `dotnet test -c Debug`
Expected: toda la suite verde (los tests previos siguen pasando; los pasos sin IA dejan los tokens en 0).

- [ ] **Step 7: Commit**

```bash
git add src/Resumenes.Core/Orquestacion/ProgresoPaso.cs \
        src/Resumenes.Core/Orquestacion/PipelineOrquestador.cs \
        src/Resumenes.Infrastructure/Aplicacion/ConstructorPipeline.cs \
        tests/Resumenes.Tests/TokensPersistenciaTests.cs
git commit -m "feat(costos): capturar y persistir tokens reales de las llamadas IA del pipeline"
```

---

## Task 3: Tarifas, `ServicioCostos` y `ClienteSaldo`

**Files:**
- Modify: `src/Resumenes.Infrastructure/Aplicacion/Configuracion.cs`
- Create: `src/Resumenes.Infrastructure/Aplicacion/ServicioCostos.cs`
- Create: `src/Resumenes.Core/Interfaces/IClienteSaldo.cs`
- Create: `src/Resumenes.Infrastructure/IA/ClienteSaldo.cs`
- Test: `tests/Resumenes.Tests/ServicioCostosTests.cs`, `tests/Resumenes.Tests/ClienteSaldoTests.cs`

**Interfaces:**
- Consumes: `IRepositorioEstado.SumarTokensAnalisis` (Task 1); `IAlmacenSecretos.ObtenerApiKey`.
- Produces:
  - `Configuracion.PrecioInputPorMillonUsd` (decimal), `Configuracion.PrecioOutputPorMillonUsd` (decimal)
  - `ServicioCostos(IRepositorioEstado repo, Configuracion cfg)` con `decimal CostoDe(string analisisId)` y `string CostoLegible(string analisisId)`
  - `record SaldoCuenta(bool Disponible, string Moneda, string TotalDisponible)` y `interface IClienteSaldo { Task<SaldoCuenta?> ObtenerAsync(CancellationToken ct); }`
  - `ClienteSaldo(HttpClient http, IAlmacenSecretos secretos, string baseUrl) : IClienteSaldo`

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/Resumenes.Tests/ServicioCostosTests.cs`:

```csharp
using Resumenes.Core.Modelos;
using Resumenes.Infrastructure.Aplicacion;
using Resumenes.Tests.Fakes;
using Xunit;

namespace Resumenes.Tests;

public class ServicioCostosTests
{
    [Fact]
    public void CostoDe_AplicaTarifaPorMillon()
    {
        var repo = new RepositorioEnMemoria();
        repo.GuardarAnalisis(new Analisis("an1","n","c","fp",EstadoAnalisis.Completado,DateTime.UtcNow,DateTime.UtcNow));
        repo.GuardarUnidad(new Unidad {
            AnalisisId="an1", ArchivoId="a", Etapa=Etapa.LimpiezaIA, Estado=EstadoUnidad.Completado,
            TokensEntrada=1_000_000, TokensSalida=1_000_000, ActualizadoEn=DateTime.UtcNow });

        var cfg = new Configuracion { PrecioInputPorMillonUsd = 0.27m, PrecioOutputPorMillonUsd = 1.10m };
        var svc = new ServicioCostos(repo, cfg);

        Assert.Equal(1.37m, svc.CostoDe("an1"));          // 0.27 + 1.10
        Assert.Contains("US$", svc.CostoLegible("an1"));
    }

    [Fact]
    public void CostoDe_SinTokens_EsCero()
    {
        var repo = new RepositorioEnMemoria();
        var svc = new ServicioCostos(repo, new Configuracion());
        Assert.Equal(0m, svc.CostoDe("inexistente"));
    }
}
```

Crear `tests/Resumenes.Tests/ClienteSaldoTests.cs` (con un `HttpMessageHandler` fake que devuelve un JSON de saldo de ejemplo). Reutilizar el patrón del handler de los tests de Deepseek existentes si lo hubiera; si no, incluir uno mínimo:

```csharp
using System.Net;
using System.Text;
using Resumenes.Core.Interfaces;
using Resumenes.Infrastructure.IA;
using Xunit;

namespace Resumenes.Tests;

public class ClienteSaldoTests
{
    private sealed class HandlerFijo(string json, HttpStatusCode code = HttpStatusCode.OK) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(code)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }

    private sealed class SecretosFake : IAlmacenSecretos
    {
        public string? ObtenerApiKey() => "sk-test";
        public void GuardarApiKey(string apiKey) { }
    }

    [Fact]
    public async Task ObtenerAsync_ParseaSaldoUsd()
    {
        const string json = """
        {"is_available":true,"balance_infos":[{"currency":"USD","total_balance":"12.34"}]}
        """;
        var http = new HttpClient(new HandlerFijo(json));
        var cliente = new ClienteSaldo(http, new SecretosFake(), "https://api.deepseek.com");

        var saldo = await cliente.ObtenerAsync(default);

        Assert.NotNull(saldo);
        Assert.True(saldo!.Disponible);
        Assert.Equal("USD", saldo.Moneda);
        Assert.Equal("12.34", saldo.TotalDisponible);
    }

    [Fact]
    public async Task ObtenerAsync_AnteError_DevuelveNull()
    {
        var http = new HttpClient(new HandlerFijo("nope", HttpStatusCode.Unauthorized));
        var cliente = new ClienteSaldo(http, new SecretosFake(), "https://api.deepseek.com");
        Assert.Null(await cliente.ObtenerAsync(default));
    }
}
```

- [ ] **Step 2: Correr y verificar que fallan**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter "ServicioCostosTests|ClienteSaldoTests"`
Expected: FAIL de compilación (tipos no existen).

- [ ] **Step 3: Agregar las tarifas a `Configuracion`**

En `src/Resumenes.Infrastructure/Aplicacion/Configuracion.cs`, agregar:

```csharp
    /// <summary>Tarifa estimada de tokens de entrada (USD por millón). Editable; puede variar.</summary>
    public decimal PrecioInputPorMillonUsd { get; set; } = 0.27m;
    /// <summary>Tarifa estimada de tokens de salida (USD por millón). Editable; puede variar.</summary>
    public decimal PrecioOutputPorMillonUsd { get; set; } = 1.10m;
```

- [ ] **Step 4: Crear `ServicioCostos`**

Crear `src/Resumenes.Infrastructure/Aplicacion/ServicioCostos.cs`:

```csharp
using System.Globalization;
using Resumenes.Core.Interfaces;

namespace Resumenes.Infrastructure.Aplicacion;

/// <summary>
/// Calcula el costo estimado en USD de un análisis a partir de los tokens persistidos
/// y las tarifas configurables. Es una ESTIMACIÓN (las tarifas pueden variar).
/// </summary>
public class ServicioCostos(IRepositorioEstado repo, Configuracion cfg)
{
    public decimal CostoDe(string analisisId)
    {
        var (entrada, salida) = repo.SumarTokensAnalisis(analisisId);
        return (entrada * cfg.PrecioInputPorMillonUsd + salida * cfg.PrecioOutputPorMillonUsd) / 1_000_000m;
    }

    /// <summary>Costo formateado, p. ej. "US$ 0.0123".</summary>
    public string CostoLegible(string analisisId)
        => "US$ " + CostoDe(analisisId).ToString("0.####", CultureInfo.InvariantCulture);
}
```

- [ ] **Step 5: Crear la interfaz y el record de saldo**

Crear `src/Resumenes.Core/Interfaces/IClienteSaldo.cs`:

```csharp
namespace Resumenes.Core.Interfaces;

/// <summary>Saldo de la cuenta del proveedor de IA. Null al consultarlo = no disponible.</summary>
public record SaldoCuenta(bool Disponible, string Moneda, string TotalDisponible);

public interface IClienteSaldo
{
    /// <summary>Consulta el saldo; devuelve null si falla (sin romper la app).</summary>
    Task<SaldoCuenta?> ObtenerAsync(CancellationToken ct);
}
```

- [ ] **Step 6: Crear `ClienteSaldo`**

Crear `src/Resumenes.Infrastructure/IA/ClienteSaldo.cs`:

```csharp
using System.Net.Http.Headers;
using System.Text.Json;
using Resumenes.Core.Interfaces;

namespace Resumenes.Infrastructure.IA;

/// <summary>
/// Consulta el saldo de la cuenta Deepseek (GET /user/balance). Tolera cualquier fallo
/// devolviendo null: el saldo es informativo y nunca debe romper la app.
/// </summary>
public class ClienteSaldo(HttpClient http, IAlmacenSecretos secretos, string baseUrl) : IClienteSaldo
{
    public async Task<SaldoCuenta?> ObtenerAsync(CancellationToken ct)
    {
        try
        {
            var key = secretos.ObtenerApiKey();
            if (string.IsNullOrWhiteSpace(key)) return null;

            using var msg = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl.TrimEnd('/')}/user/balance");
            msg.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

            using var resp = await http.SendAsync(msg, ct);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            bool disponible = root.TryGetProperty("is_available", out var av) && av.GetBoolean();

            if (!root.TryGetProperty("balance_infos", out var infos) || infos.GetArrayLength() == 0)
                return new SaldoCuenta(disponible, "", "");

            // Preferir USD si existe; si no, el primero.
            JsonElement elegido = infos[0];
            foreach (var bi in infos.EnumerateArray())
                if (bi.TryGetProperty("currency", out var c) && c.GetString() == "USD") { elegido = bi; break; }

            var moneda = elegido.TryGetProperty("currency", out var cu) ? cu.GetString() ?? "" : "";
            var total = elegido.TryGetProperty("total_balance", out var tb) ? tb.GetString() ?? "" : "";
            return new SaldoCuenta(disponible, moneda, total);
        }
        catch
        {
            return null; // saldo informativo: nunca rompe
        }
    }
}
```

- [ ] **Step 7: Correr los tests y verificar que pasan**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter "ServicioCostosTests|ClienteSaldoTests"`
Expected: PASS (4 tests).

- [ ] **Step 8: Commit**

```bash
git add src/Resumenes.Infrastructure/Aplicacion/Configuracion.cs \
        src/Resumenes.Infrastructure/Aplicacion/ServicioCostos.cs \
        src/Resumenes.Core/Interfaces/IClienteSaldo.cs \
        src/Resumenes.Infrastructure/IA/ClienteSaldo.cs \
        tests/Resumenes.Tests/ServicioCostosTests.cs tests/Resumenes.Tests/ClienteSaldoTests.cs
git commit -m "feat(costos): ServicioCostos, ClienteSaldo y tarifas configurables"
```

---

## Task 4: UI — saldo y tarifas en Configuración; costo en historial y resultados

**Files:**
- Modify: `src/Resumenes.Ui/App.xaml.cs`
- Modify: `src/Resumenes.Ui/ViewModels/ConfiguracionVm.cs`, `src/Resumenes.Ui/Vistas/VistaConfiguracion.xaml`
- Modify: `src/Resumenes.Ui/ViewModels/AnalisisHistorialVm.cs`, `src/Resumenes.Ui/ViewModels/InicioVm.cs`, `src/Resumenes.Ui/Vistas/VistaInicio.xaml`
- Modify: `src/Resumenes.Ui/ViewModels/ResultadosVm.cs`, `src/Resumenes.Ui/Vistas/VistaResultados.xaml`
- Test: `tests/Resumenes.Ui.Tests/InicioVmTests.cs` (si ya prueba `Cargar`, ajustar el ctor)

**Interfaces:**
- Consumes: `ServicioCostos`, `IClienteSaldo`, `Configuracion` (Task 3).

- [ ] **Step 1: Registrar servicios y actualizar factories en DI**

En `src/Resumenes.Ui/App.xaml.cs`:

Registrar los servicios nuevos (cerca de los demás `AddSingleton`):
```csharp
        sc.AddSingleton<ServicioCostos>(sp =>
            new ServicioCostos(sp.GetRequiredService<SqliteRepositorioEstado>(), cfg));
        sc.AddSingleton<Resumenes.Core.Interfaces.IClienteSaldo>(
            new ClienteSaldo(http, secretos, cfg.BaseUrlDeepseek));
```

Actualizar el factory de `ConfiguracionVm` (de Fase 1) para inyectar `Configuracion` y `IClienteSaldo`:
```csharp
        sc.AddTransient<ConfiguracionVm>(sp => new ConfiguracionVm(
            sp.GetRequiredService<IAlmacenSecretos>(),
            sp.GetRequiredService<Configuracion>(),
            sp.GetRequiredService<ServicioPrompts>(),
            sp.GetRequiredService<Resumenes.Core.Interfaces.IClienteSaldo>()));
```

Asegurar que `InicioVm` reciba `ServicioCostos`. Si hoy se registra como `AddTransient<InicioVm>()` (auto), cambiarlo a un factory explícito que incluya `ServicioCostos`:
```csharp
        sc.AddTransient<InicioVm>(sp => new InicioVm(
            sp.GetRequiredService<IRepositorioEstado>(),
            sp.GetRequiredService<ServicioNavegacion>(),
            sp.GetRequiredService<IServicioAnalisis>(),
            sp.GetRequiredService<Configuracion>(),
            sp.GetRequiredService<Wpf.Ui.IContentDialogService>(),
            sp.GetRequiredService<ServicioCostos>()));
```

Y el factory de `ResultadosVm` (ya es factory): agregar `ServicioCostos` como parámetro (ver Step 5).

- [ ] **Step 2: `ConfiguracionVm` — saldo y tarifas**

En `src/Resumenes.Ui/ViewModels/ConfiguracionVm.cs`:

Agregar el campo y extender el constructor (agregando `IClienteSaldo clienteSaldo` al final):
```csharp
    private readonly Resumenes.Core.Interfaces.IClienteSaldo _saldo;
```
```csharp
    public ConfiguracionVm(IAlmacenSecretos secretos, Configuracion cfg, ServicioPrompts prompts,
        Resumenes.Core.Interfaces.IClienteSaldo saldo)
    {
        _secretos = secretos;
        _cfg = cfg;
        _prompts = prompts;
        _saldo = saldo;
        Cargar();
    }
```

Agregar propiedades observables:
```csharp
    [ObservableProperty] private string _saldoTexto = "Sin consultar";
    [ObservableProperty] private bool _consultandoSaldo;
    [ObservableProperty] private decimal _precioInput;
    [ObservableProperty] private decimal _precioOutput;

    /// <summary>True cuando NO se está consultando (habilita el botón).</summary>
    public bool PuedeConsultar => !ConsultandoSaldo;
```

Y notificar `PuedeConsultar` cuando cambia `ConsultandoSaldo` (agregar el hook parcial del toolkit):
```csharp
    partial void OnConsultandoSaldoChanged(bool value) => OnPropertyChanged(nameof(PuedeConsultar));
```

En `Cargar()`, al final, agregar:
```csharp
        PrecioInput = _cfg.PrecioInputPorMillonUsd;
        PrecioOutput = _cfg.PrecioOutputPorMillonUsd;
```

En `GuardarConfig()` (el comando existente), antes de serializar, persistir las tarifas:
```csharp
            _cfg.PrecioInputPorMillonUsd = PrecioInput;
            _cfg.PrecioOutputPorMillonUsd = PrecioOutput;
```

Agregar el comando de saldo:
```csharp
    [RelayCommand]
    private async Task ConsultarSaldo()
    {
        ConsultandoSaldo = true;
        SaldoTexto = "Consultando…";
        try
        {
            var s = await _saldo.ObtenerAsync(CancellationToken.None);
            SaldoTexto = s is null
                ? "No disponible (verificá la API key o tu conexión)."
                : (s.Disponible ? $"{s.TotalDisponible} {s.Moneda}" : $"{s.TotalDisponible} {s.Moneda} (cuenta no disponible)");
        }
        catch (Exception ex) { SaldoTexto = $"No disponible: {ex.Message}"; }
        finally { ConsultandoSaldo = false; }
    }
```

(Agregar `using System.Threading; using System.Threading.Tasks;` si faltan.)

- [ ] **Step 3: `VistaConfiguracion.xaml` — sección "Cuenta y costos"**

Insertar, antes del botón "Guardar configuración", una `ui:Card`:

```xml
      <!-- ── Cuenta y costos ── -->
      <ui:Card Padding="20" Margin="0,0,0,16">
        <StackPanel>
          <TextBlock Text="Cuenta y costos" FontSize="14" FontWeight="SemiBold"
                     Foreground="{DynamicResource TextFillColorPrimaryBrush}" Margin="0,0,0,12"/>

          <!-- Saldo -->
          <Grid Margin="0,0,0,12">
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="*"/>
              <ColumnDefinition Width="Auto"/>
            </Grid.ColumnDefinitions>
            <StackPanel Grid.Column="0" VerticalAlignment="Center">
              <TextBlock Text="Saldo de la cuenta (Deepseek)" FontSize="13"
                         Foreground="{DynamicResource TextFillColorPrimaryBrush}"/>
              <TextBlock Text="{Binding SaldoTexto}" FontSize="12"
                         Foreground="{DynamicResource TextFillColorSecondaryBrush}" Margin="0,2,0,0"/>
            </StackPanel>
            <ui:Button Grid.Column="1" Content="Actualizar saldo"
                       Command="{Binding ConsultarSaldoCommand}"
                       Icon="{ui:SymbolIcon Symbol=ArrowSync24}"
                       IsEnabled="{Binding PuedeConsultar}"
                       VerticalAlignment="Center"/>
          </Grid>

          <!-- Tarifas -->
          <TextBlock Text="Tarifas estimadas (USD por millón de tokens). Son una estimación y pueden variar."
                     FontSize="11" TextWrapping="Wrap"
                     Foreground="{DynamicResource TextFillColorTertiaryBrush}" Margin="0,0,0,8"/>
          <Grid>
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="*"/>
              <ColumnDefinition Width="*"/>
            </Grid.ColumnDefinitions>
            <StackPanel Grid.Column="0" Margin="0,0,8,0">
              <TextBlock Text="Entrada (input)" FontSize="12"
                         Foreground="{DynamicResource TextFillColorSecondaryBrush}"/>
              <ui:NumberBox Value="{Binding PrecioInput, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                            Minimum="0" SmallChange="0.01" MaxDecimalPlaces="4"
                            SpinButtonPlacementMode="Compact"/>
            </StackPanel>
            <StackPanel Grid.Column="1" Margin="8,0,0,0">
              <TextBlock Text="Salida (output)" FontSize="12"
                         Foreground="{DynamicResource TextFillColorSecondaryBrush}"/>
              <ui:NumberBox Value="{Binding PrecioOutput, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                            Minimum="0" SmallChange="0.01" MaxDecimalPlaces="4"
                            SpinButtonPlacementMode="Compact"/>
            </StackPanel>
          </Grid>
        </StackPanel>
      </ui:Card>
```

(Nota: el botón usa `IsEnabled="{Binding PuedeConsultar}"`, la propiedad booleana agregada en el Step 2 — no se usa converter de Visibility para `IsEnabled`.)

- [ ] **Step 4: Costo en el historial**

En `src/Resumenes.Ui/ViewModels/AnalisisHistorialVm.cs`, agregar un parámetro opcional de costo al constructor y exponerlo:

```csharp
    private readonly string _costoLegible;
    public AnalisisHistorialVm(Analisis analisis, string costoLegible = "")
    {
        _analisis = analisis;
        _costoLegible = costoLegible;
    }
    public string CostoLegible => _costoLegible;
    public bool TieneCosto => !string.IsNullOrEmpty(_costoLegible);
```

En `src/Resumenes.Ui/ViewModels/InicioVm.cs`, inyectar `ServicioCostos` y usarlo en `Cargar`:

- Agregar el campo `private readonly ServicioCostos _costos;`, agregar el parámetro al constructor (al final) y asignarlo.
- En `Cargar()`, construir cada VM con su costo:
```csharp
        foreach (var an in lista)
            Analisis.Add(new AnalisisHistorialVm(an, _costos.CostoLegible(an.Id)));
```

En `src/Resumenes.Ui/Vistas/VistaInicio.xaml`, en el `StackPanel` de "Carpeta + fecha" (Row 1), agregar al final un separador y el costo:

```xml
                  <TextBlock Text=" · " FontSize="11"
                             Foreground="{DynamicResource TextFillColorTertiaryBrush}"
                             Visibility="{Binding TieneCosto, Converter={StaticResource BoolToVis}}"/>
                  <ui:SymbolIcon Symbol="Money24" FontSize="13"
                                 Foreground="{DynamicResource TextFillColorTertiaryBrush}"
                                 Margin="0,0,4,0"
                                 Visibility="{Binding TieneCosto, Converter={StaticResource BoolToVis}}"/>
                  <TextBlock Text="{Binding CostoLegible}" FontSize="11"
                             Foreground="{DynamicResource TextFillColorTertiaryBrush}"
                             Visibility="{Binding TieneCosto, Converter={StaticResource BoolToVis}}"/>
```

- [ ] **Step 5: Costo en Resultados**

En `src/Resumenes.Ui/ViewModels/ResultadosVm.cs`:
- Agregar `private readonly ServicioCostos _costos;` y el parámetro al constructor (al final), asignándolo.
- Agregar `[ObservableProperty] private string _costoLegible = string.Empty;` (requiere que la clase ya sea `partial` — lo es).
- En `Cargar(Analisis an)`, al final, setear: `CostoLegible = _costos.CostoLegible(an.Id);`

Actualizar el factory de `ResultadosVm` en `App.xaml.cs` para pasar `ServicioCostos`.

En `src/Resumenes.Ui/Vistas/VistaResultados.xaml`, agregar cerca del `MensajeEstado`/encabezado un `TextBlock` con el costo:
```xml
      <TextBlock Text="{Binding CostoLegible, StringFormat='Costo estimado del análisis: {0}'}"
                 FontSize="12"
                 Foreground="{DynamicResource TextFillColorSecondaryBrush}"
                 Margin="0,0,0,8"
                 Visibility="{Binding CostoLegible, Converter={StaticResource StrToVis}}"/>
```
(Si `VistaResultados.xaml` no tiene declarado `StrToVis`, agregar `<vm:StringToVisibilityConverter x:Key="StrToVis"/>` en `Page.Resources`, como en `VistaConfiguracion.xaml`.)

- [ ] **Step 6: Ajustar tests de UI afectados**

Si `tests/Resumenes.Ui.Tests/InicioVmTests.cs` construye `InicioVm`, actualizar la construcción para pasar un `ServicioCostos` (con `RepositorioEnMemoria` y una `Configuracion`). Ejemplo de helper:
```csharp
        var costos = new ServicioCostos(repo, new Configuracion());
        var vm = new InicioVm(repo, nav, servicio, cfg, dialogos, costos);
```
(Adaptar a los nombres reales de las variables del test.)

- [ ] **Step 7: Compilar y correr todo**

Run: `dotnet build Resumenes.sln -c Debug`
Expected: BUILD succeeded. Si algún `Symbol` (`ArrowSync24`, `Money24`) no existe en la versión de WPF-UI, reemplazar por `ArrowClockwise24` / `Wallet24` respectivamente.

Run: `dotnet test -c Debug`
Expected: toda la suite verde.

- [ ] **Step 8: Verificación manual (el usuario prueba la UI)**

Cerrar `Resumenes.Ui`. Ejecutar la app:
1. Configuración → "Cuenta y costos": "Actualizar saldo" muestra el saldo (o "No disponible" si la key/red fallan); editar tarifas y "Guardar configuración" las persiste.
2. Procesar un análisis y verificar que en el historial aparece el costo estimado, y en "Ver resultados" el "Costo estimado del análisis".

- [ ] **Step 9: Commit**

```bash
git add src/Resumenes.Ui/
git add tests/Resumenes.Ui.Tests/InicioVmTests.cs
git commit -m "feat(costos): UI de saldo y tarifas en Configuracion, costo en historial y resultados"
```

---

## Self-Review (cobertura de la spec, Fase 3)

- **Saldo USD (GET /user/balance), tolerante a fallos:** Task 3 (`ClienteSaldo`), Task 4 (UI). ✅
- **Costo por análisis con tokens reales:** Task 1 (persistencia), Task 2 (captura), Task 3 (`ServicioCostos`). ✅
- **Tarifas configurables con aviso:** Task 3 (`Configuracion`), Task 4 (NumberBox + aviso). ✅
- **Costo en historial y resultados:** Task 4. ✅
- **Migración no destructiva (`schema_version`→4, ADD COLUMN con guarda):** Task 1. ✅
- **API key nunca expuesta:** `ClienteSaldo` usa la key solo para el header Bearer; no la devuelve. ✅
- **Idempotencia intacta:** persistir tokens ocurre en el bloque de éxito del orquestador; no toca `hash_entrada`. ✅

## Notas de diseño

- El costo **no incluye la detección de temas** (no es una `Unidad` persistida; es una llamada chica). Cubre limpieza + resumen. Mejora futura: contabilizarla aparte (backlog).
- Las tarifas por defecto (0.27 / 1.10 USD por millón) son una **estimación**; el usuario las ajusta en Configuración. La UI lo aclara.
- El saldo se muestra en **Configuración** (no en el TitleBar); el header queda como mejora futura.
- `ServicioCostos.CostoDe` usa `decimal` y formato `InvariantCulture` para evitar problemas de separador decimal.
