using Dapper;
using MusicaCatalogo.Data;
using MusicaCatalogo.Data.Entidades;

namespace MusicaCatalogo.Services.Repositorios;

/// <summary>
/// Repositorio para operaciones de búsqueda y autocompletado.
/// </summary>
public class RepositorioBusqueda : RepositorioBase
{
    public RepositorioBusqueda(BaseDatos db) : base(db) { }

    /// <summary>
    /// Búsqueda global por nombre de tema, intérprete o número de formato.
    /// </summary>
    public async Task<List<ResultadoBusqueda>> BuscarAsync(string consulta, int limite = 50)
    {
        if (string.IsNullOrWhiteSpace(consulta))
            return new List<ResultadoBusqueda>();

        using var conn = ObtenerConexion();
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

        using var conn = ObtenerConexion();
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
}
