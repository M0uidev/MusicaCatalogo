using Dapper;
using MusicaCatalogo.Data;
using MusicaCatalogo.Data.Entidades;
using Microsoft.Data.Sqlite;
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
            SELECT 'cassette' AS Tipo, t.num_formato AS NumFormato, t.tema AS Tema, 
                   i.nombre AS Interprete, (t.lado || ':' || t.desde || '-' || t.hasta) AS Posicion
            FROM temas t
            JOIN interpretes i ON t.id_interprete = i.id
            WHERE t.tema LIKE @patron OR i.nombre LIKE @patron OR t.num_formato LIKE @patron
            UNION ALL
            SELECT 'cd' AS Tipo, t.num_formato AS NumFormato, t.tema AS Tema,
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
    public async Task<List<SugerenciaTema>> AutocompletarTemasAsync(string consulta, int limite = 15)
    {
        if (string.IsNullOrWhiteSpace(consulta) || consulta.Length < 2)
            return new List<SugerenciaTema>();

        using var conn = _db.ObtenerConexion();
        var consultaNorm = NormalizarTexto(consulta);

        // Obtener todos los temas y filtrar en memoria para búsqueda sin tildes
        var temasCassette = await conn.QueryAsync<(string Tema, string Interprete, string NumFormato, string Lado, int Desde, int Hasta)>("""
            SELECT t.tema, i.nombre, t.num_formato, t.lado, t.desde, t.hasta
            FROM temas t
            JOIN interpretes i ON t.id_interprete = i.id
            """);

        var temasCd = await conn.QueryAsync<(string Tema, string Interprete, string NumFormato, int Ubicacion)>("""
            SELECT t.tema, i.nombre, t.num_formato, t.ubicacion
            FROM temas_cd t
            JOIN interpretes i ON t.id_interprete = i.id
            """);

        var resultados = new List<SugerenciaTema>();

        // Buscar en cassettes
        foreach (var t in temasCassette)
        {
            var temaNorm = NormalizarTexto(t.Tema);
            var interpNorm = NormalizarTexto(t.Interprete);
            if (temaNorm.Contains(consultaNorm) || interpNorm.Contains(consultaNorm))
            {
                resultados.Add(new SugerenciaTema
                {
                    Tema = t.Tema,
                    Interprete = t.Interprete,
                    NumFormato = t.NumFormato,
                    Tipo = "cassette",
                    Ubicacion = $"{t.Lado}:{t.Desde}-{t.Hasta}"
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
                resultados.Add(new SugerenciaTema
                {
                    Tema = t.Tema,
                    Interprete = t.Interprete,
                    NumFormato = t.NumFormato,
                    Tipo = "cd",
                    Ubicacion = $"Track {t.Ubicacion}"
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
    public async Task<DetalleFormato?> ObtenerFormatoAsync(string numFormato)
    {
        using var conn = _db.ObtenerConexion();

        // Primero buscar en cassettes
        var cassette = await conn.QueryFirstOrDefaultAsync<DetalleFormato>("""
            SELECT fg.num_formato AS NumFormato, f.nombre AS TipoFormato, m.nombre AS Marca,
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
            WHERE fg.num_formato = @numFormato
            """, new { numFormato });

        if (cassette != null)
            return cassette;

        // Buscar en CDs
        var cd = await conn.QueryFirstOrDefaultAsync<DetalleFormato>("""
            SELECT fg.num_formato AS NumFormato, f.nombre AS TipoFormato, m.nombre AS Marca,
                   g.nombre AS Grabador, fu.nombre AS Fuente, fg.fecha_grabacion AS FechaInicio,
                   NULL AS FechaTermino, NULL AS Ecualizador, NULL AS Supresor,
                   NULL AS Bias, NULL AS Modo,
                   (SELECT COUNT(*) FROM temas_cd WHERE num_formato = fg.num_formato) AS TotalTemas
            FROM formato_grabado_cd fg
            LEFT JOIN formato f ON fg.id_formato = f.id_formato
            LEFT JOIN marca m ON fg.id_marca = m.id_marca
            LEFT JOIN grabador g ON fg.id_deck = g.id_deck
            LEFT JOIN fuente fu ON fg.id_fuente = fu.id_fuente
            WHERE fg.num_formato = @numFormato
            """, new { numFormato });

        return cd;
    }

    /// <summary>
    /// Obtiene los temas de un formato específico.
    /// </summary>
    public async Task<List<TemaEnFormato>> ObtenerTemasDeFormatoAsync(string numFormato)
    {
        using var conn = _db.ObtenerConexion();

        // Verificar si es cassette
        var esCassette = await conn.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM formato_grabado WHERE num_formato = @numFormato",
            new { numFormato }) > 0;

        if (esCassette)
        {
            var temas = await conn.QueryAsync<TemaEnFormato>("""
                SELECT t.tema AS Tema, i.nombre AS Interprete, t.id_interprete AS IdInterprete,
                       t.lado AS Lado, t.desde AS Desde, t.hasta AS Hasta, NULL AS Ubicacion
                FROM temas t
                JOIN interpretes i ON t.id_interprete = i.id
                WHERE t.num_formato = @numFormato
                ORDER BY t.lado, t.desde
                """, new { numFormato });
            return temas.ToList();
        }

        // Es CD
        var temasCd = await conn.QueryAsync<TemaEnFormato>("""
            SELECT t.tema AS Tema, i.nombre AS Interprete, t.id_interprete AS IdInterprete,
                   NULL AS Lado, NULL AS Desde, NULL AS Hasta, t.ubicacion AS Ubicacion
            FROM temas_cd t
            JOIN interpretes i ON t.id_interprete = i.id
            WHERE t.num_formato = @numFormato
            ORDER BY t.ubicacion
            """, new { numFormato });
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
            SELECT 'cassette' AS Tipo, num_formato AS NumFormato, tema AS Tema,
                   (lado || ':' || desde || '-' || hasta) AS Posicion
            FROM temas WHERE id_interprete = @id
            ORDER BY num_formato, lado, desde
            """, new { id = interprete.Id });

        var temasCd = await conn.QueryAsync<TemaDeInterprete>("""
            SELECT 'cd' AS Tipo, num_formato AS NumFormato, tema AS Tema,
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
        var conteoFormatos = await conn.QueryAsync<ConteoFormato>("""
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
            ConteosPorFormato = conteoFormatos.ToList(),
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
    public async Task<List<DetalleFormato>> ListarFormatosAsync(string? tipo = null, int limite = 100)
    {
        using var conn = _db.ObtenerConexion();

        var resultados = new List<DetalleFormato>();

        if (tipo == null || tipo.ToLower() == "cassette")
        {
            var cassettes = await conn.QueryAsync<DetalleFormato>("""
                SELECT fg.num_formato AS NumFormato, f.nombre AS TipoFormato, m.nombre AS Marca,
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
            var cds = await conn.QueryAsync<DetalleFormato>("""
                SELECT fg.num_formato AS NumFormato, f.nombre AS TipoFormato, m.nombre AS Marca,
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
    public async Task<CrudResponse> CrearFormatoAsync(FormatoRequest request)
    {
        using var conn = _db.ObtenerConexion();

        try
        {
            // Verificar que no exista
            var existe = await conn.QueryFirstOrDefaultAsync<int>(
                "SELECT COUNT(*) FROM formato_grabado WHERE num_formato = @NumFormato",
                new { request.NumFormato });
            if (existe > 0)
            {
                existe = await conn.QueryFirstOrDefaultAsync<int>(
                    "SELECT COUNT(*) FROM formato_grabado_cd WHERE num_formato = @NumFormato",
                    new { request.NumFormato });
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

            if (request.TipoFormato.ToLower() == "cassette")
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
                    VALUES (@NumFormato, 1, @idMarca, @idDeck, @idEcual, @idDolby, @idBias, @idModo, @idFuente, @FechaInicio, @FechaTermino)
                    """, new
                {
                    request.NumFormato,
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
                    VALUES (@NumFormato, 2, @idMarca, @idDeck, @idFuente, @FechaInicio)
                    """, new
                {
                    request.NumFormato,
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
    public async Task<CrudResponse> ActualizarFormatoAsync(string numFormato, FormatoRequest request)
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

            if (request.TipoFormato.ToLower() == "cassette")
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
                    WHERE num_formato = @numFormato
                    """, new
                {
                    numFormato,
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
                    WHERE num_formato = @numFormato
                    """, new
                {
                    numFormato,
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
    public async Task<CrudResponse> EliminarFormatoAsync(string numFormato)
    {
        using var conn = _db.ObtenerConexion();

        try
        {
            // Eliminar canciones primero
            await conn.ExecuteAsync("DELETE FROM temas WHERE num_formato = @numFormato", new { numFormato });
            await conn.ExecuteAsync("DELETE FROM temas_cd WHERE num_formato = @numFormato", new { numFormato });

            // Eliminar formato
            var rows = await conn.ExecuteAsync("DELETE FROM formato_grabado WHERE num_formato = @numFormato", new { numFormato });
            if (rows == 0)
                rows = await conn.ExecuteAsync("DELETE FROM formato_grabado_cd WHERE num_formato = @numFormato", new { numFormato });

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
    public async Task<List<TemaConId>> ObtenerTemasConIdAsync(string numFormato)
    {
        using var conn = _db.ObtenerConexion();

        // Verificar si es cassette
        var esCassette = await conn.QueryFirstOrDefaultAsync<int>(
            "SELECT COUNT(*) FROM formato_grabado WHERE num_formato = @numFormato",
            new { numFormato }) > 0;

        if (esCassette)
        {
            var temas = await conn.QueryAsync<TemaConId>("""
                SELECT t.id AS Id, t.tema AS Tema, i.nombre AS Interprete, t.id_interprete AS IdInterprete,
                       t.lado AS Lado, t.desde AS Desde, t.hasta AS Hasta, NULL AS Ubicacion
                FROM temas t
                JOIN interpretes i ON t.id_interprete = i.id
                WHERE t.num_formato = @numFormato
                ORDER BY t.lado, t.desde
                """, new { numFormato });
            return temas.ToList();
        }

        // Es CD
        var temasCd = await conn.QueryAsync<TemaConId>("""
            SELECT t.id AS Id, t.tema AS Tema, i.nombre AS Interprete, t.id_interprete AS IdInterprete,
                   NULL AS Lado, NULL AS Desde, NULL AS Hasta, t.ubicacion AS Ubicacion
            FROM temas_cd t
            JOIN interpretes i ON t.id_interprete = i.id
            WHERE t.num_formato = @numFormato
            ORDER BY t.ubicacion
            """, new { numFormato });
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
            if (request.TipoFormato.ToLower() == "cassette")
            {
                await conn.ExecuteAsync("""
                    INSERT INTO temas (num_formato, id_interprete, tema, lado, desde, hasta)
                    VALUES (@NumFormato, @idInterprete, @Tema, @Lado, @Desde, @Hasta)
                    """, new
                {
                    request.NumFormato,
                    idInterprete,
                    request.Tema,
                    Lado = request.Lado ?? "A",
                    Desde = request.Desde ?? 1,
                    Hasta = request.Hasta ?? 1
                });
                idCreado = await conn.QueryFirstAsync<long>("SELECT last_insert_rowid()");
            }
            else
            {
                // Obtener próxima ubicación
                var maxUbicacion = await conn.QueryFirstOrDefaultAsync<int?>(
                    "SELECT MAX(ubicacion) FROM temas_cd WHERE num_formato = @NumFormato",
                    new { request.NumFormato }) ?? 0;

                await conn.ExecuteAsync("""
                    INSERT INTO temas_cd (num_formato, id_interprete, tema, ubicacion)
                    VALUES (@NumFormato, @idInterprete, @Tema, @ubicacion)
                    """, new
                {
                    request.NumFormato,
                    idInterprete,
                    request.Tema,
                    ubicacion = request.Ubicacion ?? (maxUbicacion + 1)
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
            if (request.TipoFormato.ToLower() == "cassette")
            {
                rows = await conn.ExecuteAsync("""
                    UPDATE temas SET id_interprete = @idInterprete, tema = @Tema, lado = @Lado, desde = @Desde, hasta = @Hasta
                    WHERE id = @id
                    """, new
                {
                    id,
                    idInterprete,
                    request.Tema,
                    Lado = request.Lado ?? "A",
                    Desde = request.Desde ?? 1,
                    Hasta = request.Hasta ?? 1
                });
            }
            else
            {
                rows = await conn.ExecuteAsync("""
                    UPDATE temas_cd SET id_interprete = @idInterprete, tema = @Tema, ubicacion = @Ubicacion
                    WHERE id = @id
                    """, new
                {
                    id,
                    idInterprete,
                    request.Tema,
                    Ubicacion = request.Ubicacion ?? 1
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
    public async Task<CrudResponse> EliminarCancionAsync(int id, string tipoFormato)
    {
        using var conn = _db.ObtenerConexion();

        try
        {
            int rows;
            if (tipoFormato.ToLower() == "cassette")
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
            if (request.TipoFormato.ToLower() == "cd")
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
}
