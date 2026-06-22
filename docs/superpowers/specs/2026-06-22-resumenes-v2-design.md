# Resúmenes v2 — Prompts editables, multi-idioma, caché, costos y simulador de exámenes

> **Fecha:** 2026-06-22
> **Estado:** Aprobado (diseño). Pendiente: plan de implementación (`writing-plans`).
> **Alcance:** una sola spec con 7 mejoras, implementación por fases.

## Goal

Evolucionar la app de Resúmenes (v1.0.0) con siete mejoras que la hacen más útil,
genérica y económica:

1. Prompts editables desde Configuración (con restaurar default).
2. Soporte de cualquier materia e idioma (autodetección, prompts neutros).
3. Reutilización de OCR/limpieza entre análisis vía caché global por contenido.
4. Saldo en dólares de la API + costo estimado por análisis.
5. Simulador de exámenes interactivo con corrección por IA e historial.
6. Enlace a LinkedIn del autor en la UI.
7. Quitar/excluir archivos antes de procesar un análisis.

## Principios que se mantienen (de v1)

- **Puertos y adaptadores:** dominio + interfaces en `Resumenes.Core`; adaptadores en
  `Resumenes.Infrastructure`; UI (MVVM, WPF-UI) en `Resumenes.Ui`.
- **SQLite es el índice de estado**, no almacén de archivos; los contenidos viven en disco.
- **Mínimos tokens:** solo viaja a la IA lo imprescindible; lo objetivo se resuelve local.
- **Idempotencia y tolerancia a fallos:** unidades con `hash_entrada`; reanudación;
  "N exitosos / M con error" sin abortar todo.
- **Originales intactos:** se trabaja sobre copias; salidas trazables.
- **Windows-only, por usuario:** datos en `%LOCALAPPDATA%/ResumenesApp/`.
- **Testing:** lógica testeable (VMs, servicios, correctores) con xUnit + fakes de IA.

---

## 1. Prompts editables (#1)

### Decisión
- Edición **parcial y segura**: cada prompt se compone de **[parte editable: rol/estilo]**
  + **[parte fija: formato]** inyectada por código. El usuario edita solo la parte de
  instrucciones; el formato (marcadores del PDF, JSON de detección) no se puede romper.
- Aplica a los **3 prompts**: limpieza de OCR, detección de temas, resumen final.
- **Restaurar default** por prompt.

### Modelo de datos
Nueva tabla:

```sql
CREATE TABLE IF NOT EXISTS AjustePrompt (
    clave          TEXT PRIMARY KEY,   -- 'limpieza' | 'deteccion' | 'resumen'
    texto_editable TEXT NOT NULL,      -- la parte rol/estilo que escribió el usuario
    actualizado_en TEXT NOT NULL
);
```

Si no hay fila para una clave, se usa el default del código (`Prompts.*`). "Restaurar
default" = borrar la fila.

### Componentes
- `ServicioPrompts` (Infrastructure): resuelve el prompt efectivo = `editable (override o
  default) + formato fijo`. Expone `ObtenerEditable(clave)`, `GuardarEditable`,
  `RestaurarDefault`, y `ComponerSystem(clave, …parámetros)`.
- `ConstructorPipeline` y `DetectorTemas` dejan de usar las constantes directamente y piden
  el prompt a `ServicioPrompts`.
- UI: pestaña **"Prompts (avanzado)"** en Configuración, con aviso de que es algo vital;
  un editor por prompt (muestra la parte fija como solo-lectura para contexto) + botón
  restaurar.

### Idempotencia (clave)
El `hash_entrada` de las unidades con IA pasa a incorporar el **hash del texto editable
del prompt efectivo**:
- `LimpiezaIA`: `hash(archivo_bruto) + hash(prompt_limpieza_editable) + modelo`.
- `ResumenFinal`: ya incluye `promptResumen` del alumno; se agrega también
  `hash(prompt_resumen_editable)`.
- `DetectorTemas`: la detección no es una "unidad" cacheada por hash (depende de
  `temas.json`); al editar su prompt no se fuerza recálculo automático (queda documentado;
  el usuario puede borrar `temas.json` para re-detectar).

Efecto: editar un prompt invalida las unidades afectadas → se reprocesan en el próximo
análisis/regeneración; sin cambios, todo se saltea como hoy.

---

## 2. Multi-materia e idiomas (#2)

### Decisión
- **Idioma autodetectado, prompts neutros.** Sin campo de contexto, sin perfiles, sin
  materias predefinidas (evita sesgar a una carrera).
