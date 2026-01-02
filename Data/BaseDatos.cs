using Microsoft.Data.Sqlite;
using Dapper;

namespace MusicaCatalogo.Data;

/// <summary>
/// Gestión de la base de datos SQLite: conexión, creación de esquema e índices.
/// </summary>
public class BaseDatos
{
    private readonly string _connectionString;
    private readonly string _rutaDb;

    public BaseDatos(string rutaDb)
    {
        _rutaDb = rutaDb;
        _connectionString = $"Data Source={rutaDb}";
    }

    public SqliteConnection ObtenerConexion()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public async Task<bool> ProbarConexionAsync()
    {
        try
        {
            using var conn = ObtenerConexion();
            await conn.ExecuteAsync("SELECT 1");
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Error] No se pudo conectar a SQLite: {ex.Message}");
            return false;
        }
    }

    public bool BaseDatosExiste()
    {
        return File.Exists(_rutaDb);
    }

    public void CrearEsquema()
    {
        using var conn = ObtenerConexion();
        
        // Ejecutar cada statement por separado
        var statements = EsquemaSQL.Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries)
            .Where(s => !string.IsNullOrWhiteSpace(s) && !s.Trim().StartsWith("--"))
            .ToList();

        foreach (var stmt in statements)
        {
            var sql = stmt.Trim();
            if (!string.IsNullOrEmpty(sql))
            {
                try
                {
                    conn.Execute(sql);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Advertencia] Error en SQL: {ex.Message}");
                }
            }
        }
        
        Console.WriteLine("[BaseDatos] Esquema SQLite creado correctamente.");
    }

    private const string EsquemaSQL = """
        CREATE TABLE IF NOT EXISTS ecualizador (
            id_ecual INTEGER PRIMARY KEY,
            nombre TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS formato (
            id_formato INTEGER PRIMARY KEY,
            nombre TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS fuente (
            id_fuente INTEGER PRIMARY KEY,
            nombre TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS grabador (
            id_deck INTEGER PRIMARY KEY,
            nombre TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS marca (
            id_marca INTEGER PRIMARY KEY,
            nombre TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS bias (
            id_bias INTEGER PRIMARY KEY,
            nombre TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS modo (
            id_modo INTEGER PRIMARY KEY,
            nombre TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS supresor (
            id_dolby INTEGER PRIMARY KEY,
            nombre TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS interpretes (
            id INTEGER PRIMARY KEY,
            nombre TEXT NOT NULL,
            foto INTEGER
        );

        CREATE TABLE IF NOT EXISTS formato_grabado (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            num_formato TEXT NOT NULL UNIQUE,
            id_formato INTEGER NOT NULL DEFAULT 1,
            id_marca INTEGER NOT NULL,
            id_deck INTEGER NOT NULL,
            id_ecual INTEGER NOT NULL,
            id_dolby INTEGER NOT NULL,
            id_bias INTEGER NOT NULL,
            id_modo INTEGER NOT NULL,
            id_fuente INTEGER NOT NULL,
            fecha_inicio TEXT,
            fecha_termino TEXT
        );

        CREATE TABLE IF NOT EXISTS formato_grabado_cd (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            num_formato TEXT NOT NULL UNIQUE,
            id_formato INTEGER NOT NULL DEFAULT 2,
            id_marca INTEGER NOT NULL,
            id_deck INTEGER NOT NULL,
            id_fuente INTEGER NOT NULL,
            fecha_grabacion TEXT
        );

        CREATE TABLE IF NOT EXISTS temas (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            num_formato TEXT NOT NULL,
            id_interprete INTEGER NOT NULL,
            tema TEXT NOT NULL,
            lado TEXT NOT NULL,
            desde INTEGER NOT NULL,
            hasta INTEGER NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_temas_formato ON temas(num_formato);
        CREATE INDEX IF NOT EXISTS idx_temas_interprete ON temas(id_interprete);

        CREATE TABLE IF NOT EXISTS temas_cd (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            num_formato TEXT NOT NULL,
            id_interprete INTEGER NOT NULL,
            tema TEXT NOT NULL,
            ubicacion INTEGER NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_temascd_formato ON temas_cd(num_formato);
        CREATE INDEX IF NOT EXISTS idx_temascd_interprete ON temas_cd(id_interprete);

        CREATE TABLE IF NOT EXISTS importacion_metadata (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            archivo TEXT NOT NULL,
            fecha_importacion TEXT NOT NULL,
            registros_importados INTEGER NOT NULL,
            hash_archivo TEXT
        )
        """;

    /// <summary>
    /// Inicializa la conexión a SQLite y crea el esquema si es necesario.
    /// </summary>
    public async Task InicializarAsync()
    {
        // Probar conexión
        if (!await ProbarConexionAsync())
        {
            throw new Exception("No se pudo conectar a la base de datos SQLite.");
        }

        // Crear esquema si no existe
        CrearEsquema();
    }
}
