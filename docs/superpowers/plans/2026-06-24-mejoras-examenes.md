# Mejoras al simulador de exámenes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que al terminar un examen el alumno vea la respuesta correcta de cada pregunta y reciba una devolución de IA con sus temas flojos; corrección en 3 niveles; "marcar para revisar" funcional con mini-mapa; y emparejar sin opciones duplicadas.

**Architecture:** Lógica pura (respuesta legible por tipo, estado por umbral) en `Resumenes.Core`; generación/corrección/devolución (IA) en `Resumenes.Infrastructure`; persistencia SQLite; UI MVVM (WPF-UI) en `Resumenes.Ui`. Cada fase es independiente y testeable.

**Tech Stack:** .NET 9, CommunityToolkit.Mvvm 8.4.2, WPF-UI 4.3.0, Microsoft.Data.Sqlite, IA Deepseek (vía `IClienteIA`), xUnit.

## Global Constraints

- **TFM:** `Resumenes.Core`/`Resumenes.Infrastructure` = `net9.0`; `Resumenes.Ui` = `net9.0-windows`. `Nullable=enable`, `ImplicitUsings=enable`.
- **Identidad git del repo:** identidad **local** `emmavzmymtec / emmavzmymtec@gmail.com`. Verificá `git config --get user.email` antes de commitear. No tocar la global.
- **Rama:** trabajar en una rama de feature (NO en main directo). Trabajar desde `D:\Desarrollo\Programacion\Resumenes`.
- **Umbrales de estado:** `obtenido/puntos ≥ 0.85` → Correcta; `≥ 0.40` → Parcial; resto → Incorrecta. Valores exactos.
- **Tests:** Core/Infrastructure → `tests/Resumenes.Tests`. Ui → `tests/Resumenes.Ui.Tests`. xUnit, `<Using Include="Xunit" />` global.
- **Fakes de IA en tests:** respetá la firma real de `IClienteIA` / `SolicitudIA` / `RespuestaIA`. Ya hay tests de exámenes en `tests/Resumenes.Tests` que fakean la IA (generador/corrector) — copiá ese patrón exacto (ctor de `RespuestaIA`, parámetros de `SolicitudIA`) en vez de asumirlo. Lo mismo para `IRepositorioExamenes`/`IServicioExamenes` fakes en Ui.Tests.
- **DatosJson por tipo** (lo que produce el GeneradorExamen, ya en uso):
  - `McUna`/`McVarias`: `{"opciones":[{"texto":"...","correcta":true|false}]}`
  - `VfJustificado`: `{"afirmacion":"...","esVerdadero":true|false}` (+ se agrega `"justificacion":"..."`)
  - `Desarrollo`: `{"criterios":"..."}` (+ se agrega `"respuestaEsperada":"..."`)
  - `DesarrolloItems`: `{"items":[{"enunciado":"...","criterios":"..."}]}` (+ cada ítem `"respuestaEsperada":"..."`)
  - `Completar`: `{"texto":"frase con ___","respuestas":["..."]}`
  - `Emparejar`: `{"izquierda":["..."],"derecha":["..."],"pares":[[i,j]]}`
- **RespuestaJson por tipo** (lo que produce `PreguntaRendirVm.ConstruirRespuestaJson`):
  - McUna: índice como string (o `"null"`). McVarias: array de índices. Completar: array de strings.
  - Emparejar: array de pares `[i,j]`. DesarrolloItems: array de strings. VfJustificado: `{"vf":bool,"justificacion":"..."}`. Desarrollo: string (el texto).

---

## File Structure

| Archivo | Cambio | Fase |
|---|---|---|
| `src/Resumenes.Core/Modelos/Examenes.cs` | `enum EstadoRespuesta`; `RespuestaUsuario.MarcadaRevisar` | B, D |
| `src/Resumenes.Core/Examenes/EvaluadorRespuesta.cs` (nuevo) | estado por umbral (puro) | B |
| `src/Resumenes.Core/Examenes/DescriptorRespuestas.cs` (nuevo) | respuesta legible (usuario/correcta) por tipo (puro) | C |
| `src/Resumenes.Core/Interfaces/ICorrectorExamen.cs` | `GenerarDevolucionAsync` | B |
| `src/Resumenes.Infrastructure/Examenes/GeneradorExamen.cs` | prompt: respuesta esperada en abiertas | A |
| `src/Resumenes.Infrastructure/Examenes/CorrectorExamen.cs` | `GenerarDevolucionAsync` + prompt de corrección más justo | B |
| `src/Resumenes.Infrastructure/Examenes/ServicioExamenes.cs` | invocar la devolución al corregir | B |
| `src/Resumenes.Infrastructure/schema.sql` | columna `marcada_revisar` en `RespuestaUsuario` | D |
| `src/Resumenes.Infrastructure/Persistencia/SqliteRepositorioEstado.cs` | `AsegurarColumna` para `marcada_revisar` | D |
| `src/Resumenes.Infrastructure/Persistencia/SqliteRepositorioExamenes.cs` | guardar/leer `marcada_revisar` | D |
| `src/Resumenes.Ui/ViewModels/ResultadoExamenVm.cs` | `ItemResultadoVm`: respuestas legibles + estado | C |
| `src/Resumenes.Ui/Vistas/VistaResultadoExamen.xaml` | mostrar tu respuesta + correcta + chip de estado | C |
| `src/Resumenes.Ui/ViewModels/RendirExamenVm.cs` | persistir/restaurar marcado + mini-mapa + saltar | D |
| `src/Resumenes.Ui/ViewModels/PreguntaRendirVm.cs` | `Respondida`; emparejar sin duplicados | D, E |
| `src/Resumenes.Ui/Vistas/VistaRendirExamen.xaml` | mini-mapa de dots; combos de emparejar | D, E |

---

## Task 1 (Fase A): Generación con respuesta esperada en abiertas

**Files:**
- Modify: `src/Resumenes.Infrastructure/Examenes/GeneradorExamen.cs` (constante `FormatoJson`)
- Test: `tests/Resumenes.Tests/GeneradorExamenFormatoTests.cs`

**Interfaces:**
- Produces: el `DatosJson` de `Desarrollo` incluye `respuestaEsperada`; cada ítem de `DesarrolloItems` incluye `respuestaEsperada`; `VfJustificado` incluye `justificacion`. (El parseo NO cambia: `GeneradorExamen.Parsear` ya guarda `datos` raw, así que los campos extra se persisten solos.)

- [ ] **Step 1: Escribir el test que falla**

`tests/Resumenes.Tests/GeneradorExamenFormatoTests.cs`:

```csharp
using System.Text.Json;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;
using Resumenes.Infrastructure.Examenes;

namespace Resumenes.Tests;

public class GeneradorExamenFormatoTests
{
    // IA fake que devuelve un examen con una pregunta de Desarrollo que incluye respuestaEsperada.
    private sealed class IaFake : IClienteIA
    {
        public Task<RespuestaIA> CompletarAsync(SolicitudIA s, CancellationToken ct)
            => Task.FromResult(new RespuestaIA(
                "{\"preguntas\":[{\"tipo\":\"Desarrollo\",\"enunciado\":\"Explicá X\",\"puntos\":10," +
                "\"datos\":{\"criterios\":\"mencionar A y B\",\"respuestaEsperada\":\"A y B en breve\"}}]}",
                10, 20));
    }

    [Fact]
    public void FormatoJson_PideRespuestaEsperadaYJustificacion()
    {
        // El system prompt (constante) debe instruir a la IA a devolver estos campos.
        var fmt = GeneradorExamen.FormatoJson;
        Assert.Contains("respuestaEsperada", fmt);
        Assert.Contains("justificacion", fmt);
    }

    [Fact]
    public async Task Generar_PreservaRespuestaEsperadaEnDatosJson()
    {
        var gen = new GeneradorExamen(new IaFake());
        var cfg = new ConfigExamen(new[] { new CantidadPorTipo(TipoPregunta.Desarrollo, 1) },
            Array.Empty<string>(), "media", 10, 0, "rapido");

        var r = await gen.GenerarAsync("ex1", "contenido", cfg, "modelo", default);

        var datos = JsonDocument.Parse(r.Preguntas[0].DatosJson).RootElement;
        Assert.Equal("A y B en breve", datos.GetProperty("respuestaEsperada").GetString());
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/Resumenes.Tests --filter GeneradorExamenFormatoTests`
Expected: FAIL (`FormatoJson` es `private`; el prompt no menciona `respuestaEsperada`/`justificacion`).

