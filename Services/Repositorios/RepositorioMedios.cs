using Dapper;
using MusicaCatalogo.Data;
using MusicaCatalogo.Data.Entidades;

namespace MusicaCatalogo.Services.Repositorios;

/// <summary>
/// Repositorio para operaciones CRUD de medios (cassettes y CDs).
/// </summary>
public class RepositorioMedios : RepositorioBase
{
    public RepositorioMedios(BaseDatos db) : base(db) { }

    /// <summary>
    /// Obtiene el detalle de un formato (cassette o CD).
    /// </summary>
    public async Task<DetalleMedio?> ObtenerMedioAsync(string numMedio)
    {
        using var conn = ObtenerConexion();

        // Primero buscar en cassettes
        var cassette = await conn.QueryFirstOrDefaultAsync<DetalleMedio>("""
            SELECT fg.num_formato AS numMedio, f.nombre AS TipoMedio, m.nombre AS Marca,
                   g.nombre AS Grabador, fu.nombre AS Fuente, fg.fecha_inicio AS FechaInicio,
                   fg.fecha_termino AS FechaTermino, e.nombre AS Ecualizador, s.nombre AS Supresor,
                   b.nombre AS Bias, mo.nombre AS Modo,
                   (SELECT COUNT(*) FROM temas WHERE num_formato = fg.num_formato) AS TotalTemas
            FROM formato_grabado fg
            LEFT JOIN formato f ON fg.id_formato = f.id_formato
            LEFT JOIN marca m ON fg.id_marca = m.id_marca
            LEFT JOIN grabador g ON fg.id_deck = g.id_deck
            LEFT JOIN fuente fu ON fg.id_fuente = fu.id_fuente
            LEFT JOIN ecualizador e ON fg.id_ecual = e.id_ecual
            LEFT JOIN supresor s ON fg.id_dolby = s.id_dolby
            LEFT JOIN bias b ON fg.id_bias = b.id_bias
            LEFT JOIN modo mo ON fg.id_modo = mo.id_modo
            WHERE fg.num_formato = @numMedio
            """, new { numMedio });

        if (cassette != null)
            return cassette;

        // Buscar en CDs
        var cd = await conn.QueryFirstOrDefaultAsync<DetalleMedio>("""
            SELECT fg.num_formato AS numMedio, f.nombre AS TipoMedio, m.nombre AS Marca,
                   g.nombre AS Grabador, fu.nombre AS Fuente, fg.fecha_grabacion AS FechaInicio,
                   NULL AS FechaTermino, NULL AS Ecualizador, NULL AS Supresor,
                   NULL AS Bias, NULL AS Modo,
                   (SELECT COUNT(*) FROM temas_cd WHERE num_formato = fg.num_formato) AS TotalTemas
            FROM formato_grabado_cd fg
            LEFT JOIN formato f ON fg.id_formato = f.id_formato
            LEFT JOIN marca m ON fg.id_marca = m.id_marca
            LEFT JOIN grabador g ON fg.id_deck = g.id_deck
            LEFT JOIN fuente fu ON fg.id_fuente = fu.id_fuente
            WHERE fg.num_formato = @numMedio
            """, new { numMedio });

        return cd;
    }

    /// <summary>
    /// Obtiene lista de formatos (cassettes y CDs).
    /// </summary>
    public async Task<List<DetalleMedio>> ListarMediosAsync(string? tipo = null, int limite = 100)
    {
        using var conn = ObtenerConexion();
        var resultados = new List<DetalleMedio>();

        if (tipo == null || tipo.ToLower() == "cassette")
        {
            var cassettes = await conn.QueryAsync<DetalleMedio>("""
                SELECT fg.num_formato AS numMedio, f.nombre AS TipoMedio, m.nombre AS Marca,
                       g.nombre AS Grabador, fu.nombre AS Fuente, fg.fecha_inicio AS FechaInicio,
                       b.nombre AS Bias,
                       (SELECT COUNT(*) FROM temas WHERE num_formato = fg.num_formato) AS TotalTemas
                FROM formato_grabado fg
                LEFT JOIN formato f ON fg.id_formato = f.id_formato
                LEFT JOIN marca m ON fg.id_marca = m.id_marca
                LEFT JOIN grabador g ON fg.id_deck = g.id_deck
                LEFT JOIN fuente fu ON fg.id_fuente = fu.id_fuente
                LEFT JOIN bias b ON fg.id_bias = b.id_bias
                ORDER BY fg.num_formato
                LIMIT @limite
                """, new { limite });
            resultados.AddRange(cassettes);
        }

        if (tipo == null || tipo.ToLower() == "cd")
        {
            var cds = await conn.QueryAsync<DetalleMedio>("""
                SELECT fg.num_formato AS numMedio, f.nombre AS TipoMedio, m.nombre AS Marca,
                       g.nombre AS Grabador, fu.nombre AS Fuente, fg.fecha_grabacion AS FechaInicio,
                       NULL AS Bias,
                       (SELECT COUNT(*) FROM temas_cd WHERE num_formato = fg.num_formato) AS TotalTemas
                FROM formato_grabado_cd fg
                LEFT JOIN formato f ON fg.id_formato = f.id_formato
                LEFT JOIN marca m ON fg.id_marca = m.id_marca
                LEFT JOIN grabador g ON fg.id_deck = g.id_deck
                LEFT JOIN fuente fu ON fg.id_fuente = fu.id_fuente
                ORDER BY fg.num_formato
                LIMIT @limite
                """, new { limite });
            resultados.AddRange(cds);
        }

        return resultados;
    }

