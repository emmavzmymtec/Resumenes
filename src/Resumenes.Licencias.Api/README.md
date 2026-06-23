# API de Licencias

Minimal API (.NET 9) para activar/validar/administrar licencias de la app Resúmenes.
Esquema creado con `EnsureCreated()`. Postgres en Railway, SQLite en local.

## Variables de entorno

| Variable | Para qué | Dónde |
|---|---|---|
| `FIRMA_PRIVADA_PEM` | Clave privada EC (P-256) para firmar tokens | Railway (secreto) |
| `ADMIN_KEY` | Secreto de los endpoints `/admin/*` | Railway (secreto) |
| `DATABASE_URL` | Postgres (la inyecta Railway al sumar el plugin) | Railway |
| `PORT` | Puerto de escucha (lo inyecta Railway) | Railway |

Sin `DATABASE_URL` la API usa SQLite (`licencias.db`) — útil para correr local.

## Generar el par de claves (una sola vez)

    dotnet run --project src/Resumenes.Licencias.Api -- gen-keys

Copiá el bloque **privado** a `FIRMA_PRIVADA_PEM` en Railway. Guardá el bloque
**público**: se embebe en el cliente (fase futura). NO commitear ninguna de las dos.

## Despliegue en Railway

1. Nuevo servicio → "Deploy from repo" apuntando a este subdirectorio (o Root
   Directory = `src/Resumenes.Licencias.Api`). Railway detecta el `Dockerfile`.
2. Agregar el plugin **PostgreSQL** → setea `DATABASE_URL` automáticamente.
3. Cargar variables `FIRMA_PRIVADA_PEM` y `ADMIN_KEY`.
4. Deploy. Probar `GET https://<tu-dominio>.railway.app/salud` → `ok`.

## Correr local

    dotnet run --project src/Resumenes.Licencias.Api
    # usa SQLite; setear ADMIN_KEY y FIRMA_PRIVADA_PEM en el entorno primero

## Limitaciones conocidas

1. **Límite de máquinas bajo concurrencia (TOCTOU):** la verificación `Count >= MaxMaquinas` en `ServicioActivacion` se realiza en memoria antes de `SaveChangesAsync`. Dos activaciones concurrentes con HWIDs distintos sobre la misma licencia al límite podrían exceder `MaxMaquinas` por una carrera de datos. El índice único `(LicenciaId, Hwid)` no lo previene porque los HWIDs son distintos. El riesgo es bajísimo al volumen esperado (activaciones manuales, no hot-path). El fix real —transacción serializable o constraint a nivel DB— queda para una fase posterior.

2. **Rate limiting detrás del proxy de Railway:** el limiter particiona por `RemoteIpAddress`, que detrás del proxy de Railway suele ser la IP del proxy → el límite efectivo es global (60 req/min para todo el tráfico junto), no por-cliente. El endpoint `/salud` queda excluido del limiter para que el healthcheck periódico de Railway no consuma cuota. Si se requiere un límite real por-cliente, evaluar `app.UseForwardedHeaders()` para honrar `X-Forwarded-For`.

3. **TLS a Postgres (`Trust Server Certificate=true`):** la conexión a Postgres cifra el tráfico pero no valida la cadena del certificado del servidor (configuración deliberada para Railway, cuyos certificados no siempre encadenan a una CA pública; con la conexión privada API↔Postgres dentro del mismo proyecto Railway el riesgo real es bajo). Si Railway publica su CA de Postgres, migrar a `SSL Mode=VerifyFull` más `Root Certificate` apuntando al CA correspondiente.