- [ ] **Step 3: Implementar**

En `GeneradorExamen.cs`: (a) hacer `public` la constante `FormatoJson` (para el test), y (b) ampliar las líneas de `Desarrollo`, `DesarrolloItems` y `VfJustificado` del formato. Reemplazá la constante por:

```csharp
    public const string FormatoJson =
        "Devolvé SOLO un JSON: {\"preguntas\":[{\"tipo\":\"<Tipo>\",\"enunciado\":\"...\",\"puntos\":<n>,\"datos\":{...}}]}. " +
        "Tipos válidos y su 'datos': " +
        "McUna/McVarias → {\"opciones\":[{\"texto\":\"...\",\"correcta\":true|false}]}; " +
        "VfJustificado → {\"afirmacion\":\"...\",\"esVerdadero\":true|false,\"justificacion\":\"por qué es verdadero o falso, breve\"}; " +
        "Desarrollo → {\"criterios\":\"qué debe contener una buena respuesta\",\"respuestaEsperada\":\"respuesta modelo breve (2-4 frases)\"}; " +
        "DesarrolloItems → {\"items\":[{\"enunciado\":\"...\",\"criterios\":\"...\",\"respuestaEsperada\":\"respuesta breve del ítem\"}]}; " +
        "Completar → {\"texto\":\"frase con ___\",\"respuestas\":[\"...\"]}; " +
        "Emparejar → {\"izquierda\":[\"...\"],\"derecha\":[\"...\"],\"pares\":[[0,1]]}. " +
        "Sin texto fuera del JSON. Respetá el idioma del contenido.";
```

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test tests/Resumenes.Tests --filter GeneradorExamenFormatoTests`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Resumenes.Infrastructure/Examenes/GeneradorExamen.cs tests/Resumenes.Tests/GeneradorExamenFormatoTests.cs
git commit -m "feat(examenes): generar respuesta esperada/justificacion en preguntas abiertas"
```

---

## Task 2 (Fase B): EstadoRespuesta por umbral (Core)

**Files:**
- Modify: `src/Resumenes.Core/Modelos/Examenes.cs` (agregar `enum EstadoRespuesta`)
- Create: `src/Resumenes.Core/Examenes/EvaluadorRespuesta.cs`
- Test: `tests/Resumenes.Tests/EvaluadorRespuestaTests.cs`

**Interfaces:**
- Produces: `enum EstadoRespuesta { Correcta, Parcial, Incorrecta }`; `static class EvaluadorRespuesta { EstadoRespuesta Estado(double obtenido, double puntos); }`.

- [ ] **Step 1: Escribir el test que falla**

`tests/Resumenes.Tests/EvaluadorRespuestaTests.cs`:

```csharp
using Resumenes.Core.Examenes;
using Resumenes.Core.Modelos;

namespace Resumenes.Tests;

public class EvaluadorRespuestaTests
{
    [Theory]
    [InlineData(10, 10, EstadoRespuesta.Correcta)]   // 100%
    [InlineData(8.5, 10, EstadoRespuesta.Correcta)]  // 85% exacto
    [InlineData(8.4, 10, EstadoRespuesta.Parcial)]   // 84%
    [InlineData(4, 10, EstadoRespuesta.Parcial)]     // 40% exacto
    [InlineData(3.9, 10, EstadoRespuesta.Incorrecta)]// 39%
    [InlineData(0, 10, EstadoRespuesta.Incorrecta)]  // 0%
    public void Estado_SegunUmbral(double obtenido, double puntos, EstadoRespuesta esperado)
        => Assert.Equal(esperado, EvaluadorRespuesta.Estado(obtenido, puntos));

    [Fact]
    public void Estado_PuntosCero_EsIncorrecta()
        => Assert.Equal(EstadoRespuesta.Incorrecta, EvaluadorRespuesta.Estado(0, 0));
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/Resumenes.Tests --filter EvaluadorRespuestaTests`
Expected: FAIL (`EvaluadorRespuesta`/`EstadoRespuesta` no existen).

- [ ] **Step 3: Implementar**

En `src/Resumenes.Core/Modelos/Examenes.cs`, agregar junto a los otros enums:

```csharp
public enum EstadoRespuesta { Correcta, Parcial, Incorrecta }
```

`src/Resumenes.Core/Examenes/EvaluadorRespuesta.cs`:

```csharp
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
```

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test tests/Resumenes.Tests --filter EvaluadorRespuestaTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/Resumenes.Core/Modelos/Examenes.cs src/Resumenes.Core/Examenes/EvaluadorRespuesta.cs tests/Resumenes.Tests/EvaluadorRespuestaTests.cs
git commit -m "feat(examenes): EstadoRespuesta por umbral (85/40)"
```

---

## Task 3 (Fase B): Devolución de IA + corrección más justa

**Files:**
- Modify: `src/Resumenes.Core/Interfaces/ICorrectorExamen.cs` (nuevo método)
- Modify: `src/Resumenes.Infrastructure/Examenes/CorrectorExamen.cs` (implementación + prompt)
- Modify: `src/Resumenes.Infrastructure/Examenes/ServicioExamenes.cs` (invocar)
- Test: `tests/Resumenes.Tests/DevolucionIaTests.cs`

**Interfaces:**
- Consumes: `PreguntaExamen`, `RespuestaUsuario`, `IClienteIA`.
- Produces: en `ICorrectorExamen`: `Task<(string texto, int tokIn, int tokOut)> GenerarDevolucionAsync(IReadOnlyList<(PreguntaExamen p, RespuestaUsuario r)> todo, double pct, string modelo, CancellationToken ct)`.

- [ ] **Step 1: Escribir el test que falla**

`tests/Resumenes.Tests/DevolucionIaTests.cs`:

```csharp
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;
using Resumenes.Infrastructure.Examenes;

namespace Resumenes.Tests;

public class DevolucionIaTests
{
    private sealed class IaFake(string texto) : IClienteIA
    {
        public string? UltimoUsuario;
        public Task<RespuestaIA> CompletarAsync(SolicitudIA s, CancellationToken ct)
        { UltimoUsuario = s.Usuario; return Task.FromResult(new RespuestaIA(texto, 5, 7)); }
    }

    private static (PreguntaExamen, RespuestaUsuario) Par(string enun, double pts, double obt)
        => (new PreguntaExamen { Id = "p", ExamenId = "e", Enunciado = enun, Puntos = pts, Tipo = TipoPregunta.Desarrollo },
            new RespuestaUsuario { Id = "r", ExamenId = "e", PreguntaId = "p", PuntosObtenidos = obt });

