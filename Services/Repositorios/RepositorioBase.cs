using Dapper;
using MusicaCatalogo.Data;
using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Text;

namespace MusicaCatalogo.Services.Repositorios;

/// <summary>
/// Clase base con funcionalidades comunes para todos los repositorios.
/// </summary>
public abstract class RepositorioBase
{
    protected readonly BaseDatos _db;

    protected RepositorioBase(BaseDatos db)
    {
        _db = db;
    }

    /// <summary>
    /// Obtiene una conexión a la base de datos.
    /// </summary>
    protected SqliteConnection ObtenerConexion() => _db.ObtenerConexion();

    /// <summary>
    /// Normaliza texto removiendo tildes y convirtiendo a minúsculas.
    /// </summary>
    protected static string NormalizarTexto(string texto)
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
    /// Resuelve un ID desde nombre. Si no existe, crea el registro.
    /// </summary>
    protected async Task<int> ResolverIdAsync(SqliteConnection conn, string tabla, string columnaId, int? id, string? nombre)
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

    /// <summary>
    /// Determina si un número de medio corresponde a un cassette.
    /// </summary>
    protected static bool EsCassette(string numMedio)
    {
        return !string.IsNullOrEmpty(numMedio) && 
               (numMedio.StartsWith("N", StringComparison.OrdinalIgnoreCase) || 
                numMedio.StartsWith("C", StringComparison.OrdinalIgnoreCase));
    }
}