- Defaults reescritos a versión neutra:
  - **Limpieza:** "Sos un corrector de texto OCR. Corregí errores de OCR, reconstruí
    palabras partidas y quitá ruido. Mantené el idioma original y respetá su ortografía,
    tildes y signos. PROHIBIDO agregar información que no esté en el texto. Devolvé solo el
    texto corregido."
  - **Detección de temas:** "Sos un organizador de material de estudio. Agrupá el contenido
    en TEMAS coherentes…" (igual que hoy pero sin supuestos de idioma). El JSON de salida
    se mantiene fijo.
  - **Resumen:** rol "Sos un asistente de estudio."; estilo default "Resumí en el mismo
    idioma del material, sin extremos, sin eliminar contenido, priorizando el original." El
    formato de marcadores se mantiene fijo.
- La autodetección la hace el propio modelo al procesar el texto (no se agrega una llamada
  extra de detección de idioma → cero costo adicional).

---

## 3. Caché global de OCR/limpieza (#3)

### Decisión
Reutilizar entre análisis distintos los derivados de un **mismo contenido** (mismo
`hash_sha256`), copiándolos al análisis nuevo:
- **OCR bruto:** se reutiliza si coincide `hash_contenido + dpi + version_modelo_ocr`
  (ahorra el paso lento).
- **Limpieza IA:** se reutiliza solo si además coincide `hash_prompt_limpieza + modelo_ia`
  (ahorra tokens). Si el prompt/modelo cambió, se rehace **solo la limpieza**.

### Almacenamiento
- Contenido en `%LOCALAPPDATA%/ResumenesApp/cache/<hash_contenido>/` (ej. `ocr_bruto.txt`,
  `limpio__<promptHash>__<modelo>.txt`).
- Índice en SQLite:

```sql
CREATE TABLE IF NOT EXISTS CacheDerivado (
    hash_contenido TEXT NOT NULL,
    tipo           TEXT NOT NULL CHECK (tipo IN ('OcrBruto','Limpieza')),
    clave_variante TEXT NOT NULL,   -- 'dpi=200;ocr=vX'  |  'prompt=<hash>;modelo=<m>'
    ruta           TEXT NOT NULL,
    creado_en      TEXT NOT NULL,
    PRIMARY KEY (hash_contenido, tipo, clave_variante)
);
```

### Componentes
- `CacheDerivados` (Infrastructure): `BuscarOcr(hash,variante)`, `BuscarLimpieza(...)`,
  `Guardar(...)`. Devuelve ruta si hay hit.
- En `ConstructorPipeline`: antes de ejecutar `OcrBruto`/`LimpiezaIA`, consultar la caché;
  si hay hit, copiar al artefacto del análisis y marcar la unidad `Completado`; si no,
  procesar y **poblar** la caché al terminar.
- La caché es aditiva (no se purga automáticamente en esta versión; purga manual queda en
  backlog). Cada análisis conserva su copia local (originales intactos, trazabilidad).

---

## 4. Saldo y costos (#4)

### Decisión
- **Saldo en dólares:** consulta `GET {BaseUrlDeepseek}/user/balance` con la API key.
  Mostrado en el header de la app y en Configuración, con botón "actualizar".
- **Costo por análisis:** se calcula con tokens reales × **tarifa configurable**.

### Modelo de datos
- A `Unidad` se agregan `tokens_entrada INTEGER` y `tokens_salida INTEGER` (además del
  `tokens` total existente). `RespuestaIA` ya trae `prompt_tokens`/`completion_tokens`.
- A `Configuracion`: `PrecioInputPorMillonUsd`, `PrecioOutputPorMillonUsd` (defaults
  razonables + aviso en UI de que las tarifas pueden cambiar).
- `Ejecucion.costo_estimado` se completa al cerrar cada etapa.

### Componentes
- `ClienteSaldo` (Infrastructure): consulta el endpoint de balance; tolera fallos
  (muestra "no disponible" sin romper).
- `ServicioCostos`: `CostoDe(analisisId)` = Σ(tokens_in × precio_in + tokens_out ×
  precio_out) / 1e6. Mostrado en el historial (columna) y en la pantalla de resultados.

---

## 5. Simulador de exámenes (#5)

### Tipos soportados
MC-1 (una correcta), MC-N (varias correctas), V/F con justificación, desarrollo,
desarrollo por ítems, respuesta corta/completar, emparejar conceptos.

### Flujo
1. **Entrada:** desde un análisis **terminado**, botón "Simular examen" y pestaña
   "Exámenes" con el historial del análisis.
2. **Asistente de creación (mixto configurable):**
   - cantidad de preguntas por tipo (o "mezcla automática"),
   - temas a incluir (de los temas del análisis),
   - dificultad: fácil / media / difícil,
   - puntos totales,
   - tiempo límite (minutos),
   - fuente: **rápido** (resúmenes finales) | **completo** (texto limpio consolidado).
