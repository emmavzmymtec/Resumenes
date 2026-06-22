-- ============================================================================
-- schema.sql  —  Esquema de estado de la App de Resúmenes (SQLite)
-- ----------------------------------------------------------------------------
-- SQLite es el ÍNDICE DE ESTADO, no un almacén de archivos: los contenidos
-- (imágenes, .txt, .md, .pdf) viven en disco; acá solo van metadatos, hashes,
-- estados y rutas. Solo la app .NET escribe esta base.
-- Codificación: UTF-8. Convención de timestamps: TEXT ISO-8601 (UTC).
-- ============================================================================

PRAGMA journal_mode = WAL;       -- escrituras concurrentes seguras
PRAGMA foreign_keys = ON;        -- integridad referencial
PRAGMA encoding = 'UTF-8';

-- ----------------------------------------------------------------------------
-- ANALISIS: una corrida completa sobre una carpeta de material.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Analisis (
    id              TEXT PRIMARY KEY,                 -- GUID o timestamp+slug (ASCII seguro)
    nombre          TEXT NOT NULL,
    carpeta_origen  TEXT NOT NULL,                    -- ruta absoluta seleccionada por el usuario
    fingerprint     TEXT NOT NULL,                    -- SHA-256 del set ordenado (ruta_rel, hash)
    estado          TEXT NOT NULL DEFAULT 'EnProceso'
                        CHECK (estado IN ('EnProceso','Completado','ConErrores','Obsoleto')),
    creado_en       TEXT NOT NULL,
    actualizado_en  TEXT NOT NULL
);

