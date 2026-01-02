using Dapper;
using MusicaCatalogo.Data;
using MusicaCatalogo.Data.Entidades;
using System.Data;

namespace MusicaCatalogo.Services.Repositorios;

/// <summary>
/// Repositorio para operaciones CRUD de álbumes.
/// </summary>
public class RepositorioAlbumes : RepositorioBase
{
    public RepositorioAlbumes(BaseDatos db) : base(db) { }

    /// <summary>Verifica si la columna es_single existe en albumes.</summary>
    private async Task<bool> ExisteColumnaEsSingle(IDbConnection conn)
    {
        var columnas = await conn.QueryAsync<string>("SELECT name FROM pragma_table_info('albumes')");
        return columnas.Contains("es_single");
    }

    /// <summary>Lista todos los álbumes.</summary>
    public async Task<List<AlbumResumen>> ListarAlbumesAsync(string? filtro = null, int limite = 100)
    {
        using var conn = ObtenerConexion();
        
        var tieneEsSingle = await ExisteColumnaEsSingle(conn);
        var esSingleCol = tieneEsSingle ? "COALESCE(a.es_single, 0)" : "0";

        var sql = $"""
            SELECT a.id AS Id, a.nombre AS Nombre, i.nombre AS Interprete, a.id_interprete AS IdInterprete,
                   a.anio AS Anio, CASE WHEN a.portada IS NOT NULL THEN 1 ELSE 0 END AS TienePortada,
                   (SELECT COUNT(*) FROM temas WHERE id_album = a.id) + 
                   (SELECT COUNT(*) FROM temas_cd WHERE id_album = a.id) AS TotalCanciones,
                   {esSingleCol} AS EsSingle
            FROM albumes a
            LEFT JOIN interpretes i ON a.id_interprete = i.id
            """;

        if (!string.IsNullOrWhiteSpace(filtro))
        {
            sql += " WHERE a.nombre LIKE @patron OR i.nombre LIKE @patron";
        }

        sql += " ORDER BY a.nombre LIMIT @limite";

        var patron = $"%{filtro}%";
        var resultado = await conn.QueryAsync<AlbumResumen>(sql, new { patron, limite });
        return resultado.ToList();
    }

    /// <summary>Obtiene el detalle de un álbum con sus canciones.</summary>
    public async Task<AlbumDetalle?> ObtenerAlbumAsync(int id)
    {
        using var conn = ObtenerConexion();
        
        var tieneEsSingle = await ExisteColumnaEsSingle(conn);
        var esSingleCol = tieneEsSingle ? "COALESCE(a.es_single, 0)" : "0";

        var album = await conn.QueryFirstOrDefaultAsync<AlbumDetalle>($"""
            SELECT a.id AS Id, a.nombre AS Nombre, i.nombre AS Interprete, a.id_interprete AS IdInterprete,
                   a.anio AS Anio, CASE WHEN a.portada IS NOT NULL THEN 1 ELSE 0 END AS TienePortada,
                   {esSingleCol} AS EsSingle
            FROM albumes a
            LEFT JOIN interpretes i ON a.id_interprete = i.id
            WHERE a.id = @id
            """, new { id });

        if (album == null)
            return null;

        // Obtener canciones del álbum
        var cancionesCassette = await conn.QueryAsync<CancionEnAlbum>("""
            SELECT t.id AS Id, 'cassette' AS Tipo, t.tema AS Tema, i.nombre AS Interprete,
                   t.num_formato AS numMedio, (t.lado || ':' || t.desde || '-' || t.hasta) AS Posicion,
                   t.link_externo AS LinkExterno
            FROM temas t
            JOIN interpretes i ON t.id_interprete = i.id
            WHERE t.id_album = @id
            ORDER BY t.num_formato, t.lado, t.desde
            """, new { id });

        var cancionesCd = await conn.QueryAsync<CancionEnAlbum>("""
            SELECT t.id AS Id, 'cd' AS Tipo, t.tema AS Tema, i.nombre AS Interprete,
                   t.num_formato AS numMedio, CAST(t.ubicacion AS TEXT) AS Posicion,
                   t.link_externo AS LinkExterno
            FROM temas_cd t
            JOIN interpretes i ON t.id_interprete = i.id
            WHERE t.id_album = @id
            ORDER BY t.num_formato, t.ubicacion
            """, new { id });

        album.Canciones = cancionesCassette.Concat(cancionesCd).ToList();
        return album;
    }

