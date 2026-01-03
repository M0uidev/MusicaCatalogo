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

        CREATE TABLE IF NOT EXISTS albumes (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            nombre TEXT NOT NULL,
            id_interprete INTEGER,
            anio TEXT,
            portada BLOB,
            fecha_creacion TEXT DEFAULT CURRENT_TIMESTAMP
        );

        CREATE INDEX IF NOT EXISTS idx_albumes_interprete ON albumes(id_interprete);
        CREATE INDEX IF NOT EXISTS idx_albumes_nombre ON albumes(nombre);

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
            hasta INTEGER NOT NULL,
            id_album INTEGER,
            link_externo TEXT,
            portada BLOB
        );

        CREATE INDEX IF NOT EXISTS idx_temas_formato ON temas(num_formato);
        CREATE INDEX IF NOT EXISTS idx_temas_interprete ON temas(id_interprete);
        CREATE INDEX IF NOT EXISTS idx_temas_album ON temas(id_album);

        CREATE TABLE IF NOT EXISTS temas_cd (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            num_formato TEXT NOT NULL,
            id_interprete INTEGER NOT NULL,
            tema TEXT NOT NULL,
            ubicacion INTEGER NOT NULL,
            id_album INTEGER,
            link_externo TEXT,
            portada BLOB
        );

        CREATE INDEX IF NOT EXISTS idx_temascd_formato ON temas_cd(num_formato);
        CREATE INDEX IF NOT EXISTS idx_temascd_interprete ON temas_cd(id_interprete);
        CREATE INDEX IF NOT EXISTS idx_temascd_album ON temas_cd(id_album);

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
        
        // Migrar columnas nuevas si es necesario
        await MigrarEsquemaAsync();
    }

    /// <summary>
    /// Agrega columnas nuevas a tablas existentes si no existen.
    /// </summary>
    private async Task MigrarEsquemaAsync()
    {
        using var conn = ObtenerConexion();
        
        // Verificar y agregar columnas a temas
        var columnasTemas = await conn.QueryAsync<string>(
            "SELECT name FROM pragma_table_info('temas')");
        var listaColumnasTemas = columnasTemas.ToList();
        
        if (!listaColumnasTemas.Contains("id_album"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas ADD COLUMN id_album INTEGER");
            Console.WriteLine("[BaseDatos] Columna id_album agregada a temas");
        }
        if (!listaColumnasTemas.Contains("link_externo"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas ADD COLUMN link_externo TEXT");
            Console.WriteLine("[BaseDatos] Columna link_externo agregada a temas");
        }
        if (!listaColumnasTemas.Contains("portada"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas ADD COLUMN portada BLOB");
            Console.WriteLine("[BaseDatos] Columna portada agregada a temas");
        }
        
        // Verificar y agregar columnas a temas_cd
        var columnasTemasCd = await conn.QueryAsync<string>(
            "SELECT name FROM pragma_table_info('temas_cd')");
        var listaColumnasTemasCd = columnasTemasCd.ToList();
        
        if (!listaColumnasTemasCd.Contains("id_album"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas_cd ADD COLUMN id_album INTEGER");
            Console.WriteLine("[BaseDatos] Columna id_album agregada a temas_cd");
        }
        if (!listaColumnasTemasCd.Contains("link_externo"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas_cd ADD COLUMN link_externo TEXT");
            Console.WriteLine("[BaseDatos] Columna link_externo agregada a temas_cd");
        }
        if (!listaColumnasTemasCd.Contains("portada"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas_cd ADD COLUMN portada BLOB");
            Console.WriteLine("[BaseDatos] Columna portada agregada a temas_cd");
        }
        
        // Crear índices de álbum si no existen
        try
        {
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_temas_album ON temas(id_album)");
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_temascd_album ON temas_cd(id_album)");
        }
        catch { /* Índice ya existe */ }

        // Agregar columna es_single a albumes
        var columnasAlbumes = await conn.QueryAsync<string>(
            "SELECT name FROM pragma_table_info('albumes')");
        var listaColumnasAlbumes = columnasAlbumes.ToList();
        
        if (!listaColumnasAlbumes.Contains("es_single"))
        {
            await conn.ExecuteAsync("ALTER TABLE albumes ADD COLUMN es_single INTEGER DEFAULT 0");
            Console.WriteLine("[BaseDatos] Columna es_single agregada a albumes");
        }
        
        // Agregar columnas de cover a temas
        if (!listaColumnasTemas.Contains("es_cover"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas ADD COLUMN es_cover INTEGER DEFAULT 0");
            Console.WriteLine("[BaseDatos] Columna es_cover agregada a temas");
        }
        if (!listaColumnasTemas.Contains("artista_original"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas ADD COLUMN artista_original TEXT");
            Console.WriteLine("[BaseDatos] Columna artista_original agregada a temas");
        }
        if (!listaColumnasTemas.Contains("es_original"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas ADD COLUMN es_original INTEGER DEFAULT 0");
            // Marcar como original las canciones que ya tienen álbum
            await conn.ExecuteAsync("UPDATE temas SET es_original = 1 WHERE id_album IS NOT NULL");
            Console.WriteLine("[BaseDatos] Columna es_original agregada a temas");
        }
        
        // Agregar columnas de cover a temas_cd
        if (!listaColumnasTemasCd.Contains("es_cover"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas_cd ADD COLUMN es_cover INTEGER DEFAULT 0");
            Console.WriteLine("[BaseDatos] Columna es_cover agregada a temas_cd");
        }
        if (!listaColumnasTemasCd.Contains("artista_original"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas_cd ADD COLUMN artista_original TEXT");
            Console.WriteLine("[BaseDatos] Columna artista_original agregada a temas_cd");
        }
        if (!listaColumnasTemasCd.Contains("es_original"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas_cd ADD COLUMN es_original INTEGER DEFAULT 0");
            // Marcar como original las canciones que ya tienen álbum
            await conn.ExecuteAsync("UPDATE temas_cd SET es_original = 1 WHERE id_album IS NOT NULL");
            Console.WriteLine("[BaseDatos] Columna es_original agregada a temas_cd");
        }
    }
}