3. **Generación (IA):** `GeneradorExamen` arma las preguntas en JSON, incluyendo la
   respuesta correcta y el puntaje de las objetivas. Prompt de generación **editable**
   (mismo mecanismo que #1; formato JSON fijo). Chunking si la fuente excede `MaxCharsIA`.
4. **Rendición interactiva** (una vista por tipo de pregunta):
   - MC-1: radios; MC-N: checkboxes; V/F: toggle + caja de justificación; desarrollo:
     editor de texto; ítems: varias cajas; completar: inputs; emparejar: selección/drag de
     pares.
   - **Timer** con cuenta regresiva; al expirar → **autoentrega** y corrige lo respondido.
   - Navegación entre preguntas, "marcar para revisar", **autoguardado** (estado `EnCurso`
     reanudable si se cierra la app).
5. **Corrección:**
   - **Objetivo (local, gratis):** MC-1, MC-N, V/F (parte V/F), completar (match exacto/
     normalizado), emparejar.
   - **Abierto (IA, en batch):** justificación de V/F, desarrollo, ítems → puntaje parcial,
     feedback y marca de **ambigüedad**. Prompt de corrección **editable**.
   - Nota final combinada = Σ puntos obtenidos / puntos totales.
6. **Resultado:**
   - Nota en escala **configurable (0-10 por defecto)** + **% de acierto** siempre;
     **nota de aprobación configurable** (aprobado/desaprobado).
   - Desglose pregunta por pregunta: respuesta dada, correcta, puntaje, feedback (en
     erróneas/ambiguas). En MC erróneas se muestra la correcta sin gastar IA.
   - Botón **reintentar** (nuevo examen con los mismos parámetros).

### Modelo de datos

```sql
CREATE TABLE IF NOT EXISTS Examen (
    id              TEXT PRIMARY KEY,
    analisis_id     TEXT NOT NULL,
    titulo          TEXT NOT NULL,
    config_json     TEXT NOT NULL,   -- tipos+cantidades, temas, dificultad, puntos,
                                     -- tiempo, fuente, escala, nota_aprobacion
    estado          TEXT NOT NULL DEFAULT 'Borrador'
                       CHECK (estado IN ('Borrador','EnCurso','Finalizado','Corregido')),
    nota            REAL,
    porcentaje      REAL,
    aprobado        INTEGER,         -- 0/1/null
    feedback_general TEXT,
    tokens          INTEGER NOT NULL DEFAULT 0,
    costo_estimado  REAL    NOT NULL DEFAULT 0,
    creado_en       TEXT NOT NULL,
    iniciado_en     TEXT,
    finalizado_en   TEXT,
    FOREIGN KEY (analisis_id) REFERENCES Analisis(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_examen_analisis ON Examen(analisis_id);

CREATE TABLE IF NOT EXISTS PreguntaExamen (
    id          TEXT PRIMARY KEY,
    examen_id   TEXT NOT NULL,
    orden       INTEGER NOT NULL,
    tipo        TEXT NOT NULL
                   CHECK (tipo IN ('McUna','McVarias','VfJustificado','Desarrollo',
                                   'DesarrolloItems','Completar','Emparejar')),
    enunciado   TEXT NOT NULL,
    puntos      REAL NOT NULL,
    datos_json  TEXT NOT NULL,   -- opciones, correcta(s), items, pares, etc.
    tema_id     TEXT,
    FOREIGN KEY (examen_id) REFERENCES Examen(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_pregunta_examen ON PreguntaExamen(examen_id);

CREATE TABLE IF NOT EXISTS RespuestaUsuario (
    id               TEXT PRIMARY KEY,
    examen_id        TEXT NOT NULL,
    pregunta_id      TEXT NOT NULL,
    respuesta_json   TEXT,
    correcta         INTEGER,        -- 0/1/null (null = abierta sin corregir)
    puntos_obtenidos REAL NOT NULL DEFAULT 0,
    feedback_ia      TEXT,
    ambigua          INTEGER NOT NULL DEFAULT 0 CHECK (ambigua IN (0,1)),
    FOREIGN KEY (examen_id)   REFERENCES Examen(id)         ON DELETE CASCADE,
    FOREIGN KEY (pregunta_id) REFERENCES PreguntaExamen(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_respuesta_examen ON RespuestaUsuario(examen_id);
```

### Componentes
- `GeneradorExamen` (Infrastructure, IA): de fuente + parámetros → preguntas JSON.
- `CorrectorExamen`: corrección local de lo objetivo + llamada IA en batch para lo abierto;
  produce nota, %, aprobado, feedback por pregunta y general.
- `RepositorioExamenes` (interfaz en Core, SQLite en Infrastructure).
- UI: `ExamenesVm` (historial), `CrearExamenVm` (asistente), `RendirExamenVm` (timer +
  navegación + autoguardado), vistas por tipo de pregunta, `ResultadoExamenVm`.

### Tokens / costo
- Generación y corrección de abiertas son las únicas llamadas a IA; lo objetivo es gratis.
- Tokens y costo del examen se guardan en `Examen` (mismo cálculo que #4).

---

## 6. LinkedIn (#6)

- Sección **"Acerca de"** en Configuración: nombre del autor, versión de la app, y botón
  al perfil `https://ar.linkedin.com/in/emmanuel-zelarayan/es` (abre el navegador).
- Ícono de LinkedIn discreto en el pie del menú lateral.

---

## 7. Quitar archivos antes de procesar (#7)

- En la pantalla previa al análisis (al elegir la carpeta), se listan los archivos
  detectados; cada uno con toggle/❌ para **excluirlo** (queda tachado). Solo se procesan
  los incluidos.
- La selección afecta el `fingerprint` del análisis (se calcula sobre el set incluido).

---

## Migración de esquema

- `schema_version` sube de `1` a `2`.
- Cambios **no destructivos**: `CREATE TABLE IF NOT EXISTS` para `AjustePrompt`,
  `CacheDerivado`, `Examen`, `PreguntaExamen`, `RespuestaUsuario`; `ALTER TABLE Unidad ADD
  COLUMN tokens_entrada/tokens_salida` (con guarda de "si no existe").

## Errores y resiliencia

- Saldo/tarifas: si el endpoint falla, mostrar "no disponible" sin romper la app.
- Caché: un miss o un archivo de caché corrupto degrada a "procesar normal".
- Simulador: si la generación IA devuelve JSON inválido, reintentar una vez y, si falla,
  mostrar error accionable sin perder el examen en borrador. Autoguardado evita perder una
  rendición en curso.

## Testing

- `ServicioPrompts` (composición, override, restaurar) — unitario.
- `CacheDerivados` (hit/miss por variante) — unitario.
- `ServicioCostos` (cálculo) — unitario.
- `CorrectorExamen` (corrección local de cada tipo objetivo; combinación con feedback IA
  fake) — unitario.
- `GeneradorExamen` con `FakeClienteIA` (parseo de JSON, tolerancia a JSON inválido).
- VMs del simulador (timer, navegación, autoguardado, cálculo de nota) — unitario.
- Vistas WPF se validan manualmente (decisión de proyecto: sin test de UI automatizado).

## Fases del plan de implementación

1. **Configuración + prompts editables + multi-idioma neutro** (`AjustePrompt`,
   `ServicioPrompts`, refactor de `ConstructorPipeline`/`DetectorTemas`, hash de entrada
   con prompt, pestaña Prompts).
2. **Caché global OCR/limpieza** (`CacheDerivado`, `CacheDerivados`, integración en el
   pipeline).
3. **Saldo + costos** (columnas de tokens, tarifas en config, `ClienteSaldo`,
   `ServicioCostos`, UI).
4. **Quitar archivos + "Acerca de"/LinkedIn** (selección previa al análisis, sección
   Acerca de).
5. **Simulador de exámenes** (modelo de datos, `GeneradorExamen`, `CorrectorExamen`,
   `RepositorioExamenes`, VMs y vistas por tipo, historial, resultado).

## Decisiones cerradas

- D1: edición de prompts = rol/estilo editable, formato protegido, restaurar default.
- D2: multi-idioma por autodetección, prompts neutros, sin perfiles ni contexto.
- D3: caché reutiliza OCR siempre y limpieza si prompt+modelo coinciden; copia al análisis.
- D4: mostrar saldo USD + costo por análisis; tarifas configurables.
- D5: simulador con 7 tipos; fuente configurable rápido/completo; IA corrige solo lo
  abierto; nota configurable 0-10 + %; composición mixta configurable; historial por
  análisis.
- D6: LinkedIn en "Acerca de" + pie del menú.
- D7: exclusión de archivos antes de procesar; afecta el fingerprint.
- D8: todo en una sola spec, implementación en 5 fases.

## Pendientes / backlog (no en esta spec)

- Purga/límite de tamaño de la caché de derivados (incluyendo archivos huérfanos: copiados a disco sin registro en SQLite si el proceso muere entre el copy y el insert).
- Re-detección automática de temas al editar el prompt de detección.
- Exportar examen/resultado a PDF.
- Tarifas con distinción cache-hit/cache-miss de Deepseek (hoy se simplifica a input/output).
