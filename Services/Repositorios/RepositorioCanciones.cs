using Dapper;
using MusicaCatalogo.Data;
using MusicaCatalogo.Data.Entidades;
using System.Data;

namespace MusicaCatalogo.Services.Repositorios;

/// <summary>
/// Repositorio para operaciones CRUD de canciones/temas.
/// </summary>
public class RepositorioCanciones : RepositorioBase
{
    public RepositorioCanciones(BaseDatos db) : base(db) { }

    /// <summary>Obtiene temas con ID para poder editarlos.</summary>
    public async Task<List<TemaConId>> ObtenerTemasConIdAsync(string numMedio)
    {
        using var conn = ObtenerConexion();

        // Verificar si es cassette
        var esCassette = await conn.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM formato_grabado WHERE num_formato = @numMedio",
            new { numMedio }) > 0;

        if (esCassette)
        {
            var temas = await conn.QueryAsync<TemaConId>("""
                SELECT t.id AS Id, t.tema AS Tema, i.nombre AS Interprete, t.id_interprete AS IdInterprete,
                       t.lado AS Lado, t.desde AS Desde, t.hasta AS Hasta, NULL AS Ubicacion,
                       t.id_album AS IdAlbum, a.nombre AS NombreAlbum, t.link_externo AS LinkExterno,
                       CASE WHEN t.portada IS NOT NULL THEN 1 ELSE 0 END AS TienePortada
                FROM temas t
                JOIN interpretes i ON t.id_interprete = i.id
                LEFT JOIN albumes a ON t.id_album = a.id
                WHERE t.num_formato = @numMedio
                ORDER BY t.lado, t.desde
                """, new { numMedio });
            return temas.ToList();
        }

