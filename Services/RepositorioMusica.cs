using Dapper;
using TagLibFile = TagLib.File;
using MusicaCatalogo.Data;
using MusicaCatalogo.Data.Entidades;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Globalization;
using System.Text;
using System.Collections.Concurrent; // Para el cache

namespace MusicaCatalogo.Services;

/// <summary>
/// Repositorio para consultas a la base de datos SQLite de música.
/// </summary>
public class RepositorioMusica
{
    private readonly BaseDatos _db;
    
    // Cache para evitar leer metadatos de archivos repetidamente
    private static readonly ConcurrentDictionary<string, bool> _cachePortadaEmbebida = new();

    public RepositorioMusica(BaseDatos db)
    {
        _db = db;
    }

    /// <summary>
    /// Normaliza texto removiendo tildes y convirtiendo a minúsculas.
    /// </summary>
    private static string NormalizarTexto(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return texto;
        var normalizado = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalizado)
        {
            var uc = CharUnicodeInfo.GetUnicodeCategory(c);
            if (uc != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
    }

    /// <summary>
    /// Búsqueda global por nombre de tema, intérprete o número de formato.
    /// </summary>
    public async Task<List<ResultadoBusqueda>> BuscarAsync(string consulta, int limite = 50)
    {
        if (string.IsNullOrWhiteSpace(consulta))
            return new List<ResultadoBusqueda>();

        using var conn = _db.ObtenerConexion();
        var patron = $"%{consulta}%";

        var resultados = await conn.QueryAsync<ResultadoBusqueda>("""
            SELECT t.id AS Id, LOWER(f.nombre) AS Tipo, t.num_formato AS numMedio, t.tema AS Tema, 
                   i.nombre AS Interprete, (t.lado || ':' || t.desde || '-' || t.hasta) AS Posicion
            FROM temas t
            JOIN formato_grabado fg ON t.num_formato = fg.num_formato
            JOIN formato f ON fg.id_formato = f.id_formato
            JOIN interpretes i ON t.id_interprete = i.id
            WHERE t.tema LIKE @patron OR i.nombre LIKE @patron OR t.num_formato LIKE @patron
            UNION ALL
            SELECT t.id AS Id, LOWER(f.nombre) AS Tipo, t.num_formato AS numMedio, t.tema AS Tema,
                   i.nombre AS Interprete, CAST(t.ubicacion AS TEXT) AS Posicion
            FROM temas_cd t
            JOIN formato_grabado_cd fg ON t.num_formato = fg.num_formato
            JOIN formato f ON fg.id_formato = f.id_formato
            JOIN interpretes i ON t.id_interprete = i.id
            WHERE t.tema LIKE @patron OR i.nombre LIKE @patron OR t.num_formato LIKE @patron
            ORDER BY Tema
            LIMIT @limite
            """, new { patron, limite });

        return resultados.ToList();
    }

    /// <summary>
    /// Autocompletado de canciones (búsqueda fuzzy sin tildes).
    /// </summary>
    public async Task<List<SugerenciaTemaConId>> AutocompletarTemasAsync(string consulta, int limite = 15)
    {
        if (string.IsNullOrWhiteSpace(consulta) || consulta.Length < 2)
            return new List<SugerenciaTemaConId>();

        using var conn = _db.ObtenerConexion();
        var consultaNorm = NormalizarTexto(consulta);

        // Obtener todos los temas con info de álbum y filtrar en memoria para búsqueda sin tildes
        var temasCassette = await conn.QueryAsync<(int Id, string Tema, string Interprete, string numMedio, string Lado, int Desde, int Hasta, int? IdAlbum, string? AlbumNombre)>("""
            SELECT t.id, t.tema, i.nombre, t.num_formato, t.lado, t.desde, t.hasta, t.id_album, a.nombre as album_nombre
            FROM temas t
            JOIN interpretes i ON t.id_interprete = i.id
            LEFT JOIN albumes a ON t.id_album = a.id
            """);

        var temasCd = await conn.QueryAsync<(int Id, string Tema, string Interprete, string numMedio, int Ubicacion, int? IdAlbum, string? AlbumNombre)>("""
            SELECT t.id, t.tema, i.nombre, t.num_formato, t.ubicacion, t.id_album, a.nombre as album_nombre
            FROM temas_cd t
            JOIN interpretes i ON t.id_interprete = i.id
            LEFT JOIN albumes a ON t.id_album = a.id
            """);

        var resultados = new List<SugerenciaTemaConId>();

        // Buscar en cassettes
        foreach (var t in temasCassette)
        {
            var temaNorm = NormalizarTexto(t.Tema);
            var interpNorm = NormalizarTexto(t.Interprete);
            if (temaNorm.Contains(consultaNorm) || interpNorm.Contains(consultaNorm))
            {
                resultados.Add(new SugerenciaTemaConId
                {
                    Id = t.Id,
                    Tema = t.Tema,
                    Interprete = t.Interprete,
                    numMedio = t.numMedio,
                    Tipo = "cassette",
                    Ubicacion = $"{t.Lado}:{t.Desde}-{t.Hasta}",
                    IdAlbum = t.IdAlbum,
                    AlbumNombre = t.AlbumNombre
                });
            }
        }

        // Buscar en CDs
        foreach (var t in temasCd)
        {
            var temaNorm = NormalizarTexto(t.Tema);
            var interpNorm = NormalizarTexto(t.Interprete);
            if (temaNorm.Contains(consultaNorm) || interpNorm.Contains(consultaNorm))
            {
                resultados.Add(new SugerenciaTemaConId
                {
                    Id = t.Id,
                    Tema = t.Tema,
                    Interprete = t.Interprete,
                    numMedio = t.numMedio,
                    Tipo = "cd",
                    Ubicacion = $"Track {t.Ubicacion}",
                    IdAlbum = t.IdAlbum,
                    AlbumNombre = t.AlbumNombre
                });
            }
        }

        // Ordenar por relevancia: primero los que empiezan con la consulta
        return resultados
            .OrderByDescending(r => NormalizarTexto(r.Tema).StartsWith(consultaNorm))
            .ThenBy(r => r.Tema)
            .Take(limite)
            .ToList();
    }

    /// <summary>
    /// Obtiene el detalle de un formato (cassette o CD).
    /// </summary>
    public async Task<DetalleMedio?> ObtenerMedioAsync(string numMedio)
    {
        using var conn = _db.ObtenerConexion();

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
    /// Obtiene los temas de un formato específico.
    /// </summary>
    public async Task<List<TemaEnMedio>> ObtenerTemasDeMedioAsync(string numMedio)
    {
        try {
        using var conn = _db.ObtenerConexion();

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
                           COALESCE(
                               (SELECT t2.id_album FROM temas t2 
                                JOIN interpretes i2 ON t2.id_interprete = i2.id 
                                WHERE LOWER(t2.tema) = LOWER(t.tema) 
                                  AND t2.id_album IS NOT NULL 
                                  AND (
                                      LOWER(i2.nombre) = LOWER(CASE WHEN t.artista_original IS NOT NULL AND t.artista_original != '' THEN t.artista_original ELSE i.nombre END)
                                      OR t2.es_original = 1
                                  )
                                ORDER BY t2.es_original DESC
                                LIMIT 1),
                               (SELECT t3.id_album FROM temas_cd t3 
                                JOIN interpretes i3 ON t3.id_interprete = i3.id 
                                WHERE LOWER(t3.tema) = LOWER(t.tema) 
                                  AND t3.id_album IS NOT NULL 
                                  AND (
                                      LOWER(i3.nombre) = LOWER(CASE WHEN t.artista_original IS NOT NULL AND t.artista_original != '' THEN t.artista_original ELSE i.nombre END)
                                      OR t3.es_original = 1
                                  )
                                ORDER BY t3.es_original DESC
                                LIMIT 1)
                           )
                       ) AS IdAlbum,
                       COALESCE(a.nombre,
                           COALESCE(
                               (SELECT a2.nombre FROM temas t2 
                                JOIN interpretes i2 ON t2.id_interprete = i2.id 
                                JOIN albumes a2 ON t2.id_album = a2.id
                                WHERE LOWER(t2.tema) = LOWER(t.tema) 
                                  AND (
                                      LOWER(i2.nombre) = LOWER(CASE WHEN t.artista_original IS NOT NULL AND t.artista_original != '' THEN t.artista_original ELSE i.nombre END)
                                      OR t2.es_original = 1
                                  )
                                ORDER BY t2.es_original DESC
                                LIMIT 1),
                               (SELECT a3.nombre FROM temas_cd t3 
                                JOIN interpretes i3 ON t3.id_interprete = i3.id 
                                JOIN albumes a3 ON t3.id_album = a3.id
                                WHERE LOWER(t3.tema) = LOWER(t.tema) 
                                  AND (
                                      LOWER(i3.nombre) = LOWER(CASE WHEN t.artista_original IS NOT NULL AND t.artista_original != '' THEN t.artista_original ELSE i.nombre END)
                                      OR t3.es_original = 1
                                  )
                                ORDER BY t3.es_original DESC
                                LIMIT 1)
                           )
                       ) AS NombreAlbum,
                       t.link_externo AS LinkExterno,
                       CASE WHEN t.portada IS NOT NULL THEN 1 ELSE 0 END AS TienePortada,
                       CASE WHEN t.archivo_audio IS NOT NULL AND t.archivo_audio != '' THEN 1 ELSE 0 END AS TieneArchivoAudio,
                       COALESCE(t.es_cover, 0) AS EsCover, COALESCE(t.es_original, 0) AS EsOriginal, t.artista_original AS ArtistaOriginal
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
                       COALESCE(
                           (SELECT t2.id_album FROM temas t2 
                            JOIN interpretes i2 ON t2.id_interprete = i2.id 
                            WHERE LOWER(t2.tema) = LOWER(t.tema) 
                              AND t2.id_album IS NOT NULL 
                              AND (
                                  LOWER(i2.nombre) = LOWER(CASE WHEN t.artista_original IS NOT NULL AND t.artista_original != '' THEN t.artista_original ELSE i.nombre END)
                                  OR t2.es_original = 1
                              )
                            ORDER BY 
                              t2.es_original DESC
                            LIMIT 1),
                           (SELECT t3.id_album FROM temas_cd t3 
                            JOIN interpretes i3 ON t3.id_interprete = i3.id 
                            WHERE LOWER(t3.tema) = LOWER(t.tema) 
                              AND t3.id_album IS NOT NULL 
                              AND (
                                  LOWER(i3.nombre) = LOWER(CASE WHEN t.artista_original IS NOT NULL AND t.artista_original != '' THEN t.artista_original ELSE i.nombre END)
                                  OR t3.es_original = 1
                              )
                            ORDER BY 
                              t3.es_original DESC
                            LIMIT 1)
                       )
                   ) AS IdAlbum,
                   COALESCE(a.nombre,
                       COALESCE(
                           (SELECT a2.nombre FROM temas t2 
                            JOIN interpretes i2 ON t2.id_interprete = i2.id 
                            JOIN albumes a2 ON t2.id_album = a2.id
                            WHERE LOWER(t2.tema) = LOWER(t.tema) 
                              AND (
                                  LOWER(i2.nombre) = LOWER(CASE WHEN t.artista_original IS NOT NULL AND t.artista_original != '' THEN t.artista_original ELSE i.nombre END)
                                  OR t2.es_original = 1
                              )
                            ORDER BY 
                              t2.es_original DESC
                            LIMIT 1),
                           (SELECT a3.nombre FROM temas_cd t3 
                            JOIN interpretes i3 ON t3.id_interprete = i3.id 
                            JOIN albumes a3 ON t3.id_album = a3.id
                            WHERE LOWER(t3.tema) = LOWER(t.tema) 
                              AND (
                                  LOWER(i3.nombre) = LOWER(CASE WHEN t.artista_original IS NOT NULL AND t.artista_original != '' THEN t.artista_original ELSE i.nombre END)
                                  OR t3.es_original = 1
                              )
                            ORDER BY 
                              t3.es_original DESC
                            LIMIT 1)
                       )
                   ) AS NombreAlbum,
                   t.link_externo AS LinkExterno,
                   CASE WHEN t.portada IS NOT NULL THEN 1 ELSE 0 END AS TienePortada,
                   CASE WHEN t.archivo_audio IS NOT NULL AND t.archivo_audio != '' THEN 1 ELSE 0 END AS TieneArchivoAudio,
                   COALESCE(t.es_cover, 0) AS EsCover, COALESCE(t.es_original, 0) AS EsOriginal, t.artista_original AS ArtistaOriginal
            FROM temas_cd t
            JOIN interpretes i ON t.id_interprete = i.id
            LEFT JOIN albumes a ON t.id_album = a.id
            WHERE t.num_formato = @numMedio
            ORDER BY t.ubicacion
            """, new { numMedio });
        return temasCd.ToList();
        }
        catch (Exception ex)
        {
             System.IO.File.WriteAllText("error_log_temas.txt", $"{DateTime.Now}: {ex.Message}\n{ex.StackTrace}");
             throw;
        }
    }

    /// <summary>
    /// Obtiene el detalle de un intérprete con todos sus temas.
    /// </summary>
    public async Task<DetalleInterprete?> ObtenerInterpreteAsync(string nombre)
    {
        using var conn = _db.ObtenerConexion();

        var interprete = await conn.QueryFirstOrDefaultAsync<(int Id, string Nombre)>(
            "SELECT id AS Id, nombre AS Nombre FROM interpretes WHERE nombre = @nombre",
            new { nombre });

        if (interprete.Nombre == null)
            return null;

        var temasCassette = await conn.QueryAsync<TemaDeInterprete>("""
            SELECT t.id AS Id, 'cassette' AS Tipo, t.num_formato AS numMedio, t.tema AS Tema,
                   (t.lado || ':' || t.desde || '-' || t.hasta) AS Posicion
            FROM temas t WHERE t.id_interprete = @id
            ORDER BY t.num_formato, t.lado, t.desde
            """, new { id = interprete.Id });

        var temasCd = await conn.QueryAsync<TemaDeInterprete>("""
            SELECT t.id AS Id, 'cd' AS Tipo, t.num_formato AS numMedio, t.tema AS Tema,
                   CAST(t.ubicacion AS TEXT) AS Posicion
            FROM temas_cd t WHERE t.id_interprete = @id
            ORDER BY t.num_formato, t.ubicacion
            """, new { id = interprete.Id });

        var listaTemas = temasCassette.Concat(temasCd).ToList();

        return new DetalleInterprete
        {
            Id = interprete.Id,
            Nombre = interprete.Nombre,
            TotalTemasCassette = temasCassette.Count(),
            TotalTemasCd = temasCd.Count(),
            Temas = listaTemas
        };
    }

    /// <summary>
    /// Obtiene estadísticas generales del catálogo.
    /// </summary>
    public async Task<EstadisticasGenerales> ObtenerEstadisticasAsync()
    {
        using var conn = _db.ObtenerConexion();

        var totalInterpretes = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM interpretes");
        var totalTemasCassette = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM temas");
        var totalTemasCd = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM temas_cd");
        var totalCassettes = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM formato_grabado");
        var totalCds = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM formato_grabado_cd");

        // Top 20 intérpretes por cantidad de temas
        var topInterpretes = await conn.QueryAsync<InterpreteTop>("""
            SELECT i.id AS Id, i.nombre AS Nombre, 
                   (SELECT COUNT(*) FROM temas WHERE id_interprete = i.id) +
                   (SELECT COUNT(*) FROM temas_cd WHERE id_interprete = i.id) AS TotalTemas
            FROM interpretes i
            ORDER BY TotalTemas DESC
            LIMIT 20
            """);

        // Conteos por tipo de formato
        var ConteoMedios = await conn.QueryAsync<ConteoMedio>("""
            SELECT 'Cassette' AS Formato, COUNT(*) AS Total FROM formato_grabado
            UNION ALL
            SELECT 'CD' AS Formato, COUNT(*) AS Total FROM formato_grabado_cd
            """);

        // Top 10 marcas más usadas
        var conteoMarcas = await conn.QueryAsync<ConteoMarca>("""
            SELECT m.nombre AS Marca, COUNT(*) AS Total
            FROM (
                SELECT id_marca FROM formato_grabado
                UNION ALL
                SELECT id_marca FROM formato_grabado_cd
            ) f
            JOIN marca m ON f.id_marca = m.id_marca
            GROUP BY m.nombre
            ORDER BY Total DESC
            LIMIT 10
            """);

        return new EstadisticasGenerales
        {
            TotalInterpretes = totalInterpretes,
            TotalTemasCassette = totalTemasCassette,
            TotalTemasCd = totalTemasCd,
            TotalCassettes = totalCassettes,
            TotalCds = totalCds,
            TopInterpretes = topInterpretes.ToList(),
            ConteosPorMedio = ConteoMedios.ToList(),
            ConteosPorMarca = conteoMarcas.ToList()
        };
    }

    /// <summary>
    /// Obtiene lista de intérpretes con búsqueda opcional.
    /// "Desconocido" aparece primero si tiene canciones, y no aparece si tiene 0.
    /// </summary>
    public async Task<List<InterpreteResumen>> ListarInterpretesAsync(string? filtro = null, int limite = 100)
    {
        using var conn = _db.ObtenerConexion();

        // Consulta que excluye "Desconocido" si tiene 0 canciones y lo ordena primero si tiene canciones
        var sql = """
            SELECT i.id AS Id, i.nombre AS Interprete, 
                   (SELECT COUNT(*) FROM temas WHERE id_interprete = i.id) +
                   (SELECT COUNT(*) FROM temas_cd WHERE id_interprete = i.id) AS TotalTemas,
                   CASE WHEN i.foto_blob IS NOT NULL THEN 1 ELSE 0 END AS TieneFoto,
                   (SELECT GROUP_CONCAT(b.nombre, ', ') 
                    FROM banda_miembros bm 
                    JOIN interpretes b ON bm.id_banda = b.id 
                    WHERE bm.id_miembro = i.id) AS IntegranteDe
            FROM interpretes i
            WHERE (
                LOWER(TRIM(i.nombre)) != 'desconocido' 
                OR (
                    (SELECT COUNT(*) FROM temas WHERE id_interprete = i.id) +
                    (SELECT COUNT(*) FROM temas_cd WHERE id_interprete = i.id)
                ) > 0
            )
            """;

        if (!string.IsNullOrWhiteSpace(filtro))
        {
            sql += " AND i.nombre LIKE @patron";
        }

        // Ordenar: "Desconocido" primero (si tiene canciones), luego alfabéticamente
        sql += """
             ORDER BY 
                CASE WHEN LOWER(TRIM(i.nombre)) = 'desconocido' THEN 0 ELSE 1 END,
                i.nombre
            LIMIT @limite
            """;

        var patron = $"%{filtro}%";
        var resultado = await conn.QueryAsync<InterpreteResumen>(sql, new { patron, limite });
        return resultado.ToList();
    }

    /// <summary>
    /// Obtiene lista de formatos (cassettes y CDs).
    /// </summary>
    public async Task<List<DetalleMedio>> ListarMediosAsync(string? tipo = null, int limite = 100)
    {
        using var conn = _db.ObtenerConexion();

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

    /// <summary>
    /// Obtiene diagnóstico de la base de datos.
    /// </summary>
    public async Task<DiagnosticoImportacion> ObtenerDiagnosticoAsync(string rutaBase)
    {
        using var conn = _db.ObtenerConexion();
        
        // MIGRACIÓN: Verificar si existe columna es_original en temas
        var infoTemas = await conn.QueryAsync<string>("SELECT name FROM pragma_table_info('temas')");
        if (!infoTemas.Contains("es_original"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas ADD COLUMN es_original INTEGER DEFAULT 0");
            await conn.ExecuteAsync("ALTER TABLE temas_cd ADD COLUMN es_original INTEGER DEFAULT 0");
            
            // Auto-migración: Marcar como originales las canciones que tienen álbum (lo que ya tenemos)
            await conn.ExecuteAsync("UPDATE temas SET es_original = 1 WHERE id_album IS NOT NULL");
            await conn.ExecuteAsync("UPDATE temas_cd SET es_original = 1 WHERE id_album IS NOT NULL");
        }

        // MIGRACIÓN: Verificar si existe columna es_cover
        if (!infoTemas.Contains("es_cover"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas ADD COLUMN es_cover INTEGER DEFAULT 0");
            await conn.ExecuteAsync("ALTER TABLE temas_cd ADD COLUMN es_cover INTEGER DEFAULT 0");
        }

        if (!infoTemas.Contains("artista_original"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas ADD COLUMN artista_original TEXT");
            await conn.ExecuteAsync("ALTER TABLE temas_cd ADD COLUMN artista_original TEXT");
        }

        // MIGRACIÓN: Verificar si existe formato 'otro'
        var existeOtro = await conn.QueryFirstOrDefaultAsync<int>("SELECT COUNT(*) FROM formato WHERE LOWER(nombre) = 'otro'");
        if (existeOtro == 0)
        {
            await conn.ExecuteAsync("INSERT INTO formato (nombre) VALUES ('otro')");
        }

        // Conteos de tablas
        var conteos = new Dictionary<string, int>
        {
            ["ecualizador"] = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM ecualizador"),
            ["formato"] = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM formato"),
            ["fuente"] = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM fuente"),
            ["grabador"] = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM grabador"),
            ["marca"] = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM marca"),
            ["bias"] = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM bias"),
            ["modo"] = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM modo"),
            ["supresor"] = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM supresor"),
            ["interpretes"] = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM interpretes"),
            ["formato_grabado"] = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM formato_grabado"),
            ["formato_grabado_cd"] = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM formato_grabado_cd"),
            ["temas"] = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM temas"),
            ["temas_cd"] = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM temas_cd")
        };

        return new DiagnosticoImportacion
        {
            UltimaImportacion = DateTime.Now,
            BaseDatosExiste = true,
            ConteosTablas = conteos,
            ArchivosCSV = new List<ArchivoCSV>() // Ya no usamos CSVs
        };
    }

    // ============================================
    // CRUD - OBTENER OPCIONES PARA FORMULARIOS
    // ============================================

    /// <summary>
    /// Obtiene todas las opciones disponibles para los select de formularios.
    /// </summary>
    public async Task<OpcionesFormulario> ObtenerOpcionesFormularioAsync()
    {
        using var conn = _db.ObtenerConexion();

        var marcas = (await conn.QueryAsync<OpcionSelect>(
            "SELECT id_marca AS Id, nombre AS Nombre FROM marca ORDER BY nombre")).ToList();
        var grabadores = (await conn.QueryAsync<OpcionSelect>(
            "SELECT id_deck AS Id, nombre AS Nombre FROM grabador ORDER BY nombre")).ToList();
        var fuentes = (await conn.QueryAsync<OpcionSelect>(
            "SELECT id_fuente AS Id, nombre AS Nombre FROM fuente ORDER BY nombre")).ToList();
        var ecualizadores = (await conn.QueryAsync<OpcionSelect>(
            "SELECT id_ecual AS Id, nombre AS Nombre FROM ecualizador ORDER BY nombre")).ToList();
        var supresores = (await conn.QueryAsync<OpcionSelect>(
            "SELECT id_dolby AS Id, nombre AS Nombre FROM supresor ORDER BY nombre")).ToList();
        var bias = (await conn.QueryAsync<OpcionSelect>(
            "SELECT id_bias AS Id, nombre AS Nombre FROM bias ORDER BY nombre")).ToList();
        var modos = (await conn.QueryAsync<OpcionSelect>(
            "SELECT id_modo AS Id, nombre AS Nombre FROM modo ORDER BY nombre")).ToList();
        var interpretes = (await conn.QueryAsync<OpcionSelect>(
            "SELECT id AS Id, nombre AS Nombre FROM interpretes ORDER BY nombre")).ToList();

        return new OpcionesFormulario
        {
            Marcas = marcas,
            Grabadores = grabadores,
            Fuentes = fuentes,
            Ecualizadores = ecualizadores,
            Supresores = supresores,
            Bias = bias,
            Modos = modos,
            Interpretes = interpretes
        };
    }

    // ============================================
    // CRUD - FORMATOS
    // ============================================

    /// <summary>Crea un nuevo formato (cassette o CD). Acepta IDs o nombres.</summary>
    public async Task<CrudResponse> CrearFormatoAsync(MedioRequest request)
    {
        using var conn = _db.ObtenerConexion();

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

            // Obtener ID del formato
            var idFormato = await conn.QueryFirstOrDefaultAsync<int>(
                "SELECT id_formato FROM formato WHERE LOWER(nombre) = @nombre", 
                new { nombre = request.TipoMedio.ToLower() });
            
            if (idFormato == 0) // Fallback si no existe (no debería pasar si se valida antes)
            {
                if (request.TipoMedio.ToLower() == "cassette") idFormato = 1;
                else if (request.TipoMedio.ToLower() == "cd") idFormato = 2;
                else idFormato = 3; // Otro
            }

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
                    VALUES (@numMedio, @idFormato, @idMarca, @idDeck, @idEcual, @idDolby, @idBias, @idModo, @idFuente, @FechaInicio, @FechaTermino)
                    """, new
                {
                    request.numMedio,
                    idFormato,
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
                // CD u Otro
                await conn.ExecuteAsync("""
                    INSERT INTO formato_grabado_cd (num_formato, id_formato, id_marca, id_deck, id_fuente, fecha_grabacion)
                    VALUES (@numMedio, @idFormato, @idMarca, @idDeck, @idFuente, @FechaInicio)
                    """, new
                {
                    request.numMedio,
                    idFormato,
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
        using var conn = _db.ObtenerConexion();

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
    
    /// <summary>Resuelve un ID desde nombre. Si no existe, crea el registro.</summary>
    private async Task<int> ResolverIdAsync(SqliteConnection conn, string tabla, string columnaId, int? id, string? nombre)
    {
        // Si ya tenemos ID, usarlo
        if (id.HasValue && id.Value > 0)
            return id.Value;
        
        // Si no hay nombre, devolver 1 (valor por defecto)
        if (string.IsNullOrWhiteSpace(nombre))
            return 1;
        
        // Buscar por nombre exacto
        var existente = await conn.QueryFirstOrDefaultAsync<int?>(
            $"SELECT {columnaId} FROM {tabla} WHERE nombre = @nombre",
            new { nombre });
        
        if (existente.HasValue)
            return existente.Value;
        
        // Crear nuevo registro
        var maxId = await conn.QueryFirstOrDefaultAsync<int?>($"SELECT MAX({columnaId}) FROM {tabla}") ?? 0;
        var nuevoId = maxId + 1;
        
        await conn.ExecuteAsync(
            $"INSERT INTO {tabla} ({columnaId}, nombre) VALUES (@id, @nombre)",
            new { id = nuevoId, nombre });
        
        return nuevoId;
    }

    /// <summary>Elimina un formato y todas sus canciones.</summary>
    public async Task<CrudResponse> EliminarFormatoAsync(string numMedio)
    {
        using var conn = _db.ObtenerConexion();

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

    // ============================================
    // CRUD - CANCIONES
    // ============================================

    /// <summary>Obtiene temas con ID para poder editarlos.</summary>
    public async Task<List<TemaConId>> ObtenerTemasConIdAsync(string numMedio)
    {
        using var conn = _db.ObtenerConexion();

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
        using var conn = _db.ObtenerConexion();

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
                    INSERT INTO temas (num_formato, id_interprete, tema, lado, desde, hasta, es_cover, es_original, artista_original, id_album, link_externo)
                    VALUES (@numMedio, @idInterprete, @Tema, @Lado, @Desde, @Hasta, @EsCover, @EsOriginal, @ArtistaOriginal, @IdAlbum, @LinkExterno)
                    """, new
                {
                    request.numMedio,
                    idInterprete,
                    request.Tema,
                    Lado = request.Lado ?? "A",
                    Desde = request.Desde ?? 1,
                    Hasta = request.Hasta ?? 1,
                    EsCover = request.EsCover ? 1 : 0,
                    EsOriginal = request.EsOriginal ? 1 : 0,
                    request.ArtistaOriginal,
                    request.IdAlbum,
                    request.LinkExterno
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
                    INSERT INTO temas_cd (num_formato, id_interprete, tema, ubicacion, es_cover, es_original, artista_original, id_album, link_externo)
                    VALUES (@numMedio, @idInterprete, @Tema, @ubicacion, @EsCover, @EsOriginal, @ArtistaOriginal, @IdAlbum, @LinkExterno)
                    """, new
                {
                    request.numMedio,
                    idInterprete,
                    request.Tema,
                    ubicacion = request.Ubicacion ?? (maxUbicacion + 1),
                    EsCover = request.EsCover ? 1 : 0,
                    EsOriginal = request.EsOriginal ? 1 : 0,
                    request.ArtistaOriginal,
                    request.IdAlbum,
                    request.LinkExterno
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
        using var conn = _db.ObtenerConexion();

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
                           es_cover = @EsCover, es_original = @EsOriginal, artista_original = @ArtistaOriginal,
                           id_album = @IdAlbum, link_externo = @LinkExterno
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
                    EsOriginal = request.EsOriginal ? 1 : 0,
                    ArtistaOriginal = request.ArtistaOriginal,
                    request.IdAlbum,
                    request.LinkExterno
                });
            }
            else
            {
                rows = await conn.ExecuteAsync("""
                    UPDATE temas_cd SET id_interprete = @idInterprete, tema = @Tema, ubicacion = @Ubicacion,
                           es_cover = @EsCover, es_original = @EsOriginal, artista_original = @ArtistaOriginal,
                           id_album = @IdAlbum, link_externo = @LinkExterno
                    WHERE id = @id
                    """, new
                {
                    id,
                    idInterprete,
                    request.Tema,
                    Ubicacion = request.Ubicacion ?? 1,
                    EsCover = request.EsCover ? 1 : 0,
                    EsOriginal = request.EsOriginal ? 1 : 0,
                    request.ArtistaOriginal,
                    request.IdAlbum,
                    request.LinkExterno
                });
            }

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Canción no encontrada" };

            // Gestionar lógica de originales si se marcó como tal
            await GestionarOriginalidadAsync(id, idInterprete, request.Tema, request.EsOriginal);

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
        using var conn = _db.ObtenerConexion();

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
        using var conn = _db.ObtenerConexion();

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

    // ============================================
    // CRUD - INTÉRPRETES
    // ============================================

    /// <summary>Crea un nuevo intérprete.</summary>
    public async Task<CrudResponse> CrearInterpreteAsync(string nombre)
    {
        using var conn = _db.ObtenerConexion();

        try
        {
            var existe = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT id FROM interpretes WHERE nombre = @nombre", new { nombre });
            
            if (existe.HasValue)
                return new CrudResponse { Exito = false, Mensaje = "Ya existe un intérprete con ese nombre" };

            var maxId = await conn.QueryFirstOrDefaultAsync<int?>("SELECT MAX(id) FROM interpretes") ?? 0;
            var nuevoId = maxId + 1;

            await conn.ExecuteAsync(
                "INSERT INTO interpretes (id, nombre) VALUES (@id, @nombre)",
                new { id = nuevoId, nombre });

            return new CrudResponse { Exito = true, Mensaje = "Intérprete creado correctamente", IdCreado = nuevoId };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Elimina un intérprete, desasociando las canciones que tenga.</summary>
    public async Task<CrudResponse> EliminarInterpreteAsync(int id)
    {
        using var conn = _db.ObtenerConexion();

        try
        {
            // Verificar si el intérprete existe
            var existe = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT id FROM interpretes WHERE id = @id", new { id });
            
            if (!existe.HasValue)
                return new CrudResponse { Exito = false, Mensaje = "Intérprete no encontrado" };

            // Contar canciones asociadas
            var cancionesCassette = await conn.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM temas WHERE id_interprete = @id", new { id });
            
            var cancionesCd = await conn.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM temas_cd WHERE id_interprete = @id", new { id });
            
            var totalCanciones = cancionesCassette + cancionesCd;
            
            // Si hay canciones, obtener o crear el intérprete "Desconocido"
            int idDesconocido = 1; // ID por defecto
            if (totalCanciones > 0)
            {
                var desconocido = await conn.QueryFirstOrDefaultAsync<int?>(
                    "SELECT id FROM interpretes WHERE nombre = 'Desconocido'");
                
                if (!desconocido.HasValue)
                {
                    // Crear el intérprete "Desconocido"
                    var maxId = await conn.QueryFirstOrDefaultAsync<int?>("SELECT MAX(id) FROM interpretes") ?? 0;
                    idDesconocido = maxId + 1;
                    await conn.ExecuteAsync(
                        "INSERT INTO interpretes (id, nombre) VALUES (@id, 'Desconocido')",
                        new { id = idDesconocido });
                }
                else
                {
                    idDesconocido = desconocido.Value;
                }
                
                // Desasociar canciones (moverlas al intérprete "Desconocido")
                await conn.ExecuteAsync(
                    "UPDATE temas SET id_interprete = @idDesconocido WHERE id_interprete = @id", 
                    new { idDesconocido, id });
                await conn.ExecuteAsync(
                    "UPDATE temas_cd SET id_interprete = @idDesconocido WHERE id_interprete = @id", 
                    new { idDesconocido, id });
            }

            // Eliminar miembros de banda si es una banda
            await conn.ExecuteAsync("DELETE FROM banda_miembros WHERE id_banda = @id", new { id });
            
            // Eliminar géneros asociados
            await conn.ExecuteAsync("DELETE FROM interprete_generos WHERE id_interprete = @id", new { id });
            
            // Eliminar álbumes del intérprete (desvinculando canciones primero)
            var albumesIds = await conn.QueryAsync<int>("SELECT id FROM albumes WHERE id_interprete = @id", new { id });
            foreach (var albumId in albumesIds)
            {
                await conn.ExecuteAsync("UPDATE temas SET id_album = NULL WHERE id_album = @albumId", new { albumId });
                await conn.ExecuteAsync("UPDATE temas_cd SET id_album = NULL WHERE id_album = @albumId", new { albumId });
            }
            await conn.ExecuteAsync("DELETE FROM albumes WHERE id_interprete = @id", new { id });

            // Finalmente eliminar el intérprete
            var rows = await conn.ExecuteAsync("DELETE FROM interpretes WHERE id = @id", new { id });

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Error al eliminar el intérprete" };

            // Construir mensaje de respuesta
            var mensaje = "Intérprete eliminado correctamente";
            if (totalCanciones > 0)
            {
                mensaje += $". {totalCanciones} canciones fueron movidas a 'Desconocido'";
            }

            return new CrudResponse { Exito = true, Mensaje = mensaje };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    // ============================================
    // CRUD - ÁLBUMES
    // ============================================

    /// <summary>Verifica si la columna es_single existe en albumes.</summary>
    private async Task<bool> ExisteColumnaEsSingle(IDbConnection conn)
    {
        var columnas = await conn.QueryAsync<string>("SELECT name FROM pragma_table_info('albumes')");
        return columnas.Contains("es_single");
    }

    /// <summary>Lista todos los álbumes, opcionalmente filtrados por texto o artista.</summary>
    public async Task<List<AlbumResumen>> ListarAlbumesAsync(string? filtro = null, int limite = 100, int? idInterprete = null)
    {
        using var conn = _db.ObtenerConexion();
        
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

        var whereClauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(filtro))
        {
            whereClauses.Add("(a.nombre LIKE @patron OR i.nombre LIKE @patron)");
        }
        if (idInterprete.HasValue)
        {
            whereClauses.Add("a.id_interprete = @idInterprete");
        }
        if (whereClauses.Count > 0)
        {
            sql += " WHERE " + string.Join(" AND ", whereClauses);
        }

        sql += " ORDER BY a.nombre LIMIT @limite";

        var patron = $"%{filtro}%";
        var resultado = await conn.QueryAsync<AlbumResumen>(sql, new { patron, limite, idInterprete });
        return resultado.ToList();
    }

    /// <summary>Obtiene el detalle de un álbum con sus canciones.</summary>
    public async Task<AlbumDetalle?> ObtenerAlbumAsync(int id)
    {
        using var conn = _db.ObtenerConexion();
        
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
        using var conn = _db.ObtenerConexion();

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
        using var conn = _db.ObtenerConexion();

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
        using var conn = _db.ObtenerConexion();

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
        using var conn = _db.ObtenerConexion();

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
        using var conn = _db.ObtenerConexion();
        return await conn.QueryFirstOrDefaultAsync<byte[]?>(
            "SELECT portada FROM albumes WHERE id = @id", new { id });
    }

    // ============================================
    // CANCIÓN INDIVIDUAL
    // ============================================

    /// <summary>Obtiene el detalle completo de una canción.</summary>
    public async Task<CancionDetalle?> ObtenerCancionAsync(int id, string tipo)
    {
        using var conn = _db.ObtenerConexion();

        // Verificar si existe la columna es_single en albumes
        var columnasAlbumes = await conn.QueryAsync<string>("SELECT name FROM pragma_table_info('albumes')");
        var tieneEsSingle = columnasAlbumes.Contains("es_single");
        var esSingleCol = tieneEsSingle ? "COALESCE(a.es_single, 0)" : "0";

        // Verificar columnas de covers/originales y audio
        var tabla = tipo.ToLower() == "cassette" ? "temas" : "temas_cd";
        var columnasTemas = await conn.QueryAsync<string>($"SELECT name FROM pragma_table_info('{tabla}')");
        var tieneEsCover = columnasTemas.Contains("es_cover");
        var tieneEsOriginal = columnasTemas.Contains("es_original");
        var tieneArtistaOriginal = columnasTemas.Contains("artista_original");
        var tieneArchivoAudio = columnasTemas.Contains("archivo_audio");
        var tieneDuracionSegundos = columnasTemas.Contains("duracion_segundos");
        var tieneFormatoAudio = columnasTemas.Contains("formato_audio");
        var tieneEsFavorito = columnasTemas.Contains("es_favorito");

        var esCoverCol = tieneEsCover ? "COALESCE(t.es_cover, 0)" : "0";
        var esOriginalCol = tieneEsOriginal ? "COALESCE(t.es_original, 0)" : "0";
        var artistaOriginalCol = tieneArtistaOriginal ? "t.artista_original" : "NULL";
        var archivoAudioCol = tieneArchivoAudio ? "t.archivo_audio" : "NULL";
        var duracionSegundosCol = tieneDuracionSegundos ? "t.duracion_segundos" : "NULL";
        var formatoAudioCol = tieneFormatoAudio ? "t.formato_audio" : "NULL";
        var esFavoritoCol = tieneEsFavorito ? "COALESCE(t.es_favorito, 0)" : "0";

        CancionDetalle? cancion = null;

        if (tipo.ToLower() == "cassette")
        {
            cancion = await conn.QueryFirstOrDefaultAsync<CancionDetalle>($"""
                SELECT t.id AS Id, 'cassette' AS Tipo, t.tema AS Tema, i.nombre AS Interprete, t.id_interprete AS IdInterprete,
                       t.num_formato AS numMedio, t.lado AS Lado, t.desde AS Desde, t.hasta AS Hasta, NULL AS Ubicacion,
                       t.id_album AS IdAlbum, a.nombre AS NombreAlbum, ia.nombre AS ArtistaAlbum, a.anio AS AnioAlbum,
                       {esSingleCol} AS EsAlbumSingle, t.link_externo AS LinkExterno,
                       CASE WHEN t.portada IS NOT NULL THEN 1 ELSE 0 END AS TienePortada,
                       CASE WHEN a.portada IS NOT NULL THEN 1 ELSE 0 END AS TienePortadaAlbum,
                       {esCoverCol} AS EsCover, {esOriginalCol} AS EsOriginal, {artistaOriginalCol} AS ArtistaOriginal,
                       {archivoAudioCol} AS ArchivoAudio, {duracionSegundosCol} AS DuracionSegundos, {formatoAudioCol} AS FormatoAudio,
                       {esFavoritoCol} AS EsFavorito
                FROM temas t
                JOIN interpretes i ON t.id_interprete = i.id
                LEFT JOIN albumes a ON t.id_album = a.id
                LEFT JOIN interpretes ia ON a.id_interprete = ia.id
                WHERE t.id = @id
                """, new { id });
        }
        else
        {
            cancion = await conn.QueryFirstOrDefaultAsync<CancionDetalle>($"""
                SELECT t.id AS Id, 'cd' AS Tipo, t.tema AS Tema, i.nombre AS Interprete, t.id_interprete AS IdInterprete,
                       t.num_formato AS numMedio, NULL AS Lado, NULL AS Desde, NULL AS Hasta, t.ubicacion AS Ubicacion,
                       t.id_album AS IdAlbum, a.nombre AS NombreAlbum, ia.nombre AS ArtistaAlbum, a.anio AS AnioAlbum,
                       {esSingleCol} AS EsAlbumSingle, t.link_externo AS LinkExterno,
                       CASE WHEN t.portada IS NOT NULL THEN 1 ELSE 0 END AS TienePortada,
                       CASE WHEN a.portada IS NOT NULL THEN 1 ELSE 0 END AS TienePortadaAlbum,
                       {esCoverCol} AS EsCover, {esOriginalCol} AS EsOriginal, {artistaOriginalCol} AS ArtistaOriginal,
                       {archivoAudioCol} AS ArchivoAudio, {duracionSegundosCol} AS DuracionSegundos, {formatoAudioCol} AS FormatoAudio,
                       {esFavoritoCol} AS EsFavorito
                FROM temas_cd t
                JOIN interpretes i ON t.id_interprete = i.id
                LEFT JOIN albumes a ON t.id_album = a.id
                LEFT JOIN interpretes ia ON a.id_interprete = ia.id
                WHERE t.id = @id
                """, new { id });
        }

        if (cancion != null)
        {
            cancion.TienePortadaEmbebida = VerificarPortadaEmbebida(cancion.ArchivoAudio);
        }

        return cancion;
    }

    /// <summary>Actualiza una canción con información extendida.</summary>
    public async Task<CrudResponse> ActualizarCancionExtendidaAsync(int id, string tipo, CancionUpdateRequest request)
    {
        using var conn = _db.ObtenerConexion();

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
                             es_cover = @EsCover, es_original = @EsOriginal, artista_original = @ArtistaOriginal
                      WHERE id = @id
                      """
                    : """
                      UPDATE temas SET tema = @Tema, id_interprete = @idInterprete,
                             link_externo = @LinkExterno, lado = @Lado, desde = @Desde, hasta = @Hasta,
                             es_cover = @EsCover, es_original = @EsOriginal, artista_original = @ArtistaOriginal
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
                    EsOriginal = request.EsOriginal ? 1 : 0,
                    request.ArtistaOriginal
                });
            }
            else
            {
                var sql = actualizarAlbum
                    ? """
                      UPDATE temas_cd SET tema = @Tema, id_interprete = @idInterprete, id_album = @idAlbum,
                             link_externo = @LinkExterno, ubicacion = @Ubicacion,
                             es_cover = @EsCover, es_original = @EsOriginal, artista_original = @ArtistaOriginal
                      WHERE id = @id
                      """
                    : """
                      UPDATE temas_cd SET tema = @Tema, id_interprete = @idInterprete,
                             link_externo = @LinkExterno, ubicacion = @Ubicacion,
                             es_cover = @EsCover, es_original = @EsOriginal, artista_original = @ArtistaOriginal
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
                    EsOriginal = request.EsOriginal ? 1 : 0,
                    request.ArtistaOriginal
                });
            }

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Canción no encontrada" };

            // Gestionar lógica de originales si se marcó como tal
            await GestionarOriginalidadAsync(id, idInterprete, request.Tema, request.EsOriginal);

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
        using var conn = _db.ObtenerConexion();

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

    /// <summary>Elimina la portada personalizada de una canción.</summary>
    public async Task<CrudResponse> EliminarPortadaCancionAsync(int id, string tipo)
    {
        using var conn = _db.ObtenerConexion();

        try
        {
            var tabla = tipo.ToLower() == "cassette" ? "temas" : "temas_cd";
            var rows = await conn.ExecuteAsync(
                $"UPDATE {tabla} SET portada = NULL WHERE id = @id",
                new { id });

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Canción no encontrada" };

            return new CrudResponse { Exito = true, Mensaje = "Portada eliminada correctamente" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }


    /// <summary>Obtiene la portada de una canción (prioridad: Embebida > Álbum > Manual > Original).</summary>
    public async Task<byte[]?> ObtenerPortadaCancionAsync(int id, string tipo)
    {
        using var conn = _db.ObtenerConexion();

        var tabla = tipo.ToLower() == "cassette" ? "temas" : "temas_cd";
        
        // Obtener datos básicos para decidir
        var datosCancion = await conn.QueryFirstOrDefaultAsync<dynamic>(
            $"SELECT portada, id_album, archivo_audio, COALESCE(es_cover, 0) as es_cover, artista_original, tema FROM {tabla} WHERE id = @id", new { id });
        
        if (datosCancion == null) return null;

        // 1. Portada EMBEBIDA (Metadatos del archivo de audio)
        string? archivoAudio = (string?)datosCancion.archivo_audio;
        if (!string.IsNullOrEmpty(archivoAudio))
        {
            var fullPath = Path.Combine(AppContext.BaseDirectory, archivoAudio);
            
            // Usamos VerificarPortadaEmbebida para chequear cache primero y evitar excepciones costosas si no hay tag
            if (VerificarPortadaEmbebida(archivoAudio))
            {
                try
                {
                    using var file = TagLib.File.Create(fullPath);
                    if (file.Tag.Pictures != null && file.Tag.Pictures.Length > 0)
                    {
                        return file.Tag.Pictures[0].Data.Data;
                    }
                }
                catch { /* Ignorar errores de lectura en este punto */ }
            }
        }

        // 2. Portada del ÁLBUM
        int? idAlbum = (int?)datosCancion.id_album;
        if (idAlbum.HasValue)
        {
            var portadaAlbum = await conn.QueryFirstOrDefaultAsync<byte[]?>(
                "SELECT portada FROM albumes WHERE id = @id", new { id = idAlbum.Value });
            
            if (portadaAlbum != null && portadaAlbum.Length > 0)
                return portadaAlbum;
        }

        // 3. Portada MANUAL (Subida específicamente para la canción)
        byte[]? portadaPropia = (byte[]?)datosCancion.portada;
        if (portadaPropia != null && portadaPropia.Length > 0)
            return portadaPropia;

        // 4. Si es cover y no tiene imagen propia ni álbum, buscar la versión ORIGINAL
        bool esCover = ((int)datosCancion.es_cover) == 1;
        string? artistaOrig = (string?)datosCancion.artista_original;
        string tema = (string)datosCancion.tema;

        if (esCover && !string.IsNullOrEmpty(artistaOrig))
        {
            var temaNorm = NormalizarTexto(tema);
            var artistaNorm = NormalizarTexto(artistaOrig);

            // Buscar primero en cassettes
            var originalCassette = await conn.QueryFirstOrDefaultAsync<dynamic>(
                """
                SELECT t.id_album, t.portada 
                FROM temas t
                JOIN interpretes i ON t.id_interprete = i.id
                WHERE LOWER(t.tema) = @temaNorm AND LOWER(i.nombre) = @artistaNorm
                LIMIT 1
                """, new { temaNorm, artistaNorm });

            if (originalCassette != null)
            {
                // Si la original tiene portada propia
                if (originalCassette.portada != null && ((byte[])originalCassette.portada).Length > 0)
                    return originalCassette.portada;
                
                // Si la original tiene álbum, usar portada del álbum
                if (originalCassette.id_album != null)
                {
                    var portadaAlbumOrig = await conn.QueryFirstOrDefaultAsync<byte[]?>(
                        "SELECT portada FROM albumes WHERE id = @id", new { id = (int)originalCassette.id_album });
                    if (portadaAlbumOrig != null && portadaAlbumOrig.Length > 0)
                        return portadaAlbumOrig;
                }
            }

            // Si no, buscar en CDs
            var originalCd = await conn.QueryFirstOrDefaultAsync<dynamic>(
                """
                SELECT t.id_album, t.portada 
                FROM temas_cd t
                JOIN interpretes i ON t.id_interprete = i.id
                WHERE LOWER(t.tema) = @temaNorm AND LOWER(i.nombre) = @artistaNorm
                LIMIT 1
                """, new { temaNorm, artistaNorm });

            if (originalCd != null)
            {
                if (originalCd.portada != null && ((byte[])originalCd.portada).Length > 0)
                    return originalCd.portada;
                
                if (originalCd.id_album != null)
                {
                    var portadaAlbumOrig = await conn.QueryFirstOrDefaultAsync<byte[]?>(
                        "SELECT portada FROM albumes WHERE id = @id", new { id = (int)originalCd.id_album });
                    if (portadaAlbumOrig != null && portadaAlbumOrig.Length > 0)
                        return portadaAlbumOrig;
                }
            }
        }

        return null;
    }

    // ============================================
    // ASIGNACIÓN DE CANCIONES A ÁLBUMES
    // ============================================

    /// <summary>Obtiene todas las canciones para mostrar en la galería.</summary>
    public async Task<List<CancionGaleria>> ObtenerTodasCancionesAsync()
    {
        using var conn = _db.ObtenerConexion();
        
        var tieneEsSingle = await ExisteColumnaEsSingle(conn);
        var esSingleCol = tieneEsSingle ? "COALESCE(a.es_single, 0)" : "0";

        var sqlCassette = $"""
            SELECT t.id AS Id, 'cassette' AS Tipo, t.tema AS Tema, i.nombre AS Interprete,
                   t.num_formato AS numMedio, t.id_album AS IdAlbum, a.nombre AS AlbumNombre,
                   {esSingleCol} AS EsAlbumSingle, a.anio AS Anio,
                   COALESCE(t.es_cover, 0) AS EsCover, t.artista_original AS ArtistaOriginal,
                   t.lado AS Lado, t.desde AS Desde, t.hasta AS Hasta, NULL AS Ubicacion,
                   t.archivo_audio AS ArchivoAudio,
                   (CASE WHEN t.portada IS NOT NULL AND length(t.portada) > 0 THEN 1 ELSE 0 END) AS TienePortada
            FROM temas t
            JOIN interpretes i ON t.id_interprete = i.id
            LEFT JOIN albumes a ON t.id_album = a.id
            """;

        var sqlCd = $"""
            SELECT t.id AS Id, 'cd' AS Tipo, t.tema AS Tema, i.nombre AS Interprete,
                   t.num_formato AS numMedio, t.id_album AS IdAlbum, a.nombre AS AlbumNombre,
                   {esSingleCol} AS EsAlbumSingle, a.anio AS Anio,
                   COALESCE(t.es_cover, 0) AS EsCover, t.artista_original AS ArtistaOriginal,
                   NULL AS Lado, NULL AS Desde, NULL AS Hasta, t.ubicacion AS Ubicacion,
                   t.archivo_audio AS ArchivoAudio,
                   (CASE WHEN t.portada IS NOT NULL AND length(t.portada) > 0 THEN 1 ELSE 0 END) AS TienePortada
            FROM temas_cd t
            JOIN interpretes i ON t.id_interprete = i.id
            LEFT JOIN albumes a ON t.id_album = a.id
            """;

        var sql = $"{sqlCassette} UNION ALL {sqlCd} ORDER BY Tema";

        var resultado = await conn.QueryAsync<CancionGaleria>(sql);
        var lista = resultado.ToList();

        // Enriquecer con información de portada embebida (usa cache interno)
        foreach (var c in lista)
        {
            c.TienePortadaEmbebida = VerificarPortadaEmbebida(c.ArchivoAudio);
        }

        return lista;
    }

    /// <summary>Obtiene canciones disponibles para asignar a un álbum (con filtro opcional).</summary>
    public async Task<List<CancionDisponible>> ObtenerCancionesDisponiblesAsync(string? filtro = null, int? excluirAlbumId = null, int limite = 200, bool soloSinAlbum = false)
    {
        using var conn = _db.ObtenerConexion();
        
        // Si no hay filtro, devolver las primeras N canciones ordenadas por tema
        // Si hay filtro, buscar por tema o intérprete
        var patron = string.IsNullOrWhiteSpace(filtro) ? "%" : $"%{filtro}%";
        var tieneFiltro = !string.IsNullOrWhiteSpace(filtro);

        var sqlCassette = """
            SELECT t.id AS Id, 'cassette' AS Tipo, t.tema AS Tema, i.nombre AS Interprete,
                   t.num_formato AS numMedio, (t.lado || ':' || t.desde || '-' || t.hasta) AS Posicion,
                   t.id_album AS IdAlbumActual, a.nombre AS NombreAlbumActual
            FROM temas t
            JOIN interpretes i ON t.id_interprete = i.id
            LEFT JOIN albumes a ON t.id_album = a.id
            WHERE (t.tema LIKE @patron OR i.nombre LIKE @patron OR t.num_formato LIKE @patron)
            """;

        var sqlCd = """
            SELECT t.id AS Id, 'cd' AS Tipo, t.tema AS Tema, i.nombre AS Interprete,
                   t.num_formato AS numMedio, CAST(t.ubicacion AS TEXT) AS Posicion,
                   t.id_album AS IdAlbumActual, a.nombre AS NombreAlbumActual
            FROM temas_cd t
            JOIN interpretes i ON t.id_interprete = i.id
            LEFT JOIN albumes a ON t.id_album = a.id
            WHERE (t.tema LIKE @patron OR i.nombre LIKE @patron OR t.num_formato LIKE @patron)
            """;

        // Si soloSinAlbum es true, excluir todas las canciones que ya tienen álbum
        if (soloSinAlbum)
        {
            sqlCassette += " AND t.id_album IS NULL";
            sqlCd += " AND t.id_album IS NULL";
        }
        else if (excluirAlbumId.HasValue)
        {
            // Solo excluir las canciones del álbum específico (comportamiento anterior)
            sqlCassette += " AND (t.id_album IS NULL OR t.id_album != @excluirAlbumId)";
            sqlCd += " AND (t.id_album IS NULL OR t.id_album != @excluirAlbumId)";
        }

        var sql = $"{sqlCassette} UNION ALL {sqlCd} ORDER BY Tema LIMIT @limite";

        var resultado = await conn.QueryAsync<CancionDisponible>(sql, new { patron, excluirAlbumId, limite });
        return resultado.ToList();
    }

    /// <summary>Asigna múltiples canciones a un álbum.</summary>
    public async Task<CrudResponse> AsignarCancionesAlbumAsync(int albumId, AsignarCancionesRequest request)
    {
        using var conn = _db.ObtenerConexion();

        try
        {
            // Verificar que el álbum existe
            var albumExiste = await conn.QueryFirstOrDefaultAsync<int?>(
                "SELECT id FROM albumes WHERE id = @albumId", new { albumId });
            
            if (!albumExiste.HasValue)
                return new CrudResponse { Exito = false, Mensaje = "Álbum no encontrado" };

            int asignadas = 0;
            var noEncontradas = new List<string>();
            
            foreach (var cancion in request.Canciones)
            {
                var tabla = cancion.Tipo.ToLower() == "cassette" ? "temas" : "temas_cd";
                
                // Verificar que la canción existe antes de asignarla
                var cancionExiste = await conn.QueryFirstOrDefaultAsync<int?>(
                    $"SELECT id FROM {tabla} WHERE id = @id", new { id = cancion.Id });
                
                if (!cancionExiste.HasValue)
                {
                    noEncontradas.Add($"ID {cancion.Id} ({cancion.Tipo})");
                    continue;
                }
                
                var rows = await conn.ExecuteAsync(
                    $"UPDATE {tabla} SET id_album = @albumId WHERE id = @id",
                    new { albumId, id = cancion.Id });
                asignadas += rows;
            }

            if (noEncontradas.Count > 0 && asignadas == 0)
            {
                return new CrudResponse 
                { 
                    Exito = false, 
                    Mensaje = $"Canción(es) no encontrada(s): {string.Join(", ", noEncontradas)}. Es posible que hayan sido eliminadas." 
                };
            }
            
            var mensaje = $"{asignadas} canción(es) asignada(s) al álbum";
            if (noEncontradas.Count > 0)
            {
                mensaje += $". No se encontraron: {string.Join(", ", noEncontradas)}";
            }

            return new CrudResponse 
            { 
                Exito = true, 
                Mensaje = mensaje 
            };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Asigna una sola canción a un álbum (o la quita si idAlbum es null).</summary>
    public async Task<CrudResponse> AsignarCancionAAlbumAsync(int cancionId, string tipo, int? idAlbum)
    {
        using var conn = _db.ObtenerConexion();

        try
        {
            var tabla = tipo.ToLower() == "cassette" ? "temas" : "temas_cd";
            var rows = await conn.ExecuteAsync(
                $"UPDATE {tabla} SET id_album = @idAlbum WHERE id = @cancionId",
                new { cancionId, idAlbum });

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Canción no encontrada" };

            var mensaje = idAlbum.HasValue 
                ? "Canción asignada al álbum correctamente" 
                : "Canción removida del álbum";

            return new CrudResponse { Exito = true, Mensaje = mensaje };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Quita una canción de su álbum actual.</summary>
    public async Task<CrudResponse> QuitarCancionDeAlbumAsync(int cancionId, string tipo)
    {
        using var conn = _db.ObtenerConexion();

        try
        {
            var tabla = tipo.ToLower() == "cassette" ? "temas" : "temas_cd";
            var rows = await conn.ExecuteAsync(
                $"UPDATE {tabla} SET id_album = NULL WHERE id = @cancionId",
                new { cancionId });

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Canción no encontrada" };

            return new CrudResponse { Exito = true, Mensaje = "Canción removida del álbum" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    // ============================================
    // NOTIFICACIONES DE DATA HYGIENE
    // ============================================

    /// <summary>Obtiene notificaciones sobre datos incompletos o con problemas.</summary>
    /// <summary>Obtiene notificaciones sobre datos incompletos o con problemas.</summary>
    public async Task<List<NotificacionDatos>> ObtenerNotificacionesAsync()
    {
        using var conn = _db.ObtenerConexion();
        var notificaciones = new List<NotificacionDatos>();
        int contador = 0;

        // 1. DUPLICADOS MULTI-ARTISTA (Lo más importante)
        var duplicados = await ObtenerDuplicadosAsync("multiartista");
        foreach (var grupo in duplicados)
        {
            // Verificar si alguna canción del grupo ya está marcada como original
            bool tieneOriginal = grupo.Canciones.Any(c => c.EsOriginal);
            int countOriginal = grupo.Canciones.Count(c => c.EsOriginal);
            
            // Si no hay original explícito, verificamos si hay ambigüedad
            // Si hay exactamente una canción que NO es cover, asumimos que esa es la original implícita
            int noCovers = grupo.Canciones.Count(c => !c.EsCover);
            bool esAmbiguo = noCovers != 1;

            if (countOriginal > 1)
            {
                // Agrupar por artista para ofrecer opciones
                var artistas = grupo.Canciones
                    .GroupBy(c => c.IdInterprete)
                    .Select(g => new 
                    { 
                        IdInterprete = g.Key, 
                        Nombre = g.First().Interprete, 
                        Cantidad = g.Count() 
                    })
                    .OrderByDescending(a => a.Cantidad)
                    .ToList();

                // Crear lista de opciones para el frontend
                var opciones = artistas.Select(a => new OpcionArtistaOriginal 
                {
                    IdInterprete = a.IdInterprete,
                    Nombre = a.Nombre,
                    CantidadCopias = a.Cantidad,
                    EsMasAntiguo = false
                }).ToList();

                notificaciones.Add(new NotificacionDatos
                {
                    Id = $"notif-{++contador}",
                    Tipo = "duplicado",
                    Severidad = "warning",
                    Mensaje = $"'{grupo.TemaNormalizado}' tiene {grupo.TotalArtistas} artistas distintos. ¿Cuál es el original?",
                    GrupoId = grupo.Id,
                    OpcionesArtista = opciones,
                    UrlArreglar = $"perfil-cancion.html?grupo={System.Net.WebUtility.UrlEncode(grupo.Id)}"
                });
            }
        }

        // 2. Canciones sin intérprete válido (cassettes)
        var sinInterpreteCassette = await conn.QueryAsync<(int Id, string Tema, string numMedio)>("""
            SELECT t.id, t.tema, t.num_formato 
            FROM temas t 
            LEFT JOIN interpretes i ON t.id_interprete = i.id 
            WHERE i.id IS NULL
            """);
        foreach (var c in sinInterpreteCassette)
        {
            notificaciones.Add(new NotificacionDatos
            {
                Id = $"notif-{++contador}",
                Tipo = "cancion",
                Severidad = "error",
                Mensaje = $"Canción '{c.Tema}' (Cassette {c.numMedio}) sin intérprete asignado",
                EntidadId = c.Id.ToString(),
                EntidadTipo = "cassette",
                CampoFaltante = "interprete",
                UrlArreglar = $"cancion.html?id={c.Id}&tipo=cassette"
            });
        }

        // 3. Canciones sin intérprete válido (CDs)
        var sinInterpreteCd = await conn.QueryAsync<(int Id, string Tema, string numMedio)>("""
            SELECT t.id, t.tema, t.num_formato 
            FROM temas_cd t 
            LEFT JOIN interpretes i ON t.id_interprete = i.id 
            WHERE i.id IS NULL
            """);
        foreach (var c in sinInterpreteCd)
        {
            notificaciones.Add(new NotificacionDatos
            {
                Id = $"notif-{++contador}",
                Tipo = "cancion",
                Severidad = "error",
                Mensaje = $"Canción '{c.Tema}' (CD {c.numMedio}) sin intérprete asignado",
                EntidadId = c.Id.ToString(),
                EntidadTipo = "cd",
                CampoFaltante = "interprete",
                UrlArreglar = $"cancion.html?id={c.Id}&tipo=cd"
            });
        }

        // 3b. Canciones con intérprete "Desconocido" (cassettes)
        var desconocidoCassette = await conn.QueryAsync<(int Id, string Tema, string numMedio)>("""
            SELECT t.id, t.tema, t.num_formato 
            FROM temas t 
            JOIN interpretes i ON t.id_interprete = i.id 
            WHERE LOWER(TRIM(i.nombre)) = 'desconocido'
            """);
        foreach (var c in desconocidoCassette)
        {
            notificaciones.Add(new NotificacionDatos
            {
                Id = $"notif-{++contador}",
                Tipo = "cancion",
                Severidad = "warning",
                Mensaje = $"Canción '{c.Tema}' (Cassette {c.numMedio}) tiene intérprete 'Desconocido'",
                EntidadId = c.Id.ToString(),
                EntidadTipo = "cassette",
                CampoFaltante = "interprete",
                UrlArreglar = $"cancion.html?id={c.Id}&tipo=cassette"
            });
        }

        // 3c. Canciones con intérprete "Desconocido" (CDs)
        var desconocidoCd = await conn.QueryAsync<(int Id, string Tema, string numMedio)>("""
            SELECT t.id, t.tema, t.num_formato 
            FROM temas_cd t 
            JOIN interpretes i ON t.id_interprete = i.id 
            WHERE LOWER(TRIM(i.nombre)) = 'desconocido'
            """);
        foreach (var c in desconocidoCd)
        {
            notificaciones.Add(new NotificacionDatos
            {
                Id = $"notif-{++contador}",
                Tipo = "cancion",
                Severidad = "warning",
                Mensaje = $"Canción '{c.Tema}' (CD {c.numMedio}) tiene intérprete 'Desconocido'",
                EntidadId = c.Id.ToString(),
                EntidadTipo = "cd",
                CampoFaltante = "interprete",
                UrlArreglar = $"cancion.html?id={c.Id}&tipo=cd"
            });
        }

        // 4. Álbumes sin portada
        var albumesSinPortada = await conn.QueryAsync<(int Id, string Nombre)>("""
            SELECT id, nombre FROM albumes WHERE portada IS NULL AND es_single = 0
            """);
        if (albumesSinPortada.Any())
        {
            notificaciones.Add(new NotificacionDatos
            {
                Id = $"notif-{++contador}",
                Tipo = "album",
                Severidad = "info",
                Mensaje = $"{albumesSinPortada.Count()} álbumes no tienen imagen de portada",
                UrlArreglar = "albumes.html?filtro=sin-portada" // Asumiendo que podemos filtrar así en el futuro, o redirigir a general
            });
        }
        
        // 5. Álbumes sin año
        var albumesSinAnio = await conn.QueryAsync<(int Id, string Nombre)>("""
            SELECT id, nombre FROM albumes WHERE (anio IS NULL OR anio = '') AND es_single = 0
            """);
        foreach (var a in albumesSinAnio.Take(5)) // Limitamos para no inundar
        {
            notificaciones.Add(new NotificacionDatos
            {
                Id = $"notif-{++contador}",
                Tipo = "album",
                Severidad = "info",
                Mensaje = $"El álbum '{a.Nombre}' no tiene año de lanzamiento",
                EntidadId = a.Id.ToString(),
                UrlArreglar = $"albumes.html?id={a.Id}"
            });
        }
        if (albumesSinAnio.Count() > 5)
        {
            notificaciones.Add(new NotificacionDatos
            {
                Id = $"notif-{++contador}",
                Tipo = "album",
                Severidad = "info",
                Mensaje = $"...y otros {albumesSinAnio.Count() - 5} álbumes sin año",
                UrlArreglar = "albumes.html"
            });
        }

        // 6. Resumen de canciones sin álbum
        // Solo contamos las que tienen intérprete (para no duplicar errores)
        var totalSinAlbum = await conn.QueryFirstAsync<int>("""
            SELECT (
                (SELECT COUNT(*) FROM temas WHERE id_album IS NULL AND id_interprete IS NOT NULL) + 
                (SELECT COUNT(*) FROM temas_cd WHERE id_album IS NULL AND id_interprete IS NOT NULL)
            )
            """);
            
        if (totalSinAlbum > 0)
        {
            notificaciones.Add(new NotificacionDatos
            {
                Id = $"notif-{++contador}",
                Tipo = "organizacion",
                Severidad = "info",
                Mensaje = $"{totalSinAlbum} canciones aún no están en ningún álbum",
                UrlArreglar = "buscar.html"
            });
        }

        // 6b. Canciones sin archivo de audio
        var totalSinAudio = await conn.QueryFirstAsync<int>("""
            SELECT (
                (SELECT COUNT(*) FROM temas WHERE (archivo_audio IS NULL OR TRIM(archivo_audio) = '') AND id_interprete IS NOT NULL) + 
                (SELECT COUNT(*) FROM temas_cd WHERE (archivo_audio IS NULL OR TRIM(archivo_audio) = '') AND id_interprete IS NOT NULL)
            )
            """);
            
        if (totalSinAudio > 0)
        {
            notificaciones.Add(new NotificacionDatos
            {
                Id = $"notif-{++contador}",
                Tipo = "audio",
                Severidad = "info",
                Mensaje = $"{totalSinAudio} canciones sin archivo de audio",
                UrlArreglar = "buscar.html?sinAudio=1"
            });
        }


        // 7. Álbumes vacíos (sin canciones)
        var albumesVacios = await conn.QueryAsync<(int Id, string Nombre)>("""
            SELECT a.id, a.nombre FROM albumes a
            WHERE NOT EXISTS (SELECT 1 FROM temas WHERE id_album = a.id)
            AND NOT EXISTS (SELECT 1 FROM temas_cd WHERE id_album = a.id)
            """);
        foreach (var a in albumesVacios)
        {
            notificaciones.Add(new NotificacionDatos
            {
                Id = $"notif-{++contador}",
                Tipo = "album",
                Severidad = "warning",
                Mensaje = $"Álbum '{a.Nombre}' está vacío (sin canciones)",
                EntidadId = a.Id.ToString(),
                UrlArreglar = $"albumes.html?id={a.Id}"
            });
        }

        // 8. Canciones con nombre vacío
        var nombreVacioCassette = await conn.QueryAsync<(int Id, string numMedio)>("""
            SELECT id, num_formato FROM temas WHERE tema IS NULL OR TRIM(tema) = ''
            """);
        foreach (var c in nombreVacioCassette)
        {
            notificaciones.Add(new NotificacionDatos
            {
                Id = $"notif-{++contador}",
                Tipo = "cancion",
                Severidad = "error",
                Mensaje = $"Canción sin nombre en formato {c.numMedio}",
                EntidadId = c.Id.ToString(),
                EntidadTipo = "cassette",
                CampoFaltante = "nombre",
                UrlArreglar = $"formato.html?num={c.numMedio}&cancion={c.Id}&tipo=cassette"
            });
        }

        var nombreVacioCd = await conn.QueryAsync<(int Id, string numMedio)>("""
            SELECT id, num_formato FROM temas_cd WHERE tema IS NULL OR TRIM(tema) = ''
            """);
        foreach (var c in nombreVacioCd)
        {
            notificaciones.Add(new NotificacionDatos
            {
                Id = $"notif-{++contador}",
                Tipo = "cancion",
                Severidad = "error",
                Mensaje = $"Canción sin nombre en formato {c.numMedio}",
                EntidadId = c.Id.ToString(),
                EntidadTipo = "cd",
                CampoFaltante = "nombre",
                UrlArreglar = $"formato.html?num={c.numMedio}&cancion={c.Id}&tipo=cd"
            });
        }

        // 9. Formatos (cassettes) sin marca
        var formatosSinMarca = await conn.QueryAsync<(int Id, string numMedio)>("""
            SELECT fg.id, fg.num_formato 
            FROM formato_grabado fg
            LEFT JOIN marca m ON fg.id_marca = m.id_marca
            WHERE m.id_marca IS NULL OR m.nombre IS NULL OR TRIM(m.nombre) = ''
            """);
        foreach (var f in formatosSinMarca)
        {
            notificaciones.Add(new NotificacionDatos
            {
                Id = $"notif-{++contador}",
                Tipo = "formato",
                Severidad = "warning",
                Mensaje = $"Formato {f.numMedio} sin marca definida",
                EntidadId = f.numMedio,
                EntidadTipo = "cassette",
                CampoFaltante = "marca",
                UrlArreglar = $"formato.html?num={f.numMedio}"
            });
        }

        // 10. Intérpretes sin nombre
        var interpretesSinNombre = await conn.QueryAsync<int>("""
            SELECT id FROM interpretes WHERE nombre IS NULL OR TRIM(nombre) = ''
            """);
        foreach (var id in interpretesSinNombre)
        {
            notificaciones.Add(new NotificacionDatos
            {
                Id = $"notif-{++contador}",
                Tipo = "interprete",
                Severidad = "error",
                Mensaje = $"Intérprete (ID: {id}) sin nombre",
                EntidadId = id.ToString(),
                EntidadTipo = "interprete",
                CampoFaltante = "nombre",
                UrlArreglar = $"interprete.html?id={id}"
            });
        }

        return notificaciones;
    }


    // ============================================
    // DETECCIÓN DE CANCIONES DUPLICADAS
    // ============================================

    /// <summary>
    /// Obtiene grupos de canciones duplicadas (misma canción en diferentes formatos o repetida)
    /// Agrupa SOLO por nombre de canción, sin importar el intérprete
    /// </summary>
    public async Task<List<GrupoDuplicados>> ObtenerDuplicadosAsync(string? filtroTipo = null)
    {
        using var conn = _db.ObtenerConexion();

        // Obtener todas las canciones de cassettes
        var temasCassette = await conn.QueryAsync<dynamic>("""
            SELECT 
                t.id AS Id,
                'cassette' AS Tipo,
                t.tema AS Tema,
                i.nombre AS Interprete,
                t.id_interprete AS IdInterprete,
                t.num_formato AS numMedio,
                t.lado || ': ' || t.desde || '-' || t.hasta AS Posicion,
                t.id_album AS IdAlbum,
                a.nombre AS NombreAlbum,
                CASE WHEN t.portada IS NOT NULL AND LENGTH(t.portada) > 0 THEN 1 ELSE 0 END AS TienePortada,
                t.link_externo AS LinkExterno,
                COALESCE(t.es_cover, 0) AS EsCover,
                t.artista_original AS ArtistaOriginal,
                t.archivo_audio AS ArchivoAudio
            FROM temas t
            JOIN interpretes i ON t.id_interprete = i.id
            LEFT JOIN albumes a ON t.id_album = a.id
            """);

        // Obtener todas las canciones de CDs
        var temasCd = await conn.QueryAsync<dynamic>("""
            SELECT 
                t.id AS Id,
                'cd' AS Tipo,
                t.tema AS Tema,
                i.nombre AS Interprete,
                t.id_interprete AS IdInterprete,
                t.num_formato AS numMedio,
                'Track ' || t.ubicacion AS Posicion,
                t.id_album AS IdAlbum,
                a.nombre AS NombreAlbum,
                CASE WHEN t.portada IS NOT NULL AND LENGTH(t.portada) > 0 THEN 1 ELSE 0 END AS TienePortada,
                t.link_externo AS LinkExterno,
                COALESCE(t.es_cover, 0) AS EsCover,
                t.artista_original AS ArtistaOriginal,
                t.archivo_audio AS ArchivoAudio
            FROM temas_cd t
            JOIN interpretes i ON t.id_interprete = i.id
            LEFT JOIN albumes a ON t.id_album = a.id
            """);

        // Convertir a CancionDuplicada manualmente para manejar tipos correctamente
        var todasCanciones = new List<CancionDuplicada>();
        
        foreach (var t in temasCassette)
        {
            todasCanciones.Add(new CancionDuplicada
            {
                Id = (int)(long)t.Id,
                Tipo = (string)t.Tipo,
                Tema = (string)(t.Tema ?? ""),
                Interprete = (string)(t.Interprete ?? ""),
                IdInterprete = (int)(long)t.IdInterprete,
                numMedio = (string)(t.numMedio ?? ""),
                Posicion = (string?)t.Posicion,
                IdAlbum = t.IdAlbum != null ? (int?)(long)t.IdAlbum : null,
                NombreAlbum = (string?)t.NombreAlbum,
                TienePortada = t.TienePortada != null && (long)t.TienePortada == 1,
                LinkExterno = (string?)t.LinkExterno,
                EsCover = t.EsCover != null && (long)t.EsCover == 1,
                ArtistaOriginal = (string?)t.ArtistaOriginal,
                ArchivoAudio = (string?)t.ArchivoAudio
            });
        }
        
        foreach (var t in temasCd)
        {
            todasCanciones.Add(new CancionDuplicada
            {
                Id = (int)(long)t.Id,
                Tipo = (string)t.Tipo,
                Tema = (string)(t.Tema ?? ""),
                Interprete = (string)(t.Interprete ?? ""),
                IdInterprete = (int)(long)t.IdInterprete,
                numMedio = (string)(t.numMedio ?? ""),
                Posicion = (string?)t.Posicion,
                IdAlbum = t.IdAlbum != null ? (int?)(long)t.IdAlbum : null,
                NombreAlbum = (string?)t.NombreAlbum,
                TienePortada = t.TienePortada != null && (long)t.TienePortada == 1,
                LinkExterno = (string?)t.LinkExterno,
                EsCover = t.EsCover != null && (long)t.EsCover == 1,
                ArtistaOriginal = (string?)t.ArtistaOriginal,
                ArchivoAudio = (string?)t.ArchivoAudio
            });
        }

        // Agrupar SOLO por tema normalizado (sin intérprete)
        var grupos = todasCanciones
            .Where(c => !string.IsNullOrWhiteSpace(c.Tema))
            .GroupBy(c => NormalizarTexto(c.Tema))
            .Where(g => g.Count() > 1) // Solo grupos con más de una canción
            .Select(g => new GrupoDuplicados
            {
                Id = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(g.Key)).Replace("/", "_").Replace("+", "-"),
                TemaNormalizado = g.First().Tema,
                Canciones = g.OrderBy(c => c.EsCover).ThenBy(c => c.Interprete).ToList()
            })
            .OrderByDescending(g => g.TotalInstancias)
            .ThenBy(g => g.TemaNormalizado)
            .ToList();

        // Aplicar filtro si se especificó
        if (!string.IsNullOrEmpty(filtroTipo))
        {
            grupos = filtroTipo.ToLower() switch
            {
                "mixtos" => grupos.Where(g => g.TieneMixFormatos).ToList(),
                "cassette" => grupos.Where(g => g.Canciones.All(c => c.Tipo == "cassette")).ToList(),
                "cd" => grupos.Where(g => g.Canciones.All(c => c.Tipo == "cd")).ToList(),
                "covers" => grupos.Where(g => g.TieneCovers).ToList(),
                "multiartista" => grupos.Where(g => g.TotalArtistas > 1).ToList(),
                _ => grupos
            };
        }

        return grupos;
    }

    /// <summary>
    /// Obtiene estadísticas de duplicados
    /// </summary>
    public async Task<EstadisticasDuplicados> ObtenerEstadisticasDuplicadosAsync()
    {
        var grupos = await ObtenerDuplicadosAsync();

        return new EstadisticasDuplicados
        {
            TotalGrupos = grupos.Count,
            TotalCancionesDuplicadas = grupos.Sum(g => g.TotalInstancias),
            GruposMixtos = grupos.Count(g => g.TieneMixFormatos),
            GruposSoloCassette = grupos.Count(g => g.Canciones.All(c => c.Tipo == "cassette")),
            GruposSoloCd = grupos.Count(g => g.Canciones.All(c => c.Tipo == "cd"))
        };
    }

    /// <summary>
    /// Obtiene un grupo de duplicados específico por su ID
    /// </summary>
    public async Task<GrupoDuplicados?> ObtenerGrupoDuplicadoPorIdAsync(string grupoId)
    {
        var grupos = await ObtenerDuplicadosAsync();
        return grupos.FirstOrDefault(g => g.Id == grupoId);
    }

    /// <summary>
    /// Obtiene el perfil multi-artista de una canción con todas sus versiones
    /// </summary>
    public async Task<PerfilCancionMultiArtista?> ObtenerPerfilMultiArtistaAsync(string grupoId)
    {
        var grupo = await ObtenerGrupoDuplicadoPorIdAsync(grupoId);
        if (grupo == null) return null;

        // Agrupar canciones por artista
        var versionesPorArtista = grupo.Canciones
            .GroupBy(c => c.Interprete.ToLowerInvariant().Trim())
            .Select(g => 
            {
                var primera = g.First();
                var ubicaciones = g.Select(c => new UbicacionCancion
                {
                    Id = c.Id,
                    Tipo = c.Tipo,
                    numMedio = c.numMedio,
                    Posicion = c.Posicion,
                    IdAlbum = c.IdAlbum,
                    NombreAlbum = c.NombreAlbum,
                    TienePortada = c.TienePortada,
                    LinkExterno = c.LinkExterno,
                    EsCover = c.EsCover,
                    ArtistaOriginal = c.ArtistaOriginal,
                    ArchivoAudio = c.ArchivoAudio
                }).OrderBy(u => u.Tipo).ThenBy(u => u.numMedio).ToList();

                // Determinar si es original: cualquier instancia marcada como NO cover
                var esOriginal = g.Any(c => !c.EsCover);
                // El link externo preferido es el primero que tenga
                var linkExterno = g.FirstOrDefault(c => !string.IsNullOrEmpty(c.LinkExterno))?.LinkExterno;
                // El álbum principal es el primero que tenga (preferir CD)
                var albumPrincipal = g.FirstOrDefault(c => c.IdAlbum.HasValue && c.Tipo == "cd")?.IdAlbum 
                    ?? g.FirstOrDefault(c => c.IdAlbum.HasValue)?.IdAlbum;
                // Artista original referenciado (si es cover)
                var artistaOriginalRef = g.FirstOrDefault(c => !string.IsNullOrEmpty(c.ArtistaOriginal))?.ArtistaOriginal;

                return new VersionArtista
                {
                    Artista = primera.Interprete,
                    IdInterprete = primera.IdInterprete,
                    EsOriginal = esOriginal,
                    ArtistaOriginalRef = artistaOriginalRef,
                    TotalCopias = g.Count(),
                    IdAlbumPrincipal = albumPrincipal,
                    LinkExterno = linkExterno,
                    Ubicaciones = ubicaciones
                };
            })
            .OrderByDescending(v => v.EsOriginal) // Originales primero
            .ThenByDescending(v => v.TotalCopias) // Luego por cantidad
            .ToList();

        // Verificar si hay al menos un artista marcado como original
        var tieneOriginalDefinido = versionesPorArtista.Any(v => v.EsOriginal);

        return new PerfilCancionMultiArtista
        {
            Tema = grupo.Canciones.First().Tema,
            GrupoId = grupo.Id,
            TotalVersiones = versionesPorArtista.Count,
            TotalArtistas = versionesPorArtista.Count,
            TotalCopias = grupo.TotalInstancias,
            TieneArtistaOriginalDefinido = tieneOriginalDefinido,
            Versiones = versionesPorArtista
        };
    }

    /// <summary>
    /// Marca todas las canciones de un artista en un grupo como "original" (no cover)
    /// </summary>
    public async Task<CrudResponse> MarcarArtistaOriginalAsync(string grupoId, int idInterprete)
    {
        using var conn = _db.ObtenerConexion();

        try
        {
            // Decodificar grupoId para obtener el tema normalizado
            string temaNorm;
            try
            {
                temaNorm = System.Text.Encoding.UTF8.GetString(
                    Convert.FromBase64String(grupoId.Replace("_", "/").Replace("-", "+")));
            }
            catch
            {
                return new CrudResponse { Exito = false, Mensaje = "ID de grupo inválido" };
            }

            // Obtener nombre del intérprete seleccionado como original
            var nombreOriginal = await conn.QueryFirstOrDefaultAsync<string>(
                "SELECT nombre FROM interpretes WHERE id = @id", new { id = idInterprete });

            if (string.IsNullOrEmpty(nombreOriginal))
                return new CrudResponse { Exito = false, Mensaje = "Intérprete no encontrado" };

            // Buscar todas las canciones de este grupo en cassettes
            var cancionesCassette = await conn.QueryAsync<dynamic>(
                "SELECT id, tema, id_interprete FROM temas");
            
            var idsCassetteOtros = cancionesCassette
                .Where(c => NormalizarTexto((string)(c.tema ?? "")) == temaNorm && (int)(long)c.id_interprete != idInterprete)
                .Select(c => (int)(long)c.id)
                .ToList();
            
            var idsCassetteOriginal = cancionesCassette
                .Where(c => NormalizarTexto((string)(c.tema ?? "")) == temaNorm && (int)(long)c.id_interprete == idInterprete)
                .Select(c => (int)(long)c.id)
                .ToList();

            // Buscar todas las canciones de este grupo en CDs
            var cancionesCd = await conn.QueryAsync<dynamic>(
                "SELECT id, tema, id_interprete FROM temas_cd");
            
            var idsCdOtros = cancionesCd
                .Where(c => NormalizarTexto((string)(c.tema ?? "")) == temaNorm && (int)(long)c.id_interprete != idInterprete)
                .Select(c => (int)(long)c.id)
                .ToList();
            
            var idsCdOriginal = cancionesCd
                .Where(c => NormalizarTexto((string)(c.tema ?? "")) == temaNorm && (int)(long)c.id_interprete == idInterprete)
                .Select(c => (int)(long)c.id)
                .ToList();

            // 1. Marcar canciones de OTROS artistas como covers
            if (idsCassetteOtros.Count > 0)
            {
                await conn.ExecuteAsync(
                    $"UPDATE temas SET es_cover = 1, artista_original = @artistaOriginal WHERE id IN ({string.Join(",", idsCassetteOtros)})",
                    new { artistaOriginal = nombreOriginal });
            }
            
            if (idsCdOtros.Count > 0)
            {
                await conn.ExecuteAsync(
                    $"UPDATE temas_cd SET es_cover = 1, artista_original = @artistaOriginal WHERE id IN ({string.Join(",", idsCdOtros)})",
                    new { artistaOriginal = nombreOriginal });
            }

            // 2. Marcar canciones del artista seleccionado como originales (no cover)
            if (idsCassetteOriginal.Count > 0)
            {
                await conn.ExecuteAsync(
                    $"UPDATE temas SET es_cover = 0, artista_original = NULL WHERE id IN ({string.Join(",", idsCassetteOriginal)})");
            }
            
            if (idsCdOriginal.Count > 0)
            {
                await conn.ExecuteAsync(
                    $"UPDATE temas_cd SET es_cover = 0, artista_original = NULL WHERE id IN ({string.Join(",", idsCdOriginal)})");
            }

            var totalActualizados = idsCassetteOtros.Count + idsCdOtros.Count + idsCassetteOriginal.Count + idsCdOriginal.Count;
            return new CrudResponse { Exito = true, Mensaje = $"'{nombreOriginal}' marcado como artista original ({totalActualizados} canciones actualizadas)" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = ex.Message };
        }
    }

    /// <summary>
    /// Busca artistas que tienen una canción con el mismo nombre (para sugerir artista original en covers)
    /// </summary>
    public async Task<List<ArtistaParaCover>> BuscarArtistasParaCoverAsync(string tema, int? excluirIdInterprete = null)
    {
        using var conn = _db.ObtenerConexion();

        var temaNorm = NormalizarTexto(tema).ToLower();

        // Buscar en cassettes
        var artistasCassette = await conn.QueryAsync<dynamic>($"""
            SELECT DISTINCT i.id, i.nombre, 
                   CASE WHEN t.es_cover = 0 OR t.es_cover IS NULL THEN 1 ELSE 0 END as es_original
            FROM temas t
            INNER JOIN interpretes i ON t.id_interprete = i.id
            WHERE LOWER(REPLACE(REPLACE(REPLACE(tema, ' ', ''), '-', ''), '''', '')) = @temaNorm
            """, new { temaNorm });

        // Buscar en CDs
        var artistasCd = await conn.QueryAsync<dynamic>($"""
            SELECT DISTINCT i.id, i.nombre,
                   CASE WHEN t.es_cover = 0 OR t.es_cover IS NULL THEN 1 ELSE 0 END as es_original
            FROM temas_cd t
            INNER JOIN interpretes i ON t.id_interprete = i.id
            WHERE LOWER(REPLACE(REPLACE(REPLACE(tema, ' ', ''), '-', ''), '''', '')) = @temaNorm
            """, new { temaNorm });

        // Combinar y eliminar duplicados
        var todosArtistas = artistasCassette.Concat(artistasCd)
            .GroupBy(a => (int)(long)a.id)
            .Select(g => new ArtistaParaCover
            {
                IdInterprete = g.Key,
                Nombre = (string)g.First().nombre,
                EsOriginal = g.Any(a => (long)a.es_original == 1)
            })
            .Where(a => !excluirIdInterprete.HasValue || a.IdInterprete != excluirIdInterprete.Value)
            .OrderByDescending(a => a.EsOriginal) // Originales primero
            .ThenBy(a => a.Nombre)
            .ToList();

        return todosArtistas;
    }

    /// <summary>
    /// Obtiene el perfil unificado de una canción con todas sus ubicaciones físicas
    /// </summary>
    public async Task<PerfilCancion?> ObtenerPerfilCancionAsync(string? tema, string? artista, string? grupoId)
    {
        using var conn = _db.ObtenerConexion();

        // Si tenemos grupoId, decodificarlo para obtener tema|artista normalizado
        string? temaNorm = null;
        string? artistaNorm = null;
        
        if (!string.IsNullOrEmpty(grupoId))
        {
            try
            {
                var decoded = System.Text.Encoding.UTF8.GetString(
                    Convert.FromBase64String(grupoId.Replace("_", "/").Replace("-", "+")));
                var partes = decoded.Split('|');
                if (partes.Length == 2)
                {
                    temaNorm = partes[0];
                    artistaNorm = partes[1];
                }
            }
            catch { }
        }
        
        // Si no tenemos datos del grupo, usar tema y artista directos
        if (string.IsNullOrEmpty(temaNorm) && !string.IsNullOrEmpty(tema))
            temaNorm = NormalizarTexto(tema);
        if (string.IsNullOrEmpty(artistaNorm) && !string.IsNullOrEmpty(artista))
            artistaNorm = NormalizarTexto(artista);

        if (string.IsNullOrEmpty(temaNorm) || string.IsNullOrEmpty(artistaNorm))
            return null;

        // Obtener todas las canciones que coincidan
        var temasCassette = await conn.QueryAsync<dynamic>("""
            SELECT 
                t.id AS Id,
                'cassette' AS Tipo,
                t.tema AS Tema,
                i.nombre AS Interprete,
                t.num_formato AS numMedio,
                t.lado || ': ' || t.desde || '-' || t.hasta AS Posicion,
                t.id_album AS IdAlbum,
                a.nombre AS NombreAlbum,
                CASE WHEN t.portada IS NOT NULL AND LENGTH(t.portada) > 0 THEN 1 ELSE 0 END AS TienePortada,
                t.link_externo AS LinkExterno,
                t.archivo_audio AS ArchivoAudio
            FROM temas t
            JOIN interpretes i ON t.id_interprete = i.id
            LEFT JOIN albumes a ON t.id_album = a.id
            """);

        var temasCd = await conn.QueryAsync<dynamic>("""
            SELECT 
                t.id AS Id,
                'cd' AS Tipo,
                t.tema AS Tema,
                i.nombre AS Interprete,
                t.num_formato AS numMedio,
                'Track ' || t.ubicacion AS Posicion,
                t.id_album AS IdAlbum,
                a.nombre AS NombreAlbum,
                CASE WHEN t.portada IS NOT NULL AND LENGTH(t.portada) > 0 THEN 1 ELSE 0 END AS TienePortada,
                t.link_externo AS LinkExterno,
                t.archivo_audio AS ArchivoAudio
            FROM temas_cd t
            JOIN interpretes i ON t.id_interprete = i.id
            LEFT JOIN albumes a ON t.id_album = a.id
            """);

        var ubicaciones = new List<UbicacionCancion>();
        string? temaOriginal = null;
        string? artistaOriginal = null;

        // Procesar cassettes
        foreach (var t in temasCassette)
        {
            var tNorm = NormalizarTexto((string)(t.Tema ?? ""));
            var iNorm = NormalizarTexto((string)(t.Interprete ?? ""));
            
            if (tNorm == temaNorm && iNorm == artistaNorm)
            {
                if (temaOriginal == null)
                {
                    temaOriginal = (string)(t.Tema ?? "");
                    artistaOriginal = (string)(t.Interprete ?? "");
                }
                
                ubicaciones.Add(new UbicacionCancion
                {
                    Id = (int)(long)t.Id,
                    Tipo = "cassette",
                    numMedio = (string)(t.numMedio ?? ""),
                    Posicion = (string?)t.Posicion,
                    IdAlbum = t.IdAlbum != null ? (int?)(long)t.IdAlbum : null,
                    NombreAlbum = (string?)t.NombreAlbum,
                    TienePortada = t.TienePortada != null && (long)t.TienePortada == 1,
                    LinkExterno = (string?)t.LinkExterno,
                    ArchivoAudio = (string?)t.ArchivoAudio
                });
            }
        }

        // Procesar CDs
        foreach (var t in temasCd)
        {
            var tNorm = NormalizarTexto((string)(t.Tema ?? ""));
            var iNorm = NormalizarTexto((string)(t.Interprete ?? ""));
            
            if (tNorm == temaNorm && iNorm == artistaNorm)
            {
                if (temaOriginal == null)
                {
                    temaOriginal = (string)(t.Tema ?? "");
                    artistaOriginal = (string)(t.Interprete ?? "");
                }
                
                ubicaciones.Add(new UbicacionCancion
                {
                    Id = (int)(long)t.Id,
                    Tipo = "cd",
                    numMedio = (string)(t.numMedio ?? ""),
                    Posicion = (string?)t.Posicion,
                    IdAlbum = t.IdAlbum != null ? (int?)(long)t.IdAlbum : null,
                    NombreAlbum = (string?)t.NombreAlbum,
                    TienePortada = t.TienePortada != null && (long)t.TienePortada == 1,
                    LinkExterno = (string?)t.LinkExterno,
                    ArchivoAudio = (string?)t.ArchivoAudio
                });
            }
        }

        if (ubicaciones.Count == 0)
            return null;

        return new PerfilCancion
        {
            Tema = temaOriginal!,
            Artista = artistaOriginal!,
            Ubicaciones = ubicaciones.OrderBy(u => u.Tipo).ThenBy(u => u.numMedio).ToList()
        };
    }

    /// <summary>
    /// Sincroniza los álbumes de los covers basándose en el tema original.
    /// </summary>
    public async Task<CrudResponse> SincronizarAlbumesCoversAsync()
    {
        using var conn = _db.ObtenerConexion();
        try
        {
            // 1. Actualizar Cassettes (temas)
            // Prioridad 1: Coincidencia exacta de nombre de tema y artista (definido como original en la misma fila o el intérprete del tema)
            var rowsTemas = await conn.ExecuteAsync("""
                UPDATE temas
                SET id_album = (
                    SELECT t2.id_album 
                    FROM temas t2 
                    JOIN interpretes i2 ON t2.id_interprete = i2.id
                    JOIN interpretes i1 ON temas.id_interprete = i1.id
                    WHERE LOWER(t2.tema) = LOWER(temas.tema)
                    AND t2.id_album IS NOT NULL
                    AND (
                        LOWER(i2.nombre) = LOWER(CASE WHEN temas.artista_original IS NOT NULL AND temas.artista_original != '' THEN temas.artista_original ELSE i1.nombre END)
                        OR t2.es_original = 1
                    )
                    ORDER BY 
                      CASE WHEN LOWER(i2.nombre) = LOWER(CASE WHEN temas.artista_original IS NOT NULL AND temas.artista_original != '' THEN temas.artista_original ELSE i1.nombre END) THEN 1 ELSE 2 END,
                      t2.es_original DESC
                    LIMIT 1
                )
                WHERE id_album IS NULL
                """);

            // 2. Actualizar CDs (temas_cd)
            var rowsCds = await conn.ExecuteAsync("""
                UPDATE temas_cd
                SET id_album = (
                    SELECT t2.id_album 
                    FROM temas t2 
                    JOIN interpretes i2 ON t2.id_interprete = i2.id
                    JOIN interpretes i1 ON temas_cd.id_interprete = i1.id
                    WHERE LOWER(t2.tema) = LOWER(temas_cd.tema)
                    AND t2.id_album IS NOT NULL
                    AND (
                        LOWER(i2.nombre) = LOWER(CASE WHEN temas_cd.artista_original IS NOT NULL AND temas_cd.artista_original != '' THEN temas_cd.artista_original ELSE i1.nombre END)
                        OR t2.es_original = 1
                    )
                    ORDER BY 
                      CASE WHEN LOWER(i2.nombre) = LOWER(CASE WHEN temas_cd.artista_original IS NOT NULL AND temas_cd.artista_original != '' THEN temas_cd.artista_original ELSE i1.nombre END) THEN 1 ELSE 2 END,
                      t2.es_original DESC
                    LIMIT 1
                )
                WHERE id_album IS NULL
                """);
            
            // También comprobar contra la tabla opuesta (si el original está en CD y el cover en Cassette, etc) 
            // Esto se podría refinar, pero por ahora la lógica principal cubre la mayoría de casos donde el original suele estar en la misma tabla o se busca en ambas.
            // La query de arriba solo busca originales en 'temas'. Haremos una pasada extra buscando originales en 'temas_cd'.

             var rowsTemasCross = await conn.ExecuteAsync("""
                UPDATE temas
                SET id_album = (
                    SELECT t3.id_album 
                    FROM temas_cd t3
                    JOIN interpretes i3 ON t3.id_interprete = i3.id
                    JOIN interpretes i1 ON temas.id_interprete = i1.id
                    WHERE LOWER(t3.tema) = LOWER(temas.tema)
                    AND t3.id_album IS NOT NULL
                    AND (
                        LOWER(i3.nombre) = LOWER(CASE WHEN temas.artista_original IS NOT NULL AND temas.artista_original != '' THEN temas.artista_original ELSE i1.nombre END)
                        OR t3.es_original = 1
                    )
                    ORDER BY 
                      CASE WHEN LOWER(i3.nombre) = LOWER(CASE WHEN temas.artista_original IS NOT NULL AND temas.artista_original != '' THEN temas.artista_original ELSE i1.nombre END) THEN 1 ELSE 2 END,
                      t3.es_original DESC
                    LIMIT 1
                )
                WHERE id_album IS NULL
                """);

             var rowsCdsCross = await conn.ExecuteAsync("""
                UPDATE temas_cd
                SET id_album = (
                    SELECT t3.id_album 
                    FROM temas_cd t3
                    JOIN interpretes i3 ON t3.id_interprete = i3.id
                    JOIN interpretes i1 ON temas_cd.id_interprete = i1.id
                    WHERE LOWER(t3.tema) = LOWER(temas_cd.tema)
                    AND t3.id_album IS NOT NULL
                    AND (
                        LOWER(i3.nombre) = LOWER(CASE WHEN temas_cd.artista_original IS NOT NULL AND temas_cd.artista_original != '' THEN temas_cd.artista_original ELSE i1.nombre END)
                        OR t3.es_original = 1
                    )
                    ORDER BY 
                      CASE WHEN LOWER(i3.nombre) = LOWER(CASE WHEN temas_cd.artista_original IS NOT NULL AND temas_cd.artista_original != '' THEN temas_cd.artista_original ELSE i1.nombre END) THEN 1 ELSE 2 END,
                      t3.es_original DESC
                    LIMIT 1
                )
                WHERE id_album IS NULL
                """);

            return new CrudResponse 
            { 
                Exito = true, 
                Mensaje = $"Sincronización completada. Actualizados: {rowsTemas + rowsTemasCross} en Cassettes, {rowsCds + rowsCdsCross} en CDs." 
            };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = "Error al sincronizar: " + ex.Message };
        }
    }

    // ============================================
    // MÉTODOS AUXILIARES PRIVADOS
    // ============================================

    /// <summary>
    /// Verifica si un archivo de audio tiene portada embebida, usando caché.
    /// </summary>
    private bool VerificarPortadaEmbebida(string? rutaRelativa)
    {
        if (string.IsNullOrEmpty(rutaRelativa)) return false;

        // Verificar cache memoria
        if (_cachePortadaEmbebida.TryGetValue(rutaRelativa, out var cached))
            return cached;

        try
        {
            var fullPath = Path.Combine(AppContext.BaseDirectory, rutaRelativa);
            
            // Si el archivo no existe, no tiene portada
            if (!System.IO.File.Exists(fullPath)) 
            {
                _cachePortadaEmbebida.TryAdd(rutaRelativa, false);
                return false;
            }

            // Usar TagLib para leer metadatos
            using var file = TagLib.File.Create(fullPath);
            var tiene = file.Tag.Pictures != null && file.Tag.Pictures.Length > 0;
            
            // Guardar en cache
            _cachePortadaEmbebida.TryAdd(rutaRelativa, tiene);
            return tiene;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error TagLib] {rutaRelativa}: {ex.Message}");
            // En caso de error, asumir que no tiene portada para no bloquear
            _cachePortadaEmbebida.TryAdd(rutaRelativa, false);
            return false;
        }
    }

    /// <summary>
    /// Gestiona la lógica de original/cover/versión tras una actualización.
    /// Asegura que solo haya un original y clasifica el resto automáticamente.
    /// </summary>
    private async Task GestionarOriginalidadAsync(int id, int idInterprete, string tema, bool esOriginal)
    {
        if (!esOriginal) return;

        using var conn = _db.ObtenerConexion();
        
        // 1. Obtener nombre del artista original (el actual)
        var nombreArtista = await conn.QueryFirstOrDefaultAsync<string>("SELECT nombre FROM interpretes WHERE id = @id", new { id = idInterprete });
        if (string.IsNullOrEmpty(nombreArtista)) return;

        // 2. Normalizar tema para buscar versiones
        var temaNorm = NormalizarTexto(tema);

        // 3. Buscar TODAS las canciones con el mismo tema (en tablas temas y temas_cd)
        // Excluyendo la actual
        var duplicadosCassette = await conn.QueryAsync<(int Id, int IdInterprete)>("""
            SELECT id, id_interprete FROM temas 
            WHERE id != @id AND LOWER(tema) = @temaNorm
            """, new { id, temaNorm = tema.ToLower() }); // NormalizarTexto ya devuelve minúsculas si es similar a la implementación standard

        var duplicadosCd = await conn.QueryAsync<(int Id, int IdInterprete)>("""
            SELECT id, id_interprete FROM temas_cd 
            WHERE id != @id AND LOWER(tema) = @temaNorm
            """, new { id, temaNorm = tema.ToLower() });
            
        // NOTA: NormalizarTexto puede ser compleja, aquí usamosToLower() básico para la query SQL, 
        // pero idealmente deberíamos usar la misma lógica de normalización que ObtenerDuplicadosAsync.
        // Asumiendo que 'tema' ya viene limpio o que el SQL LIKE maneja acentos si la DB está configurada.

        // 4. Actualizar duplicados
        foreach (var d in duplicadosCassette)
        {
            bool mismoArtista = d.IdInterprete == idInterprete;
            await conn.ExecuteAsync("""
                UPDATE temas SET 
                    es_original = 0,
                    es_cover = @esCover,
                    artista_original = @artistaOriginal
                WHERE id = @idCancion
                """, new 
                { 
                    idCancion = d.Id,
                    esCover = mismoArtista ? 0 : 1,
                    artistaOriginal = mismoArtista ? null : nombreArtista 
                });
        }

        foreach (var d in duplicadosCd)
        {
            bool mismoArtista = d.IdInterprete == idInterprete;
            await conn.ExecuteAsync("""
                UPDATE temas_cd SET 
                    es_original = 0,
                    es_cover = @esCover,
                    artista_original = @artistaOriginal
                WHERE id = @idCancion
                """, new 
                { 
                    idCancion = d.Id,
                    esCover = mismoArtista ? 0 : 1,
                    artistaOriginal = mismoArtista ? null : nombreArtista 
                });
        }
    }

    // ==========================================
    // GESTIÓN DE ARCHIVOS DE AUDIO
    // ==========================================

    /// <summary>
    /// Guarda un archivo de audio en el sistema de archivos y actualiza la base de datos
    /// </summary>
    public async Task<CrudResponse> GuardarArchivoAudioAsync(int id, string tipo, string nombreArchivo, byte[] contenido)
    {
        try
        {
            tipo = tipo.ToLower();
            var tabla = tipo == "cassette" ? "temas" : "temas_cd";
            
            // Obtener extensión del archivo
            var extension = Path.GetExtension(nombreArchivo).ToLowerInvariant();
            var formatosPermitidos = new[] { ".mp3", ".wav", ".m4a", ".flac", ".ogg" };
            
            if (!formatosPermitidos.Contains(extension))
            {
                return new CrudResponse 
                { 
                    Exito = false, 
                    Mensaje = $"Formato no soportado. Use: {string.Join(", ", formatosPermitidos)}" 
                };
            }
            
            // Crear nombre único: tipo_id.ext
            var nombreArchivoFinal = $"{tipo}_{id}{extension}";
            
            // Determinar carpeta: audio/cassette/ o audio/cd/
            var directorioBase = AppContext.BaseDirectory;
            var carpetaAudio = Path.Combine(directorioBase, "audio", tipo);
            Directory.CreateDirectory(carpetaAudio);
            
            // Ruta completa del archivo
            var rutaCompleta = Path.Combine(carpetaAudio, nombreArchivoFinal);
            
            // Eliminar archivo anterior si existe
            if (File.Exists(rutaCompleta))
            {
                File.Delete(rutaCompleta);
            }
            
            // Guardar archivo
            await File.WriteAllBytesAsync(rutaCompleta, contenido);
            
            // 1. Obtener datos actuales de la canción
            using var conn = _db.ObtenerConexion();
            var songData = await conn.QueryFirstOrDefaultAsync<dynamic>(
                $"SELECT id_interprete, id_album, tema FROM {tabla} WHERE id = @id", new { id });

            // Calcular duración real usando TagLibSharp y extraer metadatos
            int duracionReal = 0;
            string metadataAlbum = null;
            string metadataAnio = null;
            byte[] portadaArchivo = null;

            try 
            {
                using var tfile = TagLib.File.Create(rutaCompleta);
                duracionReal = (int)tfile.Properties.Duration.TotalSeconds;
                
                // Extraer metadata
                metadataAlbum = tfile.Tag.Album;
                if (tfile.Tag.Year > 0) metadataAnio = tfile.Tag.Year.ToString();
                
                if (tfile.Tag.Pictures != null && tfile.Tag.Pictures.Length > 0)
                {
                    portadaArchivo = tfile.Tag.Pictures[0].Data.Data;
                }
            }
            catch (Exception ex)
            {
                // Fallback a aproximación si falla TagLib
                Console.WriteLine($"Error al leer metadata con TagLib: {ex.Message}");
                duracionReal = contenido.Length / (16000); // ~128kbps
            }

            // Lógica de Álbum automático
            int? finalAlbumId = (int?)songData?.id_album;

            if (!string.IsNullOrWhiteSpace(metadataAlbum) && songData != null)
            {
                // Solo si no tiene álbum asignado o es 0
                if (finalAlbumId == null || finalAlbumId == 0)
                {
                    var albumExistente = await conn.QueryFirstOrDefaultAsync<int?>(
                        "SELECT id FROM albumes WHERE LOWER(nombre) = LOWER(@nombre) AND id_interprete = @idInterprete",
                        new { nombre = metadataAlbum.Trim(), idInterprete = (int)songData.id_interprete });

                    if (albumExistente.HasValue)
                    {
                        finalAlbumId = albumExistente.Value;
                    }
                    else
                    {
                        // Crear el álbum automáticamente
                        var respAlbum = await CrearAlbumAsync(new AlbumRequest {
                            Nombre = metadataAlbum.Trim(),
                            IdInterprete = (int)songData.id_interprete,
                            Anio = metadataAnio,
                            EsSingle = false
                        });

                        if (respAlbum.Exito)
                        {
                            finalAlbumId = respAlbum.IdCreado;
                            // Si tenemos portada en el archivo, ponerla al álbum
                            if (portadaArchivo != null && portadaArchivo.Length > 0)
                            {
                                await conn.ExecuteAsync("UPDATE albumes SET portada = @portada WHERE id = @id", 
                                    new { portada = portadaArchivo, id = finalAlbumId });
                            }
                        }
                    }
                }
            }
            
            // Actualizar base de datos con ruta relativa
            var rutaRelativa = $"audio/{tipo}/{nombreArchivoFinal}";
            var formato = extension.TrimStart('.');
            
            await conn.ExecuteAsync(
                $"""
                UPDATE {tabla} 
                SET archivo_audio = @rutaRelativa, 
                    duracion_segundos = @duracion, 
                    formato_audio = @formato,
                    id_album = COALESCE(id_album, @idAlbum)
                WHERE id = @id
                """,
                new { id, rutaRelativa, duracion = duracionReal, formato, idAlbum = finalAlbumId });
            
            return new CrudResponse 
            { 
                Exito = true, 
                Mensaje = "Audio guardado correctamente",
                IdCreado = id
            };
        }
        catch (Exception ex)
        {
            return new CrudResponse 
            { 
                Exito = false, 
                Mensaje = $"Error al guardar audio: {ex.Message}" 
            };
        }
    }

    /// <summary>
    /// Obtiene la ruta absoluta del archivo de audio de una canción
    /// </summary>
    public async Task<string?> ObtenerRutaAudioAsync(int id, string tipo)
    {
        using var conn = _db.ObtenerConexion();
        var tabla = tipo.ToLower() == "cassette" ? "temas" : "temas_cd";
        
        var rutaRelativa = await conn.QueryFirstOrDefaultAsync<string?>(
            $"SELECT archivo_audio FROM {tabla} WHERE id = @id", 
            new { id });
        
        if (string.IsNullOrEmpty(rutaRelativa))
            return null;
        
        var rutaAbsoluta = Path.Combine(AppContext.BaseDirectory, rutaRelativa);
        
        // Verificar que el archivo existe
        if (!File.Exists(rutaAbsoluta))
        {
            // Limpiar registro de BD si el archivo no existe
            await conn.ExecuteAsync(
                $"UPDATE {tabla} SET archivo_audio = NULL, duracion_segundos = NULL, formato_audio = NULL WHERE id = @id",
                new { id });
            return null;
        }
        
        return rutaAbsoluta;
    }

    /// <summary>
    /// Elimina el archivo de audio de una canción y actualiza la base de datos
    /// </summary>
    public async Task<CrudResponse> EliminarArchivoAudioAsync(int id, string tipo)
    {
        try
        {
            var rutaArchivo = await ObtenerRutaAudioAsync(id, tipo);
            
            // Eliminar archivo físico si existe
            if (rutaArchivo != null && File.Exists(rutaArchivo))
            {
                File.Delete(rutaArchivo);
            }
            
            // Actualizar base de datos
            using var conn = _db.ObtenerConexion();
            var tabla = tipo.ToLower() == "cassette" ? "temas" : "temas_cd";
            
            await conn.ExecuteAsync(
                $"""
                UPDATE {tabla} 
                SET archivo_audio = NULL, 
                    duracion_segundos = NULL, 
                    formato_audio = NULL 
                WHERE id = @id
                """,
                new { id });
            
            return new CrudResponse 
            { 
                Exito = true, 
                Mensaje = "Audio eliminado correctamente" 
            };
        }
        catch (Exception ex)
        {
            return new CrudResponse 
            { 
                Exito = false, 
                Mensaje = $"Error al eliminar audio: {ex.Message}" 
            };
        }
    }

    /// <summary>
    /// Sincroniza los archivos de audio existentes en el sistema de archivos con la base de datos.
    /// Escanea las carpetas audio/cd/ y audio/cassette/ y actualiza la columna archivo_audio.
    /// </summary>
    public async Task<CrudResponse> SincronizarArchivosAudioAsync()
    {
        try
        {
            var directorioBase = AppContext.BaseDirectory;
            var carpetaAudio = Path.Combine(directorioBase, "audio");
            
            if (!Directory.Exists(carpetaAudio))
            {
                return new CrudResponse 
                { 
                    Exito = false, 
                    Mensaje = $"La carpeta de audio no existe: {carpetaAudio}" 
                };
            }

            using var conn = _db.ObtenerConexion();
            int actualizados = 0;
            var errores = new List<string>();

            // Procesar CDs
            var carpetaCd = Path.Combine(carpetaAudio, "cd");
            if (Directory.Exists(carpetaCd))
            {
                foreach (var archivo in Directory.GetFiles(carpetaCd))
                {
                    var nombreArchivo = Path.GetFileName(archivo);
                    // Formato: cd_ID.extension (ej: cd_1179.mp3)
                    var match = System.Text.RegularExpressions.Regex.Match(nombreArchivo, @"^cd_(\d+)\.(mp3|wav|m4a|flac|ogg)$");
                    if (match.Success)
                    {
                        var id = int.Parse(match.Groups[1].Value);
                        var extension = match.Groups[2].Value;
                        var rutaRelativa = $"audio/cd/{nombreArchivo}";
                        
                        // Calcular duración aproximada
                        var fileInfo = new FileInfo(archivo);
                        int duracionAproximada = (int)(fileInfo.Length / 16000);
                        
                        var rows = await conn.ExecuteAsync(
                            """
                            UPDATE temas_cd 
                            SET archivo_audio = @rutaRelativa, 
                                duracion_segundos = @duracion, 
                                formato_audio = @formato 
                            WHERE id = @id
                            """,
                            new { id, rutaRelativa, duracion = duracionAproximada, formato = extension });
                        
                        if (rows > 0) actualizados++;
                        Console.WriteLine($"[Audio] Sincronizado CD {id}: {nombreArchivo}");
                    }
                }
            }

            // Procesar Cassettes
            var carpetaCassette = Path.Combine(carpetaAudio, "cassette");
            if (Directory.Exists(carpetaCassette))
            {
                foreach (var archivo in Directory.GetFiles(carpetaCassette))
                {
                    var nombreArchivo = Path.GetFileName(archivo);
                    // Formato: cassette_ID.extension (ej: cassette_234.mp3)
                    var match = System.Text.RegularExpressions.Regex.Match(nombreArchivo, @"^cassette_(\d+)\.(mp3|wav|m4a|flac|ogg)$");
                    if (match.Success)
                    {
                        var id = int.Parse(match.Groups[1].Value);
                        var extension = match.Groups[2].Value;
                        var rutaRelativa = $"audio/cassette/{nombreArchivo}";
                        
                        // Calcular duración aproximada
                        var fileInfo = new FileInfo(archivo);
                        int duracionAproximada = (int)(fileInfo.Length / 16000);
                        
                        var rows = await conn.ExecuteAsync(
                            """
                            UPDATE temas 
                            SET archivo_audio = @rutaRelativa, 
                                duracion_segundos = @duracion, 
                                formato_audio = @formato 
                            WHERE id = @id
                            """,
                            new { id, rutaRelativa, duracion = duracionAproximada, formato = extension });
                        
                        if (rows > 0) actualizados++;
                        Console.WriteLine($"[Audio] Sincronizado Cassette {id}: {nombreArchivo}");
                    }
                }
            }
            
            return new CrudResponse 
            { 
                Exito = true, 
                Mensaje = $"Sincronización completada. {actualizados} archivos de audio vinculados a canciones." 
            };
        }
        catch (Exception ex)
        {
            return new CrudResponse 
            { 
                Exito = false, 
                Mensaje = $"Error al sincronizar audio: {ex.Message}" 
            };
        }
    }


    /// <summary>
    /// Marca o desmarca una canción como favorita
    /// </summary>
    public async Task<CrudResponse> MarcarComoFavoritoAsync(int id, string tipo, bool esFavorito)
    {
        try
        {
            using var conn = _db.ObtenerConexion();
            var tabla = tipo.ToLower() == "cassette" ? "temas" : "temas_cd";
            
            await conn.ExecuteAsync(
                $"UPDATE {tabla} SET es_favorito = @EsFavorito WHERE id = @Id",
                new { Id = id, EsFavorito = esFavorito ? 1 : 0 }
            );
            
            return new CrudResponse 
            { 
                Exito = true, 
                Mensaje = esFavorito ? "Canción agregada a favoritos" : "Canción quitada de favoritos"
            };
        }
        catch (Exception ex)
        {
            return new CrudResponse 
            { 
                Exito = false, 
                Mensaje = $"Error al actualizar favorito: {ex.Message}" 
            };
        }
    }

    /// <summary>
    /// Obtiene todas las canciones de un medio específico, ordenadas para reproducción
    /// </summary>
    public async Task<List<CancionDetalle>> ObtenerCancionesDelMedioAsync(string numMedio, string tipo)
    {
        using var conn = _db.ObtenerConexion();
        tipo = tipo.ToLower();

        if (tipo == "cassette")
        {
            var canciones = await conn.QueryAsync<dynamic>("""
                SELECT 
                    t.id, t.num_formato as numMedio, t.tema, t.lado, t.desde, t.hasta,
                    t.id_interprete as idInterprete, i.nombre as interprete,
                    t.id_album as idAlbum, a.nombre as nombreAlbum, a.anio as anioAlbum,
                    a.es_single as esAlbumSingle,
                    ai.nombre as artistaAlbum,
                    t.link_externo as linkExterno,
                    CASE WHEN t.portada IS NOT NULL THEN 1 ELSE 0 END as tienePortada,
                    CASE WHEN a.portada IS NOT NULL THEN 1 ELSE 0 END as tienePortadaAlbum,
                    t.es_cover as esCover, t.es_original as esOriginal, t.artista_original as artistaOriginal,
                    t.archivo_audio as archivoAudio, t.duracion_segundos as duracionSegundos, t.formato_audio as formatoAudio
                FROM temas t
                INNER JOIN interpretes i ON t.id_interprete = i.id
                LEFT JOIN albumes a ON t.id_album = a.id
                LEFT JOIN interpretes ai ON a.id_interprete = ai.id
                WHERE t.num_formato = @numMedio
                ORDER BY t.lado, t.desde
                """, new { numMedio });

            return canciones.Select(c => new CancionDetalle
            {
                Id = c.id,
                Tipo = "cassette",
                Tema = c.tema,
                Interprete = c.interprete,
                IdInterprete = c.idInterprete,
                numMedio = c.numMedio,
                Lado = c.lado,
                Desde = c.desde,
                Hasta = c.hasta,
                IdAlbum = c.idAlbum,
                NombreAlbum = c.nombreAlbum,
                ArtistaAlbum = c.artistaAlbum,
                AnioAlbum = c.anioAlbum,
                EsAlbumSingle = c.esAlbumSingle == 1,
                LinkExterno = c.linkExterno,
                TienePortada = c.tienePortada == 1,
                TienePortadaAlbum = c.tienePortadaAlbum == 1,
                EsCover = c.esCover == 1,
                EsOriginal = c.esOriginal == 1,
                ArtistaOriginal = c.artistaOriginal,
                ArchivoAudio = c.archivoAudio,
                DuracionSegundos = c.duracionSegundos,
                FormatoAudio = c.formatoAudio
            }).ToList();
        }
        else // cd
        {
            var canciones = await conn.QueryAsync<dynamic>("""
                SELECT 
                    t.id, t.num_formato as numMedio, t.tema, t.ubicacion,
                    t.id_interprete as idInterprete, i.nombre as interprete,
                    t.id_album as idAlbum, a.nombre as nombreAlbum, a.anio as anioAlbum,
                    a.es_single as esAlbumSingle,
                    ai.nombre as artistaAlbum,
                    t.link_externo as linkExterno,
                    CASE WHEN t.portada IS NOT NULL THEN 1 ELSE 0 END as tienePortada,
                    CASE WHEN a.portada IS NOT NULL THEN 1 ELSE 0 END as tienePortadaAlbum,
                    t.es_cover as esCover, t.es_original as esOriginal, t.artista_original as artistaOriginal,
                    t.archivo_audio as archivoAudio, t.duracion_segundos as duracionSegundos, t.formato_audio as formatoAudio
                FROM temas_cd t
                INNER JOIN interpretes i ON t.id_interprete = i.id
                LEFT JOIN albumes a ON t.id_album = a.id
                LEFT JOIN interpretes ai ON a.id_interprete = ai.id
                WHERE t.num_formato = @numMedio
                ORDER BY t.ubicacion
                """, new { numMedio });

            return canciones.Select(c => new CancionDetalle
            {
                Id = c.id,
                Tipo = "cd",
                Tema = c.tema,
                Interprete = c.interprete,
                IdInterprete = c.idInterprete,
                numMedio = c.numMedio,
                Ubicacion = c.ubicacion,
                IdAlbum = c.idAlbum,
                NombreAlbum = c.nombreAlbum,
                ArtistaAlbum = c.artistaAlbum,
                AnioAlbum = c.anioAlbum,
                EsAlbumSingle = c.esAlbumSingle == 1,
                LinkExterno = c.linkExterno,
                TienePortada = c.tienePortada == 1,
                TienePortadaAlbum = c.tienePortadaAlbum == 1,
                EsCover = c.esCover == 1,
                EsOriginal = c.esOriginal == 1,
                ArtistaOriginal = c.artistaOriginal,
                ArchivoAudio = c.archivoAudio,
                DuracionSegundos = c.duracionSegundos,
                FormatoAudio = c.formatoAudio
            }).ToList();
        }
    }

    /// <summary>
    /// Obtiene todas las canciones que tienen archivo de audio (para el pool del reproductor)
    /// </summary>
    public async Task<List<CancionConAudio>> ObtenerCancionesConAudioAsync()
    {
        using var conn = _db.ObtenerConexion();
        var canciones = new List<CancionConAudio>();

        // Obtener cassettes con audio
        var cassettes = await conn.QueryAsync<dynamic>("""
            SELECT 
                t.id, 'cassette' as tipo, t.tema, i.nombre as interprete,
                t.id_album, a.nombre as nombreAlbum,
                t.num_formato as numMedio, t.lado, t.desde, t.hasta,
                t.archivo_audio,
                CASE WHEN t.portada IS NOT NULL THEN 1 ELSE 0 END as tienePortada,
                CASE WHEN a.portada IS NOT NULL THEN 1 ELSE 0 END as tienePortadaAlbum
            FROM temas t
            INNER JOIN interpretes i ON t.id_interprete = i.id
            LEFT JOIN albumes a ON t.id_album = a.id
            WHERE t.archivo_audio IS NOT NULL AND t.archivo_audio != ''
            """);

        foreach (var c in cassettes)
        {
            canciones.Add(new CancionConAudio
            {
                Id = (int)(long)c.id,
                Tipo = "cassette",
                Tema = (string)c.tema,
                Interprete = (string)c.interprete,
                IdAlbum = c.id_album != null ? (int?)(long)c.id_album : null,
                NombreAlbum = (string?)c.nombreAlbum,
                numMedio = (string)c.numMedio,
                Posicion = $"{c.lado}: {c.desde}-{c.hasta}",
                RutaArchivo = (string)c.archivo_audio,
                TienePortada = c.tienePortada != null && (long)c.tienePortada == 1,
                TienePortadaAlbum = c.tienePortadaAlbum != null && (long)c.tienePortadaAlbum == 1
            });
        }

        // Obtener CDs con audio
        var cds = await conn.QueryAsync<dynamic>("""
            SELECT 
                t.id, 'cd' as tipo, t.tema, i.nombre as interprete,
                t.id_album, a.nombre as nombreAlbum,
                t.num_formato as numMedio, t.ubicacion,
                t.archivo_audio,
                CASE WHEN t.portada IS NOT NULL THEN 1 ELSE 0 END as tienePortada,
                CASE WHEN a.portada IS NOT NULL THEN 1 ELSE 0 END as tienePortadaAlbum
            FROM temas_cd t
            INNER JOIN interpretes i ON t.id_interprete = i.id
            LEFT JOIN albumes a ON t.id_album = a.id
            WHERE t.archivo_audio IS NOT NULL AND t.archivo_audio != ''
            """);

        foreach (var c in cds)
        {
            canciones.Add(new CancionConAudio
            {
                Id = (int)(long)c.id,
                Tipo = "cd",
                Tema = (string)c.tema,
                Interprete = (string)c.interprete,
                IdAlbum = c.id_album != null ? (int?)(long)c.id_album : null,
                NombreAlbum = (string?)c.nombreAlbum,
                numMedio = (string)c.numMedio,
                Posicion = $"Track {c.ubicacion}",
                RutaArchivo = (string)c.archivo_audio,
                TienePortada = c.tienePortada != null && (long)c.tienePortada == 1,
                TienePortadaAlbum = c.tienePortadaAlbum != null && (long)c.tienePortadaAlbum == 1
            });
        }

        return canciones;
    }

    // ==========================================
    // GESTIÓN DE COMPOSICIONES (Agrupar versiones/covers)
    // ==========================================

    /// <summary>
    /// Crea una nueva composición
    /// </summary>
    public async Task<CrudResponse> CrearComposicionAsync(ComposicionRequest request)
    {
        try
        {
            using var conn = _db.ObtenerConexion();
            
            var id = await conn.QuerySingleAsync<int>("""
                INSERT INTO composiciones (titulo_canonico, compositor, anio_original, notas)
                VALUES (@TituloCanonico, @Compositor, @AnioOriginal, @Notas);
                SELECT last_insert_rowid();
                """, request);
            
            return new CrudResponse 
            { 
                Exito = true, 
                Mensaje = "Composición creada correctamente",
                IdCreado = id
            };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>
    /// Lista todas las composiciones con conteo de versiones
    /// </summary>
    public async Task<List<ComposicionResumen>> ListarComposicionesAsync()
    {
        using var conn = _db.ObtenerConexion();
        
        var composiciones = await conn.QueryAsync<dynamic>("""
            SELECT 
                c.id,
                c.titulo_canonico,
                c.compositor,
                c.anio_original,
                (SELECT COUNT(*) FROM temas WHERE id_composicion = c.id) +
                (SELECT COUNT(*) FROM temas_cd WHERE id_composicion = c.id) as total_versiones
            FROM composiciones c
            ORDER BY c.titulo_canonico
            """);
        
        var resultado = new List<ComposicionResumen>();
        
        foreach (var c in composiciones)
        {
            // Contar artistas únicos
            var artistasCassette = await conn.QueryAsync<int>(
                "SELECT DISTINCT id_interprete FROM temas WHERE id_composicion = @id",
                new { id = (int)(long)c.id });
            var artistasCd = await conn.QueryAsync<int>(
                "SELECT DISTINCT id_interprete FROM temas_cd WHERE id_composicion = @id",
                new { id = (int)(long)c.id });
            
            var todosArtistas = artistasCassette.Concat(artistasCd).Distinct().Count();
            
            resultado.Add(new ComposicionResumen
            {
                Id = (int)(long)c.id,
                TituloCanonico = (string)c.titulo_canonico,
                Compositor = (string?)c.compositor,
                AnioOriginal = (string?)c.anio_original,
                TotalVersiones = (int)(long)c.total_versiones,
                TotalArtistas = todosArtistas
            });
        }
        
        return resultado;
    }

    /// <summary>
    /// Separa una canción de su composición (la hace independiente)
    /// </summary>
    public async Task<CrudResponse> SepararDeComposicionAsync(SepararCancionRequest request)
    {
        try
        {
            using var conn = _db.ObtenerConexion();
            var tabla = request.Tipo.ToLower() == "cassette" ? "temas" : "temas_cd";
            
            // Verificar que la canción existe y tiene composición
            var idComposicionActual = await conn.QueryFirstOrDefaultAsync<int?>(
                $"SELECT id_composicion FROM {tabla} WHERE id = @id",
                new { id = request.IdCancion });
            
            if (idComposicionActual == null)
            {
                return new CrudResponse { Exito = false, Mensaje = "La canción ya es independiente" };
            }
            
            // Quitar la canción de la composición
            await conn.ExecuteAsync(
                $"UPDATE {tabla} SET id_composicion = NULL WHERE id = @id",
                new { id = request.IdCancion });
            
            // Verificar si la composición quedó vacía
            var restantesCassette = await conn.QueryFirstAsync<int>(
                "SELECT COUNT(*) FROM temas WHERE id_composicion = @id",
                new { id = idComposicionActual });
            var restantesCd = await conn.QueryFirstAsync<int>(
                "SELECT COUNT(*) FROM temas_cd WHERE id_composicion = @id",
                new { id = idComposicionActual });
            
            // Si solo queda 1 canción o menos, desagrupar esa también y eliminar la composición
            if (restantesCassette + restantesCd <= 1)
            {
                await conn.ExecuteAsync(
                    "UPDATE temas SET id_composicion = NULL WHERE id_composicion = @id",
                    new { id = idComposicionActual });
                await conn.ExecuteAsync(
                    "UPDATE temas_cd SET id_composicion = NULL WHERE id_composicion = @id",
                    new { id = idComposicionActual });
                await conn.ExecuteAsync(
                    "DELETE FROM composiciones WHERE id = @id",
                    new { id = idComposicionActual });
            }
            
            return new CrudResponse 
            { 
                Exito = true, 
                Mensaje = "Canción separada correctamente. Ahora es independiente." 
            };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>
    /// Une canciones a una misma composición (agrupa como versiones de la misma obra)
    /// </summary>
    public async Task<CrudResponse> UnirAComposicionAsync(UnirCancionesRequest request)
    {
        try
        {
            using var conn = _db.ObtenerConexion();
            
            int idComposicion;
            
            if (request.IdComposicionExistente.HasValue)
            {
                // Usar composición existente
                idComposicion = request.IdComposicionExistente.Value;
                
                // Verificar que existe
                var existe = await conn.QueryFirstOrDefaultAsync<int?>(
                    "SELECT id FROM composiciones WHERE id = @id",
                    new { id = idComposicion });
                
                if (existe == null)
                {
                    return new CrudResponse { Exito = false, Mensaje = "La composición especificada no existe" };
                }
            }
            else
            {
                // Crear nueva composición
                if (string.IsNullOrEmpty(request.TituloNuevaComposicion))
                {
                    // Obtener el nombre de la primera canción como título
                    var primeraCancion = request.Canciones.FirstOrDefault();
                    if (primeraCancion != null)
                    {
                        var tabla = primeraCancion.Tipo.ToLower() == "cassette" ? "temas" : "temas_cd";
                        var nombreTema = await conn.QueryFirstOrDefaultAsync<string>(
                            $"SELECT tema FROM {tabla} WHERE id = @id",
                            new { id = primeraCancion.Id });
                        request = request with { TituloNuevaComposicion = nombreTema ?? "Sin título" };
                    }
                    else
                    {
                        return new CrudResponse { Exito = false, Mensaje = "Debe proporcionar al menos una canción" };
                    }
                }
                
                idComposicion = await conn.QuerySingleAsync<int>("""
                    INSERT INTO composiciones (titulo_canonico)
                    VALUES (@Titulo);
                    SELECT last_insert_rowid();
                    """, new { Titulo = request.TituloNuevaComposicion });
            }
            
            // Actualizar todas las canciones con el id de composición
            int actualizadas = 0;
            foreach (var cancion in request.Canciones)
            {
                var tabla = cancion.Tipo.ToLower() == "cassette" ? "temas" : "temas_cd";
                var filas = await conn.ExecuteAsync(
                    $"UPDATE {tabla} SET id_composicion = @idComp WHERE id = @id",
                    new { idComp = idComposicion, id = cancion.Id });
                actualizadas += filas;
            }
            
            return new CrudResponse 
            { 
                Exito = true, 
                Mensaje = $"{actualizadas} canciones agrupadas correctamente",
                IdCreado = idComposicion
            };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>
    /// Obtiene todas las canciones de una composición específica
    /// </summary>
    public async Task<List<CancionDuplicada>> ObtenerCancionesDeComposicionAsync(int idComposicion)
    {
        using var conn = _db.ObtenerConexion();
        var canciones = new List<CancionDuplicada>();
        
        // Obtener de cassettes
        var temasCassette = await conn.QueryAsync<dynamic>("""
            SELECT 
                t.id AS Id,
                'cassette' AS Tipo,
                t.tema AS Tema,
                i.nombre AS Interprete,
                t.id_interprete AS IdInterprete,
                t.num_formato AS numMedio,
                t.lado || ': ' || t.desde || '-' || t.hasta AS Posicion,
                t.id_album AS IdAlbum,
                a.nombre AS NombreAlbum,
                CASE WHEN t.portada IS NOT NULL AND LENGTH(t.portada) > 0 THEN 1 ELSE 0 END AS TienePortada,
                t.link_externo AS LinkExterno,
                COALESCE(t.es_cover, 0) AS EsCover,
                COALESCE(t.es_original, 0) AS EsOriginal,
                t.artista_original AS ArtistaOriginal,
                t.archivo_audio AS ArchivoAudio,
                t.id_composicion AS IdComposicion
            FROM temas t
            JOIN interpretes i ON t.id_interprete = i.id
            LEFT JOIN albumes a ON t.id_album = a.id
            WHERE t.id_composicion = @idComp
            """, new { idComp = idComposicion });
        
        foreach (var t in temasCassette)
        {
            canciones.Add(new CancionDuplicada
            {
                Id = (int)(long)t.Id,
                Tipo = "cassette",
                Tema = (string)(t.Tema ?? ""),
                Interprete = (string)(t.Interprete ?? ""),
                IdInterprete = (int)(long)t.IdInterprete,
                numMedio = (string)(t.numMedio ?? ""),
                Posicion = (string?)t.Posicion,
                IdAlbum = t.IdAlbum != null ? (int?)(long)t.IdAlbum : null,
                NombreAlbum = (string?)t.NombreAlbum,
                TienePortada = t.TienePortada != null && (long)t.TienePortada == 1,
                LinkExterno = (string?)t.LinkExterno,
                EsCover = t.EsCover != null && (long)t.EsCover == 1,
                EsOriginal = t.EsOriginal != null && (long)t.EsOriginal == 1,
                ArtistaOriginal = (string?)t.ArtistaOriginal,
                ArchivoAudio = (string?)t.ArchivoAudio,
                IdComposicion = t.IdComposicion != null ? (int?)(long)t.IdComposicion : null
            });
        }
        
        // Obtener de CDs
        var temasCd = await conn.QueryAsync<dynamic>("""
            SELECT 
                t.id AS Id,
                'cd' AS Tipo,
                t.tema AS Tema,
                i.nombre AS Interprete,
                t.id_interprete AS IdInterprete,
                t.num_formato AS numMedio,
                'Track ' || t.ubicacion AS Posicion,
                t.id_album AS IdAlbum,
                a.nombre AS NombreAlbum,
                CASE WHEN t.portada IS NOT NULL AND LENGTH(t.portada) > 0 THEN 1 ELSE 0 END AS TienePortada,
                t.link_externo AS LinkExterno,
                COALESCE(t.es_cover, 0) AS EsCover,
                COALESCE(t.es_original, 0) AS EsOriginal,
                t.artista_original AS ArtistaOriginal,
                t.archivo_audio AS ArchivoAudio,
                t.id_composicion AS IdComposicion
            FROM temas_cd t
            JOIN interpretes i ON t.id_interprete = i.id
            LEFT JOIN albumes a ON t.id_album = a.id
            WHERE t.id_composicion = @idComp
            """, new { idComp = idComposicion });
        
        foreach (var t in temasCd)
        {
            canciones.Add(new CancionDuplicada
            {
                Id = (int)(long)t.Id,
                Tipo = "cd",
                Tema = (string)(t.Tema ?? ""),
                Interprete = (string)(t.Interprete ?? ""),
                IdInterprete = (int)(long)t.IdInterprete,
                numMedio = (string)(t.numMedio ?? ""),
                Posicion = (string?)t.Posicion,
                IdAlbum = t.IdAlbum != null ? (int?)(long)t.IdAlbum : null,
                NombreAlbum = (string?)t.NombreAlbum,
                TienePortada = t.TienePortada != null && (long)t.TienePortada == 1,
                LinkExterno = (string?)t.LinkExterno,
                EsCover = t.EsCover != null && (long)t.EsCover == 1,
                EsOriginal = t.EsOriginal != null && (long)t.EsOriginal == 1,
                ArtistaOriginal = (string?)t.ArtistaOriginal,
                ArchivoAudio = (string?)t.ArchivoAudio,
                IdComposicion = t.IdComposicion != null ? (int?)(long)t.IdComposicion : null
            });
        }
        
        return canciones.OrderBy(c => c.EsCover).ThenBy(c => c.Interprete).ToList();
    }

    // ============================================
    // INTÉRPRETES - FOTOS Y GESTIÓN EXTENDIDA
    // ============================================

    /// <summary>Obtiene la foto de un intérprete.</summary>
    public async Task<byte[]?> ObtenerFotoInterpreteAsync(int id)
    {
        using var conn = _db.ObtenerConexion();
        return await conn.QueryFirstOrDefaultAsync<byte[]?>(
            "SELECT foto_blob FROM interpretes WHERE id = @id", new { id });
    }

    /// <summary>Guarda la foto de un intérprete.</summary>
    public async Task<CrudResponse> GuardarFotoInterpreteAsync(int id, byte[] foto)
    {
        using var conn = _db.ObtenerConexion();

        try
        {
            var rows = await conn.ExecuteAsync(
                "UPDATE interpretes SET foto_blob = @foto WHERE id = @id",
                new { id, foto });

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Intérprete no encontrado" };

            return new CrudResponse { Exito = true, Mensaje = "Foto guardada correctamente" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Elimina la foto de un intérprete.</summary>
    public async Task<CrudResponse> EliminarFotoInterpreteAsync(int id)
    {
        using var conn = _db.ObtenerConexion();

        try
        {
            var rows = await conn.ExecuteAsync(
                "UPDATE interpretes SET foto_blob = NULL WHERE id = @id",
                new { id });

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Intérprete no encontrado" };

            return new CrudResponse { Exito = true, Mensaje = "Foto eliminada correctamente" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Actualiza los datos de un intérprete.</summary>
    public async Task<CrudResponse> ActualizarInterpreteAsync(int id, InterpreteUpdateRequest request)
    {
        using var conn = _db.ObtenerConexion();

        try
        {
            var rows = await conn.ExecuteAsync(
                "UPDATE interpretes SET nombre = @Nombre, biografia = @Biografia WHERE id = @id",
                new { id, request.Nombre, request.Biografia });

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Intérprete no encontrado" };

            return new CrudResponse { Exito = true, Mensaje = "Intérprete actualizado correctamente" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Obtiene un intérprete por ID con información completa.</summary>
    public async Task<InterpreteCompleto?> ObtenerInterpreteCompletoAsync(int id)
    {
        using var conn = _db.ObtenerConexion();

        var interprete = await conn.QueryFirstOrDefaultAsync<InterpreteCompleto>("""
            SELECT id AS Id, nombre AS Nombre, foto_blob AS FotoBlob, biografia AS Biografia,
                   (SELECT COUNT(*) FROM temas WHERE id_interprete = @id) AS TotalTemasCassette,
                   (SELECT COUNT(*) FROM temas_cd WHERE id_interprete = @id) AS TotalTemasCd
            FROM interpretes WHERE id = @id
            """, new { id });

        return interprete;
    }

    // ============================================
    // MULTI-ARTISTA EN CANCIONES
    // ============================================

    /// <summary>Obtiene los artistas asociados a una canción.</summary>
    public async Task<List<ArtistaEnCancion>> ObtenerArtistasDeCancionAsync(int idCancion, string tipo)
    {
        using var conn = _db.ObtenerConexion();

        var artistas = await conn.QueryAsync<ArtistaEnCancion>("""
            SELECT ca.id_interprete AS IdInterprete, i.nombre AS Nombre, 
                   ca.es_principal AS EsPrincipal, ca.rol AS Rol,
                   CASE WHEN i.foto_blob IS NOT NULL THEN 1 ELSE 0 END AS TieneFoto
            FROM cancion_artistas ca
            JOIN interpretes i ON ca.id_interprete = i.id
            WHERE ca.id_cancion = @idCancion AND ca.tipo_cancion = @tipo
            ORDER BY ca.es_principal DESC, i.nombre
            """, new { idCancion, tipo });

        return artistas.ToList();
    }

    /// <summary>Asigna múltiples artistas a una canción.</summary>
    public async Task<CrudResponse> AsignarArtistasACancionAsync(AsignarArtistasCancionRequest request)
    {
        using var conn = _db.ObtenerConexion();
        using var trans = conn.BeginTransaction();

        try
        {
            // Eliminar asignaciones anteriores
            await conn.ExecuteAsync(
                "DELETE FROM cancion_artistas WHERE id_cancion = @IdCancion AND tipo_cancion = @TipoCancion",
                new { request.IdCancion, request.TipoCancion }, trans);

            // Insertar nuevas asignaciones
            foreach (var artista in request.Artistas)
            {
                await conn.ExecuteAsync("""
                    INSERT INTO cancion_artistas (id_cancion, tipo_cancion, id_interprete, es_principal, rol)
                    VALUES (@IdCancion, @TipoCancion, @IdInterprete, @EsPrincipal, @Rol)
                    """, new
                {
                    request.IdCancion,
                    request.TipoCancion,
                    artista.IdInterprete,
                    EsPrincipal = artista.EsPrincipal ? 1 : 0,
                    artista.Rol
                }, trans);
            }

            trans.Commit();
            return new CrudResponse { Exito = true, Mensaje = $"{request.Artistas.Count} artista(s) asignados correctamente" };
        }
        catch (Exception ex)
        {
            trans.Rollback();
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Quita un artista de una canción.</summary>
    public async Task<CrudResponse> QuitarArtistaDeCancionAsync(int idCancion, string tipo, int idInterprete)
    {
        using var conn = _db.ObtenerConexion();

        try
        {
            var rows = await conn.ExecuteAsync(
                "DELETE FROM cancion_artistas WHERE id_cancion = @idCancion AND tipo_cancion = @tipo AND id_interprete = @idInterprete",
                new { idCancion, tipo, idInterprete });

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Artista no encontrado en esta canción" };

            return new CrudResponse { Exito = true, Mensaje = "Artista removido de la canción" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    // ============================================
    // PERFILES DE ARTISTAS (TIPO SPOTIFY)
    // ============================================

    /// <summary>Obtiene el perfil completo de un artista.</summary>
    public async Task<PerfilArtista?> ObtenerPerfilArtistaAsync(int id)
    {
        using var conn = _db.ObtenerConexion();

        var datos = await conn.QueryFirstOrDefaultAsync<dynamic>("""
            SELECT id AS Id, nombre AS Nombre, 
                   COALESCE(tipo_artista, 'artista') AS TipoArtista,
                   biografia AS Biografia, pais AS Pais,
                   anio_inicio AS AnioInicio, anio_fin AS AnioFin,
                   discografica AS Discografica, sitio_web AS SitioWeb,
                   CASE WHEN foto_blob IS NOT NULL THEN 1 ELSE 0 END AS TieneFoto,
                   (SELECT COUNT(*) FROM temas WHERE id_interprete = @id) AS TotalTemasCassette,
                   (SELECT COUNT(*) FROM temas_cd WHERE id_interprete = @id) AS TotalTemasCd
            FROM interpretes WHERE id = @id
            """, new { id });

        if (datos == null) return null;

        var perfil = new PerfilArtista
        {
            Id = (int)(long)datos.Id,
            Nombre = (string)datos.Nombre,
            TipoArtista = (string?)datos.TipoArtista ?? "artista",
            Biografia = (string?)datos.Biografia,
            Pais = (string?)datos.Pais,
            AnioInicio = datos.AnioInicio != null ? (int?)(long)datos.AnioInicio : null,
            AnioFin = datos.AnioFin != null ? (int?)(long)datos.AnioFin : null,
            Discografica = (string?)datos.Discografica,
            SitioWeb = (string?)datos.SitioWeb,
            TieneFoto = (long)datos.TieneFoto == 1,
            TotalTemasCassette = (int)(long)datos.TotalTemasCassette,
            TotalTemasCd = (int)(long)datos.TotalTemasCd
        };

        // Obtener géneros
        var generos = await conn.QueryAsync<string>(
            "SELECT genero FROM interprete_generos WHERE id_interprete = @id ORDER BY genero",
            new { id });
        perfil.Generos = generos.ToList();

        // Obtener miembros (solo si es banda)
        if (perfil.TipoArtista == "banda")
        {
            var miembros = await conn.QueryAsync<dynamic>("""
                SELECT bm.id AS Id, bm.id_miembro AS IdMiembro, bm.nombre_miembro AS NombreMiembro,
                       bm.rol AS Rol, bm.anio_ingreso AS AnioIngreso, bm.anio_salida AS AnioSalida,
                       bm.es_fundador AS EsFundador,
                       CASE WHEN i.foto_blob IS NOT NULL THEN 1 ELSE 0 END AS TieneFoto
                FROM banda_miembros bm
                LEFT JOIN interpretes i ON bm.id_miembro = i.id
                WHERE bm.id_banda = @id
                ORDER BY bm.es_fundador DESC, bm.anio_ingreso
                """, new { id });

            perfil.Miembros = miembros.Select(m => new MiembroBanda
            {
                Id = (int)(long)m.Id,
                IdMiembro = m.IdMiembro != null ? (int?)(long)m.IdMiembro : null,
                NombreMiembro = (string)m.NombreMiembro,
                Rol = (string)m.Rol,
                AnioIngreso = m.AnioIngreso != null ? (int?)(long)m.AnioIngreso : null,
                AnioSalida = m.AnioSalida != null ? (int?)(long)m.AnioSalida : null,
                EsFundador = m.EsFundador != null && (long)m.EsFundador == 1,
                TieneFoto = m.TieneFoto != null && (long)m.TieneFoto == 1
            }).ToList();
        }

        return perfil;
    }

    /// <summary>Actualiza el perfil de un artista. Si el nuevo nombre ya existe, unifica ambos intérpretes.</summary>
    public async Task<CrudResponse> ActualizarPerfilArtistaAsync(int id, ActualizarPerfilArtistaRequest request)
    {
        using var conn = _db.ObtenerConexion();
        using var trans = conn.BeginTransaction();

        try
        {
            // Verificar si el nuevo nombre ya pertenece a otro intérprete
            if (!string.IsNullOrWhiteSpace(request.Nombre))
            {
                var existente = await conn.QueryFirstOrDefaultAsync<int?>(
                    "SELECT id FROM interpretes WHERE nombre = @nombre COLLATE NOCASE AND id != @id",
                    new { nombre = request.Nombre, id }, trans);

                if (existente.HasValue)
                {
                    // Unificar: mover todo del intérprete actual (id) al existente (existente.Value)
                    await UnificarInterpretesAsync(conn, trans, id, existente.Value);

                    trans.Commit();
                    return new CrudResponse
                    {
                        Exito = true,
                        Mensaje = $"Intérpretes unificados: '{request.Nombre}' ahora contiene todas las canciones",
                        IdCreado = existente.Value // Devolver el ID del intérprete destino
                    };
                }
            }

            // No hay duplicado, actualizar normalmente
            var sql = """
                UPDATE interpretes SET
                    tipo_artista = COALESCE(@TipoArtista, tipo_artista),
                    nombre = COALESCE(@Nombre, nombre),
                    biografia = COALESCE(@Biografia, biografia),
                    pais = COALESCE(@Pais, pais),
                    anio_inicio = COALESCE(@AnioInicio, anio_inicio),
                    anio_fin = @AnioFin,
                    discografica = COALESCE(@Discografica, discografica),
                    sitio_web = COALESCE(@SitioWeb, sitio_web)
                WHERE id = @id
                """;

            var rows = await conn.ExecuteAsync(sql, new
            {
                id,
                request.TipoArtista,
                request.Nombre,
                request.Biografia,
                request.Pais,
                request.AnioInicio,
                request.AnioFin,
                request.Discografica,
                request.SitioWeb
            }, trans);

            if (rows == 0)
            {
                trans.Rollback();
                return new CrudResponse { Exito = false, Mensaje = "Artista no encontrado" };
            }

            // Actualizar géneros si se proporcionan
            if (request.Generos != null)
            {
                await conn.ExecuteAsync("DELETE FROM interprete_generos WHERE id_interprete = @id", new { id }, trans);
                foreach (var genero in request.Generos)
                {
                    await conn.ExecuteAsync(
                        "INSERT INTO interprete_generos (id_interprete, genero) VALUES (@id, @genero)",
                        new { id, genero }, trans);
                }
            }

            trans.Commit();
            return new CrudResponse { Exito = true, Mensaje = "Perfil actualizado correctamente" };
        }
        catch (Exception ex)
        {
            trans.Rollback();
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Unifica dos intérpretes, moviendo todas las relaciones del origen al destino y eliminando el origen.</summary>
    private async Task UnificarInterpretesAsync(System.Data.IDbConnection conn, System.Data.IDbTransaction trans, int idOrigen, int idDestino)
    {
        // Mover canciones de cassette
        await conn.ExecuteAsync(
            "UPDATE temas SET id_interprete = @dest WHERE id_interprete = @orig",
            new { dest = idDestino, orig = idOrigen }, trans);

        // Mover canciones de CD
        await conn.ExecuteAsync(
            "UPDATE temas_cd SET id_interprete = @dest WHERE id_interprete = @orig",
            new { dest = idDestino, orig = idOrigen }, trans);

        // Mover álbumes
        await conn.ExecuteAsync(
            "UPDATE albumes SET id_interprete = @dest WHERE id_interprete = @orig",
            new { dest = idDestino, orig = idOrigen }, trans);

        // Mover relaciones multi-artista (evitar duplicados con OR IGNORE)
        await conn.ExecuteAsync(
            "UPDATE OR IGNORE cancion_artistas SET id_interprete = @dest WHERE id_interprete = @orig",
            new { dest = idDestino, orig = idOrigen }, trans);
        await conn.ExecuteAsync(
            "DELETE FROM cancion_artistas WHERE id_interprete = @orig",
            new { orig = idOrigen }, trans);

        // Mover miembros de banda (donde el origen era miembro de otra banda)
        await conn.ExecuteAsync(
            "UPDATE OR IGNORE banda_miembros SET id_miembro = @dest WHERE id_miembro = @orig",
            new { dest = idDestino, orig = idOrigen }, trans);
        // Limpiar referencias huérfanas
        await conn.ExecuteAsync(
            "DELETE FROM banda_miembros WHERE id_miembro = @orig",
            new { orig = idOrigen }, trans);

        // Si el origen era una banda, mover sus miembros al destino
        await conn.ExecuteAsync(
            "UPDATE banda_miembros SET id_banda = @dest WHERE id_banda = @orig",
            new { dest = idDestino, orig = idOrigen }, trans);

        // Fusionar géneros (evitar duplicados con OR IGNORE)
        await conn.ExecuteAsync(@"
            INSERT OR IGNORE INTO interprete_generos (id_interprete, genero)
            SELECT @dest, genero FROM interprete_generos WHERE id_interprete = @orig",
            new { dest = idDestino, orig = idOrigen }, trans);
        await conn.ExecuteAsync(
            "DELETE FROM interprete_generos WHERE id_interprete = @orig",
            new { orig = idOrigen }, trans);

        // Eliminar el intérprete origen
        await conn.ExecuteAsync(
            "DELETE FROM interpretes WHERE id = @orig",
            new { orig = idOrigen }, trans);
    }

    // ============================================
    // MIEMBROS DE BANDA
    // ============================================

    /// <summary>Obtiene los miembros de una banda.</summary>
    public async Task<List<MiembroBanda>> ObtenerMiembrosBandaAsync(int idBanda)
    {
        using var conn = _db.ObtenerConexion();

        var miembros = await conn.QueryAsync<dynamic>("""
            SELECT bm.id AS Id, bm.id_miembro AS IdMiembro, bm.nombre_miembro AS NombreMiembro,
                   bm.rol AS Rol, bm.anio_ingreso AS AnioIngreso, bm.anio_salida AS AnioSalida,
                   bm.es_fundador AS EsFundador,
                   CASE WHEN i.foto_blob IS NOT NULL THEN 1 ELSE 0 END AS TieneFoto
            FROM banda_miembros bm
            LEFT JOIN interpretes i ON bm.id_miembro = i.id
            WHERE bm.id_banda = @idBanda
            ORDER BY bm.es_fundador DESC, bm.anio_ingreso
            """, new { idBanda });

        return miembros.Select(m => new MiembroBanda
        {
            Id = (int)(long)m.Id,
            IdMiembro = m.IdMiembro != null ? (int?)(long)m.IdMiembro : null,
            NombreMiembro = (string)m.NombreMiembro,
            Rol = (string)m.Rol,
            AnioIngreso = m.AnioIngreso != null ? (int?)(long)m.AnioIngreso : null,
            AnioSalida = m.AnioSalida != null ? (int?)(long)m.AnioSalida : null,
            EsFundador = m.EsFundador != null && (long)m.EsFundador == 1,
            TieneFoto = m.TieneFoto != null && (long)m.TieneFoto == 1
        }).ToList();
    }

    /// <summary>Agrega un miembro a una banda. Si no existe el intérprete, lo crea automáticamente.</summary>
    public async Task<CrudResponse> AgregarMiembroBandaAsync(int idBanda, AgregarMiembroRequest request)
    {
        using var conn = _db.ObtenerConexion();

        try
        {
            // Verificar que sea una banda
            var tipo = await conn.QueryFirstOrDefaultAsync<string>(
                "SELECT tipo_artista FROM interpretes WHERE id = @idBanda", new { idBanda });
            
            if (tipo != "banda")
            {
                // Auto-convertir a banda
                await conn.ExecuteAsync(
                    "UPDATE interpretes SET tipo_artista = 'banda' WHERE id = @idBanda", 
                    new { idBanda });
            }

            // Determinar ID y nombre del miembro
            int? idMiembro = request.IdMiembro;
            string nombreMiembro = request.NombreMiembro ?? "";

            // Si no hay IdMiembro pero hay NombreMiembro, crear nuevo intérprete
            if (!idMiembro.HasValue && !string.IsNullOrWhiteSpace(nombreMiembro))
            {
                // Verificar si ya existe un intérprete con ese nombre exacto
                var existente = await conn.QueryFirstOrDefaultAsync<int?>(
                    "SELECT id FROM interpretes WHERE nombre = @nombreMiembro",
                    new { nombreMiembro });

                if (existente.HasValue)
                {
                    // Usar el existente
                    idMiembro = existente.Value;
                }
                else
                {
                    // Crear nuevo intérprete como artista solista
                    idMiembro = await conn.QuerySingleAsync<int>("""
                        INSERT INTO interpretes (nombre, tipo_artista)
                        VALUES (@nombreMiembro, 'artista');
                        SELECT last_insert_rowid();
                        """, new { nombreMiembro });
                }
            }
            
            // Si hay IdMiembro pero no nombre, obtener el nombre del intérprete
            if (idMiembro.HasValue && string.IsNullOrEmpty(nombreMiembro))
            {
                nombreMiembro = await conn.QueryFirstOrDefaultAsync<string>(
                    "SELECT nombre FROM interpretes WHERE id = @id", 
                    new { id = idMiembro }) ?? "";
            }

            await conn.ExecuteAsync("""
                INSERT INTO banda_miembros (id_banda, id_miembro, nombre_miembro, rol, anio_ingreso, anio_salida, es_fundador)
                VALUES (@idBanda, @idMiembro, @nombreMiembro, @Rol, @AnioIngreso, @AnioSalida, @EsFundador)
                """, new
            {
                idBanda,
                idMiembro,
                nombreMiembro,
                request.Rol,
                request.AnioIngreso,
                request.AnioSalida,
                EsFundador = request.EsFundador ? 1 : 0
            });

            return new CrudResponse { Exito = true, Mensaje = "Miembro agregado correctamente", IdCreado = idMiembro };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Actualiza un miembro de banda.</summary>
    public async Task<CrudResponse> ActualizarMiembroAsync(int idMiembro, ActualizarMiembroRequest request)
    {
        using var conn = _db.ObtenerConexion();

        try
        {
            var sql = """
                UPDATE banda_miembros SET
                    nombre_miembro = COALESCE(@NombreMiembro, nombre_miembro),
                    rol = COALESCE(@Rol, rol),
                    anio_ingreso = COALESCE(@AnioIngreso, anio_ingreso),
                    anio_salida = @AnioSalida,
                    es_fundador = COALESCE(@EsFundador, es_fundador)
                WHERE id = @idMiembro
                """;

            var rows = await conn.ExecuteAsync(sql, new
            {
                idMiembro,
                request.NombreMiembro,
                request.Rol,
                request.AnioIngreso,
                request.AnioSalida,
                EsFundador = request.EsFundador.HasValue ? (request.EsFundador.Value ? 1 : 0) : (int?)null
            });

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Miembro no encontrado" };

            return new CrudResponse { Exito = true, Mensaje = "Miembro actualizado correctamente" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Quita un miembro de una banda.</summary>
    public async Task<CrudResponse> QuitarMiembroAsync(int idMiembro)
    {
        using var conn = _db.ObtenerConexion();

        try
        {
            var rows = await conn.ExecuteAsync(
                "DELETE FROM banda_miembros WHERE id = @idMiembro",
                new { idMiembro });

            if (rows == 0)
                return new CrudResponse { Exito = false, Mensaje = "Miembro no encontrado" };

            return new CrudResponse { Exito = true, Mensaje = "Miembro removido correctamente" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    // ============================================
    // CATÁLOGOS (ROLES Y GÉNEROS)
    // ============================================

    /// <summary>Lista todos los roles disponibles.</summary>
    public async Task<List<ItemCatalogo>> ListarRolesAsync()
    {
        using var conn = _db.ObtenerConexion();
        var roles = await conn.QueryAsync<ItemCatalogo>(
            "SELECT id AS Id, nombre AS Nombre FROM roles_artista ORDER BY nombre");
        return roles.ToList();
    }

    /// <summary>Lista todos los géneros disponibles.</summary>
    public async Task<List<ItemCatalogo>> ListarGenerosAsync()
    {
        using var conn = _db.ObtenerConexion();
        var generos = await conn.QueryAsync<ItemCatalogo>(
            "SELECT id AS Id, nombre AS Nombre FROM generos_musicales ORDER BY nombre");
        return generos.ToList();
    }

    /// <summary>Crea un nuevo rol.</summary>
    public async Task<CrudResponse> CrearRolAsync(string nombre)
    {
        using var conn = _db.ObtenerConexion();
        try
        {
            await conn.ExecuteAsync(
                "INSERT INTO roles_artista (nombre) VALUES (@nombre)",
                new { nombre });
            return new CrudResponse { Exito = true, Mensaje = "Rol creado correctamente" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>Crea un nuevo género.</summary>
    public async Task<CrudResponse> CrearGeneroAsync(string nombre)
    {
        using var conn = _db.ObtenerConexion();
        try
        {
            await conn.ExecuteAsync(
                "INSERT INTO generos_musicales (nombre) VALUES (@nombre)",
                new { nombre });
            return new CrudResponse { Exito = true, Mensaje = "Género creado correctamente" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    // ==========================================
    // PLAYER MÓVIL - Estado de reproducción
    // ==========================================

    /// <summary>
    /// Obtiene el estado actual del player móvil
    /// </summary>
    public async Task<PlayerState?> ObtenerEstadoPlayerAsync()
    {
        using var conn = _db.ObtenerConexion();
        return await conn.QueryFirstOrDefaultAsync<PlayerState>(
            "SELECT cancion_id AS CancionId, cancion_tipo AS CancionTipo, posicion_segundos AS PosicionSegundos, " +
            "ultima_actualizacion AS UltimaActualizacion, playlist_json AS PlaylistJson, " +
            "shuffle AS Shuffle, repeat_mode AS RepeatMode FROM player_state WHERE id = 1"
        );
    }

    /// <summary>
    /// Guarda el estado actual del player móvil
    /// </summary>
    public async Task<CrudResponse> GuardarEstadoPlayerAsync(PlayerState estado)
    {
        using var conn = _db.ObtenerConexion();
        try
        {
            await conn.ExecuteAsync(
                @"UPDATE player_state SET 
                    cancion_id = @CancionId,
                    cancion_tipo = @CancionTipo,
                    posicion_segundos = @PosicionSegundos,
                    ultima_actualizacion = datetime('now'),
                    playlist_json = @PlaylistJson,
                    shuffle = @Shuffle,
                    repeat_mode = @RepeatMode
                WHERE id = 1",
                new
                {
                    estado.CancionId,
                    estado.CancionTipo,
                    estado.PosicionSegundos,
                    estado.PlaylistJson,
                    Shuffle = estado.Shuffle ? 1 : 0,
                    estado.RepeatMode
                }
            );
            return new CrudResponse { Exito = true, Mensaje = "Estado guardado" };
        }
        catch (Exception ex)
        {
            return new CrudResponse { Exito = false, Mensaje = $"Error: {ex.Message}" };
        }
    }

    /// <summary>
    /// Obtiene todas las canciones marcadas como favoritas
    /// </summary>
    public async Task<List<CancionUnificada>> ObtenerFavoritosAsync()
    {
        using var conn = _db.ObtenerConexion();
        
        var sql = @"
            SELECT 
                t.id AS Id,
                'cassette' AS Tipo,
                t.tema AS Tema,
                i.nombre AS Interprete,
                t.num_formato AS NumFormato,
                t.archivo_audio AS ArchivoAudio,
                t.duracion_segundos AS DuracionSegundos,
                CASE WHEN t.portada IS NOT NULL THEN 1 ELSE 0 END AS TienePortada,
                1 AS EsFavorito
            FROM temas t
            LEFT JOIN interpretes i ON t.id_interprete = i.id
            WHERE t.es_favorito = 1
            
            UNION ALL
            
            SELECT 
                tc.id AS Id,
                'cd' AS Tipo,
                tc.tema AS Tema,
                i.nombre AS Interprete,
                tc.num_formato AS NumFormato,
                tc.archivo_audio AS ArchivoAudio,
                tc.duracion_segundos AS DuracionSegundos,
                CASE WHEN tc.portada IS NOT NULL THEN 1 ELSE 0 END AS TienePortada,
                1 AS EsFavorito
            FROM temas_cd tc
            LEFT JOIN interpretes i ON tc.id_interprete = i.id
            WHERE tc.es_favorito = 1
            
            ORDER BY Tema
        ";
        
        var resultado = await conn.QueryAsync<CancionUnificada>(sql);
        return resultado.ToList();
    }

    // ============================================
    // ESTADÍSTICAS Y TRACKING
    // ============================================

    /// <summary>
    /// Registra una reproducción en el historial.
    /// </summary>
    /// <summary>
    /// Registra una reproducción en el historial y devuelve el ID insertado.
    /// </summary>
    public async Task<long> RegistrarReproduccion(int id, string tipo, int segundos)
    {
        using var conn = _db.ObtenerConexion();
        return await conn.QuerySingleAsync<long>("""
            INSERT INTO reproducciones_historial (id_cancion, tipo_medio, fecha, segundos_reproducidos)
            VALUES (@id, @tipo, DATETIME('now', 'localtime'), @segundos)
            RETURNING id;
            """, new { id, tipo, segundos });
    }

    /// <summary>
    /// Actualiza el tiempo reproducido de una entrada del historial.
    /// </summary>
    public async Task ActualizarFilaReproduccion(long id, int segundos)
    {
        using var conn = _db.ObtenerConexion();
        // Solo actualizamos si el nuevo valor es mayor (por seguridad, aunque el cliente debería manejarlo)
        await conn.ExecuteAsync("""
            UPDATE reproducciones_historial 
            SET segundos_reproducidos = @segundos 
            WHERE id = @id
            """, new { id, segundos });
    }

    /// <summary>
    /// Obtiene estadísticas de reproducciones del usuario.
    /// </summary>
    public async Task<EstadisticasUsuario> ObtenerEstadisticasUsuario()
    {
        using var conn = _db.ObtenerConexion();

        // 1. Top 5 Canciones (por cantidad de reproducciones - plays)
        var topCancionesRaw = await conn.QueryAsync<(int Id, string Tipo, int Conteo)>("""
            SELECT id_cancion, tipo_medio, COUNT(*) as c
            FROM reproducciones_historial
            GROUP BY id_cancion, tipo_medio
            ORDER BY c DESC
            LIMIT 5
            """);

        var topCanciones = new List<ItemTop>();
        foreach (var c in topCancionesRaw)
        {
            // Obtener detalles de la canción incluyendo portada y álbum
            string sql = c.Tipo == "cassette" 
                ? "SELECT t.tema, i.nombre, t.id_album, (t.portada IS NOT NULL OR t.id_album IS NOT NULL) as TieneImg FROM temas t JOIN interpretes i ON t.id_interprete = i.id WHERE t.id = @id"
                : "SELECT t.tema, i.nombre, t.id_album, (t.portada IS NOT NULL OR t.id_album IS NOT NULL) as TieneImg FROM temas_cd t JOIN interpretes i ON t.id_interprete = i.id WHERE t.id = @id";
            
            var details = await conn.QueryFirstOrDefaultAsync<(string Tema, string Interprete, int? IdAlbum, bool TieneImg)>(sql, new { id = c.Id });
            
            if (details.Tema != null)
            {
                topCanciones.Add(new ItemTop
                {
                    Id = $"{c.Tipo}_{c.Id}",
                    IdReferencia = c.Id,
                    Tipo = c.Tipo,
                    Nombre = details.Tema,
                    Subtitulo = details.Interprete,
                    Conteo = c.Conteo,
                    TieneImagen = details.TieneImg,
                    IdExtra = details.IdAlbum
                });
            }
        }

        // 2. Top 5 Artistas
        var topArtistas = await conn.QueryAsync<ItemTop>("""
            SELECT 
                CAST(i.id AS TEXT) as Id, 
                i.id as IdReferencia,
                i.nombre as Nombre, 
                COUNT(*) as Conteo,
                (i.foto_blob IS NOT NULL) as TieneImagen
            FROM reproducciones_historial h
            JOIN (
                SELECT id, id_interprete, 'cassette' as tipo FROM temas 
                UNION ALL 
                SELECT id, id_interprete, 'cd' as tipo FROM temas_cd
            ) t ON h.id_cancion = t.id AND h.tipo_medio = t.tipo
            JOIN interpretes i ON t.id_interprete = i.id
            GROUP BY i.id
            ORDER BY Conteo DESC
            LIMIT 5
            """);

        // 3. Tiempo Total Escuchado (Real, de la columna segundos_reproducidos)
        // Para registros viejos o sin duración, asumimos 0 (o podríamos asumir un promedio si quisiéramos)
        var totalSegundos = await conn.ExecuteScalarAsync<long?>("""
            SELECT SUM(segundos_reproducidos) FROM reproducciones_historial
            """);
        
        long segundosReales = totalSegundos ?? 0;

        // 4. Actividad últimos 7 días
        var diasRaw = await conn.QueryAsync<(string Fecha, int Total)>("""
            SELECT date(fecha) as f, count(*) as c
            FROM reproducciones_historial
            WHERE fecha >= date('now', 'localtime', '-6 days')
            GROUP BY date(fecha)
            ORDER BY f
            """);

        // Rellenar ceros para los 7 días
        var ultimos7Dias = new List<int>();
        for (int i = 6; i >= 0; i--)
        {
            var fechaTarget = DateTime.Now.AddDays(-i).ToString("yyyy-MM-dd");
            var dato = diasRaw.FirstOrDefault(d => d.Fecha == fechaTarget);
            ultimos7Dias.Add(dato.Total); // 0 si no existe (default struct)
        }

        // Formatear tiempo total
        var tiempoSpan = TimeSpan.FromSeconds(segundosReales);
        string tiempoTexto;
        
        if (tiempoSpan.TotalDays >= 1)
        {
            int dias = (int)tiempoSpan.TotalDays;
            int horas = tiempoSpan.Hours;
            tiempoTexto = horas > 0 ? $"{dias} d {horas} h" : $"{dias} d";
        }
        else if (tiempoSpan.TotalHours >= 1)
        {
            int horas = (int)tiempoSpan.TotalHours;
            int minutos = tiempoSpan.Minutes;
            tiempoTexto = minutos > 0 ? $"{horas} h {minutos} min" : $"{horas} h";
        }
        else
        {
            tiempoTexto = $"{tiempoSpan.Minutes} min";
        }

        return new EstadisticasUsuario
        {
            TopCanciones = topCanciones,
            TopArtistas = topArtistas.ToList(),
            TiempoTotalEscuchado = tiempoTexto,
            ReproduccionesUltimos7Dias = ultimos7Dias
        };
    }
}



