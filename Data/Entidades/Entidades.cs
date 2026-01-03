namespace MusicaCatalogo.Data.Entidades;

// ============================================
// TABLAS DE REFERENCIA (Lookup)
// ============================================

public record Ecualizador(int IdEcual, string Nombre);
public record Formato(int IdFormato, string Nombre);
public record Fuente(int IdFuente, string Nombre);
public record Grabador(int IdDeck, string Nombre);
public record Marca(int IdMarca, string Nombre);
public record Bias(int IdBias, string Nombre);
public record Modo(int IdModo, string Nombre);
public record Supresor(int IdDolby, string Nombre);

// ============================================
// TABLA MAESTRA
// ============================================

public record Interprete(int Id, string Nombre, int? Foto);

// ============================================
// ÁLBUMES
// ============================================

public record Album
{
    public int Id { get; init; }
    public required string Nombre { get; init; }
    public int? IdInterprete { get; init; }
    public string? Anio { get; init; }
    public byte[]? Portada { get; init; }
    public DateTime? FechaCreacion { get; init; }
}

// ============================================
// TABLAS DE GRABACIONES
// ============================================

public record FormatoGrabado
{
    public int Id { get; init; }
    public required string numMedio { get; init; }
    public int IdFormato { get; init; }
    public int IdMarca { get; init; }
    public int IdDeck { get; init; }
    public int IdEcual { get; init; }
    public int IdDolby { get; init; }
    public int IdBias { get; init; }
    public int IdModo { get; init; }
    public int IdFuente { get; init; }
    public string? FechaInicio { get; init; }
    public string? FechaTermino { get; init; }
}

public record FormatoGrabadoCd
{
    public int Id { get; init; }
    public required string numMedio { get; init; }
    public int IdFormato { get; init; }
    public int IdMarca { get; init; }
    public int IdDeck { get; init; }
    public int IdFuente { get; init; }
    public string? FechaGrabacion { get; init; }
}

// ============================================
// TABLAS DE TEMAS (Tracks)
// ============================================

public record Tema
{
    public int Id { get; init; }
    public required string numMedio { get; init; }
    public int IdInterprete { get; init; }
    public required string NombreTema { get; init; }
    public required string Lado { get; init; }
    public int Desde { get; init; }
    public int Hasta { get; init; }
    public int? IdAlbum { get; init; }
    public string? LinkExterno { get; init; }
    public byte[]? Portada { get; init; }
    public bool EsCover { get; init; }
    public bool EsOriginal { get; init; }
    public string? ArtistaOriginal { get; init; }
}

public record TemaCd
{
    public int Id { get; init; }
    public required string numMedio { get; init; }
    public int IdInterprete { get; init; }
    public required string NombreTema { get; init; }
    public int Ubicacion { get; init; }
    public int? IdAlbum { get; init; }
    public string? LinkExterno { get; init; }
    public byte[]? Portada { get; init; }
    public bool EsCover { get; init; }
    public bool EsOriginal { get; init; }
    public string? ArtistaOriginal { get; init; }
}

// ============================================
// DTOs PARA LA API
// ============================================

public record ResultadoBusqueda
{
    public int Id { get; init; }
    public required string Tipo { get; init; } // "cassette" o "cd"
    public required string numMedio { get; init; }
    public required string Tema { get; init; }
    public required string Interprete { get; init; }
    public string? Posicion { get; init; }
}

public record DetalleMedio
{
    public required string numMedio { get; init; }
    public required string TipoMedio { get; init; }
    public required string Marca { get; init; }
    public required string Grabador { get; init; }
    public required string Fuente { get; init; }
    public string? FechaInicio { get; init; }
    public string? FechaTermino { get; init; }
    // Solo para cassettes
    public string? Ecualizador { get; init; }
    public string? Supresor { get; init; }
    public string? Bias { get; init; }
    public string? Modo { get; init; }
    public int TotalTemas { get; init; }
}

public record TemaEnMedio
{
    public int Id { get; init; }
    public required string Tema { get; init; }
    public required string Interprete { get; init; }
    public int IdInterprete { get; init; }
    public string? Lado { get; init; }
    public int? Desde { get; init; }
    public int? Hasta { get; init; }
    public int? Ubicacion { get; init; }
    public int? Duracion => Hasta.HasValue && Desde.HasValue ? Hasta - Desde : null;
    public int? IdAlbum { get; init; }
    public string? NombreAlbum { get; init; }
    public string? LinkExterno { get; init; }
    public bool TienePortada { get; init; }
    public bool EsCover { get; init; }
    public bool EsOriginal { get; init; }
    public string? ArtistaOriginal { get; init; }
}

