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
