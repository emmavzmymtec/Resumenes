# Fase 5a — Simulador de exámenes: motor — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Construir el motor del simulador de exámenes (modelo de datos, generación de preguntas con IA, corrección local + IA, y un servicio orquestador), todo testeable sin UI. La UI se construye aparte (plan 5b).

**Architecture:** Tres tablas SQLite (`Examen`, `PreguntaExamen`, `RespuestaUsuario`) con `IRepositorioExamenes`. `GeneradorExamen` arma las preguntas a partir de un texto fuente y una configuración, devolviendo un contrato JSON tipado. `CorrectorExamen` corrige lo objetivo localmente y lo abierto con IA, y calcula la nota. `ServicioExamenes` orquesta: ensambla el contenido fuente del análisis (resúmenes o consolidado), genera, persiste, y al finalizar corrige y calcula el resultado.

**Tech Stack:** .NET 9, C#, Microsoft.Data.Sqlite, System.Text.Json, xUnit.

## Global Constraints

- **Plataforma:** Windows-only; datos por usuario en `%LOCALAPPDATA%/ResumenesApp/`; workspace en `cfg.RutaWorkspace`.
- **Mínimos tokens:** solo lo objetivo se corrige local (gratis); generación y corrección de abiertas son las únicas llamadas IA. La fuente "rápido" usa los resúmenes ya generados; "completo" usa el texto limpio consolidado.
- **Tipos de pregunta (7):** `McUna` (una correcta), `McVarias` (varias correctas), `VfJustificado` (V/F + justificación), `Desarrollo`, `DesarrolloItems` (sub-ítems), `Completar` (respuesta corta/huecos), `Emparejar`.
- **Corrección:** objetivo (McUna, McVarias, Vf, Completar, Emparejar) = local; abierto (justificación de VF, Desarrollo, DesarrolloItems) = IA. Nota final combinada = Σ puntos obtenidos / Σ puntos.
- **Nota:** escala configurable (default 0-10) + % de acierto siempre; nota de aprobación configurable.
- **SQLite es índice de estado.** Migración no destructiva: `schema_version`→6; `CREATE TABLE IF NOT EXISTS`; bump con upsert.
- **Resiliencia:** si la IA devuelve JSON inválido al generar, reintentar una vez y, si falla, lanzar un error accionable (sin dejar estado corrupto). La corrección de abiertas tolera respuestas faltantes.
- **Tokens/costo del examen:** se acumulan en `Examen.tokens`/`costo_estimado` (mismo criterio que la Fase 3).
- **Build de verificación:** `dotnet build Resumenes.sln -c Debug` debe pasar (incluye `Resumenes.Cli`).
- **Tests:** xUnit + fakes (`FakeClienteIA`, fake de `IRepositorioExamenes`).

## Decisión a confirmar (prompts del examen)

En esta entrega los prompts de generación y corrección del examen son **fijos** (constantes en `GeneradorExamen`/`CorrectorExamen`). Hacerlos editables desde "Prompts (avanzado)" (como en la Fase 1) queda como mejora para 5b/backlog. (La spec los listaba editables; se prioriza entregar el motor funcional.)

---

## File Structure

- `src/Resumenes.Infrastructure/schema.sql` + `schema.sql` — **Modify**: tablas `Examen`, `PreguntaExamen`, `RespuestaUsuario`; `schema_version`→6.
- `src/Resumenes.Core/Modelos/Examenes.cs` — **Create**: enums `TipoPregunta`/`EstadoExamen`; entidades `Examen`, `PreguntaExamen`, `RespuestaUsuario`; modelos de datos de pregunta y de configuración/resultado.
- `src/Resumenes.Core/Interfaces/IRepositorioExamenes.cs` — **Create**: interfaz del repo de exámenes.
- `src/Resumenes.Infrastructure/Persistencia/SqliteRepositorioExamenes.cs` — **Create**: implementación SQLite.
- `src/Resumenes.Core/Interfaces/IGeneradorExamen.cs`, `ICorrectorExamen.cs` — **Create**: interfaces.
- `src/Resumenes.Infrastructure/Examenes/GeneradorExamen.cs` — **Create**.
- `src/Resumenes.Infrastructure/Examenes/CorrectorExamen.cs` — **Create**.
- `src/Resumenes.Infrastructure/Examenes/ServicioExamenes.cs` — **Create**: orquestador.
- `src/Resumenes.Core/Interfaces/IServicioExamenes.cs` — **Create**.
- `src/Resumenes.Infrastructure/Aplicacion/Configuracion.cs` — **Modify**: `EscalaNotaMaxima`, `NotaAprobacion`.
- `src/Resumenes.Ui/App.xaml.cs` + `src/Resumenes.Cli/Program.cs` — **Modify**: registrar/instanciar los servicios nuevos.
- Tests: `tests/Resumenes.Tests/RepositorioExamenesTests.cs`, `GeneradorExamenTests.cs`, `CorrectorExamenTests.cs`, `ServicioExamenesTests.cs`, y un fake en `tests/Resumenes.Tests/Fakes/Fakes.cs`.

---

## Task 1: Esquema, entidades y repositorio de exámenes

**Files:**
- Modify: `src/Resumenes.Infrastructure/schema.sql`, `schema.sql`
- Create: `src/Resumenes.Core/Modelos/Examenes.cs`
- Create: `src/Resumenes.Core/Interfaces/IRepositorioExamenes.cs`
- Create: `src/Resumenes.Infrastructure/Persistencia/SqliteRepositorioExamenes.cs`
- Modify: `tests/Resumenes.Tests/Fakes/Fakes.cs`
- Test: `tests/Resumenes.Tests/RepositorioExamenesTests.cs`

**Interfaces:**
- Produces (en `Resumenes.Core.Modelos`):
  - `enum TipoPregunta { McUna, McVarias, VfJustificado, Desarrollo, DesarrolloItems, Completar, Emparejar }`
  - `enum EstadoExamen { Borrador, EnCurso, Finalizado, Corregido }`
  - `class Examen { string Id; string AnalisisId; string Titulo; string ConfigJson; EstadoExamen Estado; double? Nota; double? Porcentaje; bool? Aprobado; string? FeedbackGeneral; int Tokens; double CostoEstimado; DateTime CreadoEn; DateTime? IniciadoEn; DateTime? FinalizadoEn; }`
  - `class PreguntaExamen { string Id; string ExamenId; int Orden; TipoPregunta Tipo; string Enunciado; double Puntos; string DatosJson; string? TemaId; }`
  - `class RespuestaUsuario { string Id; string ExamenId; string PreguntaId; string? RespuestaJson; bool? Correcta; double PuntosObtenidos; string? FeedbackIa; bool Ambigua; }`
- Produces (en `Resumenes.Core.Interfaces.IRepositorioExamenes`):
  - `void GuardarExamen(Examen e)`, `Examen? ObtenerExamen(string id)`, `IReadOnlyList<Examen> ListarExamenes(string analisisId)`, `void EliminarExamen(string id)`
  - `void GuardarPregunta(PreguntaExamen p)`, `IReadOnlyList<PreguntaExamen> ListarPreguntas(string examenId)`
  - `void GuardarRespuesta(RespuestaUsuario r)`, `IReadOnlyList<RespuestaUsuario> ListarRespuestas(string examenId)`

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/Resumenes.Tests/RepositorioExamenesTests.cs`:

```csharp
using Resumenes.Core.Modelos;
using Resumenes.Infrastructure.Persistencia;
using Xunit;

namespace Resumenes.Tests;

