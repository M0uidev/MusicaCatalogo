using Dapper;
using MusicaCatalogo.Data;
using MusicaCatalogo.Data.Entidades;
using Microsoft.Data.Sqlite;
using System.Data;
using System.Globalization;
using System.Text;

namespace MusicaCatalogo.Services;

/// <summary>
/// Repositorio para consultas a la base de datos SQLite de música.
/// </summary>
public class RepositorioMusica
{
    private readonly BaseDatos _db;

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
            SELECT t.id AS Id, 'cassette' AS Tipo, t.num_formato AS numMedio, t.tema AS Tema, 
                   i.nombre AS Interprete, (t.lado || ':' || t.desde || '-' || t.hasta) AS Posicion
            FROM temas t
            JOIN interpretes i ON t.id_interprete = i.id
            WHERE t.tema LIKE @patron OR i.nombre LIKE @patron OR t.num_formato LIKE @patron
            UNION ALL
            SELECT t.id AS Id, 'cd' AS Tipo, t.num_formato AS numMedio, t.tema AS Tema,
                   i.nombre AS Interprete, CAST(t.ubicacion AS TEXT) AS Posicion
            FROM temas_cd t
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
            SELECT 'cassette' AS Tipo, num_formato AS numMedio, tema AS Tema,
                   (lado || ':' || desde || '-' || hasta) AS Posicion
            FROM temas WHERE id_interprete = @id
            ORDER BY num_formato, lado, desde
            """, new { id = interprete.Id });

        var temasCd = await conn.QueryAsync<TemaDeInterprete>("""
            SELECT 'cd' AS Tipo, num_formato AS numMedio, tema AS Tema,
                   CAST(ubicacion AS TEXT) AS Posicion
            FROM temas_cd WHERE id_interprete = @id
            ORDER BY num_formato, ubicacion
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
    /// </summary>
    public async Task<List<InterpreteResumen>> ListarInterpretesAsync(string? filtro = null, int limite = 100)
    {
        using var conn = _db.ObtenerConexion();

        var sql = """
            SELECT i.id AS Id, i.nombre AS Interprete, 
                   (SELECT COUNT(*) FROM temas WHERE id_interprete = i.id) +
                   (SELECT COUNT(*) FROM temas_cd WHERE id_interprete = i.id) AS TotalTemas
            FROM interpretes i
            """;

        if (!string.IsNullOrWhiteSpace(filtro))
        {
            sql += " WHERE i.nombre LIKE @patron";
        }

        sql += " ORDER BY i.nombre LIMIT @limite";

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

    // ============================================
    // CRUD - ÁLBUMES
    // ============================================

    /// <summary>Verifica si la columna es_single existe en albumes.</summary>
    private async Task<bool> ExisteColumnaEsSingle(IDbConnection conn)
    {
        var columnas = await conn.QueryAsync<string>("SELECT name FROM pragma_table_info('albumes')");
        return columnas.Contains("es_single");
    }

    /// <summary>Lista todos los álbumes.</summary>
    public async Task<List<AlbumResumen>> ListarAlbumesAsync(string? filtro = null, int limite = 100)
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

    /// <summary>Obtiene la portada de una canción (propia o heredada del álbum).</summary>
    public async Task<byte[]?> ObtenerPortadaCancionAsync(int id, string tipo)
    {
        using var conn = _db.ObtenerConexion();

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

    // ============================================
    // ASIGNACIÓN DE CANCIONES A ÁLBUMES
    // ============================================

    /// <summary>Obtiene todas las canciones para mostrar en la galería.</summary>
    public async Task<List<CancionGaleria>> ObtenerTodasCancionesAsync()
    {
        using var conn = _db.ObtenerConexion();
        
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

        var sql = $"{sqlCassette} UNION ALL {sqlCd} ORDER BY Tema";

        var resultado = await conn.QueryAsync<CancionGaleria>(sql);
        return resultado.ToList();
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
    public async Task<List<NotificacionDatos>> ObtenerNotificacionesAsync()
    {
        using var conn = _db.ObtenerConexion();
        var notificaciones = new List<NotificacionDatos>();
        int contador = 0;

        // 1. Canciones sin intérprete válido (cassettes)
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
                Mensaje = $"Canción '{c.Tema}' sin intérprete asignado",
                EntidadId = c.Id.ToString(),
                EntidadTipo = "cassette",
                CampoFaltante = "interprete",
                UrlArreglar = $"formato.html?num={c.numMedio}&cancion={c.Id}&tipo=cassette"
            });
        }

        // 2. Canciones sin intérprete válido (CDs)
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
                Mensaje = $"Canción '{c.Tema}' sin intérprete asignado",
                EntidadId = c.Id.ToString(),
                EntidadTipo = "cd",
                CampoFaltante = "interprete",
                UrlArreglar = $"formato.html?num={c.numMedio}&cancion={c.Id}&tipo=cd"
            });
        }

        // 3. Canciones con nombre vacío
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

        // 4. Álbumes sin portada
        var albumesSinPortada = await conn.QueryAsync<(int Id, string Nombre)>("""
            SELECT id, nombre FROM albumes WHERE portada IS NULL
            """);
        foreach (var a in albumesSinPortada)
        {
            notificaciones.Add(new NotificacionDatos
            {
                Id = $"notif-{++contador}",
                Tipo = "album",
                Severidad = "warning",
                Mensaje = $"Álbum '{a.Nombre}' sin portada",
                EntidadId = a.Id.ToString(),
                EntidadTipo = "album",
                CampoFaltante = "portada",
                UrlArreglar = $"albumes.html?id={a.Id}"
            });
        }

        // 5. Álbumes sin canciones
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
                Severidad = "info",
                Mensaje = $"Álbum '{a.Nombre}' sin canciones asignadas",
                EntidadId = a.Id.ToString(),
                EntidadTipo = "album",
                CampoFaltante = "canciones",
                UrlArreglar = $"albumes.html?id={a.Id}"
            });
        }

        // 6. Formatos (cassettes) sin marca
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

        // 7. Intérpretes sin nombre
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

        // 8. Grupos de duplicados multi-artista sin artista original definido
        try
        {
            var duplicados = await ObtenerDuplicadosAsync("multiartista");
            foreach (var grupo in duplicados)
            {
                // Verificar si hay al menos una canción marcada como NO cover (es decir, original)
                var tieneOriginal = grupo.Canciones.Any(c => !c.EsCover);
                
                if (!tieneOriginal && grupo.TotalArtistas > 1)
                {
                    // Obtener opciones de artista, el más antiguo por defecto
                    var artistasPorAntiguedad = grupo.Canciones
                        .GroupBy(c => new { c.Interprete, c.IdInterprete })
                        .Select((g, index) => new OpcionArtistaOriginal
                        {
                            IdInterprete = g.Key.IdInterprete,
                            Nombre = g.Key.Interprete,
                            CantidadCopias = g.Count(),
                            EsMasAntiguo = index == 0 // El primero es el más antiguo (ordenado por ID)
                        })
                        .ToList();

                    notificaciones.Add(new NotificacionDatos
                    {
                        Id = $"notif-{++contador}",
                        Tipo = "duplicado",
                        Severidad = "warning",
                        Mensaje = $"'{grupo.Canciones.First().Tema}' tiene {grupo.TotalArtistas} artistas - Seleccionar original",
                        EntidadId = grupo.Id,
                        EntidadTipo = "grupo",
                        GrupoId = grupo.Id,
                        UrlArreglar = $"perfil-cancion.html?grupo={grupo.Id}",
                        OpcionesArtista = artistasPorAntiguedad
                    });
                }
            }
        }
        catch { /* Ignorar errores al obtener duplicados */ }

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
                t.artista_original AS ArtistaOriginal
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
                t.artista_original AS ArtistaOriginal
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
                ArtistaOriginal = (string?)t.ArtistaOriginal
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
                ArtistaOriginal = (string?)t.ArtistaOriginal
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
                    ArtistaOriginal = c.ArtistaOriginal
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
                t.link_externo AS LinkExterno
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
                t.link_externo AS LinkExterno
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
                    LinkExterno = (string?)t.LinkExterno
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
                    LinkExterno = (string?)t.LinkExterno
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
}