    [Fact]
    public async Task GenerarDevolucion_DevuelveTextoDeLaIa_YTokens()
    {
        var ia = new IaFake("Muy bien; reforzá los aranceles.");
        var sut = new CorrectorExamen(ia);
        var (txt, tin, tout) = await sut.GenerarDevolucionAsync(
            new[] { Par("Aranceles", 10, 4) }, 40, "modelo", default);

        Assert.Equal("Muy bien; reforzá los aranceles.", txt);
        Assert.Equal(5, tin);
        Assert.Equal(7, tout);
        Assert.Contains("Aranceles", ia.UltimoUsuario!); // el enunciado viaja a la IA
    }

    [Fact]
    public async Task GenerarDevolucion_SiLaIaFalla_DevuelveRespaldo_SinLanzar()
    {
        var sut = new CorrectorExamen(new IaQueLanza());
        var (txt, tin, tout) = await sut.GenerarDevolucionAsync(
            new[] { Par("X", 10, 10) }, 100, "modelo", default);

        Assert.False(string.IsNullOrWhiteSpace(txt)); // texto de respaldo
        Assert.Equal(0, tin);
        Assert.Equal(0, tout);
    }

    private sealed class IaQueLanza : IClienteIA
    {
        public Task<RespuestaIA> CompletarAsync(SolicitudIA s, CancellationToken ct)
            => throw new HttpRequestException("sin red");
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/Resumenes.Tests --filter DevolucionIaTests`
Expected: FAIL (`GenerarDevolucionAsync` no existe).

- [ ] **Step 3: Implementar**

En `ICorrectorExamen.cs`, agregar a la interfaz:

```csharp
    /// <summary>Genera una devolución breve y motivadora con IA a partir del desempeño. Best-effort: ante error devuelve un texto de respaldo y (0,0) tokens.</summary>
    Task<(string texto, int tokIn, int tokOut)> GenerarDevolucionAsync(
        IReadOnlyList<(PreguntaExamen p, RespuestaUsuario r)> todo, double pct, string modelo, CancellationToken ct);
```

En `CorrectorExamen.cs`, agregar el método (usa `StringBuilder` y `EvaluadorRespuesta`, ya hay `using System.Text;`):

```csharp
    public async Task<(string texto, int tokIn, int tokOut)> GenerarDevolucionAsync(
        IReadOnlyList<(PreguntaExamen p, RespuestaUsuario r)> todo, double pct, string modelo, CancellationToken ct)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"El alumno hizo un examen y sacó {pct:0}% de acierto. Detalle por pregunta:");
            foreach (var (p, r) in todo)
            {
                var estado = Resumenes.Core.Examenes.EvaluadorRespuesta.Estado(r.PuntosObtenidos, p.Puntos);
                sb.AppendLine($"- [{estado}] {p.Enunciado}");
            }
            sb.AppendLine("Escribí una devolución breve (2-4 frases), en segunda persona, motivadora: " +
                          "reconocé lo que hizo bien y señalá 1-2 temas o conceptos puntuales a profundizar. " +
                          "Inferí los temas del contenido de las preguntas. Sin markdown, solo texto.");

            var sys = "Sos un tutor cercano y honesto que da devoluciones útiles y alentadoras.";
            var resp = await ia.CompletarAsync(new SolicitudIA(sys, sb.ToString(), 0.5, 600, "examen-devol-v1", modelo), ct);
            var txt = resp.Texto.Trim();
            return (string.IsNullOrWhiteSpace(txt) ? Respaldo(pct) : txt, resp.TokensPrompt, resp.TokensCompletion);
        }
        catch
        {
            return (Respaldo(pct), 0, 0);
        }
    }

    private static string Respaldo(double pct) =>
        pct >= 60 ? $"¡Buen trabajo! Sacaste {pct:0}%. Repasá las preguntas que fallaste para afianzar."
                  : $"Sacaste {pct:0}%. No aflojes: repasá los temas de las preguntas que fallaste y volvé a intentar.";
```

En `CorrectorExamen.CorregirAbiertasAsync`, ajustar el `sys` para corrección más justa. Reemplazá la línea del `sys` por:

```csharp
        var sys = "Sos un evaluador de exámenes justo. Asigná puntaje PROPORCIONAL a lo correcto " +
                  "(no exijas perfección: una respuesta parcialmente buena merece parte de los puntos). " +
                  "Penalizá solo lo incorrecto y reconocé lo correcto. Marcá 'ambigua' si es interpretable de varias formas.";
```

En `ServicioExamenes.FinalizarYCorregirAsync`, después de `var res = corrector.CalcularResultado(...)` y antes de guardar respuestas, agregar la devolución (reemplaza `examen.FeedbackGeneral = res.FeedbackGeneral;`):

```csharp
        var (devolucion, dIn, dOut) = await corrector.GenerarDevolucionAsync(pares, res.Porcentaje, cfg.Modelo, ct);

        foreach (var (_, r) in pares) repo.GuardarRespuesta(r);

        examen.Estado = EstadoExamen.Corregido;
        examen.Nota = res.Nota; examen.Porcentaje = res.Porcentaje; examen.Aprobado = res.Aprobado;
        examen.FeedbackGeneral = devolucion;
        examen.Tokens += tokIn + tokOut + dIn + dOut;
        examen.CostoEstimado += Costo(tokIn + dIn, tokOut + dOut);
```

> (Quitá la línea vieja `examen.Tokens += tokIn + tokOut;` y `examen.CostoEstimado += Costo(tokIn, tokOut);` y `examen.FeedbackGeneral = res.FeedbackGeneral;` — reemplazadas arriba.)

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test tests/Resumenes.Tests --filter DevolucionIaTests` y luego `dotnet test tests/Resumenes.Tests`
Expected: PASS (la devolución + nada roto en el resto de la suite de Core).

- [ ] **Step 5: Commit**

```bash
git add src/Resumenes.Core/Interfaces/ICorrectorExamen.cs src/Resumenes.Infrastructure/Examenes/CorrectorExamen.cs src/Resumenes.Infrastructure/Examenes/ServicioExamenes.cs tests/Resumenes.Tests/DevolucionIaTests.cs
git commit -m "feat(examenes): devolucion de IA automatica al corregir + correccion mas justa"
```

---

## Task 4 (Fase C): DescriptorRespuestas (respuesta legible por tipo)

**Files:**
- Create: `src/Resumenes.Core/Examenes/DescriptorRespuestas.cs`
- Test: `tests/Resumenes.Tests/DescriptorRespuestasTests.cs`

**Interfaces:**
- Consumes: `PreguntaExamen`, `RespuestaUsuario` (su `DatosJson`/`RespuestaJson`).
- Produces: `static class DescriptorRespuestas { (string usuario, string correcta) Describir(PreguntaExamen p, string? respuestaJson); }`. Devuelve textos legibles; nunca lanza (ante JSON inválido devuelve `("", "")` o lo que pueda).

- [ ] **Step 1: Escribir los tests que fallan**

`tests/Resumenes.Tests/DescriptorRespuestasTests.cs`:

