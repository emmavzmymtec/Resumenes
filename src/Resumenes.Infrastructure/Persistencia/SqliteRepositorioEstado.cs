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
        // Migración: agregar columnas nuevas a bases existentes (ALTER TABLE ADD COLUMN con guarda).
        AsegurarColumna(con, "Unidad", "tokens_entrada", "INTEGER");
        AsegurarColumna(con, "Unidad", "tokens_salida", "INTEGER");
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

        // Limpiar exclusiones huérfanas (ExclusionArchivo no tiene FK a Analisis).
        using (var delExcl = con.CreateCommand())
        {
            delExcl.CommandText = "DELETE FROM ExclusionArchivo WHERE carpeta_origen = (SELECT carpeta_origen FROM Analisis WHERE id=$id);";
            delExcl.Parameters.AddWithValue("$id", id);
            delExcl.ExecuteNonQuery();
        }

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
                                   prompt_version, modelo_ia, tokens, tokens_entrada, tokens_salida,
                                   fijado_por_usuario, error_msg, actualizado_en
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
            Id = r.GetInt64(r.GetOrdinal("id")),
            AnalisisId = r.GetString(r.GetOrdinal("analisis_id")),
            ArchivoId = r.IsDBNull(r.GetOrdinal("archivo_id")) ? null : r.GetString(r.GetOrdinal("archivo_id")),
            TemaId = r.IsDBNull(r.GetOrdinal("tema_id")) ? null : r.GetString(r.GetOrdinal("tema_id")),
            Etapa = Enum.Parse<Etapa>(r.GetString(r.GetOrdinal("etapa"))),
            Estado = Enum.Parse<EstadoUnidad>(r.GetString(r.GetOrdinal("estado"))),
            RutaArtefacto = r.IsDBNull(r.GetOrdinal("ruta_artefacto")) ? null : r.GetString(r.GetOrdinal("ruta_artefacto")),
            HashEntrada = r.IsDBNull(r.GetOrdinal("hash_entrada")) ? null : r.GetString(r.GetOrdinal("hash_entrada")),
            PromptVersion = r.IsDBNull(r.GetOrdinal("prompt_version")) ? null : r.GetString(r.GetOrdinal("prompt_version")),
            ModeloIa = r.IsDBNull(r.GetOrdinal("modelo_ia")) ? null : r.GetString(r.GetOrdinal("modelo_ia")),
            Tokens = r.IsDBNull(r.GetOrdinal("tokens")) ? null : r.GetInt32(r.GetOrdinal("tokens")),
            TokensEntrada = r.IsDBNull(r.GetOrdinal("tokens_entrada")) ? null : r.GetInt32(r.GetOrdinal("tokens_entrada")),
            TokensSalida = r.IsDBNull(r.GetOrdinal("tokens_salida")) ? null : r.GetInt32(r.GetOrdinal("tokens_salida")),
            FijadoPorUsuario = r.GetInt32(r.GetOrdinal("fijado_por_usuario")) != 0,
            ErrorMsg = r.IsDBNull(r.GetOrdinal("error_msg")) ? null : r.GetString(r.GetOrdinal("error_msg")),
            ActualizadoEn = DateTime.Parse(r.GetString(r.GetOrdinal("actualizado_en")))
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
                                modelo_ia=$mi, tokens=$tok, tokens_entrada=$te, tokens_salida=$ts,
                                fijado_por_usuario=$fij, error_msg=$err, actualizado_en=$ac
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
        upd.Parameters.AddWithValue("$te", (object?)u.TokensEntrada ?? DBNull.Value);
        upd.Parameters.AddWithValue("$ts", (object?)u.TokensSalida ?? DBNull.Value);
        upd.Parameters.AddWithValue("$fij", u.FijadoPorUsuario ? 1 : 0);
        upd.Parameters.AddWithValue("$err", (object?)u.ErrorMsg ?? DBNull.Value);
        upd.Parameters.AddWithValue("$ac", u.ActualizadoEn.ToString("o"));
        int afectadas = upd.ExecuteNonQuery();

        if (afectadas == 0)
        {
            // No existía: INSERT
            using var ins = con.CreateCommand();
            ins.CommandText = @"INSERT INTO Unidad (analisis_id, archivo_id, tema_id, etapa, estado, ruta_artefacto,
                                    hash_entrada, prompt_version, modelo_ia, tokens, tokens_entrada, tokens_salida,
                                    fijado_por_usuario, error_msg, actualizado_en)
                                VALUES ($an,$arc,$t,$e,$est,$ruta,$he,$pv,$mi,$tok,$te,$ts,$fij,$err,$ac);";
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
            ins.Parameters.AddWithValue("$te", (object?)u.TokensEntrada ?? DBNull.Value);
            ins.Parameters.AddWithValue("$ts", (object?)u.TokensSalida ?? DBNull.Value);
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

    public IReadOnlyCollection<string> ObtenerExclusiones(string carpetaOrigen)
    {
        using var con = Abrir();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT ruta_relativa FROM ExclusionArchivo WHERE carpeta_origen=$c;";
        cmd.Parameters.AddWithValue("$c", carpetaOrigen);
        using var r = cmd.ExecuteReader();
        var lista = new List<string>();
        while (r.Read()) lista.Add(r.GetString(0));
        return lista;
    }

    public void GuardarExclusiones(string carpetaOrigen, IReadOnlyCollection<string> rutasRelativas)
    {
        using var con = Abrir();
        using var tx = con.BeginTransaction();
        using (var del = con.CreateCommand())
        {
            del.Transaction = tx;
            del.CommandText = "DELETE FROM ExclusionArchivo WHERE carpeta_origen=$c;";
            del.Parameters.AddWithValue("$c", carpetaOrigen);
            del.ExecuteNonQuery();
        }
        foreach (var ruta in rutasRelativas)
        {
            using var ins = con.CreateCommand();
            ins.Transaction = tx;
            ins.CommandText = "INSERT OR IGNORE INTO ExclusionArchivo (carpeta_origen, ruta_relativa) VALUES ($c,$r);";
            ins.Parameters.AddWithValue("$c", carpetaOrigen);
            ins.Parameters.AddWithValue("$r", ruta);
            ins.ExecuteNonQuery();
        }
        tx.Commit();
    }

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
}