        // Es CD
        var temasCd = await conn.QueryAsync<TemaConId>("""
            SELECT t.id AS Id, t.tema AS Tema, i.nombre AS Interprete, t.id_interprete AS IdInterprete,
                   NULL AS Lado, NULL AS Desde, NULL AS Hasta, t.ubicacion AS Ubicacion,
                   t.id_album AS IdAlbum, a.nombre AS NombreAlbum, t.link_externo AS LinkExterno,
                   CASE WHEN t.portada IS NOT NULL THEN 1 ELSE 0 END AS TienePortada
            FROM temas_cd t
            JOIN interpretes i ON t.id_interprete = i.id
            LEFT JOIN albumes a ON t.id_album = a.id
            WHERE t.num_formato = @numMedio
            ORDER BY t.ubicacion
            """, new { numMedio });
        return temasCd.ToList();
    }

    /// <summary>Crea una nueva canción.</summary>
    public async Task<CrudResponse> CrearCancionAsync(CancionRequest request)
    {
        using var conn = ObtenerConexion();

        try
        {
            // Obtener o crear intérprete
            int idInterprete;
            if (request.IdInterprete.HasValue)
            {
                idInterprete = request.IdInterprete.Value;
            }
            else if (!string.IsNullOrWhiteSpace(request.NombreInterprete))
            {
                // Buscar si existe
                var existente = await conn.QueryFirstOrDefaultAsync<int?>(
                    "SELECT id FROM interpretes WHERE nombre = @nombre",
                    new { nombre = request.NombreInterprete });

                if (existente.HasValue)
                {
                    idInterprete = existente.Value;
                }
                else
                {
                    // Crear nuevo intérprete
                    var maxId = await conn.QueryFirstOrDefaultAsync<int?>("SELECT MAX(id) FROM interpretes") ?? 0;
                    idInterprete = maxId + 1;
                    await conn.ExecuteAsync(
                        "INSERT INTO interpretes (id, nombre) VALUES (@id, @nombre)",
                        new { id = idInterprete, nombre = request.NombreInterprete });
                }
            }
            else
            {
                return new CrudResponse { Exito = false, Mensaje = "Debe especificar un intérprete" };
            }

            long idCreado;
            if (request.TipoMedio.ToLower() == "cassette")
            {
                await conn.ExecuteAsync("""
                    INSERT INTO temas (num_formato, id_interprete, tema, lado, desde, hasta, es_cover, artista_original)
                    VALUES (@numMedio, @idInterprete, @Tema, @Lado, @Desde, @Hasta, @EsCover, @ArtistaOriginal)
                    """, new
                {
                    request.numMedio,
                    idInterprete,
                    request.Tema,
                    Lado = request.Lado ?? "A",
                    Desde = request.Desde ?? 1,
                    Hasta = request.Hasta ?? 1,
                    EsCover = request.EsCover ? 1 : 0,
                    ArtistaOriginal = request.ArtistaOriginal
                });
                idCreado = await conn.QueryFirstAsync<long>("SELECT last_insert_rowid()");
            }
            else
            {
                // Obtener próxima ubicación
                var maxUbicacion = await conn.QueryFirstOrDefaultAsync<int?>(
                    "SELECT MAX(ubicacion) FROM temas_cd WHERE num_formato = @numMedio",
                    new { request.numMedio }) ?? 0;

                await conn.ExecuteAsync("""
                    INSERT INTO temas_cd (num_formato, id_interprete, tema, ubicacion, es_cover, artista_original)
                    VALUES (@numMedio, @idInterprete, @Tema, @ubicacion, @EsCover, @ArtistaOriginal)
                    """, new
                {
                    request.numMedio,
                    idInterprete,
                    request.Tema,
                    ubicacion = request.Ubicacion ?? (maxUbicacion + 1),
                    EsCover = request.EsCover ? 1 : 0,
                    ArtistaOriginal = request.ArtistaOriginal
                });
                idCreado = await conn.QueryFirstAsync<long>("SELECT last_insert_rowid()");
            }

            return new CrudResponse { Exito = true, Mensaje = "Canción agregada correctamente", IdCreado = (int)idCreado };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Actualiza una canción existente.</summary>
    public async Task<CrudResponse> ActualizarCancionAsync(int id, CancionRequest request)
    {
        using var conn = ObtenerConexion();

        try
        {
            // Obtener o crear intérprete
            int idInterprete;
            if (request.IdInterprete.HasValue)
            {
                idInterprete = request.IdInterprete.Value;
            }
            else if (!string.IsNullOrWhiteSpace(request.NombreInterprete))
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
            else
            {
                return new CrudResponse { Exito = false, Mensaje = "Debe especificar un intérprete" };
            }

            int rows;
            if (request.TipoMedio.ToLower() == "cassette")
            {
                rows = await conn.ExecuteAsync("""
                    UPDATE temas SET id_interprete = @idInterprete, tema = @Tema, lado = @Lado, desde = @Desde, hasta = @Hasta,
                           es_cover = @EsCover, artista_original = @ArtistaOriginal
                    WHERE id = @id
                    """, new
                {
                    id,
                    idInterprete,
                    request.Tema,
                    Lado = request.Lado ?? "A",
                    Desde = request.Desde ?? 1,
                    Hasta = request.Hasta ?? 1,
                    EsCover = request.EsCover ? 1 : 0,
                    ArtistaOriginal = request.ArtistaOriginal
                });
            }
            else
            {
                rows = await conn.ExecuteAsync("""
                    UPDATE temas_cd SET id_interprete = @idInterprete, tema = @Tema, ubicacion = @Ubicacion,
                           es_cover = @EsCover, artista_original = @ArtistaOriginal
                    WHERE id = @id
                    """, new
                {
                    id,
                    idInterprete,
                    request.Tema,
                    Ubicacion = request.Ubicacion ?? 1,
                    EsCover = request.EsCover ? 1 : 0,
                    ArtistaOriginal = request.ArtistaOriginal
                });
            }

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Canción no encontrada" };

            return new CrudResponse { Exito = true, Mensaje = "Canción actualizada correctamente" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Elimina una canción.</summary>
    public async Task<CrudResponse> EliminarCancionAsync(int id, string TipoMedio)
    {
        using var conn = ObtenerConexion();

        try
        {
            int rows;
            if (TipoMedio.ToLower() == "cassette")
            {
                rows = await conn.ExecuteAsync("DELETE FROM temas WHERE id = @id", new { id });
            }
            else
            {
                rows = await conn.ExecuteAsync("DELETE FROM temas_cd WHERE id = @id", new { id });
            }

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Canción no encontrada" };

            return new CrudResponse { Exito = true, Mensaje = "Canción eliminada correctamente" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Reordena las canciones de un formato.</summary>
    public async Task<CrudResponse> ReordenarCancionesAsync(ReordenarRequest request)
    {
        using var conn = ObtenerConexion();

        try
        {
            if (request.TipoMedio.ToLower() == "cd")
            {
                // Para CDs, actualizar la ubicación
                for (int i = 0; i < request.IdsOrdenados.Count; i++)
                {
                    await conn.ExecuteAsync(
                        "UPDATE temas_cd SET ubicacion = @ubicacion WHERE id = @id",
                        new { id = request.IdsOrdenados[i], ubicacion = i + 1 });
                }
            }
            else
            {
                // Para cassettes, actualizar desde (mantener lado)
                for (int i = 0; i < request.IdsOrdenados.Count; i++)
                {
                    await conn.ExecuteAsync(
                        "UPDATE temas SET desde = @desde WHERE id = @id",
                        new { id = request.IdsOrdenados[i], desde = i + 1 });
                }
            }

            return new CrudResponse { Exito = true, Mensaje = "Canciones reordenadas correctamente" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Obtiene el detalle completo de una canción.</summary>
    public async Task<CancionDetalle?> ObtenerCancionAsync(int id, string tipo)
    {
        using var conn = ObtenerConexion();

        // Verificar si existe la columna es_single
        var columnas = await conn.QueryAsync<string>("SELECT name FROM pragma_table_info('albumes')");
        var tieneEsSingle = columnas.Contains("es_single");
        var esSingleCol = tieneEsSingle ? "COALESCE(a.es_single, 0)" : "0";

        if (tipo.ToLower() == "cassette")
        {
            return await conn.QueryFirstOrDefaultAsync<CancionDetalle>($"""
                SELECT t.id AS Id, 'cassette' AS Tipo, t.tema AS Tema, i.nombre AS Interprete, t.id_interprete AS IdInterprete,
                       t.num_formato AS numMedio, t.lado AS Lado, t.desde AS Desde, t.hasta AS Hasta, NULL AS Ubicacion,
                       t.id_album AS IdAlbum, a.nombre AS NombreAlbum, ia.nombre AS ArtistaAlbum, a.anio AS AnioAlbum,
                       {esSingleCol} AS EsAlbumSingle, t.link_externo AS LinkExterno,
                       CASE WHEN t.portada IS NOT NULL THEN 1 ELSE 0 END AS TienePortada,
                       CASE WHEN a.portada IS NOT NULL THEN 1 ELSE 0 END AS TienePortadaAlbum,
                       COALESCE(t.es_cover, 0) AS EsCover, t.artista_original AS ArtistaOriginal
                FROM temas t
                JOIN interpretes i ON t.id_interprete = i.id
                LEFT JOIN albumes a ON t.id_album = a.id
                LEFT JOIN interpretes ia ON a.id_interprete = ia.id
                WHERE t.id = @id
                """, new { id });
        }
        else
        {
            return await conn.QueryFirstOrDefaultAsync<CancionDetalle>($"""
                SELECT t.id AS Id, 'cd' AS Tipo, t.tema AS Tema, i.nombre AS Interprete, t.id_interprete AS IdInterprete,
                       t.num_formato AS numMedio, NULL AS Lado, NULL AS Desde, NULL AS Hasta, t.ubicacion AS Ubicacion,
                       t.id_album AS IdAlbum, a.nombre AS NombreAlbum, ia.nombre AS ArtistaAlbum, a.anio AS AnioAlbum,
                       {esSingleCol} AS EsAlbumSingle, t.link_externo AS LinkExterno,
                       CASE WHEN t.portada IS NOT NULL THEN 1 ELSE 0 END AS TienePortada,
                       CASE WHEN a.portada IS NOT NULL THEN 1 ELSE 0 END AS TienePortadaAlbum,
                       COALESCE(t.es_cover, 0) AS EsCover, t.artista_original AS ArtistaOriginal
                FROM temas_cd t
                JOIN interpretes i ON t.id_interprete = i.id
                LEFT JOIN albumes a ON t.id_album = a.id
                LEFT JOIN interpretes ia ON a.id_interprete = ia.id
                WHERE t.id = @id
                """, new { id });
        }
    }

    /// <summary>Actualiza una canción con información extendida.</summary>
    public async Task<CrudResponse> ActualizarCancionExtendidaAsync(int id, string tipo, CancionUpdateRequest request)
    {
        using var conn = ObtenerConexion();

        try
        {
            // Resolver intérprete
            int idInterprete;
            if (request.IdInterprete.HasValue)
            {
                idInterprete = request.IdInterprete.Value;
            }
            else if (!string.IsNullOrWhiteSpace(request.NombreInterprete))
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
            else
            {
                return new CrudResponse { Exito = false, Mensaje = "Debe especificar un intérprete" };
            }

            // Resolver álbum SOLO si se proporciona explícitamente
            int? idAlbum = null;
            bool actualizarAlbum = request.IdAlbum.HasValue || !string.IsNullOrWhiteSpace(request.NombreAlbum);
            
            if (actualizarAlbum)
            {
                idAlbum = request.IdAlbum;
                if (!idAlbum.HasValue && !string.IsNullOrWhiteSpace(request.NombreAlbum))
                {
                    var existente = await conn.QueryFirstOrDefaultAsync<int?>(
                        "SELECT id FROM albumes WHERE nombre = @nombre",
                        new { nombre = request.NombreAlbum });

                    if (existente.HasValue)
                    {
                        idAlbum = existente.Value;
                    }
                    else
                    {
                        await conn.ExecuteAsync("""
                            INSERT INTO albumes (nombre, id_interprete, fecha_creacion)
                            VALUES (@nombre, @idInterprete, datetime('now'))
                            """, new { nombre = request.NombreAlbum, idInterprete });
                        idAlbum = await conn.QueryFirstAsync<int>("SELECT last_insert_rowid()");
                    }
                }
            }

            int rows;
            if (tipo.ToLower() == "cassette")
            {
                // Construir SQL dinámicamente para no tocar el álbum si no se especifica
                var sql = actualizarAlbum
                    ? """
                      UPDATE temas SET tema = @Tema, id_interprete = @idInterprete, id_album = @idAlbum,
                             link_externo = @LinkExterno, lado = @Lado, desde = @Desde, hasta = @Hasta,
                             es_cover = @EsCover, artista_original = @ArtistaOriginal
                      WHERE id = @id
                      """
                    : """
                      UPDATE temas SET tema = @Tema, id_interprete = @idInterprete,
                             link_externo = @LinkExterno, lado = @Lado, desde = @Desde, hasta = @Hasta,
                             es_cover = @EsCover, artista_original = @ArtistaOriginal
                      WHERE id = @id
                      """;
                      
                rows = await conn.ExecuteAsync(sql, new
                {
                    id,
                    request.Tema,
                    idInterprete,
                    idAlbum,
                    request.LinkExterno,
                    Lado = request.Lado ?? "A",
                    Desde = request.Desde ?? 1,
                    Hasta = request.Hasta ?? 1,
                    EsCover = request.EsCover ? 1 : 0,
                    request.ArtistaOriginal
                });
            }
            else
            {
                var sql = actualizarAlbum
                    ? """
                      UPDATE temas_cd SET tema = @Tema, id_interprete = @idInterprete, id_album = @idAlbum,
                             link_externo = @LinkExterno, ubicacion = @Ubicacion,
                             es_cover = @EsCover, artista_original = @ArtistaOriginal
                      WHERE id = @id
                      """
                    : """
                      UPDATE temas_cd SET tema = @Tema, id_interprete = @idInterprete,
                             link_externo = @LinkExterno, ubicacion = @Ubicacion,
                             es_cover = @EsCover, artista_original = @ArtistaOriginal
                      WHERE id = @id
                      """;
                      
                rows = await conn.ExecuteAsync(sql, new
                {
                    id,
                    request.Tema,
                    idInterprete,
                    idAlbum,
                    request.LinkExterno,
                    Ubicacion = request.Ubicacion ?? 1,
                    EsCover = request.EsCover ? 1 : 0,
                    request.ArtistaOriginal
                });
            }

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Canción no encontrada" };

            return new CrudResponse { Exito = true, Mensaje = "Canción actualizada correctamente" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Guarda la portada de una canción.</summary>
    public async Task<CrudResponse> GuardarPortadaCancionAsync(int id, string tipo, byte[] portada)
    {
        using var conn = ObtenerConexion();

        try
        {
            var tabla = tipo.ToLower() == "cassette" ? "temas" : "temas_cd";
            var rows = await conn.ExecuteAsync(
                $"UPDATE {tabla} SET portada = @portada WHERE id = @id",
                new { id, portada });

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Canción no encontrada" };

            return new CrudResponse { Exito = true, Mensaje = "Portada guardada correctamente" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Obtiene la portada de una canción (propia o heredada del álbum).</summary>
    public async Task<byte[]?> ObtenerPortadaCancionAsync(int id, string tipo)
    {
        using var conn = ObtenerConexion();

        var tabla = tipo.ToLower() == "cassette" ? "temas" : "temas_cd";
        
        // Primero buscar portada propia
        var portadaPropia = await conn.QueryFirstOrDefaultAsync<byte[]?>(
            $"SELECT portada FROM {tabla} WHERE id = @id", new { id });
        
        if (portadaPropia != null)
            return portadaPropia;

        // Si no tiene, buscar portada del álbum
        var idAlbum = await conn.QueryFirstOrDefaultAsync<int?>(
            $"SELECT id_album FROM {tabla} WHERE id = @id", new { id });

        if (idAlbum.HasValue)
        {
            return await conn.QueryFirstOrDefaultAsync<byte[]?>(
                "SELECT portada FROM albumes WHERE id = @id", new { id = idAlbum.Value });
        }

        return null;
    }

    /// <summary>Obtiene todas las canciones para mostrar en la galería.</summary>
    public async Task<List<CancionGaleria>> ObtenerTodasCancionesAsync()
    {
        using var conn = ObtenerConexion();
        
        var sqlCassette = """
            SELECT t.id AS Id, 'cassette' AS Tipo, t.tema AS Tema, i.nombre AS Interprete,
                   t.num_formato AS numMedio, t.id_album AS IdAlbum, a.nombre AS AlbumNombre,
                   COALESCE(t.es_cover, 0) AS EsCover, t.artista_original AS ArtistaOriginal,
                   t.lado AS Lado, t.desde AS Desde, t.hasta AS Hasta, NULL AS Ubicacion
            FROM temas t
            JOIN interpretes i ON t.id_interprete = i.id
            LEFT JOIN albumes a ON t.id_album = a.id
            """;

        var sqlCd = """
            SELECT t.id AS Id, 'cd' AS Tipo, t.tema AS Tema, i.nombre AS Interprete,
                   t.num_formato AS numMedio, t.id_album AS IdAlbum, a.nombre AS AlbumNombre,
                   COALESCE(t.es_cover, 0) AS EsCover, t.artista_original AS ArtistaOriginal,
                   NULL AS Lado, NULL AS Desde, NULL AS Hasta, t.ubicacion AS Ubicacion
            FROM temas_cd t
            JOIN interpretes i ON t.id_interprete = i.id
            LEFT JOIN albumes a ON t.id_album = a.id
            """;

        var cassettes = await conn.QueryAsync<CancionGaleria>(sqlCassette);
        var cds = await conn.QueryAsync<CancionGaleria>(sqlCd);

        return cassettes.Concat(cds).OrderBy(c => c.Tema).ToList();
    }
}