public record DetalleInterprete
{
    public int Id { get; init; }
    public required string Nombre { get; init; }
    public int TotalTemasCassette { get; init; }
    public int TotalTemasCd { get; init; }
    public int TotalTemas => TotalTemasCassette + TotalTemasCd;
    public List<TemaDeInterprete> Temas { get; init; } = new();
}

public record TemaDeInterprete
{
    public required string Tipo { get; init; }
    public required string numMedio { get; init; }
    public required string Tema { get; init; }
    public string? Posicion { get; init; }
}

public record EstadisticasGenerales
{
    public int TotalInterpretes { get; init; }
    public int TotalTemasCassette { get; init; }
    public int TotalTemasCd { get; init; }
    public int TotalCassettes { get; init; }
    public int TotalCds { get; init; }
    public List<InterpreteTop> TopInterpretes { get; init; } = new();
    public List<ConteoMedio> ConteosPorMedio { get; init; } = new();
    public List<ConteoMarca> ConteosPorMarca { get; init; } = new();
}

public class InterpreteTop
{
    public long Id { get; set; }
    public string Nombre { get; set; } = "";
    public long TotalTemas { get; set; }
}

public class InterpreteResumen
{
    public int Id { get; set; }
    public string Interprete { get; set; } = "";
    public long TotalTemas { get; set; }
}

public class ConteoMedio
{
    public string Formato { get; set; } = "";
    public long Total { get; set; }
}

public class ConteoMarca
{
    public string Marca { get; set; } = "";
    public long Total { get; set; }
}

public record DiagnosticoImportacion
{
    public DateTime? UltimaImportacion { get; init; }
    public bool BaseDatosExiste { get; init; }
    public Dictionary<string, int> ConteosTablas { get; init; } = new();
    public List<string> Advertencias { get; init; } = new();
    public List<ArchivoCSV> ArchivosCSV { get; init; } = new();
}

public record ArchivoCSV(string Nombre, bool Existe, long? TamanoBytes, DateTime? UltimaModificacion);

// ============================================
// SUGERENCIAS PARA AUTOCOMPLETADO
// ============================================

public class SugerenciaTema
{
    public string Tema { get; set; } = "";
    public string Interprete { get; set; } = "";
    public string numMedio { get; set; } = "";
    public string Tipo { get; set; } = ""; // "cassette" o "cd"
    public string Ubicacion { get; set; } = "";
}

// ============================================
// DTOs PARA CRUD (Crear/Editar/Eliminar)
// ============================================

/// <summary>DTO para crear o editar un formato (cassette o CD)</summary>
public record MedioRequest
{
    public required string numMedio { get; init; }
    public required string TipoMedio { get; init; } // "cassette" o "cd"
    // Acepta IDs o nombres - si se proporciona nombre, se busca/crea
    public int? IdMarca { get; init; }
    public int? IdDeck { get; init; }
    public int? IdFuente { get; init; }
    public string? NombreMarca { get; init; }
    public string? NombreGrabador { get; init; }
    public string? NombreFuente { get; init; }
    public string? FechaInicio { get; init; }
    public string? FechaTermino { get; init; }
    // Solo para cassettes
    public int? IdEcual { get; init; }
    public int? IdDolby { get; init; }
    public int? IdBias { get; init; }
    public int? IdModo { get; init; }
    public string? NombreEcualizador { get; init; }
    public string? NombreSupresor { get; init; }
    public string? NombreBias { get; init; }
    public string? NombreModo { get; init; }
}

/// <summary>DTO para crear o editar una canción</summary>
public record CancionRequest
{
    public required string numMedio { get; init; }
    public required string TipoMedio { get; init; } // "cassette" o "cd"
    public required string Tema { get; init; }
    public string? NombreInterprete { get; init; } // Si no existe, se crea
    public int? IdInterprete { get; init; } // Si ya existe
    // Para cassettes
    public string? Lado { get; init; }
    public int? Desde { get; init; }
    public int? Hasta { get; init; }
    // Para CDs
    public int? Ubicacion { get; init; }
    // Para covers
    public bool EsCover { get; init; }
    public bool EsOriginal { get; init; }
    public string? ArtistaOriginal { get; init; }
}

