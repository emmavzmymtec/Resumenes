# Changelog

Todos los cambios notables del proyecto se documentan en este archivo.
Formato basado en [Keep a Changelog](https://keepachangelog.com/es/1.1.0/);
versionado [SemVer](https://semver.org/lang/es/).

## [1.1.0] — 2026-06-23

### Añadido

- **Simulador de exámenes**: genera exámenes con IA a partir del material del análisis. Tipos de pregunta: opción múltiple (una o varias respuestas), verdadero/falso justificado, desarrollo, desarrollo con ítems, completar espacios y emparejar columnas. Incluye **tiempo límite**, **corrección automática con feedback por pregunta y nota**, historial de exámenes por análisis y opción de **reintentar**.
- **Prompts editables desde Configuración** (corrección de OCR, detección de temas y resumen), con opción de restaurar los valores por defecto. El formato de salida del PDF permanece protegido.

### Cambiado

- **Los prompts por defecto ahora son neutros y multi-idioma**: la IA detecta el idioma del material y resume en ese idioma (ya no se fuerza español).

### Corregido

- **Navegación coherente**: se reemplazó la flecha "atrás" nativa por un botón **"Volver"** explícito en cada pantalla. Antes, al volver podían aparecer pantallas en blanco, formularios sin contexto o pantallas de progreso ya terminadas.
- Se evita la **autoentrega en segundo plano** de un examen cronometrado abandonado y la **navegación fantasma** de un análisis/generación dejado a medias (al salir de la pantalla se cancela la tarea o se detiene el cronómetro).
- **Indicador de carga** al reintentar un examen, mientras la IA genera el examen nuevo.

### Importante

- Tras actualizar, los análisis existentes se reprocesan una vez: la limpieza y el resumen ahora incluyen el prompt y el modelo en su hash de entrada, por lo que se regeneran en la próxima corrida (costo de tokens puntual).

## [1.0.0] — 2026-06-19

Primera versión. Aplicación de escritorio (.NET 9 / WPF) que convierte una carpeta de
material de estudio en PDFs de resumen por tema.

### Funcionalidades

- **Ingesta universal por OCR** (PaddleOCR PP-OCRv6); el TXT se ingiere directo sin OCR.
- **Conversión Office → PDF** con **LibreOffice portable** y **fallback a Microsoft
  Office** (Word/PowerPoint/Excel vía COM) si LibreOffice falla.
- **Detección de temas** + **un PDF de resumen por tema**.
- **Resúmenes con IA** (Deepseek); motor de PDF de estilo fijo en Python (fpdf2 + fuentes
  DejaVu). La IA solo emite contenido estructurado; nunca ejecuta código.
- **Prompts editables** (detección de temas y resumen) + **re-procesar con un nuevo
  prompt** reusando la información ya extraída (sin re-OCR).
- **Instalador liviano** (Inno Setup, ~6 MB) con **descarga de dependencias** en el
  onboarding (Python portable, LibreOffice, modelos OCR) con progreso, verificación
  SHA-256 y reanudación. Bundles hospedados en **GitHub Releases** (`v1.0.0`).
- **Ícono propio** de la app (birrete + libro).

### Correcciones (estabilización de la 1.0.0)

- **UI:** el botón *"Guardar key"* no se habilitaba — el `PasswordBox` de WPF-UI no
  propagaba el texto al ViewModel; se cambió el binding a `Mode=TwoWay`.
- **LibreOffice:** diálogo *"bootstrap.ini está dañado"* en PCs con otra instalación de
  LibreOffice/OpenOffice — se neutralizan las variables de entorno heredadas
  (`URE_BOOTSTRAP`, `UNO_PATH`, etc.) al lanzar `soffice` y se agrega un timeout (90 s);
  si aun así falla, se recurre al fallback a Microsoft Office.
- **Instalador:** el bundle de modelos OCR ya no borra el cache global `~/.paddlex`
  (extracción no destructiva, `limpiarDestino:false`).

### Problemas conocidos / workarounds

- **Adobe Acrobat (Modo protegido)** no abre PDFs ubicados en `%LOCALAPPDATA%`. Usar
  **"Exportar PDFs"** (copia a Documentos/Escritorio) o desactivar el Modo protegido en
  Acrobat. Ver [README → Solución de problemas](README.md).

[1.1.0]: https://github.com/emmavzmymtec/Resumenes/releases/tag/v1.1.0
[1.0.0]: https://github.com/emmavzmymtec/Resumenes/releases/tag/v1.0.0
