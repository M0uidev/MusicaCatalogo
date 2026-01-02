using Dapper;
using MusicaCatalogo.Data;
using MusicaCatalogo.Data.Entidades;

namespace MusicaCatalogo.Services.Repositorios;

/// <summary>
/// Repositorio para operaciones con intérpretes/artistas.
/// </summary>
public class RepositorioInterpretes : RepositorioBase
{
    public RepositorioInterpretes(BaseDatos db) : base(db) { }

    /// <summary>
    /// Obtiene lista de intérpretes con búsqueda opcional.
    /// </summary>
    public async Task<List<InterpreteResumen>> ListarInterpretesAsync(string? filtro = null, int limite = 100)
    {
        using var conn = ObtenerConexion();

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
    /// Obtiene el detalle de un intérprete con todos sus temas.
    /// </summary>
    public async Task<DetalleInterprete?> ObtenerInterpreteAsync(string nombre)
    {
        using var conn = ObtenerConexion();

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

    /// <summary>Crea un nuevo intérprete.</summary>
    public async Task<CrudResponse> CrearInterpreteAsync(string nombre)
    {
        using var conn = ObtenerConexion();

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

    /// <summary>
    /// Busca artistas que tienen una canción con el mismo nombre (para sugerir artista original en covers).
    /// </summary>
    public async Task<List<ArtistaParaCover>> BuscarArtistasParaCoverAsync(string tema, int? excluirIdInterprete)
    {
        using var conn = ObtenerConexion();

        // Buscar artistas que tienen la misma canción
        var artistas = await conn.QueryAsync<ArtistaParaCover>("""
            SELECT DISTINCT i.id AS IdInterprete, i.nombre AS NombreArtista,
                   (SELECT COUNT(*) FROM temas WHERE id_interprete = i.id) + 
                   (SELECT COUNT(*) FROM temas_cd WHERE id_interprete = i.id) AS TotalCanciones
            FROM interpretes i
            WHERE i.id IN (
                SELECT DISTINCT id_interprete FROM temas WHERE LOWER(tema) = LOWER(@tema)
                UNION
                SELECT DISTINCT id_interprete FROM temas_cd WHERE LOWER(tema) = LOWER(@tema)
            )
            AND (@excluirId IS NULL OR i.id != @excluirId)
            ORDER BY TotalCanciones DESC
            """, new { tema, excluirId = excluirIdInterprete });

        return artistas.ToList();
    }
}