    /// <summary>Crea un nuevo álbum.</summary>
    public async Task<CrudResponse> CrearAlbumAsync(AlbumRequest request)
    {
        using var conn = ObtenerConexion();

        try
        {
            // Resolver intérprete si se proporciona nombre
            int? idInterprete = request.IdInterprete;
            if (!idInterprete.HasValue && !string.IsNullOrWhiteSpace(request.NombreInterprete))
            {
                var existente = await conn.QueryFirstOrDefaultAsync<int?>(
                    "SELECT id FROM interpretes WHERE nombre = @nombre",
                    new { nombre = request.NombreInterprete });

                if (existente.HasValue)
                {
                    idInterprete = existente.Value;
                }
                else
                {
                    var maxId = await conn.QueryFirstOrDefaultAsync<int?>("SELECT MAX(id) FROM interpretes") ?? 0;
                    idInterprete = maxId + 1;
                    await conn.ExecuteAsync(
                        "INSERT INTO interpretes (id, nombre) VALUES (@id, @nombre)",
                        new { id = idInterprete, nombre = request.NombreInterprete });
                }
            }

            // Verificar si existe la columna es_single
            var tieneEsSingle = await ExisteColumnaEsSingle(conn);
            
            if (tieneEsSingle)
            {
                await conn.ExecuteAsync("""
                    INSERT INTO albumes (nombre, id_interprete, anio, es_single, fecha_creacion)
                    VALUES (@Nombre, @idInterprete, @Anio, @EsSingle, datetime('now'))
                    """, new { request.Nombre, idInterprete, request.Anio, EsSingle = request.EsSingle ? 1 : 0 });
            }
            else
            {
                await conn.ExecuteAsync("""
                    INSERT INTO albumes (nombre, id_interprete, anio, fecha_creacion)
                    VALUES (@Nombre, @idInterprete, @Anio, datetime('now'))
                    """, new { request.Nombre, idInterprete, request.Anio });
            }

            var idCreado = await conn.QueryFirstAsync<int>("SELECT last_insert_rowid()");
            return new CrudResponse { Exito = true, Mensaje = "Álbum creado correctamente", IdCreado = idCreado };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Actualiza un álbum existente.</summary>
    public async Task<CrudResponse> ActualizarAlbumAsync(int id, AlbumRequest request)
    {
        using var conn = ObtenerConexion();

        try
        {
            int? idInterprete = request.IdInterprete;
            if (!idInterprete.HasValue && !string.IsNullOrWhiteSpace(request.NombreInterprete))
            {
                var existente = await conn.QueryFirstOrDefaultAsync<int?>(
                    "SELECT id FROM interpretes WHERE nombre = @nombre",
                    new { nombre = request.NombreInterprete });

                if (existente.HasValue)
                {
                    idInterprete = existente.Value;
                }
                else
                {
                    var maxId = await conn.QueryFirstOrDefaultAsync<int?>("SELECT MAX(id) FROM interpretes") ?? 0;
                    idInterprete = maxId + 1;
                    await conn.ExecuteAsync(
                        "INSERT INTO interpretes (id, nombre) VALUES (@id, @nombre)",
                        new { id = idInterprete, nombre = request.NombreInterprete });
                }
            }

            // Verificar si existe la columna es_single
            var tieneEsSingle = await ExisteColumnaEsSingle(conn);
            int rows;
            
            if (tieneEsSingle)
            {
                rows = await conn.ExecuteAsync("""
                    UPDATE albumes SET nombre = @Nombre, id_interprete = @idInterprete, anio = @Anio, es_single = @EsSingle
                    WHERE id = @id
                    """, new { id, request.Nombre, idInterprete, request.Anio, EsSingle = request.EsSingle ? 1 : 0 });
            }
            else
            {
                rows = await conn.ExecuteAsync("""
                    UPDATE albumes SET nombre = @Nombre, id_interprete = @idInterprete, anio = @Anio
                    WHERE id = @id
                    """, new { id, request.Nombre, idInterprete, request.Anio });
            }

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Álbum no encontrado" };

            return new CrudResponse { Exito = true, Mensaje = "Álbum actualizado correctamente" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Elimina un álbum (no elimina las canciones, solo desvincula).</summary>
    public async Task<CrudResponse> EliminarAlbumAsync(int id)
    {
        using var conn = ObtenerConexion();

        try
        {
            // Desvincular canciones del álbum
            await conn.ExecuteAsync("UPDATE temas SET id_album = NULL WHERE id_album = @id", new { id });
            await conn.ExecuteAsync("UPDATE temas_cd SET id_album = NULL WHERE id_album = @id", new { id });

            var rows = await conn.ExecuteAsync("DELETE FROM albumes WHERE id = @id", new { id });

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Álbum no encontrado" };

            return new CrudResponse { Exito = true, Mensaje = "Álbum eliminado correctamente" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Guarda la portada de un álbum.</summary>
    public async Task<CrudResponse> GuardarPortadaAlbumAsync(int id, byte[] portada)
    {
        using var conn = ObtenerConexion();

        try
        {
            var rows = await conn.ExecuteAsync(
                "UPDATE albumes SET portada = @portada WHERE id = @id",
                new { id, portada });

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Álbum no encontrado" };

            return new CrudResponse { Exito = true, Mensaje = "Portada guardada correctamente" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Obtiene la portada de un álbum.</summary>
    public async Task<byte[]?> ObtenerPortadaAlbumAsync(int id)
    {
        using var conn = ObtenerConexion();
        return await conn.QueryFirstOrDefaultAsync<byte[]?>(
            "SELECT portada FROM albumes WHERE id = @id", new { id });
    }

    /// <summary>Obtiene canciones disponibles para asignar a un álbum (con filtro opcional).</summary>
    public async Task<List<CancionDisponible>> ObtenerCancionesDisponiblesAsync(string? filtro, int? excluirAlbumId, int limite = 200, bool soloSinAlbum = false)
    {
        using var conn = ObtenerConexion();

        var resultados = new List<CancionDisponible>();
        var patronFiltro = string.IsNullOrWhiteSpace(filtro) ? "%" : $"%{filtro}%";

        // Obtener de cassettes
        var whereClauseCassette = "WHERE t.tema LIKE @filtro";
        if (soloSinAlbum) whereClauseCassette += " AND (t.id_album IS NULL OR t.id_album = 0)";
        if (excluirAlbumId.HasValue) whereClauseCassette += " AND (t.id_album IS NULL OR t.id_album != @excluirAlbumId)";

        var cassettes = await conn.QueryAsync<CancionDisponible>($"""
            SELECT t.id AS Id, 'cassette' AS Tipo, t.tema AS Tema, i.nombre AS Interprete,
                   t.num_formato AS numMedio, t.id_album AS IdAlbum
            FROM temas t
            JOIN interpretes i ON t.id_interprete = i.id
            {whereClauseCassette}
            ORDER BY t.tema
            LIMIT @limite
            """, new { filtro = patronFiltro, excluirAlbumId, limite });
        resultados.AddRange(cassettes);

        // Obtener de CDs
        var whereClauseCd = "WHERE t.tema LIKE @filtro";
        if (soloSinAlbum) whereClauseCd += " AND (t.id_album IS NULL OR t.id_album = 0)";
        if (excluirAlbumId.HasValue) whereClauseCd += " AND (t.id_album IS NULL OR t.id_album != @excluirAlbumId)";

        var cds = await conn.QueryAsync<CancionDisponible>($"""
            SELECT t.id AS Id, 'cd' AS Tipo, t.tema AS Tema, i.nombre AS Interprete,
                   t.num_formato AS numMedio, t.id_album AS IdAlbum
            FROM temas_cd t
            JOIN interpretes i ON t.id_interprete = i.id
            {whereClauseCd}
            ORDER BY t.tema
            LIMIT @limite
            """, new { filtro = patronFiltro, excluirAlbumId, limite });
        resultados.AddRange(cds);

        return resultados.OrderBy(c => c.Tema).Take(limite).ToList();
    }

    /// <summary>Asigna múltiples canciones a un álbum.</summary>
    public async Task<CrudResponse> AsignarCancionesAlbumAsync(int idAlbum, AsignarCancionesRequest request)
    {
        using var conn = ObtenerConexion();

        try
        {
            foreach (var cancion in request.Canciones)
            {
                if (cancion.Tipo.ToLower() == "cassette")
                {
                    await conn.ExecuteAsync(
                        "UPDATE temas SET id_album = @idAlbum WHERE id = @id",
                        new { idAlbum, id = cancion.Id });
                }
                else
                {
                    await conn.ExecuteAsync(
                        "UPDATE temas_cd SET id_album = @idAlbum WHERE id = @id",
                        new { idAlbum, id = cancion.Id });
                }
            }

            return new CrudResponse { Exito = true, Mensaje = $"Se asignaron {request.Canciones.Count} canciones al álbum" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Asigna una sola canción a un álbum (o la quita si idAlbum es null).</summary>
    public async Task<CrudResponse> AsignarCancionAAlbumAsync(int idCancion, string tipo, int? idAlbum)
    {
        using var conn = ObtenerConexion();

        try
        {
            if (tipo.ToLower() == "cassette")
            {
                await conn.ExecuteAsync(
                    "UPDATE temas SET id_album = @idAlbum WHERE id = @id",
                    new { idAlbum, id = idCancion });
            }
            else
            {
                await conn.ExecuteAsync(
                    "UPDATE temas_cd SET id_album = @idAlbum WHERE id = @id",
                    new { idAlbum, id = idCancion });
            }

            var mensaje = idAlbum.HasValue ? "Canción asignada al álbum" : "Canción removida del álbum";
            return new CrudResponse { Exito = true, Mensaje = mensaje };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Quita una canción de su álbum actual.</summary>
    public async Task<CrudResponse> QuitarCancionDeAlbumAsync(int idCancion, string tipo)
    {
        return await AsignarCancionAAlbumAsync(idCancion, tipo, null);
    }
}
