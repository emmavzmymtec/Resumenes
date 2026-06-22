using System.Reflection;
using Microsoft.Data.Sqlite;
using Resumenes.Core.Interfaces;
using Resumenes.Core.Modelos;

namespace Resumenes.Infrastructure.Persistencia;

public class SqliteRepositorioEstado(string cadenaConexion) : IRepositorioEstado
{
    private SqliteConnection Abrir()
    {
        var con = new SqliteConnection(cadenaConexion);
        con.Open();
        using var pragma = con.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return con;
    }

    public void InicializarEsquema()
    {
        var asm = Assembly.GetExecutingAssembly();
        var nombre = asm.GetManifestResourceNames().Single(n => n.EndsWith("schema.sql"));
        using var stream = asm.GetManifestResourceStream(nombre)!;
        using var reader = new StreamReader(stream);
        var sql = reader.ReadToEnd();
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
        // Limpiar el pool para que las conexiones futuras no hereden PRAGMAs de sesión
        // (foreign_keys=ON queda configurado por schema.sql en esta conexión; el pool lo reutilizaría).
        SqliteConnection.ClearPool(con);
    }

    public Analisis? ObtenerAnalisisPorFingerprint(string fingerprint)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT id, nombre, carpeta_origen, fingerprint, estado, creado_en, actualizado_en
                            FROM Analisis WHERE fingerprint = $fp LIMIT 1;";
        cmd.Parameters.AddWithValue("$fp", fingerprint);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Analisis(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
            Enum.Parse<EstadoAnalisis>(r.GetString(4)),
            DateTime.Parse(r.GetString(5)), DateTime.Parse(r.GetString(6)));
    }

    public void GuardarAnalisis(Analisis a)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO Analisis (id, nombre, carpeta_origen, fingerprint, estado, creado_en, actualizado_en)
                            VALUES ($id,$n,$c,$fp,$e,$cr,$ac)
                            ON CONFLICT(id) DO UPDATE SET nombre=$n, carpeta_origen=$c, fingerprint=$fp,
                                estado=$e, actualizado_en=$ac;";
        cmd.Parameters.AddWithValue("$id", a.Id);
        cmd.Parameters.AddWithValue("$n", a.Nombre);
        cmd.Parameters.AddWithValue("$c", a.CarpetaOrigen);
        cmd.Parameters.AddWithValue("$fp", a.Fingerprint);
        cmd.Parameters.AddWithValue("$e", a.Estado.ToString());
        cmd.Parameters.AddWithValue("$cr", a.CreadoEn.ToString("o"));
        cmd.Parameters.AddWithValue("$ac", a.ActualizadoEn.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<Analisis> ListarAnalisis()
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT id, nombre, carpeta_origen, fingerprint, estado, creado_en, actualizado_en
                            FROM Analisis ORDER BY actualizado_en DESC;";
        using var r = cmd.ExecuteReader();
        var lista = new List<Analisis>();
        while (r.Read())
            lista.Add(new Analisis(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3),
                Enum.Parse<EstadoAnalisis>(r.GetString(4)), DateTime.Parse(r.GetString(5)), DateTime.Parse(r.GetString(6))));
        return lista;
    }

    public void EliminarAnalisis(string id)
    {
        // foreign_keys=ON (Abrir) + ON DELETE CASCADE en el esquema ⇒ borrar el padre
        // elimina automáticamente Archivo, Tema, TemaArchivo, Unidad y Ejecucion asociados.
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM Analisis WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public Archivo? ObtenerArchivo(string id)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT id, analisis_id, nombre_original, ruta_relativa, hash_sha256, tamano_bytes, tipo, paginas, creado_en
                            FROM Archivo WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Archivo(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
            r.GetInt64(5), Enum.Parse<TipoArchivo>(r.GetString(6)),
            r.IsDBNull(7) ? null : r.GetInt32(7), DateTime.Parse(r.GetString(8)));
    }

    public void GuardarArchivo(Archivo a)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO Archivo (id, analisis_id, nombre_original, ruta_relativa, hash_sha256, tamano_bytes, tipo, paginas, creado_en)
                            VALUES ($id,$an,$no,$rr,$h,$t,$tipo,$pag,$cr)
                            ON CONFLICT(id) DO UPDATE SET ruta_relativa=$rr, paginas=$pag;";
        cmd.Parameters.AddWithValue("$id", a.Id);
        cmd.Parameters.AddWithValue("$an", a.AnalisisId);
        cmd.Parameters.AddWithValue("$no", a.NombreOriginal);
        cmd.Parameters.AddWithValue("$rr", a.RutaRelativa);
        cmd.Parameters.AddWithValue("$h", a.HashSha256);
        cmd.Parameters.AddWithValue("$t", a.TamanoBytes);
        cmd.Parameters.AddWithValue("$tipo", a.Tipo.ToString());
        cmd.Parameters.AddWithValue("$pag", (object?)a.Paginas ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cr", a.CreadoEn.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public Tema? ObtenerTema(string id)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT id, analisis_id, nombre, orden, confirmado_por_usuario FROM Tema WHERE id=$id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Tema(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt32(3), r.GetInt32(4) != 0);
    }

    public void GuardarTema(Tema t)
    {
        using var con = Abrir();

        // Liberar el "slot" (analisis_id, orden) si lo ocupa OTRO tema (id distinto):
        // al re-detectar, los temas nuevos reutilizan orden 1,2,3… y violarían
        // UNIQUE(analisis_id, orden). Borrar el tema obsoleto (cascada en TemaArchivo/Unidad)
        // y conservar el actual si ya existe (mismo id → ON CONFLICT actualiza, sin perder Unidades).
        using (var liberar = con.CreateCommand())
        {
            liberar.CommandText = "DELETE FROM Tema WHERE analisis_id=$an AND orden=$o AND id<>$id;";
            liberar.Parameters.AddWithValue("$an", t.AnalisisId);
            liberar.Parameters.AddWithValue("$o", t.Orden);
            liberar.Parameters.AddWithValue("$id", t.Id);
            liberar.ExecuteNonQuery();
        }

        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO Tema (id, analisis_id, nombre, orden, confirmado_por_usuario)
                            VALUES ($id,$an,$n,$o,$c)
                            ON CONFLICT(id) DO UPDATE SET nombre=$n, orden=$o, confirmado_por_usuario=$c;";
        cmd.Parameters.AddWithValue("$id", t.Id);
        cmd.Parameters.AddWithValue("$an", t.AnalisisId);
        cmd.Parameters.AddWithValue("$n", t.Nombre);
        cmd.Parameters.AddWithValue("$o", t.Orden);
        cmd.Parameters.AddWithValue("$c", t.ConfirmadoPorUsuario ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    public void GuardarTemaArchivo(string temaId, string archivoId)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO TemaArchivo (tema_id, archivo_id) VALUES ($t,$a);";
        cmd.Parameters.AddWithValue("$t", temaId);
        cmd.Parameters.AddWithValue("$a", archivoId);
        cmd.ExecuteNonQuery();
    }

    public Unidad? ObtenerUnidad(string analisisId, string? archivoId, string? temaId, Etapa etapa)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"SELECT id, analisis_id, archivo_id, tema_id, etapa, estado, ruta_artefacto, hash_entrada,
                                   prompt_version, modelo_ia, tokens, fijado_por_usuario, error_msg, actualizado_en
                            FROM Unidad
                            WHERE analisis_id=$an AND COALESCE(archivo_id,'')=$arc
                              AND COALESCE(tema_id,'')=$t AND etapa=$e;";
        cmd.Parameters.AddWithValue("$an", analisisId);
        cmd.Parameters.AddWithValue("$arc", archivoId ?? "");
        cmd.Parameters.AddWithValue("$t", temaId ?? "");
        cmd.Parameters.AddWithValue("$e", etapa.ToString());
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new Unidad
        {
            Id = r.GetInt64(0),
            AnalisisId = r.GetString(1),
            ArchivoId = r.IsDBNull(2) ? null : r.GetString(2),
            TemaId = r.IsDBNull(3) ? null : r.GetString(3),
            Etapa = Enum.Parse<Etapa>(r.GetString(4)),
            Estado = Enum.Parse<EstadoUnidad>(r.GetString(5)),
            RutaArtefacto = r.IsDBNull(6) ? null : r.GetString(6),
            HashEntrada = r.IsDBNull(7) ? null : r.GetString(7),
            PromptVersion = r.IsDBNull(8) ? null : r.GetString(8),
            ModeloIa = r.IsDBNull(9) ? null : r.GetString(9),
            Tokens = r.IsDBNull(10) ? null : r.GetInt32(10),
            FijadoPorUsuario = r.GetInt32(11) != 0,
            ErrorMsg = r.IsDBNull(12) ? null : r.GetString(12),
            ActualizadoEn = DateTime.Parse(r.GetString(13))
        };
    }

    public void GuardarUnidad(Unidad u)
    {
        using var con = Abrir();

        // Upsert manual: UPDATE si existe por clave natural, INSERT si no.
        // SQLite no soporta ON CONFLICT con índices por expresión (COALESCE).
        using var upd = con.CreateCommand();
        upd.CommandText = @"UPDATE Unidad
                            SET estado=$est, ruta_artefacto=$ruta, hash_entrada=$he, prompt_version=$pv,
                                modelo_ia=$mi, tokens=$tok, fijado_por_usuario=$fij, error_msg=$err, actualizado_en=$ac
                            WHERE analisis_id=$an AND COALESCE(archivo_id,'')=$arc
                              AND COALESCE(tema_id,'')=$t AND etapa=$e;";
        upd.Parameters.AddWithValue("$an", u.AnalisisId);
        upd.Parameters.AddWithValue("$arc", u.ArchivoId ?? "");
        upd.Parameters.AddWithValue("$t", u.TemaId ?? "");
        upd.Parameters.AddWithValue("$e", u.Etapa.ToString());
        upd.Parameters.AddWithValue("$est", u.Estado.ToString());
        upd.Parameters.AddWithValue("$ruta", (object?)u.RutaArtefacto ?? DBNull.Value);
        upd.Parameters.AddWithValue("$he", (object?)u.HashEntrada ?? DBNull.Value);
        upd.Parameters.AddWithValue("$pv", (object?)u.PromptVersion ?? DBNull.Value);
        upd.Parameters.AddWithValue("$mi", (object?)u.ModeloIa ?? DBNull.Value);
        upd.Parameters.AddWithValue("$tok", (object?)u.Tokens ?? DBNull.Value);
        upd.Parameters.AddWithValue("$fij", u.FijadoPorUsuario ? 1 : 0);
        upd.Parameters.AddWithValue("$err", (object?)u.ErrorMsg ?? DBNull.Value);
        upd.Parameters.AddWithValue("$ac", u.ActualizadoEn.ToString("o"));
        int afectadas = upd.ExecuteNonQuery();

        if (afectadas == 0)
        {
            // No existía: INSERT
            using var ins = con.CreateCommand();
            ins.CommandText = @"INSERT INTO Unidad (analisis_id, archivo_id, tema_id, etapa, estado, ruta_artefacto,
                                    hash_entrada, prompt_version, modelo_ia, tokens, fijado_por_usuario, error_msg, actualizado_en)
                                VALUES ($an,$arc,$t,$e,$est,$ruta,$he,$pv,$mi,$tok,$fij,$err,$ac);";
            ins.Parameters.AddWithValue("$an", u.AnalisisId);
            ins.Parameters.AddWithValue("$arc", (object?)u.ArchivoId ?? DBNull.Value);
            ins.Parameters.AddWithValue("$t", (object?)u.TemaId ?? DBNull.Value);
            ins.Parameters.AddWithValue("$e", u.Etapa.ToString());
            ins.Parameters.AddWithValue("$est", u.Estado.ToString());
            ins.Parameters.AddWithValue("$ruta", (object?)u.RutaArtefacto ?? DBNull.Value);
            ins.Parameters.AddWithValue("$he", (object?)u.HashEntrada ?? DBNull.Value);
            ins.Parameters.AddWithValue("$pv", (object?)u.PromptVersion ?? DBNull.Value);
            ins.Parameters.AddWithValue("$mi", (object?)u.ModeloIa ?? DBNull.Value);
            ins.Parameters.AddWithValue("$tok", (object?)u.Tokens ?? DBNull.Value);
            ins.Parameters.AddWithValue("$fij", u.FijadoPorUsuario ? 1 : 0);
            ins.Parameters.AddWithValue("$err", (object?)u.ErrorMsg ?? DBNull.Value);
            ins.Parameters.AddWithValue("$ac", u.ActualizadoEn.ToString("o"));
            ins.ExecuteNonQuery();
        }
    }

    public string? ObtenerAjustePrompt(string clave)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT texto_editable FROM AjustePrompt WHERE clave = $c;";
        cmd.Parameters.AddWithValue("$c", clave);
        var r = cmd.ExecuteScalar();
        return r is string s ? s : null;
    }

    public void GuardarAjustePrompt(string clave, string texto)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = @"INSERT INTO AjustePrompt (clave, texto_editable, actualizado_en)
                            VALUES ($c, $t, $a)
                            ON CONFLICT(clave) DO UPDATE SET texto_editable=$t, actualizado_en=$a;";
        cmd.Parameters.AddWithValue("$c", clave);
        cmd.Parameters.AddWithValue("$t", texto);
        cmd.Parameters.AddWithValue("$a", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public void EliminarAjustePrompt(string clave)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "DELETE FROM AjustePrompt WHERE clave = $c;";
        cmd.Parameters.AddWithValue("$c", clave);
        cmd.ExecuteNonQuery();
    }
}