-- ----------------------------------------------------------------------------
-- ARCHIVO: cada documento de entrada, identificado por hash de contenido.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Archivo (
    id              TEXT PRIMARY KEY,                 -- primeros 16 hex del SHA-256 (estable)
    analisis_id     TEXT NOT NULL,
    nombre_original TEXT NOT NULL,
    ruta_relativa   TEXT NOT NULL,                    -- dentro de 00_fuentes/<id>/
    hash_sha256     TEXT NOT NULL,                    -- hash completo del contenido
    tamano_bytes    INTEGER NOT NULL,
    tipo            TEXT NOT NULL
                        CHECK (tipo IN ('Pdf','Doc','Docx','Ppt','Pptx','Txt','Otro')),
    paginas         INTEGER,
    creado_en       TEXT NOT NULL,
    FOREIGN KEY (analisis_id) REFERENCES Analisis(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_archivo_analisis ON Archivo(analisis_id);
CREATE INDEX IF NOT EXISTS ix_archivo_hash     ON Archivo(hash_sha256);

-- ----------------------------------------------------------------------------
-- TEMA: temas detectados/confirmados en la consolidación (orden = N de analisisfinalN).
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Tema (
    id                     TEXT PRIMARY KEY,          -- slug saneado + sufijo de unicidad
    analisis_id            TEXT NOT NULL,
    nombre                 TEXT NOT NULL,
    orden                  INTEGER NOT NULL,          -- N: 1..n (mapea a analisisfinalN)
    confirmado_por_usuario INTEGER NOT NULL DEFAULT 0 CHECK (confirmado_por_usuario IN (0,1)),
    FOREIGN KEY (analisis_id) REFERENCES Analisis(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_tema_analisis ON Tema(analisis_id);
CREATE UNIQUE INDEX IF NOT EXISTS ux_tema_orden ON Tema(analisis_id, orden);

-- ----------------------------------------------------------------------------
-- TEMA_ARCHIVO: mapeo N:M de trazabilidad (de qué archivos salió cada tema).
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS TemaArchivo (
    tema_id    TEXT NOT NULL,
    archivo_id TEXT NOT NULL,
    PRIMARY KEY (tema_id, archivo_id),
    FOREIGN KEY (tema_id)    REFERENCES Tema(id)    ON DELETE CASCADE,
    FOREIGN KEY (archivo_id) REFERENCES Archivo(id) ON DELETE CASCADE
);

-- ----------------------------------------------------------------------------
-- UNIDAD: la pieza atómica con estado (núcleo de idempotencia y reanudación).
--   - Etapas por-archivo  -> archivo_id NOT NULL, tema_id NULL
--   - Etapas por-tema      -> tema_id NOT NULL, archivo_id NULL
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Unidad (
    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
    analisis_id        TEXT NOT NULL,
    archivo_id         TEXT,
    tema_id            TEXT,
    etapa              TEXT NOT NULL
                          CHECK (etapa IN ('Captura','OcrBruto','LimpiezaIA',
                                           'ConsolidacionTemas','ResumenFinal','GeneracionPDF')),
    estado             TEXT NOT NULL DEFAULT 'Pendiente'
                          CHECK (estado IN ('Pendiente','EnProceso','Completado','Error','Obsoleto')),
    ruta_artefacto     TEXT,                          -- ruta en disco de la salida de esta unidad
    hash_entrada       TEXT,                          -- SHA-256 canónico de las entradas
    prompt_version     TEXT,                          -- solo etapas con IA
    modelo_ia          TEXT,                          -- solo etapas con IA
    tokens             INTEGER,
    fijado_por_usuario INTEGER NOT NULL DEFAULT 0 CHECK (fijado_por_usuario IN (0,1)),
    error_msg          TEXT,
    actualizado_en     TEXT NOT NULL,
    FOREIGN KEY (analisis_id) REFERENCES Analisis(id) ON DELETE CASCADE,
    FOREIGN KEY (archivo_id)  REFERENCES Archivo(id)  ON DELETE CASCADE,
    FOREIGN KEY (tema_id)     REFERENCES Tema(id)     ON DELETE CASCADE,
    -- una unidad refiere a un archivo O a un tema, no ambos ni ninguno
    CHECK ( (archivo_id IS NOT NULL) <> (tema_id IS NOT NULL) )
);
-- Índice único que sostiene la idempotencia (una unidad por archivo/tema y etapa).
-- COALESCE evita que múltiples NULL rompan la unicidad en SQLite.
CREATE UNIQUE INDEX IF NOT EXISTS ux_unidad
    ON Unidad(analisis_id, COALESCE(archivo_id,''), COALESCE(tema_id,''), etapa);
CREATE INDEX IF NOT EXISTS ix_unidad_estado ON Unidad(analisis_id, etapa, estado);

-- ----------------------------------------------------------------------------
-- EJECUCION: resumen por corrida de una etapa (timeline, costo, tokens, errores).
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS Ejecucion (
    id              INTEGER PRIMARY KEY AUTOINCREMENT,
    analisis_id     TEXT NOT NULL,
    etapa           TEXT NOT NULL,
    inicio          TEXT NOT NULL,
    fin             TEXT,
    ok_count        INTEGER NOT NULL DEFAULT 0,
    error_count     INTEGER NOT NULL DEFAULT 0,
    tokens          INTEGER NOT NULL DEFAULT 0,
    costo_estimado  REAL    NOT NULL DEFAULT 0,
    FOREIGN KEY (analisis_id) REFERENCES Analisis(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_ejecucion_analisis ON Ejecucion(analisis_id);

-- ----------------------------------------------------------------------------
-- AJUSTE_PROMPT: override editable por el usuario de la parte rol/estilo de un
-- prompt. Si no hay fila para una clave, se usa el default del código.
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS AjustePrompt (
    clave          TEXT PRIMARY KEY,   -- 'limpieza' | 'deteccion' | 'resumen'
    texto_editable TEXT NOT NULL,
    actualizado_en TEXT NOT NULL
);

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

-- ----------------------------------------------------------------------------
-- META: versión del esquema (para futuras migraciones; migraciones -> Backlog).
-- ----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS SchemaMeta (
    clave TEXT PRIMARY KEY,
    valor TEXT NOT NULL
);
INSERT INTO SchemaMeta (clave, valor) VALUES ('schema_version', '3')
ON CONFLICT(clave) DO UPDATE SET valor='3';