public class RepositorioExamenesTests
{
    private static (SqliteRepositorioEstado estado, SqliteRepositorioExamenes ex, string tmp) Nuevo()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"resu-ex-{Guid.NewGuid():N}.db");
        var cs = $"Data Source={tmp}";
        var estado = new SqliteRepositorioEstado(cs);
        estado.InicializarEsquema();
        return (estado, new SqliteRepositorioExamenes(cs), tmp);
    }

    [Fact]
    public void Examen_Preguntas_Respuestas_RoundTrip()
    {
        var (estado, repo, tmp) = Nuevo();
        try
        {
            estado.GuardarAnalisis(new Analisis("an1","n","c","fp",EstadoAnalisis.Completado,DateTime.UtcNow,DateTime.UtcNow));

            repo.GuardarExamen(new Examen {
                Id="ex1", AnalisisId="an1", Titulo="Parcial", ConfigJson="{}",
                Estado=EstadoExamen.Borrador, Tokens=0, CostoEstimado=0, CreadoEn=DateTime.UtcNow });
            repo.GuardarPregunta(new PreguntaExamen {
                Id="p1", ExamenId="ex1", Orden=1, Tipo=TipoPregunta.McUna,
                Enunciado="¿2+2?", Puntos=1, DatosJson="{}" });
            repo.GuardarRespuesta(new RespuestaUsuario {
                Id="r1", ExamenId="ex1", PreguntaId="p1", RespuestaJson="\"4\"",
                Correcta=true, PuntosObtenidos=1, Ambigua=false });

            Assert.Equal("Parcial", repo.ObtenerExamen("ex1")!.Titulo);
            Assert.Single(repo.ListarExamenes("an1"));
            Assert.Single(repo.ListarPreguntas("ex1"));
            Assert.True(repo.ListarRespuestas("ex1")[0].Correcta);

            // Actualizar estado/nota (upsert)
            var e = repo.ObtenerExamen("ex1")!;
            e.Estado = EstadoExamen.Corregido; e.Nota = 8.5; e.Porcentaje = 85; e.Aprobado = true;
            repo.GuardarExamen(e);
            var e2 = repo.ObtenerExamen("ex1")!;
            Assert.Equal(EstadoExamen.Corregido, e2.Estado);
            Assert.Equal(8.5, e2.Nota);

            repo.EliminarExamen("ex1");
            Assert.Null(repo.ObtenerExamen("ex1"));
            Assert.Empty(repo.ListarPreguntas("ex1"));   // cascada
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter Examen_Preguntas_Respuestas_RoundTrip`
Expected: FAIL de compilación (tipos no existen).

- [ ] **Step 3: Agregar las tablas al esquema**

En `src/Resumenes.Infrastructure/schema.sql`, antes del bloque `SchemaMeta`, insertar:

```sql
-- ----------------------------------------------------------------------------
-- EXAMEN / PREGUNTA_EXAMEN / RESPUESTA_USUARIO: simulador de exámenes.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Examen (
    id               TEXT PRIMARY KEY,
    analisis_id      TEXT NOT NULL,
    titulo           TEXT NOT NULL,
    config_json      TEXT NOT NULL,
    estado           TEXT NOT NULL DEFAULT 'Borrador'
                        CHECK (estado IN ('Borrador','EnCurso','Finalizado','Corregido')),
    nota             REAL,
    porcentaje       REAL,
    aprobado         INTEGER,
    feedback_general TEXT,
    tokens           INTEGER NOT NULL DEFAULT 0,
    costo_estimado   REAL    NOT NULL DEFAULT 0,
    creado_en        TEXT NOT NULL,
    iniciado_en      TEXT,
    finalizado_en    TEXT,
    FOREIGN KEY (analisis_id) REFERENCES Analisis(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_examen_analisis ON Examen(analisis_id);

CREATE TABLE IF NOT EXISTS PreguntaExamen (
    id         TEXT PRIMARY KEY,
    examen_id  TEXT NOT NULL,
    orden      INTEGER NOT NULL,
    tipo       TEXT NOT NULL
                  CHECK (tipo IN ('McUna','McVarias','VfJustificado','Desarrollo',
                                  'DesarrolloItems','Completar','Emparejar')),
    enunciado  TEXT NOT NULL,
    puntos     REAL NOT NULL,
    datos_json TEXT NOT NULL,
    tema_id    TEXT,
    FOREIGN KEY (examen_id) REFERENCES Examen(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_pregunta_examen ON PreguntaExamen(examen_id);

CREATE TABLE IF NOT EXISTS RespuestaUsuario (
    id               TEXT PRIMARY KEY,
    examen_id        TEXT NOT NULL,
    pregunta_id      TEXT NOT NULL,
    respuesta_json   TEXT,
    correcta         INTEGER,
    puntos_obtenidos REAL NOT NULL DEFAULT 0,
    feedback_ia      TEXT,
    ambigua          INTEGER NOT NULL DEFAULT 0 CHECK (ambigua IN (0,1)),
    FOREIGN KEY (examen_id)   REFERENCES Examen(id)         ON DELETE CASCADE,
    FOREIGN KEY (pregunta_id) REFERENCES PreguntaExamen(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_respuesta_examen ON RespuestaUsuario(examen_id);
```

Y subir la versión (la línea que hoy dice `'5'`):
```sql
INSERT INTO SchemaMeta (clave, valor) VALUES ('schema_version', '6')
ON CONFLICT(clave) DO UPDATE SET valor='6';
```

Replicar en `schema.sql` de la raíz.

- [ ] **Step 4: Crear las entidades y modelos**

Crear `src/Resumenes.Core/Modelos/Examenes.cs`:

```csharp
namespace Resumenes.Core.Modelos;

public enum TipoPregunta { McUna, McVarias, VfJustificado, Desarrollo, DesarrolloItems, Completar, Emparejar }
public enum EstadoExamen { Borrador, EnCurso, Finalizado, Corregido }

public class Examen
{
    public required string Id { get; set; }
    public required string AnalisisId { get; set; }
    public required string Titulo { get; set; }
    public string ConfigJson { get; set; } = "{}";
    public EstadoExamen Estado { get; set; } = EstadoExamen.Borrador;
    public double? Nota { get; set; }
    public double? Porcentaje { get; set; }
    public bool? Aprobado { get; set; }
    public string? FeedbackGeneral { get; set; }
    public int Tokens { get; set; }
    public double CostoEstimado { get; set; }
    public DateTime CreadoEn { get; set; }
    public DateTime? IniciadoEn { get; set; }
    public DateTime? FinalizadoEn { get; set; }
}

public class PreguntaExamen
{
    public required string Id { get; set; }
    public required string ExamenId { get; set; }
    public int Orden { get; set; }
    public TipoPregunta Tipo { get; set; }
    public required string Enunciado { get; set; }
    public double Puntos { get; set; }
    public string DatosJson { get; set; } = "{}";
    public string? TemaId { get; set; }
}

public class RespuestaUsuario
{
    public required string Id { get; set; }
    public required string ExamenId { get; set; }
    public required string PreguntaId { get; set; }
    public string? RespuestaJson { get; set; }
    public bool? Correcta { get; set; }
    public double PuntosObtenidos { get; set; }
    public string? FeedbackIa { get; set; }
    public bool Ambigua { get; set; }
}
```

- [ ] **Step 5: Crear la interfaz del repositorio**

Crear `src/Resumenes.Core/Interfaces/IRepositorioExamenes.cs`:

```csharp
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
```

- [ ] **Step 6: Implementar `SqliteRepositorioExamenes`**

Crear `src/Resumenes.Infrastructure/Persistencia/SqliteRepositorioExamenes.cs`. Seguir el patrón de `SqliteRepositorioEstado` (método `Abrir()` con `PRAGMA foreign_keys=ON`, upsert manual con UPDATE→INSERT). Implementación completa:

```csharp
using Microsoft.Data.Sqlite;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;

namespace Resumenes.Infrastructure.Persistencia;

public class SqliteRepositorioExamenes(string cadenaConexion) : IRepositorioExamenes
{
    private SqliteConnection Abrir()
    {
        var con = new SqliteConnection(cadenaConexion);
        con.Open();
        using var p = con.CreateCommand();
        p.CommandText = "PRAGMA foreign_keys = ON;";
        p.ExecuteNonQuery();
        return con;
    }

    public void GuardarExamen(Examen e)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO Examen
            (id, analisis_id, titulo, config_json, estado, nota, porcentaje, aprobado, feedback_general,
             tokens, costo_estimado, creado_en, iniciado_en, finalizado_en)
            VALUES ($id,$an,$t,$cfg,$est,$nota,$pct,$apr,$fb,$tok,$costo,$cr,$ini,$fin)
            ON CONFLICT(id) DO UPDATE SET titulo=$t, config_json=$cfg, estado=$est, nota=$nota,
                porcentaje=$pct, aprobado=$apr, feedback_general=$fb, tokens=$tok, costo_estimado=$costo,
                iniciado_en=$ini, finalizado_en=$fin;";
        cmd.Parameters.AddWithValue("$id", e.Id);
        cmd.Parameters.AddWithValue("$an", e.AnalisisId);
        cmd.Parameters.AddWithValue("$t", e.Titulo);
        cmd.Parameters.AddWithValue("$cfg", e.ConfigJson);
        cmd.Parameters.AddWithValue("$est", e.Estado.ToString());
        cmd.Parameters.AddWithValue("$nota", (object?)e.Nota ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$pct", (object?)e.Porcentaje ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$apr", e.Aprobado is null ? DBNull.Value : (e.Aprobado.Value ? 1 : 0));
        cmd.Parameters.AddWithValue("$fb", (object?)e.FeedbackGeneral ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tok", e.Tokens);
        cmd.Parameters.AddWithValue("$costo", e.CostoEstimado);
        cmd.Parameters.AddWithValue("$cr", e.CreadoEn.ToString("o"));
        cmd.Parameters.AddWithValue("$ini", (object?)e.IniciadoEn?.ToString("o") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$fin", (object?)e.FinalizadoEn?.ToString("o") ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static Examen LeerExamen(SqliteDataReader r) => new()
    {
        Id = r.GetString(0), AnalisisId = r.GetString(1), Titulo = r.GetString(2), ConfigJson = r.GetString(3),
        Estado = Enum.Parse<EstadoExamen>(r.GetString(4)),
        Nota = r.IsDBNull(5) ? null : r.GetDouble(5),
        Porcentaje = r.IsDBNull(6) ? null : r.GetDouble(6),
        Aprobado = r.IsDBNull(7) ? null : r.GetInt32(7) != 0,
        FeedbackGeneral = r.IsDBNull(8) ? null : r.GetString(8),
        Tokens = r.GetInt32(9), CostoEstimado = r.GetDouble(10),
        CreadoEn = DateTime.Parse(r.GetString(11)),
        IniciadoEn = r.IsDBNull(12) ? null : DateTime.Parse(r.GetString(12)),
        FinalizadoEn = r.IsDBNull(13) ? null : DateTime.Parse(r.GetString(13)),
    };

    private const string SelectExamen =
        "SELECT id, analisis_id, titulo, config_json, estado, nota, porcentaje, aprobado, feedback_general, " +
        "tokens, costo_estimado, creado_en, iniciado_en, finalizado_en FROM Examen";

    public Examen? ObtenerExamen(string id)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = SelectExamen + " WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? LeerExamen(r) : null;
    }

    public IReadOnlyList<Examen> ListarExamenes(string analisisId)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = SelectExamen + " WHERE analisis_id=$an ORDER BY creado_en DESC;";
        cmd.Parameters.AddWithValue("$an", analisisId);
        using var r = cmd.ExecuteReader();
        var lista = new List<Examen>();
        while (r.Read()) lista.Add(LeerExamen(r));
        return lista;
    }

    public void EliminarExamen(string id)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM Examen WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void GuardarPregunta(PreguntaExamen p)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO PreguntaExamen (id, examen_id, orden, tipo, enunciado, puntos, datos_json, tema_id)
            VALUES ($id,$ex,$o,$tipo,$en,$pts,$datos,$tema)
            ON CONFLICT(id) DO UPDATE SET orden=$o, tipo=$tipo, enunciado=$en, puntos=$pts, datos_json=$datos, tema_id=$tema;";
        cmd.Parameters.AddWithValue("$id", p.Id);
        cmd.Parameters.AddWithValue("$ex", p.ExamenId);
        cmd.Parameters.AddWithValue("$o", p.Orden);
        cmd.Parameters.AddWithValue("$tipo", p.Tipo.ToString());
        cmd.Parameters.AddWithValue("$en", p.Enunciado);
        cmd.Parameters.AddWithValue("$pts", p.Puntos);
        cmd.Parameters.AddWithValue("$datos", p.DatosJson);
        cmd.Parameters.AddWithValue("$tema", (object?)p.TemaId ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<PreguntaExamen> ListarPreguntas(string examenId)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT id, examen_id, orden, tipo, enunciado, puntos, datos_json, tema_id
                            FROM PreguntaExamen WHERE examen_id=$ex ORDER BY orden;";
        cmd.Parameters.AddWithValue("$ex", examenId);
        using var r = cmd.ExecuteReader();
        var lista = new List<PreguntaExamen>();
        while (r.Read())
            lista.Add(new PreguntaExamen {
                Id = r.GetString(0), ExamenId = r.GetString(1), Orden = r.GetInt32(2),
                Tipo = Enum.Parse<TipoPregunta>(r.GetString(3)), Enunciado = r.GetString(4),
                Puntos = r.GetDouble(5), DatosJson = r.GetString(6),
                TemaId = r.IsDBNull(7) ? null : r.GetString(7) });
        return lista;
    }

    public void GuardarRespuesta(RespuestaUsuario u)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO RespuestaUsuario (id, examen_id, pregunta_id, respuesta_json, correcta, puntos_obtenidos, feedback_ia, ambigua)
            VALUES ($id,$ex,$pre,$resp,$corr,$pts,$fb,$amb)
            ON CONFLICT(id) DO UPDATE SET respuesta_json=$resp, correcta=$corr, puntos_obtenidos=$pts, feedback_ia=$fb, ambigua=$amb;";
        cmd.Parameters.AddWithValue("$id", u.Id);
        cmd.Parameters.AddWithValue("$ex", u.ExamenId);
        cmd.Parameters.AddWithValue("$pre", u.PreguntaId);
        cmd.Parameters.AddWithValue("$resp", (object?)u.RespuestaJson ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$corr", u.Correcta is null ? DBNull.Value : (u.Correcta.Value ? 1 : 0));
        cmd.Parameters.AddWithValue("$pts", u.PuntosObtenidos);
        cmd.Parameters.AddWithValue("$fb", (object?)u.FeedbackIa ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$amb", u.Ambigua ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<RespuestaUsuario> ListarRespuestas(string examenId)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT id, examen_id, pregunta_id, respuesta_json, correcta, puntos_obtenidos, feedback_ia, ambigua
                            FROM RespuestaUsuario WHERE examen_id=$ex;";
        cmd.Parameters.AddWithValue("$ex", examenId);
        using var r = cmd.ExecuteReader();
        var lista = new List<RespuestaUsuario>();
        while (r.Read())
            lista.Add(new RespuestaUsuario {
                Id = r.GetString(0), ExamenId = r.GetString(1), PreguntaId = r.GetString(2),
                RespuestaJson = r.IsDBNull(3) ? null : r.GetString(3),
                Correcta = r.IsDBNull(4) ? null : r.GetInt32(4) != 0,
                PuntosObtenidos = r.GetDouble(5),
                FeedbackIa = r.IsDBNull(6) ? null : r.GetString(6),
                Ambigua = r.GetInt32(7) != 0 });
        return lista;
    }
}
```

- [ ] **Step 7: Agregar un fake del repo (para tasks siguientes)**

En `tests/Resumenes.Tests/Fakes/Fakes.cs`, agregar un `RepositorioExamenesEnMemoria : IRepositorioExamenes` con diccionarios/listas en memoria (análogo a `RepositorioEnMemoria`):

```csharp
public class RepositorioExamenesEnMemoria : Resumenes.Core.Interfaces.IRepositorioExamenes
{
    private readonly Dictionary<string, Examen> _examenes = new();
    private readonly List<PreguntaExamen> _preguntas = new();
    private readonly List<RespuestaUsuario> _respuestas = new();

    public void GuardarExamen(Examen e) => _examenes[e.Id] = e;
    public Examen? ObtenerExamen(string id) => _examenes.TryGetValue(id, out var e) ? e : null;
    public IReadOnlyList<Examen> ListarExamenes(string analisisId)
        => _examenes.Values.Where(e => e.AnalisisId == analisisId).OrderByDescending(e => e.CreadoEn).ToList();
    public void EliminarExamen(string id)
    {
        _examenes.Remove(id);
        _preguntas.RemoveAll(p => p.ExamenId == id);
        _respuestas.RemoveAll(r => r.ExamenId == id);
    }
    public void GuardarPregunta(PreguntaExamen p) { _preguntas.RemoveAll(x => x.Id == p.Id); _preguntas.Add(p); }
    public IReadOnlyList<PreguntaExamen> ListarPreguntas(string examenId)
        => _preguntas.Where(p => p.ExamenId == examenId).OrderBy(p => p.Orden).ToList();
    public void GuardarRespuesta(RespuestaUsuario r) { _respuestas.RemoveAll(x => x.Id == r.Id); _respuestas.Add(r); }
    public IReadOnlyList<RespuestaUsuario> ListarRespuestas(string examenId)
        => _respuestas.Where(r => r.ExamenId == examenId).ToList();
}
```
(Agregar `using Resumenes.Core.Modelos;` si falta en el archivo — ya está.)

- [ ] **Step 8: Correr el test y verificar que pasa**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter Examen_Preguntas_Respuestas_RoundTrip`
Expected: PASS.

- [ ] **Step 9: Commit**

```bash
git add src/Resumenes.Infrastructure/schema.sql schema.sql \
        src/Resumenes.Core/Modelos/Examenes.cs \
        src/Resumenes.Core/Interfaces/IRepositorioExamenes.cs \
        src/Resumenes.Infrastructure/Persistencia/SqliteRepositorioExamenes.cs \
        tests/Resumenes.Tests/Fakes/Fakes.cs tests/Resumenes.Tests/RepositorioExamenesTests.cs
git commit -m "feat(examenes): esquema, entidades y repositorio de examenes"
```

---

## Task 2: `GeneradorExamen` (IA → preguntas)

**Files:**
- Create: `src/Resumenes.Core/Interfaces/IGeneradorExamen.cs`
- Create: `src/Resumenes.Infrastructure/Examenes/GeneradorExamen.cs`
- Test: `tests/Resumenes.Tests/GeneradorExamenTests.cs`

**Interfaces:**
- Consumes: `IClienteIA.CompletarAsync` (existente).
- Produces:
  - `record ConfigExamen(IReadOnlyList<CantidadPorTipo> Tipos, IReadOnlyList<string> TemasIncluidos, string Dificultad, double PuntosTotales, int TiempoLimiteMin, string Fuente)` (en `Resumenes.Core.Modelos`)
  - `record CantidadPorTipo(TipoPregunta Tipo, int Cantidad)`
  - `record ResultadoGeneracion(IReadOnlyList<PreguntaExamen> Preguntas, int TokensEntrada, int TokensSalida)`
  - `interface IGeneradorExamen { Task<ResultadoGeneracion> GenerarAsync(string examenId, string contenidoFuente, ConfigExamen cfg, string modelo, CancellationToken ct); }`

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/Resumenes.Tests/GeneradorExamenTests.cs`. Usa un `FakeClienteIA` que devuelve un JSON de preguntas fijo, y verifica el parseo a `PreguntaExamen` con `DatosJson`; y el caso de JSON inválido (reintento y luego error).

```csharp
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;
using Resumenes.Infrastructure.Examenes;
using Resumenes.Tests.Fakes;
using Xunit;

namespace Resumenes.Tests;

public class GeneradorExamenTests
{
    private const string JsonOk = """
    {"preguntas":[
      {"tipo":"McUna","enunciado":"¿Capital de Francia?","puntos":1,
       "datos":{"opciones":[{"texto":"París","correcta":true},{"texto":"Roma","correcta":false}]}},
      {"tipo":"Desarrollo","enunciado":"Explicá la fotosíntesis.","puntos":2,
       "datos":{"criterios":"menciona luz, clorofila, CO2"}}
    ]}
    """;

    private static ConfigExamen Cfg() => new(
        new[] { new CantidadPorTipo(TipoPregunta.McUna, 1), new CantidadPorTipo(TipoPregunta.Desarrollo, 1) },
        Array.Empty<string>(), "media", 3, 30, "rapido");

    [Fact]
    public async Task GenerarAsync_ParseaPreguntasYDatos()
    {
        var ia = new FakeClienteIA { Responder = _ => JsonOk };
        var gen = new GeneradorExamen(ia);

        var r = await gen.GenerarAsync("ex1", "contenido de estudio", Cfg(), "modelo-x", default);

        Assert.Equal(2, r.Preguntas.Count);
        Assert.Equal(TipoPregunta.McUna, r.Preguntas[0].Tipo);
        Assert.Equal("ex1", r.Preguntas[0].ExamenId);
        Assert.Equal(1, r.Preguntas[0].Orden);
        Assert.Contains("París", r.Preguntas[0].DatosJson);
        Assert.True(r.TokensEntrada > 0 || r.TokensSalida > 0);
    }

    [Fact]
    public async Task GenerarAsync_JsonInvalido_ReintentaYLanza()
    {
        int llamadas = 0;
        var ia = new FakeClienteIA { Responder = _ => { llamadas++; return "esto no es json"; } };
        var gen = new GeneradorExamen(ia);

        await Assert.ThrowsAnyAsync<Exception>(() => gen.GenerarAsync("ex1", "x", Cfg(), "m", default));
        Assert.True(llamadas >= 2, "debe reintentar al menos una vez");
    }
}
```

- [ ] **Step 2: Correr y verificar que fallan**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter GeneradorExamenTests`
Expected: FAIL de compilación.

- [ ] **Step 3: Crear los records de config en `Examenes.cs`**

En `src/Resumenes.Core/Modelos/Examenes.cs`, agregar al final:

```csharp
public record CantidadPorTipo(TipoPregunta Tipo, int Cantidad);
public record ConfigExamen(
    IReadOnlyList<CantidadPorTipo> Tipos,
    IReadOnlyList<string> TemasIncluidos,
    string Dificultad,
    double PuntosTotales,
    int TiempoLimiteMin,
    string Fuente);  // "rapido" | "completo"
public record ResultadoGeneracion(IReadOnlyList<PreguntaExamen> Preguntas, int TokensEntrada, int TokensSalida);
```

- [ ] **Step 4: Crear la interfaz**

Crear `src/Resumenes.Core/Interfaces/IGeneradorExamen.cs`:

```csharp
using Resumenes.Core.Modelos;

namespace Resumenes.Core.Interfaces;

public interface IGeneradorExamen
{
    Task<ResultadoGeneracion> GenerarAsync(string examenId, string contenidoFuente, ConfigExamen cfg, string modelo, CancellationToken ct);
}
```

- [ ] **Step 5: Implementar `GeneradorExamen`**

Crear `src/Resumenes.Infrastructure/Examenes/GeneradorExamen.cs`. El prompt describe el contrato JSON; se reintenta una vez si el parseo falla. `Apoyos.Ids`/`Guid` para los ids de pregunta.

```csharp
using System.Text;
using System.Text.Json;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;

namespace Resumenes.Infrastructure.Examenes;

public class GeneradorExamen(IClienteIA ia) : IGeneradorExamen
{
    private const string FormatoJson =
        "Devolvé SOLO un JSON: {\"preguntas\":[{\"tipo\":\"<Tipo>\",\"enunciado\":\"...\",\"puntos\":<n>,\"datos\":{...}}]}. " +
        "Tipos válidos y su 'datos': " +
        "McUna/McVarias → {\"opciones\":[{\"texto\":\"...\",\"correcta\":true|false}]}; " +
        "VfJustificado → {\"afirmacion\":\"...\",\"esVerdadero\":true|false}; " +
        "Desarrollo → {\"criterios\":\"qué debe contener una buena respuesta\"}; " +
        "DesarrolloItems → {\"items\":[{\"enunciado\":\"...\",\"criterios\":\"...\"}]}; " +
        "Completar → {\"texto\":\"frase con ___\",\"respuestas\":[\"...\"]}; " +
        "Emparejar → {\"izquierda\":[\"...\"],\"derecha\":[\"...\"],\"pares\":[[0,1]]}. " +
        "Sin texto fuera del JSON. Respetá el idioma del contenido.";

    public async Task<ResultadoGeneracion> GenerarAsync(string examenId, string contenidoFuente, ConfigExamen cfg, string modelo, CancellationToken ct)
    {
        var pedido = new StringBuilder();
        pedido.AppendLine($"Generá un examen de dificultad {cfg.Dificultad} con estas cantidades por tipo:");
        foreach (var t in cfg.Tipos) pedido.AppendLine($"- {t.Cantidad} de tipo {t.Tipo}");
        pedido.AppendLine($"Repartí {cfg.PuntosTotales} puntos en total entre las preguntas.");
        pedido.AppendLine("Basate EXCLUSIVAMENTE en este contenido:");
        pedido.AppendLine(contenidoFuente);

        var sys = "Sos un generador de exámenes de estudio. " + FormatoJson;

        int tokIn = 0, tokOut = 0;
        Exception? ultimo = null;
        for (int intento = 0; intento < 2; intento++)
        {
            var r = await ia.CompletarAsync(new SolicitudIA(sys, pedido.ToString(), 0.4, 8000, "examen-gen-v1", modelo), ct);
            tokIn += r.TokensPrompt; tokOut += r.TokensCompletion;
            try
            {
                var preguntas = Parsear(examenId, r.Texto);
                if (preguntas.Count == 0) throw new InvalidOperationException("El examen generado no tiene preguntas.");
                return new ResultadoGeneracion(preguntas, tokIn, tokOut);
            }
            catch (Exception ex) { ultimo = ex; }
        }
        throw new InvalidOperationException("No se pudo generar un examen válido (JSON inesperado de la IA).", ultimo);
    }

    private static List<PreguntaExamen> Parsear(string examenId, string texto)
    {
        var s = texto.Trim();
        int i = s.IndexOf('{'), j = s.LastIndexOf('}');
        if (i >= 0 && j > i) s = s[i..(j + 1)];

        using var doc = JsonDocument.Parse(s);
        var preguntas = new List<PreguntaExamen>();
        int orden = 1;
        foreach (var p in doc.RootElement.GetProperty("preguntas").EnumerateArray())
        {
            var tipo = Enum.Parse<TipoPregunta>(p.GetProperty("tipo").GetString()!, ignoreCase: true);
            var enunciado = p.GetProperty("enunciado").GetString() ?? "";
            var puntos = p.TryGetProperty("puntos", out var pe) && pe.ValueKind == JsonValueKind.Number ? pe.GetDouble() : 1;
            var datos = p.TryGetProperty("datos", out var d) ? d.GetRawText() : "{}";
            preguntas.Add(new PreguntaExamen {
                Id = Guid.NewGuid().ToString("N"), ExamenId = examenId, Orden = orden++,
                Tipo = tipo, Enunciado = enunciado, Puntos = puntos, DatosJson = datos });
        }
        return preguntas;
    }
}
```

- [ ] **Step 6: Correr los tests y verificar que pasan**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter GeneradorExamenTests`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add src/Resumenes.Core/Modelos/Examenes.cs \
        src/Resumenes.Core/Interfaces/IGeneradorExamen.cs \
        src/Resumenes.Infrastructure/Examenes/GeneradorExamen.cs \
        tests/Resumenes.Tests/GeneradorExamenTests.cs
git commit -m "feat(examenes): generador de preguntas con IA (contrato JSON + reintento)"
```

---

## Task 3: `CorrectorExamen` (objetivo local + abierto IA + nota)

**Files:**
- Create: `src/Resumenes.Core/Interfaces/ICorrectorExamen.cs`
- Create: `src/Resumenes.Infrastructure/Examenes/CorrectorExamen.cs`
- Test: `tests/Resumenes.Tests/CorrectorExamenTests.cs`

**Interfaces:**
- Consumes: `IClienteIA`.
- Produces:
  - `record ResultadoCorreccion(double Nota, double Porcentaje, bool Aprobado, string FeedbackGeneral, int TokensEntrada, int TokensSalida)`
  - `interface ICorrectorExamen { void CorregirObjetivo(PreguntaExamen p, RespuestaUsuario r); Task CorregirAbiertasAsync(IReadOnlyList<(PreguntaExamen p, RespuestaUsuario r)> abiertas, string modelo, CancellationToken ct); ResultadoCorreccion CalcularResultado(IReadOnlyList<(PreguntaExamen p, RespuestaUsuario r)> todo, double escalaMax, double notaAprobacion); }`

(Nota: `CorregirObjetivo` y `CorregirAbiertasAsync` MUTAN el `RespuestaUsuario` recibido — setean `Correcta`, `PuntosObtenidos`, `FeedbackIa`, `Ambigua`. Los tokens de la corrección abierta se acumulan; el ServicioExamenes los lee de `ResultadoCorreccion`.)

- [ ] **Step 1: Escribir los tests que fallan**

Crear `tests/Resumenes.Tests/CorrectorExamenTests.cs`. Cubre: MC-una local, Completar local (normalizado), V/F local, y el cálculo de nota. (Abierto con IA: un test con `FakeClienteIA` que devuelve un JSON de veredicto.)

```csharp
using System.Text.Json;
using Resumenes.Core.Modelos;
using Resumenes.Infrastructure.Examenes;
using Resumenes.Tests.Fakes;
using Xunit;

namespace Resumenes.Tests;

public class CorrectorExamenTests
{
    private static PreguntaExamen McUna() => new() {
        Id="p", ExamenId="e", Tipo=TipoPregunta.McUna, Enunciado="?", Puntos=1,
        DatosJson="{\"opciones\":[{\"texto\":\"A\",\"correcta\":true},{\"texto\":\"B\",\"correcta\":false}]}" };

    [Fact]
    public void CorregirObjetivo_McUna_CorrectaEIncorrecta()
    {
        var c = new CorrectorExamen(new FakeClienteIA());
        var p = McUna();

        var ok = new RespuestaUsuario { Id="r1", ExamenId="e", PreguntaId="p", RespuestaJson="0" }; // índice 0 = A
        c.CorregirObjetivo(p, ok);
        Assert.True(ok.Correcta);
        Assert.Equal(1, ok.PuntosObtenidos);

        var mal = new RespuestaUsuario { Id="r2", ExamenId="e", PreguntaId="p", RespuestaJson="1" };
        c.CorregirObjetivo(p, mal);
        Assert.False(mal.Correcta);
        Assert.Equal(0, mal.PuntosObtenidos);
    }

    [Fact]
    public void CalcularResultado_NotaYPorcentajeYAprobado()
    {
        var c = new CorrectorExamen(new FakeClienteIA());
        var p1 = McUna(); var p2 = McUna();
        var r1 = new RespuestaUsuario { Id="r1", ExamenId="e", PreguntaId=p1.Id, PuntosObtenidos=1 };
        var r2 = new RespuestaUsuario { Id="r2", ExamenId="e", PreguntaId=p2.Id, PuntosObtenidos=0 };

        var res = c.CalcularResultado(new[] { (p1, r1), (p2, r2) }, escalaMax: 10, notaAprobacion: 6);
        Assert.Equal(50, res.Porcentaje);   // 1 de 2 puntos
        Assert.Equal(5, res.Nota);          // 50% de 10
        Assert.False(res.Aprobado);         // 5 < 6
    }

    [Fact]
    public async Task CorregirAbiertasAsync_AplicaVeredictoIa()
    {
        const string veredicto = """
        {"resultados":[{"puntos":1.5,"feedback":"bien pero incompleto","ambigua":false}]}
        """;
        var ia = new FakeClienteIA { Responder = _ => veredicto };
        var c = new CorrectorExamen(ia);

        var p = new PreguntaExamen { Id="p", ExamenId="e", Tipo=TipoPregunta.Desarrollo, Enunciado="Explicá X", Puntos=2,
            DatosJson="{\"criterios\":\"...\"}" };
        var r = new RespuestaUsuario { Id="r", ExamenId="e", PreguntaId="p", RespuestaJson="\"mi respuesta\"" };

        await c.CorregirAbiertasAsync(new[] { (p, r) }, "modelo", default);

        Assert.Equal(1.5, r.PuntosObtenidos);
        Assert.Equal("bien pero incompleto", r.FeedbackIa);
    }
}
```

- [ ] **Step 2: Correr y verificar que fallan**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter CorrectorExamenTests`
Expected: FAIL de compilación.

- [ ] **Step 3: Crear la interfaz**

Crear `src/Resumenes.Core/Interfaces/ICorrectorExamen.cs`:

```csharp
using Resumenes.Core.Modelos;

namespace Resumenes.Core.Interfaces;

public record ResultadoCorreccion(double Nota, double Porcentaje, bool Aprobado, string FeedbackGeneral, int TokensEntrada, int TokensSalida);

public interface ICorrectorExamen
{
    /// <summary>Corrige una pregunta objetiva LOCALMENTE; muta r (Correcta, PuntosObtenidos).</summary>
    void CorregirObjetivo(PreguntaExamen p, RespuestaUsuario r);
    /// <summary>Corrige las preguntas abiertas con IA; muta cada r (PuntosObtenidos, FeedbackIa, Ambigua). Acumula tokens en el resultado del Servicio.</summary>
    Task<(int tokIn, int tokOut)> CorregirAbiertasAsync(IReadOnlyList<(PreguntaExamen p, RespuestaUsuario r)> abiertas, string modelo, CancellationToken ct);
    ResultadoCorreccion CalcularResultado(IReadOnlyList<(PreguntaExamen p, RespuestaUsuario r)> todo, double escalaMax, double notaAprobacion);
}
```

(El test de `CorregirAbiertasAsync` ignora el valor devuelto; la firma con tokens es para el ServicioExamenes.)

- [ ] **Step 4: Implementar `CorrectorExamen`**

Crear `src/Resumenes.Infrastructure/Examenes/CorrectorExamen.cs`. Corrección local por tipo objetivo (interpretando `DatosJson` y `RespuestaJson`); corrección abierta en un batch con IA; cálculo de nota.

```csharp
using System.Text;
using System.Text.Json;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;

namespace Resumenes.Infrastructure.Examenes;

public class CorrectorExamen(IClienteIA ia) : ICorrectorExamen
{
    public void CorregirObjetivo(PreguntaExamen p, RespuestaUsuario r)
    {
        using var datos = JsonDocument.Parse(p.DatosJson);
        var root = datos.RootElement;
        // VfJustificado NO entra aquí: es abierto (lo evalúa la IA, que recibe la afirmación
        // y 'esVerdadero' vía DatosJson y puntúa V/F + justificación de forma integral).
        bool correcta = p.Tipo switch
        {
            TipoPregunta.McUna => CorregirMcUna(root, r.RespuestaJson),
            TipoPregunta.McVarias => CorregirMcVarias(root, r.RespuestaJson),
            TipoPregunta.Completar => CorregirCompletar(root, r.RespuestaJson),
            TipoPregunta.Emparejar => CorregirEmparejar(root, r.RespuestaJson),
            _ => false
        };
        r.Correcta = correcta;
        r.PuntosObtenidos = correcta ? p.Puntos : 0;
    }

    // RespuestaJson de McUna: índice de la opción elegida (número).
    private static bool CorregirMcUna(JsonElement datos, string? resp)
    {
        if (string.IsNullOrWhiteSpace(resp) || !int.TryParse(resp.Trim('"', ' '), out var idx)) return false;
        var ops = datos.GetProperty("opciones");
        return idx >= 0 && idx < ops.GetArrayLength() && ops[idx].GetProperty("correcta").GetBoolean();
    }

    // RespuestaJson de McVarias: array de índices elegidos. Correcta = coincide EXACTO con las correctas.
    private static bool CorregirMcVarias(JsonElement datos, string? resp)
    {
        var elegidas = ParseIndices(resp);
        var correctas = new HashSet<int>();
        var ops = datos.GetProperty("opciones");
        for (int k = 0; k < ops.GetArrayLength(); k++)
            if (ops[k].GetProperty("correcta").GetBoolean()) correctas.Add(k);
        return elegidas.SetEquals(correctas);
    }

    // RespuestaJson de Completar: array de strings, una por hueco. Match normalizado.
    private static bool CorregirCompletar(JsonElement datos, string? resp)
    {
        var dadas = ParseStrings(resp);
        var esperadas = datos.GetProperty("respuestas").EnumerateArray().Select(e => Norm(e.GetString())).ToList();
        if (dadas.Count != esperadas.Count) return false;
        for (int k = 0; k < esperadas.Count; k++)
            if (Norm(dadas[k]) != esperadas[k]) return false;
        return true;
    }

    // RespuestaJson de Emparejar: array de pares [i,j]. Correcto = mismo conjunto que datos.pares.
    private static bool CorregirEmparejar(JsonElement datos, string? resp)
    {
        var dados = ParsePares(resp);
        var esperados = new HashSet<(int,int)>();
        foreach (var par in datos.GetProperty("pares").EnumerateArray())
            esperados.Add((par[0].GetInt32(), par[1].GetInt32()));
        return dados.SetEquals(esperados);
    }

    public async Task<(int tokIn, int tokOut)> CorregirAbiertasAsync(
        IReadOnlyList<(PreguntaExamen p, RespuestaUsuario r)> abiertas, string modelo, CancellationToken ct)
    {
        if (abiertas.Count == 0) return (0, 0);

        var sb = new StringBuilder();
        sb.AppendLine("Corregí estas respuestas abiertas. Para cada una devolvé puntos (0..máximo), feedback y si es ambigua.");
        sb.AppendLine("Devolvé SOLO JSON: {\"resultados\":[{\"puntos\":<n>,\"feedback\":\"...\",\"ambigua\":true|false}]} en el MISMO orden.");
        int n = 1;
        foreach (var (p, r) in abiertas)
        {
            sb.AppendLine($"--- Pregunta {n++} (máximo {p.Puntos} puntos) ---");
            sb.AppendLine($"Enunciado: {p.Enunciado}");
            sb.AppendLine($"Criterios/guía: {p.DatosJson}");
            sb.AppendLine($"Respuesta del alumno: {r.RespuestaJson}");
        }

        var sys = "Sos un evaluador de exámenes justo y conciso. Penalizá lo incorrecto y reconocé lo correcto. " +
                  "Marcá 'ambigua' si la respuesta es interpretable de varias formas.";
        var resp = await ia.CompletarAsync(new SolicitudIA(sys, sb.ToString(), 0.3, 8000, "examen-corr-v1", modelo), ct);

        var s = resp.Texto.Trim();
        int i = s.IndexOf('{'), j = s.LastIndexOf('}');
        if (i >= 0 && j > i) s = s[i..(j + 1)];
        using var doc = JsonDocument.Parse(s);
        var arr = doc.RootElement.GetProperty("resultados");
        for (int k = 0; k < abiertas.Count && k < arr.GetArrayLength(); k++)
        {
            var (p, r) = abiertas[k];
            var res = arr[k];
            var pts = res.TryGetProperty("puntos", out var pp) ? pp.GetDouble() : 0;
            r.PuntosObtenidos = Math.Clamp(pts, 0, p.Puntos);
            r.FeedbackIa = res.TryGetProperty("feedback", out var fb) ? fb.GetString() : null;
            r.Ambigua = res.TryGetProperty("ambigua", out var am) && am.GetBoolean();
            r.Correcta = r.PuntosObtenidos >= p.Puntos; // correcta si puntaje completo
        }
        return (resp.TokensPrompt, resp.TokensCompletion);
    }

    public ResultadoCorreccion CalcularResultado(
        IReadOnlyList<(PreguntaExamen p, RespuestaUsuario r)> todo, double escalaMax, double notaAprobacion)
    {
        double total = todo.Sum(x => x.p.Puntos);
        double obtenido = todo.Sum(x => x.r.PuntosObtenidos);
        double pct = total <= 0 ? 0 : Math.Round(obtenido / total * 100, 2);
        double nota = Math.Round(pct / 100 * escalaMax, 2);
        bool aprobado = nota >= notaAprobacion;
        int correctas = todo.Count(x => x.r.Correcta == true);
        var fb = $"Acertaste {correctas} de {todo.Count} preguntas ({pct}%).";
        return new ResultadoCorreccion(nota, pct, aprobado, fb, 0, 0);
    }

    // ── helpers de parseo de RespuestaJson ──
    private static HashSet<int> ParseIndices(string? resp)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(resp)) return set;
        try { foreach (var e in JsonDocument.Parse(resp).RootElement.EnumerateArray()) set.Add(e.GetInt32()); }
        catch { /* respuesta vacía o inválida ⇒ conjunto vacío */ }
        return set;
    }
    private static List<string> ParseStrings(string? resp)
    {
        var lista = new List<string>();
        if (string.IsNullOrWhiteSpace(resp)) return lista;
        try { foreach (var e in JsonDocument.Parse(resp).RootElement.EnumerateArray()) lista.Add(e.GetString() ?? ""); }
        catch { }
        return lista;
    }
    private static HashSet<(int,int)> ParsePares(string? resp)
    {
        var set = new HashSet<(int,int)>();
        if (string.IsNullOrWhiteSpace(resp)) return set;
        try { foreach (var par in JsonDocument.Parse(resp).RootElement.EnumerateArray()) set.Add((par[0].GetInt32(), par[1].GetInt32())); }
        catch { }
        return set;
    }
    private static string Norm(string? s) => (s ?? "").Trim().ToLowerInvariant();
}
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter CorrectorExamenTests`
Expected: PASS (3 tests).

- [ ] **Step 6: Commit**

```bash
git add src/Resumenes.Core/Interfaces/ICorrectorExamen.cs \
        src/Resumenes.Infrastructure/Examenes/CorrectorExamen.cs \
        tests/Resumenes.Tests/CorrectorExamenTests.cs
git commit -m "feat(examenes): corrector (objetivo local + abierto IA + nota)"
```

---

## Task 4: `ServicioExamenes` (orquestador) + configuración + DI

**Files:**
- Create: `src/Resumenes.Core/Interfaces/IServicioExamenes.cs`
- Create: `src/Resumenes.Infrastructure/Examenes/ServicioExamenes.cs`
- Modify: `src/Resumenes.Infrastructure/Aplicacion/Configuracion.cs`
- Modify: `src/Resumenes.Ui/App.xaml.cs`, `src/Resumenes.Cli/Program.cs`
- Test: `tests/Resumenes.Tests/ServicioExamenesTests.cs`

**Interfaces:**
- Consumes: `IRepositorioExamenes`, `IGeneradorExamen`, `ICorrectorExamen` (tasks 1-3); `IRepositorioEstado`; `Configuracion`; `IClienteIA`.
- Produces:
  - `Configuracion.EscalaNotaMaxima` (double, default 10), `Configuracion.NotaAprobacion` (double, default 6)
  - `interface IServicioExamenes { Task<Examen> CrearAsync(string analisisId, string titulo, ConfigExamen cfg, CancellationToken ct); Task<Examen> FinalizarYCorregirAsync(string examenId, CancellationToken ct); IReadOnlyList<Examen> Historial(string analisisId); }`
  - `ServicioExamenes(IRepositorioEstado estado, IRepositorioExamenes repo, IGeneradorExamen generador, ICorrectorExamen corrector, Configuracion cfg, IRelojUtc reloj)`

- [ ] **Step 1: Escribir el test de integración que falla**

Crear `tests/Resumenes.Tests/ServicioExamenesTests.cs`. Crea un análisis con archivos de resumen en disco, genera un examen (FakeClienteIA con JSON de preguntas), simula respuestas y finaliza, verificando que se persiste el resultado.

```csharp
using Resumenes.Core.Modelos;
using Resumenes.Infrastructure.Aplicacion;
using Resumenes.Infrastructure.Examenes;
using Resumenes.Tests.Fakes;
using Xunit;

namespace Resumenes.Tests;

public class ServicioExamenesTests : IDisposable
{
    private readonly string _ws = Path.Combine(Path.GetTempPath(), $"resu-svcex-{Guid.NewGuid():N}");

    private (ServicioExamenes svc, RepositorioExamenesEnMemoria repo, FakeClienteIA ia) Armar()
    {
        // Resúmenes en disco: <ws>/analisis/an1/resumen/tema1.txt
        var dir = Path.Combine(_ws, "analisis", "an1", "resumen");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "tema1.txt"), "La fotosíntesis convierte luz en energía.");

        var estado = new RepositorioEnMemoria();
        var repoEx = new RepositorioExamenesEnMemoria();
        var ia = new FakeClienteIA();
        var cfg = new Configuracion { RutaWorkspace = _ws, EscalaNotaMaxima = 10, NotaAprobacion = 6 };
        var svc = new ServicioExamenes(estado, repoEx,
            new GeneradorExamen(ia), new CorrectorExamen(ia), cfg, new RelojFijo());
        return (svc, repoEx, ia);
    }

    private static ConfigExamen Cfg() => new(
        new[] { new CantidadPorTipo(TipoPregunta.McUna, 1) }, Array.Empty<string>(), "media", 1, 30, "rapido");

    [Fact]
    public async Task Crear_Responder_Finalizar_PersisteResultado()
    {
        var (svc, repo, ia) = Armar();
        ia.Responder = req => req.PromptSystem.Contains("generador")
            ? "{\"preguntas\":[{\"tipo\":\"McUna\",\"enunciado\":\"?\",\"puntos\":1,\"datos\":{\"opciones\":[{\"texto\":\"A\",\"correcta\":true},{\"texto\":\"B\",\"correcta\":false}]}}]}"
            : "{\"resultados\":[]}";

        var examen = await svc.CrearAsync("an1", "Parcial 1", Cfg(), default);
        var preguntas = repo.ListarPreguntas(examen.Id);
        Assert.Single(preguntas);

        // El alumno responde la opción correcta (índice 0)
        repo.GuardarRespuesta(new RespuestaUsuario {
            Id = Guid.NewGuid().ToString("N"), ExamenId = examen.Id, PreguntaId = preguntas[0].Id, RespuestaJson = "0" });

        var corregido = await svc.FinalizarYCorregirAsync(examen.Id, default);
        Assert.Equal(EstadoExamen.Corregido, corregido.Estado);
        Assert.Equal(100, corregido.Porcentaje);
        Assert.Equal(10, corregido.Nota);
        Assert.True(corregido.Aprobado);
    }

    public void Dispose() { if (Directory.Exists(_ws)) Directory.Delete(_ws, true); }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter Crear_Responder_Finalizar_PersisteResultado`
Expected: FAIL de compilación.

- [ ] **Step 3: Agregar configuración de nota**

En `src/Resumenes.Infrastructure/Aplicacion/Configuracion.cs`, agregar:
```csharp
    /// <summary>Escala máxima de la nota (default 10 ⇒ 0–10).</summary>
    public double EscalaNotaMaxima { get; set; } = 10;
    /// <summary>Nota mínima para aprobar (en la escala configurada).</summary>
    public double NotaAprobacion { get; set; } = 6;
```

- [ ] **Step 4: Crear la interfaz**

Crear `src/Resumenes.Core/Interfaces/IServicioExamenes.cs`:
```csharp
using Resumenes.Core.Modelos;

namespace Resumenes.Core.Interfaces;

public interface IServicioExamenes
{
    Task<Examen> CrearAsync(string analisisId, string titulo, ConfigExamen cfg, CancellationToken ct);
    Task<Examen> FinalizarYCorregirAsync(string examenId, CancellationToken ct);
    IReadOnlyList<Examen> Historial(string analisisId);
}
```

- [ ] **Step 5: Implementar `ServicioExamenes`**

Crear `src/Resumenes.Infrastructure/Examenes/ServicioExamenes.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Resumenes.Core.Apoyos;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;

namespace Resumenes.Infrastructure.Examenes;

public class ServicioExamenes(
    IRepositorioEstado estado, IRepositorioExamenes repo, IGeneradorExamen generador,
    ICorrectorExamen corrector, Configuracion cfg, IRelojUtc reloj) : IServicioExamenes
{
    private static readonly TipoPregunta[] Abiertos =
        { TipoPregunta.Desarrollo, TipoPregunta.DesarrolloItems, TipoPregunta.VfJustificado };

    public async Task<Examen> CrearAsync(string analisisId, string titulo, ConfigExamen cfgExamen, CancellationToken ct)
    {
        var contenido = EnsamblarContenido(analisisId, cfgExamen);
        if (string.IsNullOrWhiteSpace(contenido))
            throw new InvalidOperationException("No hay contenido para generar el examen (procesá el análisis primero).");

        var examenId = Guid.NewGuid().ToString("N");
        var r = await generador.GenerarAsync(examenId, contenido, cfgExamen, cfg.Modelo, ct);

        var examen = new Examen {
            Id = examenId, AnalisisId = analisisId, Titulo = titulo,
            ConfigJson = JsonSerializer.Serialize(cfgExamen), Estado = EstadoExamen.EnCurso,
            Tokens = r.TokensEntrada + r.TokensSalida,
            CostoEstimado = Costo(r.TokensEntrada, r.TokensSalida),
            CreadoEn = reloj.Ahora(), IniciadoEn = reloj.Ahora() };
        repo.GuardarExamen(examen);
        foreach (var p in r.Preguntas) repo.GuardarPregunta(p);
        return examen;
    }

    public async Task<Examen> FinalizarYCorregirAsync(string examenId, CancellationToken ct)
    {
        var examen = repo.ObtenerExamen(examenId) ?? throw new InvalidOperationException("Examen no encontrado.");
        var preguntas = repo.ListarPreguntas(examenId);
        var respuestas = repo.ListarRespuestas(examenId).ToDictionary(x => x.PreguntaId);

        var pares = new List<(PreguntaExamen p, RespuestaUsuario r)>();
        foreach (var p in preguntas)
        {
            var r = respuestas.TryGetValue(p.Id, out var ru) ? ru
                : new RespuestaUsuario { Id = Guid.NewGuid().ToString("N"), ExamenId = examenId, PreguntaId = p.Id };
            pares.Add((p, r));
        }

        // Objetivo local
        foreach (var (p, r) in pares.Where(x => !Abiertos.Contains(x.p.Tipo)))
            corrector.CorregirObjetivo(p, r);

        // Abierto con IA
        var abiertas = pares.Where(x => Abiertos.Contains(x.p.Tipo)).ToList();
        var (tokIn, tokOut) = await corrector.CorregirAbiertasAsync(abiertas, cfg.Modelo, ct);

        var res = corrector.CalcularResultado(pares, cfg.EscalaNotaMaxima, cfg.NotaAprobacion);

        foreach (var (_, r) in pares) repo.GuardarRespuesta(r);

        examen.Estado = EstadoExamen.Corregido;
        examen.Nota = res.Nota; examen.Porcentaje = res.Porcentaje; examen.Aprobado = res.Aprobado;
        examen.FeedbackGeneral = res.FeedbackGeneral;
        examen.Tokens += tokIn + tokOut;
        examen.CostoEstimado += Costo(tokIn, tokOut);
        examen.FinalizadoEn = reloj.Ahora();
        repo.GuardarExamen(examen);
        return examen;
    }

    public IReadOnlyList<Examen> Historial(string analisisId) => repo.ListarExamenes(analisisId);

    // Lee los .txt de resumen/ (rápido) o consolidado/ (completo); filtra por TemasIncluidos (nombres sin extensión).
    private string EnsamblarContenido(string analisisId, ConfigExamen cfgExamen)
    {
        var sub = cfgExamen.Fuente == "completo" ? "consolidado" : "resumen";
        var dir = Path.Combine(cfg.RutaWorkspace, "analisis", analisisId, sub);
        if (!Directory.Exists(dir)) return "";

        var incluir = cfgExamen.TemasIncluidos.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sb = new StringBuilder();
        foreach (var f in Directory.GetFiles(dir, "*.txt").OrderBy(x => x))
        {
            var nombre = Path.GetFileNameWithoutExtension(f);
            if (incluir.Count > 0 && !incluir.Contains(nombre)) continue;
            sb.AppendLine(File.ReadAllText(f));
            sb.AppendLine();
        }
        return sb.ToString().Trim();
    }

    private double Costo(int tokIn, int tokOut)
        => (double)((tokIn * cfg.PrecioInputPorMillonUsd + tokOut * cfg.PrecioOutputPorMillonUsd) / 1_000_000m);
}
```

- [ ] **Step 6: Registrar en DI (UI y Cli)**

En `src/Resumenes.Ui/App.xaml.cs`, registrar los servicios del simulador (cerca de los demás `AddSingleton`):
```csharp
        sc.AddSingleton<SqliteRepositorioExamenes>(_ => new SqliteRepositorioExamenes($"Data Source={dbPath}"));
        sc.AddSingleton<Resumenes.Core.Interfaces.IRepositorioExamenes>(sp => sp.GetRequiredService<SqliteRepositorioExamenes>());
        sc.AddSingleton<Resumenes.Core.Interfaces.IGeneradorExamen>(sp => new GeneradorExamen(sp.GetRequiredService<IClienteIA>()));
        sc.AddSingleton<Resumenes.Core.Interfaces.ICorrectorExamen>(sp => new CorrectorExamen(sp.GetRequiredService<IClienteIA>()));
        sc.AddSingleton<Resumenes.Core.Interfaces.IServicioExamenes>(sp => new ServicioExamenes(
            sp.GetRequiredService<IRepositorioEstado>(),
            sp.GetRequiredService<Resumenes.Core.Interfaces.IRepositorioExamenes>(),
            sp.GetRequiredService<Resumenes.Core.Interfaces.IGeneradorExamen>(),
            sp.GetRequiredService<Resumenes.Core.Interfaces.ICorrectorExamen>(),
            sp.GetRequiredService<Configuracion>(),
            sp.GetRequiredService<IRelojUtc>()));
```
(`dbPath` es la misma variable usada para `SqliteRepositorioEstado` en `App.xaml.cs`. Agregar los `using Resumenes.Infrastructure.Examenes; using Resumenes.Infrastructure.Persistencia;` si faltan.)

En `src/Resumenes.Cli/Program.cs`: el Cli no usa el simulador (es UI), así que NO es necesario registrarlo. Solo verificar que compila (la solución entera). Si el Cli tuviera un contenedor que exija todas las interfaces, omitir; de lo contrario no tocar.

- [ ] **Step 7: Correr el test de integración y la suite**

Run: `dotnet test tests/Resumenes.Tests -c Debug --filter Crear_Responder_Finalizar_PersisteResultado`
Expected: PASS.

Run: `dotnet test -c Debug`
Expected: toda la suite verde.

- [ ] **Step 8: Compilar la solución**

Run: `dotnet build Resumenes.sln -c Debug`
Expected: BUILD succeeded.

- [ ] **Step 9: Commit**

```bash
git add src/Resumenes.Core/Interfaces/IServicioExamenes.cs \
        src/Resumenes.Infrastructure/Examenes/ServicioExamenes.cs \
        src/Resumenes.Infrastructure/Aplicacion/Configuracion.cs \
        src/Resumenes.Ui/App.xaml.cs src/Resumenes.Cli/Program.cs \
        tests/Resumenes.Tests/ServicioExamenesTests.cs
git commit -m "feat(examenes): servicio orquestador (crear/corregir/historial) + config nota + DI"
```

---

## Self-Review (cobertura, Fase 5a)

- **Modelo de datos (3 tablas):** Task 1. ✅
- **Generación con IA (7 tipos, contrato JSON, reintento):** Task 2. ✅
- **Corrección objetivo local (MC/VF/Completar/Emparejar):** Task 3. ✅
- **Corrección abierta con IA + feedback + ambigüedad:** Task 3. ✅
- **Nota configurable (escala + %) + aprobación:** Task 3 (`CalcularResultado`) + Task 4 (config). ✅
- **Fuente rápido/completo + temas incluidos:** Task 4 (`EnsamblarContenido`). ✅
- **Historial:** Task 4 + Task 1 (`ListarExamenes`). ✅
- **Tokens/costo del examen:** Task 4 (acumula generación + corrección). ✅
- **Migración no destructiva (`schema_version`→6):** Task 1. ✅

## Notas de diseño / pendientes para 5b

- **Contrato de `RespuestaJson` por tipo** (lo consume la UI en 5b): McUna = índice (número); McVarias = array de índices; VfJustificado = `{"vf":true|false,"justificacion":"..."}` (la parte V/F se corrige local leyendo `true`/`false`; la justificación va al corrector IA — 5b debe enviar ambos); Desarrollo/DesarrolloItems = string/array de strings; Completar = array de strings; Emparejar = array de pares `[i,j]`.
- **Prompts del examen fijos** en esta entrega (ver "Decisión a confirmar"); integrarlos a "Prompts (avanzado)" es mejora para 5b/backlog.
- **Autoguardado/timer/reanudar** son responsabilidad de la UI (5b); el motor ya soporta estados `EnCurso`/`Corregido` y respuestas persistidas para reanudar.
- **VfJustificado** se trata como abierto (va al corrector IA) para evaluar la justificación; el corrector IA recibe la pregunta y debe puntuar V/F + justificación juntos. (En 5b, el `RespuestaJson` incluirá la elección V/F y la justificación; afinar el prompt si hiciera falta.)
