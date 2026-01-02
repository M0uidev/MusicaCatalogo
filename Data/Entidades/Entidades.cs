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
// TABLAS DE GRABACIONES
// ============================================

public record FormatoGrabado
{
    public int Id { get; init; }
    public required string NumFormato { get; init; }
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
    public required string NumFormato { get; init; }
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
    public required string NumFormato { get; init; }
    public int IdInterprete { get; init; }
    public required string NombreTema { get; init; }
    public required string Lado { get; init; }
    public int Desde { get; init; }
    public int Hasta { get; init; }
}

public record TemaCd
{
    public int Id { get; init; }
    public required string NumFormato { get; init; }
    public int IdInterprete { get; init; }
    public required string NombreTema { get; init; }
    public int Ubicacion { get; init; }
}

// ============================================
// DTOs PARA LA API
// ============================================

public record ResultadoBusqueda
{
    public required string Tipo { get; init; } // "cassette" o "cd"
    public required string NumFormato { get; init; }
    public required string Tema { get; init; }
    public required string Interprete { get; init; }
    public string? Posicion { get; init; }
}

public record DetalleFormato
{
    public required string NumFormato { get; init; }
    public required string TipoFormato { get; init; }
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

public record TemaEnFormato
{
    public required string Tema { get; init; }
    public required string Interprete { get; init; }
    public int IdInterprete { get; init; }
    public string? Lado { get; init; }
    public int? Desde { get; init; }
    public int? Hasta { get; init; }
    public int? Ubicacion { get; init; }
    public int? Duracion => Hasta.HasValue && Desde.HasValue ? Hasta - Desde : null;
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
    public required string NumFormato { get; init; }
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
    public List<ConteoFormato> ConteosPorFormato { get; init; } = new();
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

public class ConteoFormato
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
    public string NumFormato { get; set; } = "";
    public string Tipo { get; set; } = ""; // "cassette" o "cd"
    public string Ubicacion { get; set; } = "";
}

// ============================================
// DTOs PARA CRUD (Crear/Editar/Eliminar)
// ============================================

/// <summary>DTO para crear o editar un formato (cassette o CD)</summary>
public record FormatoRequest
{
    public required string NumFormato { get; init; }
    public required string TipoFormato { get; init; } // "cassette" o "cd"
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
    public required string NumFormato { get; init; }
    public required string TipoFormato { get; init; } // "cassette" o "cd"
    public required string Tema { get; init; }
    public string? NombreInterprete { get; init; } // Si no existe, se crea
    public int? IdInterprete { get; init; } // Si ya existe
    // Para cassettes
    public string? Lado { get; init; }
    public int? Desde { get; init; }
    public int? Hasta { get; init; }
    // Para CDs
    public int? Ubicacion { get; init; }
}

/// <summary>DTO para reordenar canciones</summary>
public record ReordenarRequest
{
    public required string NumFormato { get; init; }
    public required string TipoFormato { get; init; }
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

public record OpcionSelect(int Id, string Nombre);

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
}