/// <summary>Request para marcar artista como original</summary>
public record MarcarArtistaOriginalRequest
{
    public required int IdInterprete { get; init; }
}

/// <summary>DTO para reordenar canciones</summary>
public record ReordenarRequest
{
    public required string numMedio { get; init; }
    public required string TipoMedio { get; init; }
    public required List<int> IdsOrdenados { get; init; }
}

/// <summary>Respuesta genérica para operaciones CRUD</summary>
public record CrudResponse
{
    public bool Exito { get; init; }
    public string Mensaje { get; init; } = "";
    public int? IdCreado { get; init; }
}

/// <summary>Lista de opciones para selects</summary>
public record OpcionesFormulario
{
    public List<OpcionSelect> Marcas { get; init; } = new();
    public List<OpcionSelect> Grabadores { get; init; } = new();
    public List<OpcionSelect> Fuentes { get; init; } = new();
    public List<OpcionSelect> Ecualizadores { get; init; } = new();
    public List<OpcionSelect> Supresores { get; init; } = new();
    public List<OpcionSelect> Bias { get; init; } = new();
    public List<OpcionSelect> Modos { get; init; } = new();
    public List<OpcionSelect> Interpretes { get; init; } = new();
}

public record OpcionSelect(long Id, string Nombre);

/// <summary>DTO extendido para temas con ID</summary>
public class TemaConId
{
    public int Id { get; set; }
    public string Tema { get; set; } = "";
    public string Interprete { get; set; } = "";
    public int IdInterprete { get; set; }
    public string? Lado { get; set; }
    public int? Desde { get; set; }
    public int? Hasta { get; set; }
    public int? Ubicacion { get; set; }
    public int? IdAlbum { get; set; }
    public string? NombreAlbum { get; set; }
    public string? LinkExterno { get; set; }
    public bool TienePortada { get; set; }
}

// ============================================
// DTOs PARA ÁLBUMES
// ============================================

/// <summary>DTO para listar álbumes</summary>
public class AlbumResumen
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string? Interprete { get; set; }
    public int? IdInterprete { get; set; }
    public string? Anio { get; set; }
    public bool TienePortada { get; set; }
    public int TotalCanciones { get; set; }
    public bool EsSingle { get; set; }
}

/// <summary>DTO para detalle de álbum</summary>
public class AlbumDetalle
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string? Interprete { get; set; }
    public int? IdInterprete { get; set; }
    public string? Anio { get; set; }
    public bool TienePortada { get; set; }
    public bool EsSingle { get; set; }
    public List<CancionEnAlbum> Canciones { get; set; } = new();
}

/// <summary>DTO para canciones dentro de un álbum</summary>
public class CancionEnAlbum
{
    public int Id { get; set; }
    public string Tipo { get; set; } = "";
    public string Tema { get; set; } = "";
    public string Interprete { get; set; } = "";
    public string numMedio { get; set; } = "";
    public string? Posicion { get; set; }
    public string? LinkExterno { get; set; }
}

/// <summary>DTO para crear/editar álbum</summary>
public record AlbumRequest
{
    public required string Nombre { get; init; }
    public int? IdInterprete { get; init; }
    public string? NombreInterprete { get; init; }
    public string? Anio { get; init; }
    public bool EsSingle { get; init; }
}

/// <summary>DTO para detalle individual de canción</summary>
public class CancionDetalle
{
    public int Id { get; set; }
    public string Tipo { get; set; } = ""; // "cassette" o "cd"
    public string Tema { get; set; } = "";
    public string Interprete { get; set; } = "";
    public int IdInterprete { get; set; }
    public string numMedio { get; set; } = "";
    public string? Lado { get; set; }
    public int? Desde { get; set; }
    public int? Hasta { get; set; }
    public int? Ubicacion { get; set; }
    public int? IdAlbum { get; set; }
    public string? NombreAlbum { get; set; }
    public string? ArtistaAlbum { get; set; }
    public string? AnioAlbum { get; set; }
    public bool EsAlbumSingle { get; set; }
    public string? LinkExterno { get; set; }
    public bool TienePortada { get; set; }
    public bool TienePortadaAlbum { get; set; }