```csharp
using Resumenes.Core.Examenes;
using Resumenes.Core.Modelos;

namespace Resumenes.Tests;

public class DescriptorRespuestasTests
{
    private static PreguntaExamen P(TipoPregunta t, string datos)
        => new() { Id = "p", ExamenId = "e", Enunciado = "?", Puntos = 1, Tipo = t, DatosJson = datos };

    [Fact]
    public void McUna_MuestraOpcionElegidaYCorrecta()
    {
        var p = P(TipoPregunta.McUna, "{\"opciones\":[{\"texto\":\"Roma\",\"correcta\":false},{\"texto\":\"París\",\"correcta\":true}]}");
        var (u, c) = DescriptorRespuestas.Describir(p, "0"); // eligió Roma
        Assert.Equal("Roma", u);
        Assert.Equal("París", c);
    }

    [Fact]
    public void McVarias_MuestraVariasYCorrectas()
    {
        var p = P(TipoPregunta.McVarias, "{\"opciones\":[{\"texto\":\"A\",\"correcta\":true},{\"texto\":\"B\",\"correcta\":false},{\"texto\":\"C\",\"correcta\":true}]}");
        var (u, c) = DescriptorRespuestas.Describir(p, "[0,1]"); // eligió A y B
        Assert.Contains("A", u); Assert.Contains("B", u);
        Assert.Contains("A", c); Assert.Contains("C", c);
    }

    [Fact]
    public void Completar_MuestraDadasYEsperadas()
    {
        var p = P(TipoPregunta.Completar, "{\"texto\":\"__ y __\",\"respuestas\":[\"sol\",\"luna\"]}");
        var (u, c) = DescriptorRespuestas.Describir(p, "[\"sol\",\"X\"]");
        Assert.Contains("sol", u); Assert.Contains("X", u);
        Assert.Contains("sol", c); Assert.Contains("luna", c);
    }

    [Fact]
    public void Emparejar_MuestraParesLegibles()
    {
        var p = P(TipoPregunta.Emparejar, "{\"izquierda\":[\"Mitocondria\"],\"derecha\":[\"Respiración\",\"Fotosíntesis\"],\"pares\":[[0,0]]}");
        var (u, c) = DescriptorRespuestas.Describir(p, "[[0,1]]"); // emparejó mal
        Assert.Contains("Mitocondria", u); Assert.Contains("Fotosíntesis", u);
        Assert.Contains("Mitocondria", c); Assert.Contains("Respiración", c);
    }

    [Fact]
    public void VfJustificado_MuestraVfYJustificacionEsperada()
    {
        var p = P(TipoPregunta.VfJustificado, "{\"afirmacion\":\"La tierra es plana\",\"esVerdadero\":false,\"justificacion\":\"Es un esferoide\"}");
        var (u, c) = DescriptorRespuestas.Describir(p, "{\"vf\":true,\"justificacion\":\"creo que sí\"}");
        Assert.Contains("Verdadero", u);
        Assert.Contains("Falso", c);
        Assert.Contains("esferoide", c);
    }

    [Fact]
    public void Desarrollo_MuestraTextoYRespuestaEsperada()
    {
        var p = P(TipoPregunta.Desarrollo, "{\"criterios\":\"x\",\"respuestaEsperada\":\"Lo esperado es Y\"}");
        var (u, c) = DescriptorRespuestas.Describir(p, "\"mi respuesta\"");
        Assert.Equal("mi respuesta", u);
        Assert.Equal("Lo esperado es Y", c);
    }

    [Fact]
    public void DatosOResp_Invalidos_NoLanza()
    {
        var p = P(TipoPregunta.McUna, "no es json");
        var (u, c) = DescriptorRespuestas.Describir(p, "tampoco");
        Assert.NotNull(u); Assert.NotNull(c);
    }
}
```

- [ ] **Step 2: Correr y verificar que fallan**

Run: `dotnet test tests/Resumenes.Tests --filter DescriptorRespuestasTests`
Expected: FAIL (`DescriptorRespuestas` no existe).

- [ ] **Step 3: Implementar**

`src/Resumenes.Core/Examenes/DescriptorRespuestas.cs`:

```csharp
using System.Text.Json;
using Resumenes.Core.Modelos;

namespace Resumenes.Core.Examenes;

/// <summary>Arma, por tipo de pregunta, la respuesta del alumno y la correcta en texto legible.</summary>
public static class DescriptorRespuestas
{
    public static (string usuario, string correcta) Describir(PreguntaExamen p, string? respuestaJson)
    {
        try
        {
            using var datos = JsonDocument.Parse(p.DatosJson);
            var d = datos.RootElement;
            return p.Tipo switch
            {
                TipoPregunta.McUna => McUna(d, respuestaJson),
                TipoPregunta.McVarias => McVarias(d, respuestaJson),
                TipoPregunta.Completar => Completar(d, respuestaJson),
                TipoPregunta.Emparejar => Emparejar(d, respuestaJson),
                TipoPregunta.VfJustificado => Vf(d, respuestaJson),
                TipoPregunta.DesarrolloItems => DesarrolloItems(d, respuestaJson),
                _ => Desarrollo(d, respuestaJson), // Desarrollo
            };
        }
        catch
        {
            return (TextoPlano(respuestaJson), "");
        }
    }

    private static string[] Opciones(JsonElement d) =>
        d.GetProperty("opciones").EnumerateArray().Select(o => o.GetProperty("texto").GetString() ?? "").ToArray();

    private static (string, string) McUna(JsonElement d, string? resp)
    {
        var ops = Opciones(d);
        var correctas = d.GetProperty("opciones").EnumerateArray()
            .Where(o => o.GetProperty("correcta").GetBoolean())
            .Select(o => o.GetProperty("texto").GetString() ?? "");
        var u = int.TryParse((resp ?? "").Trim('"', ' '), out var i) && i >= 0 && i < ops.Length ? ops[i] : "(sin responder)";
        return (u, string.Join(", ", correctas));
    }

    private static (string, string) McVarias(JsonElement d, string? resp)
    {
        var ops = Opciones(d);
        var elegidas = Indices(resp).Where(i => i >= 0 && i < ops.Length).Select(i => ops[i]);
        var correctas = d.GetProperty("opciones").EnumerateArray()
            .Where(o => o.GetProperty("correcta").GetBoolean()).Select(o => o.GetProperty("texto").GetString() ?? "");
        return (Unir(elegidas), string.Join(", ", correctas));
    }

    private static (string, string) Completar(JsonElement d, string? resp)
    {
        var esperadas = d.GetProperty("respuestas").EnumerateArray().Select(e => e.GetString() ?? "");
        return (Unir(Strings(resp)), string.Join(", ", esperadas));
    }

    private static (string, string) Emparejar(JsonElement d, string? resp)
    {
        var izq = d.GetProperty("izquierda").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
        var der = d.GetProperty("derecha").EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
        string Par(int i, int j) => $"{(i >= 0 && i < izq.Length ? izq[i] : "?")} → {(j >= 0 && j < der.Length ? der[j] : "?")}";
        var usuario = Pares(resp).Select(par => Par(par.Item1, par.Item2));
        var correcta = d.GetProperty("pares").EnumerateArray().Select(par => Par(par[0].GetInt32(), par[1].GetInt32()));
        return (Unir(usuario), Unir(correcta));
    }

    private static (string, string) Vf(JsonElement d, string? resp)
    {
        var esVerd = d.TryGetProperty("esVerdadero", out var ev) && ev.GetBoolean();
        var just = d.TryGetProperty("justificacion", out var ju) ? ju.GetString() ?? "" : "";
        var u = "(sin responder)";
        try { using var r = JsonDocument.Parse(resp ?? ""); var rr = r.RootElement;
            var vf = rr.TryGetProperty("vf", out var v) && v.GetBoolean();
            var jus = rr.TryGetProperty("justificacion", out var j) ? j.GetString() ?? "" : "";
            u = $"{(vf ? "Verdadero" : "Falso")}{(string.IsNullOrWhiteSpace(jus) ? "" : $" — {jus}")}"; } catch { }
        var c = $"{(esVerd ? "Verdadero" : "Falso")}{(string.IsNullOrWhiteSpace(just) ? "" : $" — {just}")}";
        return (u, c);
    }

    private static (string, string) Desarrollo(JsonElement d, string? resp)
    {
        var esperada = d.TryGetProperty("respuestaEsperada", out var re) ? re.GetString() ?? "" : "";
        return (TextoPlano(resp), esperada);
    }

    private static (string, string) DesarrolloItems(JsonElement d, string? resp)
    {
        var esperadas = d.GetProperty("items").EnumerateArray()
            .Select(it => it.TryGetProperty("respuestaEsperada", out var re) ? re.GetString() ?? "" : "");
        return (Unir(Strings(resp)), Unir(esperadas));
    }

    // helpers de parseo de RespuestaJson
    private static List<int> Indices(string? resp)
    { var l = new List<int>(); try { using var d = JsonDocument.Parse(resp ?? ""); foreach (var e in d.RootElement.EnumerateArray()) l.Add(e.GetInt32()); } catch { } return l; }
    private static List<string> Strings(string? resp)
    { var l = new List<string>(); try { using var d = JsonDocument.Parse(resp ?? ""); foreach (var e in d.RootElement.EnumerateArray()) l.Add(e.GetString() ?? ""); } catch { } return l; }
    private static List<(int, int)> Pares(string? resp)
    { var l = new List<(int, int)>(); try { using var d = JsonDocument.Parse(resp ?? ""); foreach (var par in d.RootElement.EnumerateArray()) l.Add((par[0].GetInt32(), par[1].GetInt32())); } catch { } return l; }
    private static string TextoPlano(string? resp)
    { try { using var d = JsonDocument.Parse(resp ?? ""); return d.RootElement.ValueKind == JsonValueKind.String ? d.RootElement.GetString() ?? "" : (resp ?? ""); } catch { return resp ?? ""; } }
    private static string Unir(IEnumerable<string> xs) { var s = string.Join("  ·  ", xs.Where(x => !string.IsNullOrWhiteSpace(x))); return string.IsNullOrEmpty(s) ? "(sin responder)" : s; }
}
```

