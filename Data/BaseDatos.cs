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

        CREATE TABLE IF NOT EXISTS composiciones (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            titulo_canonico TEXT NOT NULL,
            compositor TEXT,
            anio_original TEXT,
            notas TEXT,
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
        
        // Agregar columnas de audio a temas
        if (!listaColumnasTemas.Contains("archivo_audio"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas ADD COLUMN archivo_audio TEXT");
            Console.WriteLine("[BaseDatos] Columna archivo_audio agregada a temas");
        }
        if (!listaColumnasTemas.Contains("duracion_segundos"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas ADD COLUMN duracion_segundos INTEGER");
            Console.WriteLine("[BaseDatos] Columna duracion_segundos agregada a temas");
        }
        if (!listaColumnasTemas.Contains("formato_audio"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas ADD COLUMN formato_audio TEXT");
            Console.WriteLine("[BaseDatos] Columna formato_audio agregada a temas");
        }
        
        // Agregar columnas de audio a temas_cd
        if (!listaColumnasTemasCd.Contains("archivo_audio"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas_cd ADD COLUMN archivo_audio TEXT");
            Console.WriteLine("[BaseDatos] Columna archivo_audio agregada a temas_cd");
        }
        if (!listaColumnasTemasCd.Contains("duracion_segundos"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas_cd ADD COLUMN duracion_segundos INTEGER");
            Console.WriteLine("[BaseDatos] Columna duracion_segundos agregada a temas_cd");
        }
        if (!listaColumnasTemasCd.Contains("formato_audio"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas_cd ADD COLUMN formato_audio TEXT");
            Console.WriteLine("[BaseDatos] Columna formato_audio agregada a temas_cd");
        }
        
        // Agregar columna es_favorito a temas
        if (!listaColumnasTemas.Contains("es_favorito"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas ADD COLUMN es_favorito INTEGER DEFAULT 0");
            Console.WriteLine("[BaseDatos] Columna es_favorito agregada a temas");
        }
        
        // Agregar columna es_favorito a temas_cd
        if (!listaColumnasTemasCd.Contains("es_favorito"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas_cd ADD COLUMN es_favorito INTEGER DEFAULT 0");
            Console.WriteLine("[BaseDatos] Columna es_favorito agregada a temas_cd");
        }
        
        // Agregar columna id_composicion a temas (para agrupar versiones/covers)
        if (!listaColumnasTemas.Contains("id_composicion"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas ADD COLUMN id_composicion INTEGER");
            Console.WriteLine("[BaseDatos] Columna id_composicion agregada a temas");
        }
        
        // Agregar columna id_composicion a temas_cd
        if (!listaColumnasTemasCd.Contains("id_composicion"))
        {
            await conn.ExecuteAsync("ALTER TABLE temas_cd ADD COLUMN id_composicion INTEGER");
            Console.WriteLine("[BaseDatos] Columna id_composicion agregada a temas_cd");
        }
        
        // Crear índices para id_composicion
        try
        {
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_temas_composicion ON temas(id_composicion)");
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_temascd_composicion ON temas_cd(id_composicion)");
        }
        catch { /* Índice ya existe */ }
        
        // Crear carpetas para archivos de audio si no existen
        var directorioBase = AppContext.BaseDirectory;
        var carpetaAudioCassette = Path.Combine(directorioBase, "audio", "cassette");
        var carpetaAudioCd = Path.Combine(directorioBase, "audio", "cd");
        
        Directory.CreateDirectory(carpetaAudioCassette);
        Directory.CreateDirectory(carpetaAudioCd);
        Console.WriteLine("[BaseDatos] Carpetas de audio creadas");
        
        // ==========================================
        // MIGRACIÓN: Fotos de intérpretes y multi-artista
        // ==========================================
        
        var columnasInterpretes = await conn.QueryAsync<string>(
            "SELECT name FROM pragma_table_info('interpretes')");
        var listaColumnasInterpretes = columnasInterpretes.ToList();
        
        // Agregar columna foto_blob (BLOB) para almacenar imágenes
        if (!listaColumnasInterpretes.Contains("foto_blob"))
        {
            await conn.ExecuteAsync("ALTER TABLE interpretes ADD COLUMN foto_blob BLOB");
            Console.WriteLine("[BaseDatos] Columna foto_blob agregada a interpretes");
        }
        
        // Agregar columna biografia para descripción del artista
        if (!listaColumnasInterpretes.Contains("biografia"))
        {
            await conn.ExecuteAsync("ALTER TABLE interpretes ADD COLUMN biografia TEXT");
            Console.WriteLine("[BaseDatos] Columna biografia agregada a interpretes");
        }
        
        // Crear tabla cancion_artistas para relación muchos-a-muchos
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS cancion_artistas (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                id_cancion INTEGER NOT NULL,
                tipo_cancion TEXT NOT NULL,
                id_interprete INTEGER NOT NULL,
                es_principal INTEGER DEFAULT 0,
                rol TEXT,
                UNIQUE(id_cancion, tipo_cancion, id_interprete)
            )
        """);
        
        // Crear índices para la tabla cancion_artistas
        try
        {
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_cancion_artistas_cancion ON cancion_artistas(id_cancion, tipo_cancion)");
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_cancion_artistas_interprete ON cancion_artistas(id_interprete)");
        }
        catch { /* Índices ya existen */ }
        
        Console.WriteLine("[BaseDatos] Tabla cancion_artistas verificada");
        
        // ==========================================
        // MIGRACIÓN: Perfiles de Artistas (tipo Spotify)
        // ==========================================
        
        // Agregar nuevas columnas a interpretes
        if (!listaColumnasInterpretes.Contains("tipo_artista"))
        {
            await conn.ExecuteAsync("ALTER TABLE interpretes ADD COLUMN tipo_artista TEXT DEFAULT 'artista'");
            Console.WriteLine("[BaseDatos] Columna tipo_artista agregada a interpretes");
        }
        if (!listaColumnasInterpretes.Contains("pais"))
        {
            await conn.ExecuteAsync("ALTER TABLE interpretes ADD COLUMN pais TEXT");
            Console.WriteLine("[BaseDatos] Columna pais agregada a interpretes");
        }
        if (!listaColumnasInterpretes.Contains("anio_inicio"))
        {
            await conn.ExecuteAsync("ALTER TABLE interpretes ADD COLUMN anio_inicio INTEGER");
            Console.WriteLine("[BaseDatos] Columna anio_inicio agregada a interpretes");
        }
        if (!listaColumnasInterpretes.Contains("anio_fin"))
        {
            await conn.ExecuteAsync("ALTER TABLE interpretes ADD COLUMN anio_fin INTEGER");
            Console.WriteLine("[BaseDatos] Columna anio_fin agregada a interpretes");
        }
        if (!listaColumnasInterpretes.Contains("discografica"))
        {
            await conn.ExecuteAsync("ALTER TABLE interpretes ADD COLUMN discografica TEXT");
            Console.WriteLine("[BaseDatos] Columna discografica agregada a interpretes");
        }
        if (!listaColumnasInterpretes.Contains("sitio_web"))
        {
            await conn.ExecuteAsync("ALTER TABLE interpretes ADD COLUMN sitio_web TEXT");
            Console.WriteLine("[BaseDatos] Columna sitio_web agregada a interpretes");
        }
        
        // Crear tabla de géneros de artista
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS interprete_generos (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                id_interprete INTEGER NOT NULL,
                genero TEXT NOT NULL,
                UNIQUE(id_interprete, genero)
            )
        """);
        
        // Crear tabla de miembros de banda
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS banda_miembros (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                id_banda INTEGER NOT NULL,
                id_miembro INTEGER,
                nombre_miembro TEXT NOT NULL,
                rol TEXT NOT NULL,
                anio_ingreso INTEGER,
                anio_salida INTEGER,
                es_fundador INTEGER DEFAULT 0
            )
        """);
        
        // Crear catálogo de roles
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS roles_artista (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                nombre TEXT NOT NULL UNIQUE
            )
        """);
        
        // Crear catálogo de géneros
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS generos_musicales (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                nombre TEXT NOT NULL UNIQUE
            )
        """);
        
        // Insertar roles semilla si la tabla está vacía
        var countRoles = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM roles_artista");
        if (countRoles == 0)
        {
            var roles = new[] { "Vocalista", "Líder", "Guitarra", "Bajo", "Batería", 
                               "Teclado", "Piano", "Saxofón", "Trompeta", "Productor",
                               "Compositor", "DJ", "Violín", "Percusión", "Coros" };
            foreach (var rol in roles)
            {
                await conn.ExecuteAsync("INSERT OR IGNORE INTO roles_artista (nombre) VALUES (@rol)", new { rol });
            }
            Console.WriteLine("[BaseDatos] Roles semilla insertados");
        }
        
        // Insertar géneros semilla si la tabla está vacía
        var countGeneros = await conn.QueryFirstAsync<int>("SELECT COUNT(*) FROM generos_musicales");
        if (countGeneros == 0)
        {
            var generos = new[] { "Pop", "Rock", "Jazz", "Blues", "Clásica", "Electrónica",
                                  "Hip-Hop", "R&B", "Country", "Folk", "Metal", "Punk",
                                  "Reggae", "Soul", "Funk", "Disco", "Latino", "World" };
            foreach (var genero in generos)
            {
                await conn.ExecuteAsync("INSERT OR IGNORE INTO generos_musicales (nombre) VALUES (@genero)", new { genero });
            }
            Console.WriteLine("[BaseDatos] Géneros semilla insertados");
        }
        
        // Crear índices
        try
        {
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_interprete_generos ON interprete_generos(id_interprete)");
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS idx_banda_miembros ON banda_miembros(id_banda)");
        }
        catch { /* Índices ya existen */ }
        
        Console.WriteLine("[BaseDatos] Migración de perfiles de artistas completada");
    }
}