    /// <summary>Crea un nuevo formato (cassette o CD). Acepta IDs o nombres.</summary>
    public async Task<CrudResponse> CrearFormatoAsync(MedioRequest request)
    {
        using var conn = ObtenerConexion();

        try
        {
            // Verificar que no exista
            var existe = await conn.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM formato_grabado WHERE num_formato = @numMedio",
                new { request.numMedio });
            if (existe > 0)
            {
                existe = await conn.QueryFirstOrDefaultAsync<int>(
                    "SELECT COUNT(*) FROM formato_grabado_cd WHERE num_formato = @numMedio",
                    new { request.numMedio });
            }
            if (existe > 0)
                return new CrudResponse { Exito = false, Mensaje = "Ya existe un formato con ese número" };

            // Resolver IDs desde nombres si es necesario
            var idMarca = await ResolverIdAsync(conn, "marca", "id_marca", 
                request.IdMarca, request.NombreMarca);
            var idDeck = await ResolverIdAsync(conn, "grabador", "id_deck", 
                request.IdDeck, request.NombreGrabador);
            var idFuente = await ResolverIdAsync(conn, "fuente", "id_fuente", 
                request.IdFuente, request.NombreFuente);

            if (request.TipoMedio.ToLower() == "cassette")
            {
                var idEcual = await ResolverIdAsync(conn, "ecualizador", "id_ecual",
                    request.IdEcual, request.NombreEcualizador);
                var idDolby = await ResolverIdAsync(conn, "supresor", "id_dolby",
                    request.IdDolby, request.NombreSupresor);
                var idBias = await ResolverIdAsync(conn, "bias", "id_bias",
                    request.IdBias, request.NombreBias);
                var idModo = await ResolverIdAsync(conn, "modo", "id_modo",
                    request.IdModo, request.NombreModo);

                await conn.ExecuteAsync("""
                    INSERT INTO formato_grabado (num_formato, id_formato, id_marca, id_deck, id_ecual, id_dolby, id_bias, id_modo, id_fuente, fecha_inicio, fecha_termino)
                    VALUES (@numMedio, 1, @idMarca, @idDeck, @idEcual, @idDolby, @idBias, @idModo, @idFuente, @FechaInicio, @FechaTermino)
                    """, new
                {
                    request.numMedio,
                    idMarca,
                    idDeck,
                    idEcual,
                    idDolby,
                    idBias,
                    idModo,
                    idFuente,
                    request.FechaInicio,
                    request.FechaTermino
                });
            }
            else
            {
                await conn.ExecuteAsync("""
                    INSERT INTO formato_grabado_cd (num_formato, id_formato, id_marca, id_deck, id_fuente, fecha_grabacion)
                    VALUES (@numMedio, 2, @idMarca, @idDeck, @idFuente, @FechaInicio)
                    """, new
                {
                    request.numMedio,
                    idMarca,
                    idDeck,
                    idFuente,
                    request.FechaInicio
                });
            }

            return new CrudResponse { Exito = true, Mensaje = "Formato creado correctamente" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Actualiza un formato existente. Acepta IDs o nombres para las referencias.</summary>
    public async Task<CrudResponse> ActualizarFormatoAsync(string numMedio, MedioRequest request)
    {
        using var conn = ObtenerConexion();

        try
        {
            // Resolver IDs desde nombres si es necesario
            var idMarca = await ResolverIdAsync(conn, "marca", "id_marca", 
                request.IdMarca, request.NombreMarca);
            var idDeck = await ResolverIdAsync(conn, "grabador", "id_deck", 
                request.IdDeck, request.NombreGrabador);
            var idFuente = await ResolverIdAsync(conn, "fuente", "id_fuente", 
                request.IdFuente, request.NombreFuente);

            if (request.TipoMedio.ToLower() == "cassette")
            {
                var idEcual = await ResolverIdAsync(conn, "ecualizador", "id_ecual",
                    request.IdEcual, request.NombreEcualizador);
                var idDolby = await ResolverIdAsync(conn, "supresor", "id_dolby",
                    request.IdDolby, request.NombreSupresor);
                var idBias = await ResolverIdAsync(conn, "bias", "id_bias",
                    request.IdBias, request.NombreBias);
                var idModo = await ResolverIdAsync(conn, "modo", "id_modo",
                    request.IdModo, request.NombreModo);

                var rows = await conn.ExecuteAsync("""
                    UPDATE formato_grabado 
                    SET id_marca = @idMarca, id_deck = @idDeck, id_ecual = @idEcual, 
                        id_dolby = @idDolby, id_bias = @idBias, id_modo = @idModo, 
                        id_fuente = @idFuente, fecha_inicio = @FechaInicio, fecha_termino = @FechaTermino
                    WHERE num_formato = @numMedio
                    """, new
                {
                    numMedio,
                    idMarca,
                    idDeck,
                    idEcual,
                    idDolby,
                    idBias,
                    idModo,
                    idFuente,
                    request.FechaInicio,
                    request.FechaTermino
                });
                
                if (rows == 0)
                    return new CrudResponse { Exito = false, Mensaje = "Formato no encontrado" };
            }
            else
            {
                var rows = await conn.ExecuteAsync("""
                    UPDATE formato_grabado_cd 
                    SET id_marca = @idMarca, id_deck = @idDeck, id_fuente = @idFuente, fecha_grabacion = @FechaInicio
                    WHERE num_formato = @numMedio
                    """, new
                {
                    numMedio,
                    idMarca,
                    idDeck,
                    idFuente,
                    request.FechaInicio
                });
                
                if (rows == 0)
                    return new CrudResponse { Exito = false, Mensaje = "Formato no encontrado" };
            }

            return new CrudResponse { Exito = true, Mensaje = "Formato actualizado correctamente" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Elimina un formato y todas sus canciones.</summary>
    public async Task<CrudResponse> EliminarFormatoAsync(string numMedio)
    {
        using var conn = ObtenerConexion();

        try
        {
            // Eliminar canciones primero
            await conn.ExecuteAsync("DELETE FROM temas WHERE num_formato = @numMedio", new { numMedio });
            await conn.ExecuteAsync("DELETE FROM temas_cd WHERE num_formato = @numMedio", new { numMedio });

            // Eliminar formato
            var rows = await conn.ExecuteAsync("DELETE FROM formato_grabado WHERE num_formato = @numMedio", new { numMedio });
            if (rows == 0)
                rows = await conn.ExecuteAsync("DELETE FROM formato_grabado_cd WHERE num_formato = @numMedio", new { numMedio });

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Formato no encontrado" };

            return new CrudResponse { Exito = true, Mensaje = "Formato eliminado correctamente" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>
    /// Obtiene los temas de un formato específico.
    /// </summary>
    public async Task<List<TemaEnMedio>> ObtenerTemasDeMedioAsync(string numMedio)
    {
        using var conn = ObtenerConexion();

        // Verificar si es cassette
        var esCassette = await conn.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM formato_grabado WHERE num_formato = @numMedio",
            new { numMedio }) > 0;

        if (esCassette)
        {
            // Subconsulta para obtener el id_album de la versión original cuando la canción actual no tiene álbum
            var temas = await conn.QueryAsync<TemaEnMedio>("""
                SELECT t.id AS Id, t.tema AS Tema, i.nombre AS Interprete, t.id_interprete AS IdInterprete,
                       t.lado AS Lado, t.desde AS Desde, t.hasta AS Hasta, NULL AS Ubicacion,
                       COALESCE(t.id_album, 
                           -- Si no tiene álbum pero tiene artista_original, buscar el álbum de la versión original
                           CASE WHEN t.artista_original IS NOT NULL AND t.artista_original != '' THEN
                               COALESCE(
                                   -- Buscar en cassettes por el mismo tema y el artista original
                                   (SELECT t2.id_album FROM temas t2 
                                    JOIN interpretes i2 ON t2.id_interprete = i2.id 
                                    WHERE LOWER(t2.tema) = LOWER(t.tema) 
                                      AND LOWER(i2.nombre) = LOWER(t.artista_original)
                                      AND t2.id_album IS NOT NULL LIMIT 1),
                                   -- Si no, buscar en CDs
                                   (SELECT t3.id_album FROM temas_cd t3 
                                    JOIN interpretes i3 ON t3.id_interprete = i3.id 
                                    WHERE LOWER(t3.tema) = LOWER(t.tema) 
                                      AND LOWER(i3.nombre) = LOWER(t.artista_original)
                                      AND t3.id_album IS NOT NULL LIMIT 1)
                               )
                           END
                       ) AS IdAlbum,
                       COALESCE(a.nombre,
                           CASE WHEN t.artista_original IS NOT NULL AND t.artista_original != '' AND t.id_album IS NULL THEN
                               COALESCE(
                                   (SELECT a2.nombre FROM temas t2 
                                    JOIN interpretes i2 ON t2.id_interprete = i2.id 
                                    JOIN albumes a2 ON t2.id_album = a2.id
                                    WHERE LOWER(t2.tema) = LOWER(t.tema) 
                                      AND LOWER(i2.nombre) = LOWER(t.artista_original)
                                    LIMIT 1),
                                   (SELECT a3.nombre FROM temas_cd t3 
                                    JOIN interpretes i3 ON t3.id_interprete = i3.id 
                                    JOIN albumes a3 ON t3.id_album = a3.id
                                    WHERE LOWER(t3.tema) = LOWER(t.tema) 
                                      AND LOWER(i3.nombre) = LOWER(t.artista_original)
                                    LIMIT 1)
                               )
                           END
                       ) AS NombreAlbum,
                       t.link_externo AS LinkExterno,
                       CASE WHEN t.portada IS NOT NULL THEN 1 ELSE 0 END AS TienePortada,
                       COALESCE(t.es_cover, 0) AS EsCover, t.artista_original AS ArtistaOriginal
                FROM temas t
                JOIN interpretes i ON t.id_interprete = i.id
                LEFT JOIN albumes a ON t.id_album = a.id
                WHERE t.num_formato = @numMedio
                ORDER BY t.lado, t.desde
                """, new { numMedio });
            return temas.ToList();
        }

        // Es CD - misma lógica de herencia de álbum
        var temasCd = await conn.QueryAsync<TemaEnMedio>("""
            SELECT t.id AS Id, t.tema AS Tema, i.nombre AS Interprete, t.id_interprete AS IdInterprete,
                   NULL AS Lado, NULL AS Desde, NULL AS Hasta, t.ubicacion AS Ubicacion,
                   COALESCE(t.id_album,
                       CASE WHEN t.artista_original IS NOT NULL AND t.artista_original != '' THEN
                           COALESCE(
                               (SELECT t2.id_album FROM temas t2 
                                JOIN interpretes i2 ON t2.id_interprete = i2.id 
                                WHERE LOWER(t2.tema) = LOWER(t.tema) 
                                  AND LOWER(i2.nombre) = LOWER(t.artista_original)
                                  AND t2.id_album IS NOT NULL LIMIT 1),
                               (SELECT t3.id_album FROM temas_cd t3 
                                JOIN interpretes i3 ON t3.id_interprete = i3.id 
                                WHERE LOWER(t3.tema) = LOWER(t.tema) 
                                  AND LOWER(i3.nombre) = LOWER(t.artista_original)
                                  AND t3.id_album IS NOT NULL LIMIT 1)
                           )
                       END
                   ) AS IdAlbum,
                   COALESCE(a.nombre,
                       CASE WHEN t.artista_original IS NOT NULL AND t.artista_original != '' AND t.id_album IS NULL THEN
                           COALESCE(
                               (SELECT a2.nombre FROM temas t2 
                                JOIN interpretes i2 ON t2.id_interprete = i2.id 
                                JOIN albumes a2 ON t2.id_album = a2.id
                                WHERE LOWER(t2.tema) = LOWER(t.tema) 
                                  AND LOWER(i2.nombre) = LOWER(t.artista_original)
                                LIMIT 1),
                               (SELECT a3.nombre FROM temas_cd t3 
                                JOIN interpretes i3 ON t3.id_interprete = i3.id 
                                JOIN albumes a3 ON t3.id_album = a3.id
                                WHERE LOWER(t3.tema) = LOWER(t.tema) 
                                  AND LOWER(i3.nombre) = LOWER(t.artista_original)
                                LIMIT 1)
                           )
                       END
                   ) AS NombreAlbum,
                   t.link_externo AS LinkExterno,
                   CASE WHEN t.portada IS NOT NULL THEN 1 ELSE 0 END AS TienePortada,
                   COALESCE(t.es_cover, 0) AS EsCover, t.artista_original AS ArtistaOriginal
            FROM temas_cd t
            JOIN interpretes i ON t.id_interprete = i.id
            LEFT JOIN albumes a ON t.id_album = a.id
            WHERE t.num_formato = @numMedio
            ORDER BY t.ubicacion
            """, new { numMedio });
        return temasCd.ToList();
    }
}