- [ ] **Step 4: Correr y verificar que pasan**

Run: `dotnet test tests/Resumenes.Tests --filter DescriptorRespuestasTests`
Expected: PASS (los 7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Resumenes.Core/Examenes/DescriptorRespuestas.cs tests/Resumenes.Tests/DescriptorRespuestasTests.cs
git commit -m "feat(examenes): DescriptorRespuestas (respuesta del alumno y correcta por tipo)"
```

---

## Task 5 (Fase C): Pantalla de resultados enriquecida (VM + vista)

**Files:**
- Modify: `src/Resumenes.Ui/ViewModels/ResultadoExamenVm.cs` (`ItemResultadoVm`)
- Modify: `src/Resumenes.Ui/Vistas/VistaResultadoExamen.xaml`
- Test: `tests/Resumenes.Ui.Tests/ItemResultadoVmTests.cs`

**Interfaces:**
- Consumes: `DescriptorRespuestas`, `EvaluadorRespuesta`, `EstadoRespuesta`.
- Produces: `ItemResultadoVm` con `RespuestaUsuario` (string), `RespuestaCorrecta` (string), `Estado` (`EstadoRespuesta`), `EstadoLegible` (string).

- [ ] **Step 1: Escribir el test que falla**

`tests/Resumenes.Ui.Tests/ItemResultadoVmTests.cs`:

```csharp
using Resumenes.Core.Modelos;
using Resumenes.Ui.ViewModels;

namespace Resumenes.Ui.Tests;

public class ItemResultadoVmTests
{
    [Fact]
    public void Item_ExponeRespuestaUsuario_Correcta_YEstado()
    {
        var p = new PreguntaExamen
        {
            Id = "p", ExamenId = "e", Enunciado = "Capital de Francia", Puntos = 10, Tipo = TipoPregunta.McUna,
            DatosJson = "{\"opciones\":[{\"texto\":\"Roma\",\"correcta\":false},{\"texto\":\"París\",\"correcta\":true}]}"
        };
        var r = new RespuestaUsuario { Id = "r", ExamenId = "e", PreguntaId = "p", RespuestaJson = "1", PuntosObtenidos = 10, Correcta = true };

        var item = new ItemResultadoVm(p, r);

        Assert.Equal("París", item.RespuestaUsuario);
        Assert.Equal("París", item.RespuestaCorrecta);
        Assert.Equal(EstadoRespuesta.Correcta, item.Estado);
    }

    [Fact]
    public void Item_SinResponder_NoLanza_YEstadoIncorrecta()
    {
        var p = new PreguntaExamen { Id = "p", ExamenId = "e", Enunciado = "X", Puntos = 10, Tipo = TipoPregunta.Desarrollo,
            DatosJson = "{\"criterios\":\"c\",\"respuestaEsperada\":\"esperado\"}" };
        var item = new ItemResultadoVm(p, null);
        Assert.Equal("esperado", item.RespuestaCorrecta);
        Assert.Equal(EstadoRespuesta.Incorrecta, item.Estado);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/Resumenes.Ui.Tests --filter ItemResultadoVmTests`
Expected: FAIL (`ItemResultadoVm` no tiene `RespuestaUsuario`/`RespuestaCorrecta`/`Estado`).

- [ ] **Step 3: Implementar el VM**

En `src/Resumenes.Ui/ViewModels/ResultadoExamenVm.cs`, reemplazá la clase `ItemResultadoVm` por:

```csharp
public sealed class ItemResultadoVm
{
    public ItemResultadoVm(PreguntaExamen p, RespuestaUsuario? r)
    {
        Enunciado = p.Enunciado;
        Puntos = p.Puntos;
        PuntosObtenidos = r?.PuntosObtenidos ?? 0;
        Feedback = r?.FeedbackIa ?? "";
        Ambigua = r?.Ambigua == true;
        var (u, c) = Resumenes.Core.Examenes.DescriptorRespuestas.Describir(p, r?.RespuestaJson);
        RespuestaUsuario = u;
        RespuestaCorrecta = c;
        Estado = Resumenes.Core.Examenes.EvaluadorRespuesta.Estado(PuntosObtenidos, p.Puntos);
    }
    public string Enunciado { get; }
    public double Puntos { get; }
    public double PuntosObtenidos { get; }
    public string Feedback { get; }
    public bool Ambigua { get; }
    public string RespuestaUsuario { get; }
    public string RespuestaCorrecta { get; }
    public Resumenes.Core.Modelos.EstadoRespuesta Estado { get; }
    public string EstadoLegible => Estado switch
    {
        Resumenes.Core.Modelos.EstadoRespuesta.Correcta => "Correcta",
        Resumenes.Core.Modelos.EstadoRespuesta.Parcial => "Parcial",
        _ => "Incorrecta",
    };
    public string PuntajeLegible => $"{PuntosObtenidos:0.#}/{Puntos:0.#}";
}
```

> Nota: se eliminó la propiedad `EsCorrecta` (bool) anterior; si algún binding del XAML la usaba, se reemplaza por `Estado`/`EstadoLegible` en el Step siguiente.

- [ ] **Step 4: Actualizar la vista**

En `src/Resumenes.Ui/Vistas/VistaResultadoExamen.xaml`, dentro del `DataTemplate` de cada ítem del detalle (donde hoy se muestra `Enunciado`, `PuntajeLegible`, `Feedback`), agregar dos bloques de texto: **tu respuesta** y **respuesta correcta**, y reemplazar el indicador binario por un chip de `EstadoLegible`. Leé el XAML actual y, en el template del ítem, sumá (siguiendo el estilo existente):

```xml
    <TextBlock Text="{Binding EstadoLegible}" FontWeight="SemiBold" Margin="0,2,0,0"/>
    <TextBlock Margin="0,4,0,0" TextWrapping="Wrap"
               Text="{Binding RespuestaUsuario, StringFormat='Tu respuesta: {0}'}"/>
    <TextBlock Margin="0,2,0,0" TextWrapping="Wrap"
               Foreground="{DynamicResource TextFillColorSecondaryBrush}"
               Text="{Binding RespuestaCorrecta, StringFormat='Respuesta correcta: {0}'}"/>
```

Si el template usaba un binding a `EsCorrecta` (p.ej. un color/ícono), reemplazalo por `EstadoLegible` (texto) o por un mapeo de color sobre `Estado`. NO cambies la estructura general del template; solo sumá/ajustá esos bindings.

- [ ] **Step 5: Compilar, correr la suite de Ui**

Run: `dotnet build src/Resumenes.Ui` y `dotnet test tests/Resumenes.Ui.Tests --filter ItemResultadoVmTests`
Expected: BUILD SUCCEEDED; tests verdes. (Corré también `dotnet test tests/Resumenes.Ui.Tests` y confirmá que nada se rompió; si `OnboardingVmTests` falla, es flaky pre-existente, ajeno.)

- [ ] **Step 6: Commit**

```bash
git add src/Resumenes.Ui/ViewModels/ResultadoExamenVm.cs src/Resumenes.Ui/Vistas/VistaResultadoExamen.xaml tests/Resumenes.Ui.Tests/ItemResultadoVmTests.cs
git commit -m "feat(examenes): resultados muestran tu respuesta, la correcta y el estado por pregunta"
```

---

## Task 6 (Fase D): Persistir "marcada para revisar" (modelo + schema + repo)

**Files:**
- Modify: `src/Resumenes.Core/Modelos/Examenes.cs` (`RespuestaUsuario.MarcadaRevisar`)
- Modify: `src/Resumenes.Infrastructure/schema.sql` (columna en `RespuestaUsuario`)
- Modify: `src/Resumenes.Infrastructure/Persistencia/SqliteRepositorioEstado.cs` (`AsegurarColumna`)
- Modify: `src/Resumenes.Infrastructure/Persistencia/SqliteRepositorioExamenes.cs` (guardar/leer)
- Test: `tests/Resumenes.Tests/RepositorioMarcadaRevisarTests.cs`

**Interfaces:**
- Produces: `RespuestaUsuario.MarcadaRevisar` (bool) se persiste y se lee. Columna SQLite `marcada_revisar INTEGER NOT NULL DEFAULT 0`.

- [ ] **Step 1: Escribir el test que falla**

`tests/Resumenes.Tests/RepositorioMarcadaRevisarTests.cs`:

```csharp
using Resumenes.Core.Modelos;
using Resumenes.Infrastructure.Persistencia;

namespace Resumenes.Tests;

public class RepositorioMarcadaRevisarTests : IDisposable
{
    private readonly string _ruta = Path.Combine(Path.GetTempPath(), $"exdb-{Guid.NewGuid():N}.sqlite");
    private readonly SqliteRepositorioEstado _estado;
    private readonly SqliteRepositorioExamenes _ex;

    public RepositorioMarcadaRevisarTests()
    {
        var cs = $"Data Source={_ruta}";
        _estado = new SqliteRepositorioEstado(cs);
        _estado.InicializarEsquema();
        _ex = new SqliteRepositorioExamenes(cs);
        // Sembrar análisis/examen/pregunta para respetar las FK.
        _ex.GuardarExamen(new Examen { Id = "e", AnalisisId = "a", Titulo = "t", ConfigJson = "{}", CreadoEn = DateTime.UtcNow });
    }

    [Fact]
    public void GuardarYLeer_PreservaMarcadaRevisar()
    {
        _ex.GuardarRespuesta(new RespuestaUsuario { Id = "e:p", ExamenId = "e", PreguntaId = "p", MarcadaRevisar = true });
        var leida = _ex.ListarRespuestas("e").Single();
        Assert.True(leida.MarcadaRevisar);
    }

    [Fact]
    public void PorDefecto_EsFalse()
    {
        _ex.GuardarRespuesta(new RespuestaUsuario { Id = "e:p2", ExamenId = "e", PreguntaId = "p2" });
        Assert.False(_ex.ListarRespuestas("e").Single(r => r.Id == "e:p2").MarcadaRevisar);
    }

    public void Dispose() { try { File.Delete(_ruta); } catch { } }
}
```

> Nota: el test siembra un `Examen` con `AnalisisId="a"` pero NO crea el `Analisis` ni la `Pregunta`. Si las FK lo impiden, sembrá también un `Analisis` (vía `SqliteRepositorioEstado`) y una `PreguntaExamen` con `_ex.GuardarPregunta(...)`. Ajustá el sembrado mínimo para que las FK pasen (mirá las FK en `schema.sql`).

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/Resumenes.Tests --filter RepositorioMarcadaRevisarTests`
Expected: FAIL (`RespuestaUsuario.MarcadaRevisar` no existe; la columna no existe).

- [ ] **Step 3: Implementar**

(a) En `Core/Modelos/Examenes.cs`, agregar a `RespuestaUsuario`:
```csharp
    public bool MarcadaRevisar { get; set; }
```

(b) En `schema.sql`, agregar la columna a la tabla `RespuestaUsuario` (para bases nuevas), después de `ambigua`:
```sql
    marcada_revisar  INTEGER NOT NULL DEFAULT 0 CHECK (marcada_revisar IN (0,1)),
```

(c) En `SqliteRepositorioEstado.InicializarEsquema` (mirá el método: ya hay llamadas `AsegurarColumna(...)` para columnas agregadas en versiones previas), agregar junto a ellas:
```csharp
        AsegurarColumna("RespuestaUsuario", "marcada_revisar", "INTEGER NOT NULL DEFAULT 0");
```
> Si no encontrás `AsegurarColumna`, buscá el helper que hace `ALTER TABLE ... ADD COLUMN` con guarda de "columna ya existe" (se usa para las columnas de exámenes/costos). Seguí ese patrón exacto.

(d) En `SqliteRepositorioExamenes.GuardarRespuesta`, agregar la columna al INSERT/UPDATE:
```csharp
        cmd.CommandText = @"INSERT INTO RespuestaUsuario (id, examen_id, pregunta_id, respuesta_json, correcta, puntos_obtenidos, feedback_ia, ambigua, marcada_revisar)
            VALUES ($id,$ex,$pre,$resp,$corr,$pts,$fb,$amb,$mr)
            ON CONFLICT(id) DO UPDATE SET respuesta_json=$resp, correcta=$corr, puntos_obtenidos=$pts, feedback_ia=$fb, ambigua=$amb, marcada_revisar=$mr;";
```
y antes de `ExecuteNonQuery()`:
```csharp
        cmd.Parameters.AddWithValue("$mr", u.MarcadaRevisar ? 1 : 0);
```

(e) En `SqliteRepositorioExamenes.ListarRespuestas`, agregar `marcada_revisar` al SELECT y al mapeo:
```csharp
        cmd.CommandText = @"SELECT id, examen_id, pregunta_id, respuesta_json, correcta, puntos_obtenidos, feedback_ia, ambigua, marcada_revisar
                            FROM RespuestaUsuario WHERE examen_id=$ex;";
```
y en el `new RespuestaUsuario { ... }` agregar:
```csharp
                Ambigua = r.GetInt32(7) != 0,
                MarcadaRevisar = r.GetInt32(8) != 0 };
```

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test tests/Resumenes.Tests --filter RepositorioMarcadaRevisarTests` y `dotnet test tests/Resumenes.Tests`
Expected: PASS (incluida la suite de Core).

- [ ] **Step 5: Commit**

```bash
git add src/Resumenes.Core/Modelos/Examenes.cs src/Resumenes.Infrastructure/schema.sql src/Resumenes.Infrastructure/Persistencia/SqliteRepositorioEstado.cs src/Resumenes.Infrastructure/Persistencia/SqliteRepositorioExamenes.cs tests/Resumenes.Tests/RepositorioMarcadaRevisarTests.cs
git commit -m "feat(examenes): persistir 'marcada para revisar' (columna + repo)"
```

---

## Task 7 (Fase D): "Marcar para revisar" en la UI + mini-mapa

**Files:**
- Modify: `src/Resumenes.Ui/ViewModels/PreguntaRendirVm.cs` (`Respondida`)
- Modify: `src/Resumenes.Ui/ViewModels/RendirExamenVm.cs` (persistir/restaurar marcado, comando saltar)
- Modify: `src/Resumenes.Ui/Vistas/VistaRendirExamen.xaml` (mini-mapa de dots)
- Test: `tests/Resumenes.Ui.Tests/RendirMarcarRevisarTests.cs`

**Interfaces:**
- Consumes: `IRepositorioExamenes`, `RespuestaUsuario.MarcadaRevisar`.
- Produces: `RendirExamenVm.IrAPreguntaCommand` (salta al índice); `GuardarActual`/`EntregarAsync` persisten `MarcadaRevisar`; `CargarInterno` lo restaura. `PreguntaRendirVm.Respondida` (bool, calculada).

- [ ] **Step 1: Escribir el test que falla**

`tests/Resumenes.Ui.Tests/RendirMarcarRevisarTests.cs`:

```csharp
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;
using Resumenes.Ui.ViewModels;

namespace Resumenes.Ui.Tests;

public class RendirMarcarRevisarTests
{
    private sealed class RepoFake : IRepositorioExamenes
    {
        public readonly List<RespuestaUsuario> Guardadas = new();
        public List<RespuestaUsuario> Previas = new();
        public List<PreguntaExamen> Preguntas = new();
        public Examen? ObtenerExamen(string id) => new() { Id = id, AnalisisId = "a", Titulo = "t", ConfigJson = "{}" };
        public IReadOnlyList<PreguntaExamen> ListarPreguntas(string examenId) => Preguntas;
        public IReadOnlyList<RespuestaUsuario> ListarRespuestas(string examenId) => Previas;
        public void GuardarRespuesta(RespuestaUsuario u) { Guardadas.RemoveAll(x => x.Id == u.Id); Guardadas.Add(u); }
        public void GuardarExamen(Examen e) { } public void GuardarPregunta(PreguntaExamen p) { }
        public Examen? ObtenerExamenPorId(string id) => null;
        public IReadOnlyList<Examen> ListarExamenes(string analisisId) => new List<Examen>();
        public void EliminarExamen(string id) { }
    }
    private sealed class SvcFake : IServicioExamenes
    {
        public Task<Examen> CrearAsync(string a, string t, ConfigExamen c, CancellationToken ct) => Task.FromResult(new Examen { Id="x", AnalisisId=a, Titulo=t, ConfigJson="{}" });
        public Task<Examen> FinalizarYCorregirAsync(string id, CancellationToken ct) => Task.FromResult(new Examen { Id=id, AnalisisId="a", Titulo="t", ConfigJson="{}" });
        public IReadOnlyList<Examen> Historial(string a) => new List<Examen>();
    }

    private static PreguntaExamen Preg(string id) => new()
    { Id = id, ExamenId = "e", Enunciado = "?", Puntos = 1, Tipo = TipoPregunta.Desarrollo, DatosJson = "{}" };

    [Fact]
    public void GuardarActual_PersisteElMarcado()
    {
        var repo = new RepoFake { Preguntas = { Preg("p1") } };
        var vm = new RendirExamenVm(repo, new SvcFake());
        vm.Cargar("e", new Analisis(), 0);
        vm.Actual!.MarcadaParaRevisar = true;
        vm.GuardarActual();
        Assert.True(repo.Guardadas.Single().MarcadaRevisar);
    }

    [Fact]
    public void Cargar_RestauraElMarcadoPrevio()
    {
        var repo = new RepoFake { Preguntas = { Preg("p1") } };
        repo.Previas = new() { new RespuestaUsuario { Id = "e:p1", ExamenId = "e", PreguntaId = "p1", MarcadaRevisar = true } };
        var vm = new RendirExamenVm(repo, new SvcFake());
        vm.Cargar("e", new Analisis(), 0);
        Assert.True(vm.Preguntas[0].MarcadaParaRevisar);
    }

    [Fact]
    public void IrAPregunta_CambiaElActual()
    {
        var repo = new RepoFake { Preguntas = { Preg("p1"), Preg("p2") } };
        var vm = new RendirExamenVm(repo, new SvcFake());
        vm.Cargar("e", new Analisis(), 0);
        vm.IrAPreguntaCommand.Execute(2); // orden 2 (1-based) = segunda pregunta (índice 1)
        Assert.Equal(1, vm.IndiceActual);
        Assert.Same(vm.Preguntas[1], vm.Actual);
    }
}
```

> Ajustá `RepoFake` a la interfaz `IRepositorioExamenes` real (agregá/quitá miembros para que compile; el patrón importa, no los miembros exactos de este ejemplo). `Analisis` tiene un ctor sin args o con required — usá el que compile.

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/Resumenes.Ui.Tests --filter RendirMarcarRevisarTests`
Expected: FAIL (no se persiste el marcado; no existe `IrAPreguntaCommand`; no se restaura).

- [ ] **Step 3: Implementar**

(a) En `RendirExamenVm.GuardarActual` y en el loop de `EntregarAsync`, agregar `MarcadaRevisar = <pr>.MarcadaParaRevisar` al `new RespuestaUsuario { ... }` (en `GuardarActual` es `Actual.MarcadaParaRevisar`; en `EntregarAsync` es `pr.MarcadaParaRevisar`).

(b) En `CargarInterno`, después de poblar `Preguntas`, restaurar el marcado leyendo las respuestas previas:
```csharp
        var previas = _repo.ListarRespuestas(examenId).ToDictionary(x => x.PreguntaId);
        foreach (var pr in Preguntas)
            if (previas.TryGetValue(pr.Pregunta.Id, out var ru)) pr.MarcadaParaRevisar = ru.MarcadaRevisar;
```
(agregá `using System.Linq;` si falta).

(c) Agregar el comando de salto en `RendirExamenVm`:
```csharp
    [RelayCommand]
    private void IrAPregunta(int orden) // orden 1-based (Pregunta.Orden)
    {
        GuardarActual();
        var indice = orden - 1;
        if (indice >= 0 && indice < Preguntas.Count) { IndiceActual = indice; Actual = Preguntas[indice]; }
    }
```

(d) En `PreguntaRendirVm`, agregar una propiedad calculada `Respondida` (para el color del dot). Como la respuesta vive en sub-colecciones, exponé:
```csharp
    public bool Respondida => !string.IsNullOrWhiteSpace(ConstruirRespuestaJson()) &&
                              ConstruirRespuestaJson() is not "null" and not "\"\"" and not "[]";
```

(e) En `VistaRendirExamen.xaml`, donde está el comentario `<!-- mini mapa de preguntas (dots) -->`, agregar un `ItemsControl` horizontal sobre `Preguntas`, con un `Button` por pregunta que llame `IrAPreguntaCommand` con el índice y muestre el número; resaltá visualmente el actual y los marcados. Mínimo viable:
```xml
<ItemsControl ItemsSource="{Binding Preguntas}">
    <ItemsControl.ItemsPanel>
        <ItemsPanelTemplate><WrapPanel/></ItemsPanelTemplate>
    </ItemsControl.ItemsPanel>
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <ui:Button Width="34" Height="34" Margin="3" Padding="0"
                       Content="{Binding Pregunta.Orden}"
                       Command="{Binding DataContext.IrAPreguntaCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                       CommandParameter="{Binding Pregunta.Orden}"
                       ToolTip="{Binding Enunciado}">
                <ui:Button.Style>
                    <Style TargetType="ui:Button" BasedOn="{StaticResource {x:Type ui:Button}}">
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding MarcadaParaRevisar}" Value="True">
                                <Setter Property="Appearance" Value="Caution"/>
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </ui:Button.Style>
            </ui:Button>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```
> Convención: `IrAPregunta` recibe el **orden 1-based** (`Pregunta.Orden`) y adentro hace `orden-1` (ya reflejado en el código del Step 3c y en el test con `Execute(2)`). El `CommandParameter="{Binding Pregunta.Orden}"` es coherente con eso.

- [ ] **Step 4: Compilar y correr**

Run: `dotnet build src/Resumenes.Ui` y `dotnet test tests/Resumenes.Ui.Tests --filter RendirMarcarRevisarTests`
Expected: BUILD SUCCEEDED; tests verdes. Corré la suite de Ui completa (ojo flaky de Onboarding, ajeno).

- [ ] **Step 5: Commit**

```bash
git add src/Resumenes.Ui/ViewModels/RendirExamenVm.cs src/Resumenes.Ui/ViewModels/PreguntaRendirVm.cs src/Resumenes.Ui/Vistas/VistaRendirExamen.xaml tests/Resumenes.Ui.Tests/RendirMarcarRevisarTests.cs
git commit -m "feat(examenes): marcar para revisar funcional + mini-mapa de preguntas"
```

---

## Task 8 (Fase E): Emparejar sin opciones duplicadas

**Files:**
- Modify: `src/Resumenes.Ui/ViewModels/PreguntaRendirVm.cs` (`EmparejamientoItemVm.OpcionesDisponibles`)
- Modify: `src/Resumenes.Ui/Vistas/VistaRendirExamen.xaml` (ComboBox usa OpcionesDisponibles)
- Test: `tests/Resumenes.Ui.Tests/EmparejarSinDuplicadosTests.cs`

**Interfaces:**
- Produces: cada `EmparejamientoItemVm` expone `OpcionesDisponibles` (las de la derecha que no eligió OTRA fila, más la propia).

- [ ] **Step 1: Escribir el test que falla**

`tests/Resumenes.Ui.Tests/EmparejarSinDuplicadosTests.cs`:

```csharp
using System.Collections.ObjectModel;
using Resumenes.Ui.ViewModels;

namespace Resumenes.Ui.Tests;

public class EmparejarSinDuplicadosTests
{
    [Fact]
    public void OpcionDeOtraFila_NoApareceDisponible()
    {
        var derecha = new ObservableCollection<string> { "A", "B", "C" };
        var sel = new ObservableCollection<int> { -1, -1 };
        var fila0 = new EmparejamientoItemVm(sel, 0) { TextoIzquierda = "x", Derecha = derecha };
        var fila1 = new EmparejamientoItemVm(sel, 1) { TextoIzquierda = "y", Derecha = derecha };
        fila0.Sincronizar(new[] { fila0, fila1 });
        fila1.Sincronizar(new[] { fila0, fila1 });

        fila0.SeleccionIndice = 1; // fila0 elige "B"

        Assert.DoesNotContain("B", fila1.OpcionesDisponibles.Select(o => o.Texto)); // ya usada por fila0
        Assert.Contains("B", fila0.OpcionesDisponibles.Select(o => o.Texto));       // la propia sí
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/Resumenes.Ui.Tests --filter EmparejarSinDuplicadosTests`
Expected: FAIL (`OpcionesDisponibles`/`Sincronizar` no existen).

- [ ] **Step 3: Implementar**

En `PreguntaRendirVm.cs`, ampliá `EmparejamientoItemVm` con una lista de opciones disponibles (texto + índice real) que se recalcula cuando cambia cualquier selección. Agregá:

```csharp
public sealed class OpcionDerechaVm { public required string Texto { get; init; } public required int Indice { get; init; } }

public partial class EmparejamientoItemVm : ObservableObject
{
    // ... lo existente (_selecciones, _indice, TextoIzquierda, Derecha, SeleccionIndice, ctor) ...

    private IReadOnlyList<EmparejamientoItemVm> _filas = System.Array.Empty<EmparejamientoItemVm>();
    public ObservableCollection<OpcionDerechaVm> OpcionesDisponibles { get; } = new();

    /// <summary>Conecta esta fila con todas las filas del emparejamiento para excluir las ya elegidas.</summary>
    public void Sincronizar(IReadOnlyList<EmparejamientoItemVm> filas)
    {
        _filas = filas;
        Recalcular();
    }

    public void Recalcular()
    {
        var usadasPorOtros = _filas.Where(f => !ReferenceEquals(f, this) && f.SeleccionIndice >= 0)
                                   .Select(f => f.SeleccionIndice).ToHashSet();
        OpcionesDisponibles.Clear();
        for (int i = 0; i < Derecha.Count; i++)
            if (!usadasPorOtros.Contains(i))
                OpcionesDisponibles.Add(new OpcionDerechaVm { Texto = Derecha[i], Indice = i });
    }
}
```

Y en el setter de `SeleccionIndice`, después de notificar, recalcular todas las filas:
```csharp
    public int SeleccionIndice
    {
        get => _selecciones[_indice];
        set { if (_selecciones[_indice] != value) { _selecciones[_indice] = value; OnPropertyChanged();
              foreach (var f in _filas) f.Recalcular(); } }
    }
```

En `PreguntaRendirVm` (ctor, case `Emparejar`), después de crear todos los `EmparejamientoItems`, conectarlos:
```csharp
                foreach (var it in EmparejamientoItems) it.Sincronizar(EmparejamientoItems);
```

- [ ] **Step 4: Actualizar la vista**

En `VistaRendirExamen.xaml`, el `ComboBox` de emparejar (hoy bindeado a `Derecha` + `SeleccionIndice`) debe usar `OpcionesDisponibles` con `SelectedValue`/`SelectedValuePath`:
```xml
<ComboBox ItemsSource="{Binding OpcionesDisponibles}"
          DisplayMemberPath="Texto" SelectedValuePath="Indice"
          SelectedValue="{Binding SeleccionIndice, Mode=TwoWay}"/>
```
Leé el ComboBox actual y reemplazá solo esos atributos (no muevas el control). La corrección no cambia (sigue serializando pares `[i,j]` con los índices reales).

- [ ] **Step 5: Compilar y correr**

Run: `dotnet build src/Resumenes.Ui` y `dotnet test tests/Resumenes.Ui.Tests --filter EmparejarSinDuplicadosTests`
Expected: BUILD SUCCEEDED; test verde. Corré la suite de Ui completa.

- [ ] **Step 6: Commit**

```bash
git add src/Resumenes.Ui/ViewModels/PreguntaRendirVm.cs src/Resumenes.Ui/Vistas/VistaRendirExamen.xaml tests/Resumenes.Ui.Tests/EmparejarSinDuplicadosTests.cs
git commit -m "feat(examenes): emparejar no permite elegir dos veces la misma opcion"
```

---

## Cierre

Al terminar las 8 tareas: el alumno ve la respuesta correcta y la suya por pregunta, con estado (Correcta/Parcial/Incorrecta) y una devolución de IA al pie; las abiertas se corrigen de forma más justa; "marcar para revisar" funciona con mini-mapa; y emparejar no deja duplicar opciones.

**Validación manual del usuario (UI):** generar un examen con los 7 tipos, rendirlo (probar marcar/saltar y emparejar), entregarlo y revisar la pantalla de resultados (respuestas correctas + devolución). El smoke visual lo hace el usuario.

**Backlog (del spec):** respuesta modelo para exámenes ya creados; issues menores de robustez (GroupName "VF", `"null"` string en McUna).