    public bool EsCover { get; set; }
    public bool EsOriginal { get; set; }
    public string? ArtistaOriginal { get; set; }
}

/// <summary>DTO para actualizar canción individual</summary>
public record CancionUpdateRequest
{
    public required string Tema { get; init; }
    public int? IdInterprete { get; init; }
    public string? NombreInterprete { get; init; }
    public int? IdAlbum { get; init; }
    public string? NombreAlbum { get; init; }
    public string? LinkExterno { get; init; }
    public bool EsCover { get; init; }
    public bool EsOriginal { get; init; }
    public string? ArtistaOriginal { get; init; }
    // Para cassettes
    public string? Lado { get; init; }
    public int? Desde { get; init; }
    public int? Hasta { get; init; }
    // Para CDs
    public int? Ubicacion { get; init; }
}

/// <summary>DTO para sugerencias de autocompletado con ID</summary>
public class SugerenciaTemaConId
{
    public int Id { get; set; }
    public string Tema { get; set; } = "";
    public string Interprete { get; set; } = "";
    public string numMedio { get; set; } = "";
    public string Tipo { get; set; } = "";
    public string Ubicacion { get; set; } = "";
    public int? IdAlbum { get; set; }
    public string? AlbumNombre { get; set; }
}

// ============================================
// DTOs PARA ASIGNACIÓN DE ÁLBUMES
// ============================================

/// <summary>DTO para asignar canciones a un álbum</summary>
public record AsignarCancionesRequest
{
    public required List<CancionRef> Canciones { get; init; }
}

/// <summary>DTO para asignar una sola canción a un álbum</summary>
public record AsignarCancionSimpleRequest
{
    public int IdCancion { get; init; }
    public required string Tipo { get; init; } // "cassette" o "cd"
    public int? IdAlbum { get; init; } // null para quitar del álbum
}

/// <summary>Referencia a una canción (ID + tipo)</summary>
public record CancionRef
{
    public int Id { get; init; }
    public required string Tipo { get; init; } // "cassette" o "cd"
}

/// <summary>DTO para mostrar canciones en la galería</summary>
public class CancionGaleria
{
    public int Id { get; set; }
    public string Tipo { get; set; } = "";
    public string Tema { get; set; } = "";
    public string Interprete { get; set; } = "";
    public string numMedio { get; set; } = "";
    public int? IdAlbum { get; set; }
    public string? AlbumNombre { get; set; }
    public int EsCover { get; set; }
    public int EsOriginal { get; set; }
    public string? ArtistaOriginal { get; set; }
    public string? Lado { get; set; }
    public long? Desde { get; set; }
    public long? Hasta { get; set; }
    public long? Ubicacion { get; set; }
}

/// <summary>DTO para obtener canciones disponibles para asignar</summary>
public class CancionDisponible
{
    public int Id { get; set; }
    public string Tipo { get; set; } = "";
    public string Tema { get; set; } = "";
    public string Interprete { get; set; } = "";
    public string numMedio { get; set; } = "";
    public string? Posicion { get; set; }
    public int? IdAlbumActual { get; set; }
    public string? NombreAlbumActual { get; set; }
}

// ============================================
// DTOs PARA NOTIFICACIONES (DATA HYGIENE)
// ============================================

/// <summary>Notificación de problema en los datos</summary>
public class NotificacionDatos
{
    public string Id { get; set; } = "";
    public string Tipo { get; set; } = ""; // "cancion", "album", "formato", "interprete", "duplicado"
    public string Severidad { get; set; } = "info"; // "info", "warning", "error"
    public string Mensaje { get; set; } = "";
    public string? EntidadId { get; set; }
    public string? EntidadTipo { get; set; }
    public string? CampoFaltante { get; set; }
    public string? UrlArreglar { get; set; }
    public string? GrupoId { get; set; } // Para duplicados multi-artista
    public List<OpcionArtistaOriginal>? OpcionesArtista { get; set; } // Opciones para seleccionar artista original
}

/// <summary>Opción de artista para seleccionar como original</summary>
public class OpcionArtistaOriginal
{
    public int IdInterprete { get; set; }
    public string Nombre { get; set; } = "";
    public int CantidadCopias { get; set; }
    public bool EsMasAntiguo { get; set; }
}

// ============================================
// DTOs PARA CANCIONES DUPLICADAS
// ============================================

/// <summary>Grupo de canciones que son potencialmente la misma</summary>
public class GrupoDuplicados
{
    public string Id { get; set; } = ""; // Hash del tema normalizado
    public string TemaNormalizado { get; set; } = "";
    public List<CancionDuplicada> Canciones { get; set; } = new();
    public int TotalInstancias => Canciones.Count;
    public int TotalArtistas => Canciones.Select(c => c.Interprete.ToLowerInvariant()).Distinct().Count();
    public bool TieneMixFormatos => Canciones.Any(c => c.Tipo == "cassette") && Canciones.Any(c => c.Tipo == "cd");
    public bool TieneCovers => Canciones.Any(c => c.EsCover);
}

/// <summary>Información de una canción dentro de un grupo de duplicados</summary>
public class CancionDuplicada
{
    public int Id { get; set; }
    public string Tipo { get; set; } = ""; // "cassette" o "cd"
    public string Tema { get; set; } = "";
    public string Interprete { get; set; } = "";
    public int IdInterprete { get; set; }
    public string numMedio { get; set; } = "";
    public string? Posicion { get; set; }
    public int? IdAlbum { get; set; }
    public string? NombreAlbum { get; set; }
    public bool TienePortada { get; set; }
    public string? LinkExterno { get; set; }
    public bool EsCover { get; set; }
    public bool EsOriginal { get; set; }
    public string? ArtistaOriginal { get; set; }
}

/// <summary>Estadísticas de duplicados</summary>
public class EstadisticasDuplicados
{
    public int TotalGrupos { get; set; }
    public int TotalCancionesDuplicadas { get; set; }
    public int GruposMixtos { get; set; } // CD + Cassette
    public int GruposSoloCassette { get; set; }
    public int GruposSoloCd { get; set; }
}

// ============================================
// DTOs PARA PERFIL DE CANCIÓN UNIFICADO
// ============================================

/// <summary>Perfil unificado de una canción con todas sus ubicaciones físicas</summary>
public class PerfilCancion
{
    public string Tema { get; set; } = "";
    public string Artista { get; set; } = "";
    public List<UbicacionCancion> Ubicaciones { get; set; } = new();
}

/// <summary>Perfil multi-artista de una canción (original + covers)</summary>
public class PerfilCancionMultiArtista
{
    public string Tema { get; set; } = "";
    public string GrupoId { get; set; } = "";
    public int TotalVersiones { get; set; }
    public int TotalArtistas { get; set; }
    public int TotalCopias { get; set; }
    public bool TieneArtistaOriginalDefinido { get; set; }
    public List<VersionArtista> Versiones { get; set; } = new();
}

/// <summary>Versión de una canción por un artista específico</summary>
public class VersionArtista
{
    public string Artista { get; set; } = "";
    public int IdInterprete { get; set; }
    public bool EsOriginal { get; set; }
    public string? ArtistaOriginalRef { get; set; } // Si es cover, referencia al artista original
    public int TotalCopias { get; set; }
    public int? IdAlbumPrincipal { get; set; } // Álbum para mostrar portada
    public string? LinkExterno { get; set; } // Link a YouTube/Spotify de esta versión
    public List<UbicacionCancion> Ubicaciones { get; set; } = new();
}

/// <summary>Ubicación física de una canción (cassette o CD)</summary>
public class UbicacionCancion
{
    public int Id { get; set; }
    public string Tipo { get; set; } = ""; // "cassette" o "cd"
    public string numMedio { get; set; } = "";
    public string? Posicion { get; set; }
    public int? IdAlbum { get; set; }
    public string? NombreAlbum { get; set; }
    public bool TienePortada { get; set; }
    public string? LinkExterno { get; set; }
    public bool EsCover { get; set; }
    public bool EsOriginal { get; set; }
    public string? ArtistaOriginal { get; set; }
}

/// <summary>Artista sugerido para marcar como original en covers</summary>
public class ArtistaParaCover
{
    public int IdInterprete { get; set; }
    public string Nombre { get; set; } = "";
    public bool EsOriginal { get; set; } // Si ya está marcado como original en el sistema
}
