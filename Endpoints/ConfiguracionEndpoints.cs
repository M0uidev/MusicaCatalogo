using MusicaCatalogo.Data;
using MusicaCatalogo.Data.Entidades;
using MusicaCatalogo.Services;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;

namespace MusicaCatalogo.Endpoints;

/// <summary>
/// Configuración de los endpoints de la API REST.
/// </summary>
public static class ConfiguracionEndpoints
{
    // Diccionario para rastrear sesiones de QR
    private static readonly ConcurrentDictionary<string, ConexionQR> _sesionesQR = new();

    public record ConexionQR(string SessionId, DateTime Creado, bool Conectado, DateTime? ConectadoEn);

    public static void MapearEndpoints(this WebApplication app, BaseDatos db, string rutaCSVs)
    {
        var repo = new RepositorioMusica(db);

        // ==========================================
        // BÚSQUEDA GLOBAL
        // ==========================================

        app.MapGet("/api/buscar", async (string? q, int? limite) =>
        {
            var resultados = await repo.BuscarAsync(q ?? "", limite ?? 50);
            return Results.Ok(resultados);
        })
        .WithName("Buscar")
        .WithTags("Búsqueda");

        // Autocompletado de canciones (sin tildes)
        app.MapGet("/api/autocompletar", async (string? q, int? limite) =>
        {
            var sugerencias = await repo.AutocompletarTemasAsync(q ?? "", limite ?? 15);
            return Results.Ok(sugerencias);
        })
        .WithName("Autocompletar")
        .WithTags("Búsqueda");

        // ==========================================
        // FORMATOS (Cassettes y CDs)
        // ==========================================

        app.MapGet("/api/medios", async (string? tipo, int? limite) =>
        {
            var formatos = await repo.ListarMediosAsync(tipo, limite ?? 200);
            return Results.Ok(formatos);
        })
        .WithName("ListarMedios")
        .WithTags("Medios");

        app.MapGet("/api/medios/{numMedio}", async (string numMedio) =>
        {
            var formato = await repo.ObtenerMedioAsync(numMedio);
            return formato is null ? Results.NotFound() : Results.Ok(formato);
        })
        .WithName("ObtenerMedio")
        .WithTags("Medios");

        app.MapGet("/api/medios/{numMedio}/temas", async (string numMedio) =>
        {
            var temas = await repo.ObtenerTemasDeMedioAsync(numMedio);
            return Results.Ok(temas);
        })
        .WithName("ObtenerTemasDeFormato")
        .WithTags("Medios");

        // ==========================================
        // INTÉRPRETES
        // ==========================================

        app.MapGet("/api/interpretes", async (string? filtro, int? limite) =>
        {
            var interpretes = await repo.ListarInterpretesAsync(filtro, limite ?? 100);
            return Results.Ok(interpretes);
        })
        .WithName("ListarInterpretes")
        .WithTags("Intérpretes");

        app.MapGet("/api/interpretes/{nombre}", async (string nombre) =>
        {
            var interprete = await repo.ObtenerInterpreteAsync(nombre);
            return interprete is null ? Results.NotFound() : Results.Ok(interprete);
        })
        .WithName("ObtenerInterprete")
        .WithTags("Intérpretes");

        // ==========================================
        // ESTADÍSTICAS
        // ==========================================

        app.MapGet("/api/estadisticas", async () =>
        {
            var stats = await repo.ObtenerEstadisticasAsync();
            return Results.Ok(stats);
        })
        .WithName("ObtenerEstadisticas")
        .WithTags("Estadísticas");

        // ==========================================
        // DIAGNÓSTICO
        // ==========================================

        app.MapGet("/api/diagnostico", async () =>
        {
            var diag = await repo.ObtenerDiagnosticoAsync(rutaCSVs);
            return Results.Ok(diag);
        })
        .WithName("ObtenerDiagnostico")
        .WithTags("Sistema");

        // Endpoint de información del sistema (reemplaza reimportar)
        app.MapPost("/api/reimportar", () =>
        {
            return Results.Ok(new { 
                mensaje = "La reimportación no está disponible en la versión SQLite. Los datos están integrados en el ejecutable.",
                info = "Para modificar datos, usa los endpoints CRUD de la API."
            });
        })
        .WithName("Reimportar")
        .WithTags("Sistema");

        // ==========================================
        // INFORMACIÓN DE RED
        // ==========================================

        app.MapGet("/api/red", () =>
        {
            var ips = ServicioRed.ObtenerIPsLocales();
            return Results.Ok(new { ips, puerto = 5179 });
        })
        .WithName("ObtenerInfoRed")
        .WithTags("Sistema");

        // ==========================================
        // SISTEMA QR - Conexión móvil
        // ==========================================

        // Crear nueva sesión QR
        app.MapPost("/api/qr/crear", () =>
        {
            var sessionId = Guid.NewGuid().ToString("N")[..12];
            var sesion = new ConexionQR(sessionId, DateTime.Now, false, null);
            _sesionesQR[sessionId] = sesion;
            
            // Limpiar sesiones viejas (más de 10 minutos)
            var viejas = _sesionesQR.Where(x => DateTime.Now - x.Value.Creado > TimeSpan.FromMinutes(10)).ToList();
            foreach (var vieja in viejas)
            {
                _sesionesQR.TryRemove(vieja.Key, out _);
            }

            var ips = ServicioRed.ObtenerIPsLocales();
            var ipPrincipal = ips.FirstOrDefault(ip => ip.StartsWith("192.168")) ?? ips.FirstOrDefault() ?? "localhost";
            var url = $"http://{ipPrincipal}:5179/?qr={sessionId}";

            return Results.Ok(new { sessionId, url });
        })
        .WithName("CrearSesionQR")
        .WithTags("QR");

        // Confirmar conexión desde móvil
        app.MapPost("/api/qr/conectar/{sessionId}", (string sessionId) =>
        {
            if (_sesionesQR.TryGetValue(sessionId, out var sesion))
            {
                var actualizada = sesion with { Conectado = true, ConectadoEn = DateTime.Now };
                _sesionesQR[sessionId] = actualizada;
                return Results.Ok(new { exito = true, mensaje = "Conexión establecida" });
            }
            return Results.NotFound(new { exito = false, mensaje = "Sesión no encontrada" });
        })
        .WithName("ConectarQR")
        .WithTags("QR");

        // Verificar estado de conexión (polling desde PC)
        app.MapGet("/api/qr/estado/{sessionId}", (string sessionId) =>
        {
            if (_sesionesQR.TryGetValue(sessionId, out var sesion))
            {
                return Results.Ok(new { conectado = sesion.Conectado, conectadoEn = sesion.ConectadoEn });
            }
            return Results.NotFound(new { conectado = false });
        })
        .WithName("EstadoQR")
        .WithTags("QR");

        // ==========================================
        // CRUD - OPCIONES PARA FORMULARIOS
        // ==========================================

        app.MapGet("/api/opciones", async () =>
        {
            try
            {
                var opciones = await repo.ObtenerOpcionesFormularioAsync();
                return Results.Ok(opciones);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] /api/opciones: {ex.Message}");
                return Results.Problem(ex.Message);
            }
        })
        .WithName("ObtenerOpciones")
        .WithTags("CRUD");

        // ==========================================
        // CRUD - FORMATOS
        // ==========================================

        app.MapPost("/api/medios", async (MedioRequest request) =>
        {
            var resultado = await repo.CrearFormatoAsync(request);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("CrearFormato")
        .WithTags("CRUD");

        app.MapPut("/api/medios/{numMedio}", async (string numMedio, MedioRequest request) =>
        {
            var resultado = await repo.ActualizarFormatoAsync(numMedio, request);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("ActualizarFormato")
        .WithTags("CRUD");

        app.MapDelete("/api/medios/{numMedio}", async (string numMedio) =>
        {
            var resultado = await repo.EliminarFormatoAsync(numMedio);
            return resultado.Exito ? Results.Ok(resultado) : Results.NotFound(resultado);
        })
        .WithName("EliminarFormato")
        .WithTags("CRUD");

        // ==========================================
        // CRUD - CANCIONES
        // ==========================================

        app.MapGet("/api/medios/{numMedio}/canciones", async (string numMedio) =>
        {
            var temas = await repo.ObtenerTemasConIdAsync(numMedio);
            return Results.Ok(temas);
        })
        .WithName("ObtenerCancionesConId")
        .WithTags("CRUD");

        app.MapPost("/api/canciones", async (CancionRequest request) =>
        {
            var resultado = await repo.CrearCancionAsync(request);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("CrearCancion")
        .WithTags("CRUD");

        app.MapPut("/api/canciones/{id}", async (int id, CancionRequest request) =>
        {
            var resultado = await repo.ActualizarCancionAsync(id, request);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("ActualizarCancion")
        .WithTags("CRUD");

        app.MapDelete("/api/canciones/{id}", async (int id, string tipo) =>
        {
            var resultado = await repo.EliminarCancionAsync(id, tipo);
            return resultado.Exito ? Results.Ok(resultado) : Results.NotFound(resultado);
        })
        .WithName("EliminarCancion")
        .WithTags("CRUD");

        app.MapPost("/api/canciones/reordenar", async (ReordenarRequest request) =>
        {
            var resultado = await repo.ReordenarCancionesAsync(request);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("ReordenarCanciones")
        .WithTags("CRUD");

        // ==========================================
        // CRUD - INTÉRPRETES
        // ==========================================

        app.MapPost("/api/interpretes", async (HttpContext context) =>
        {
            var body = await context.Request.ReadFromJsonAsync<Dictionary<string, string>>();
            var nombre = body?.GetValueOrDefault("nombre") ?? "";
            
            if (string.IsNullOrWhiteSpace(nombre))
                return Results.BadRequest(new CrudResponse { Exito = false, Mensaje = "Nombre requerido" });
            
            var resultado = await repo.CrearInterpreteAsync(nombre);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("CrearInterprete")
        .WithTags("CRUD");

        // ==========================================
        // ÁLBUMES
        // ==========================================

        app.MapGet("/api/albumes", async (string? filtro, int? limite) =>
        {
            var albumes = await repo.ListarAlbumesAsync(filtro, limite ?? 100);
            return Results.Ok(albumes);
        })
        .WithName("ListarAlbumes")
        .WithTags("Álbumes");

        app.MapGet("/api/albumes/{id:int}", async (int id) =>
        {
            var album = await repo.ObtenerAlbumAsync(id);
            return album is null ? Results.NotFound() : Results.Ok(album);
        })
        .WithName("ObtenerAlbum")
        .WithTags("Álbumes");

        app.MapPost("/api/albumes", async (AlbumRequest request) =>
        {
            var resultado = await repo.CrearAlbumAsync(request);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("CrearAlbum")
        .WithTags("Álbumes");

        app.MapPut("/api/albumes/{id:int}", async (int id, AlbumRequest request) =>
        {
            var resultado = await repo.ActualizarAlbumAsync(id, request);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("ActualizarAlbum")
        .WithTags("Álbumes");

        app.MapDelete("/api/albumes/{id:int}", async (int id) =>
        {
            var resultado = await repo.EliminarAlbumAsync(id);
            return resultado.Exito ? Results.Ok(resultado) : Results.NotFound(resultado);
        })
        .WithName("EliminarAlbum")
        .WithTags("Álbumes");

        // Portada de álbum
        app.MapGet("/api/albumes/{id:int}/portada", async (int id) =>
        {
            var portada = await repo.ObtenerPortadaAlbumAsync(id);
            if (portada == null || portada.Length == 0)
                return Results.NotFound();
            return Results.File(portada, "image/jpeg");
        })
        .WithName("ObtenerPortadaAlbum")
        .WithTags("Álbumes");

        app.MapPost("/api/albumes/{id:int}/portada", async (int id, HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
                return Results.BadRequest(new CrudResponse { Exito = false, Mensaje = "Archivo requerido" });

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var resultado = await repo.GuardarPortadaAlbumAsync(id, ms.ToArray());
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("SubirPortadaAlbum")
        .WithTags("Álbumes")
        .DisableAntiforgery();

        // ==========================================
        // CANCIÓN INDIVIDUAL
        // ==========================================

        // Obtener todas las canciones para la galería
        app.MapGet("/api/canciones/todas", async () =>
        {
            try
            {
                var canciones = await repo.ObtenerTodasCancionesAsync();
                return Results.Ok(canciones);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] /api/canciones/todas: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                System.IO.File.WriteAllText("error_log.txt", $"{DateTime.Now}: {ex.Message}\n{ex.StackTrace}");
                return Results.Problem($"Error: {ex.Message}");
            }
        })
        .WithName("ObtenerTodasCanciones")
        .WithTags("Canciones");

        app.MapGet("/api/canciones/{id:int}", async (int id, string tipo) =>
        {
            var cancion = await repo.ObtenerCancionAsync(id, tipo);
            return cancion is null ? Results.NotFound() : Results.Ok(cancion);
        })
        .WithName("ObtenerCancionDetalle")
        .WithTags("Canciones");

        app.MapPut("/api/canciones/{id:int}/detalle", async (int id, string tipo, CancionUpdateRequest request) =>
        {
            var resultado = await repo.ActualizarCancionExtendidaAsync(id, tipo, request);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("ActualizarCancionDetalle")
        .WithTags("Canciones");

        // Portada de canción
        app.MapGet("/api/canciones/{id:int}/portada", async (int id, string tipo) =>
        {
            var portada = await repo.ObtenerPortadaCancionAsync(id, tipo);
            if (portada == null || portada.Length == 0)
                return Results.NotFound();
            return Results.File(portada, "image/jpeg");
        })
        .WithName("ObtenerPortadaCancion")
        .WithTags("Canciones");

        app.MapPost("/api/canciones/{id:int}/portada", async (int id, string tipo, HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
                return Results.BadRequest(new CrudResponse { Exito = false, Mensaje = "Archivo requerido" });

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var resultado = await repo.GuardarPortadaCancionAsync(id, tipo, ms.ToArray());
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("SubirPortadaCancion")
        .WithTags("Canciones")
        .DisableAntiforgery();

        app.MapDelete("/api/canciones/{id:int}/portada", async (int id, string tipo) =>
        {
            var resultado = await repo.EliminarPortadaCancionAsync(id, tipo);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("EliminarPortadaCancion")
        .WithTags("Canciones");

        // ==========================================
        // AUDIO DE CANCIÓN
        // ==========================================

        // Obtener/reproducir archivo de audio
        app.MapGet("/api/canciones/{id:int}/audio", async (int id, string tipo) =>
        {
            var rutaArchivo = await repo.ObtenerRutaAudioAsync(id, tipo);
            
            if (rutaArchivo == null || !File.Exists(rutaArchivo))
                return Results.NotFound();
            
            var stream = File.OpenRead(rutaArchivo);
            var extension = Path.GetExtension(rutaArchivo).ToLowerInvariant();
            var contentType = extension switch
            {
                ".mp3" => "audio/mpeg",
                ".wav" => "audio/wav",
                ".m4a" => "audio/mp4",
                ".flac" => "audio/flac",
                ".ogg" => "audio/ogg",
                _ => "application/octet-stream"
            };
            
            return Results.File(stream, contentType, enableRangeProcessing: true);
        })
        .WithName("ObtenerAudioCancion")
        .WithTags("Canciones");

        // Subir archivo de audio
        app.MapPost("/api/canciones/{id:int}/audio", async (int id, string tipo, HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            
            if (file == null || file.Length == 0)
                return Results.BadRequest(new CrudResponse { Exito = false, Mensaje = "Archivo requerido" });
            
            // Validar tamaño (límite 20MB)
            if (file.Length > 20 * 1024 * 1024)
                return Results.BadRequest(new CrudResponse { Exito = false, Mensaje = "El archivo no debe superar 20MB" });
            
            // Validar extensión
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            var formatosPermitidos = new[] { ".mp3", ".wav", ".m4a", ".flac", ".ogg" };
            
            if (!formatosPermitidos.Contains(extension))
                return Results.BadRequest(new CrudResponse 
                { 
                    Exito = false, 
                    Mensaje = $"Formato no soportado. Use: {string.Join(", ", formatosPermitidos)}" 
                });
            
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            var resultado = await repo.GuardarArchivoAudioAsync(id, tipo, file.FileName, ms.ToArray());
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("SubirAudioCancion")
        .WithTags("Canciones")
        .DisableAntiforgery();

        // Eliminar archivo de audio
        app.MapDelete("/api/canciones/{id:int}/audio", async (int id, string tipo) =>
        {
            var resultado = await repo.EliminarArchivoAudioAsync(id, tipo);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("EliminarAudioCancion")
        .WithTags("Canciones");

        // Marcar/desmarcar canción como favorita
        app.MapPost("/api/canciones/{id:int}/favorito", async (int id, string tipo, FavoritoRequest request) =>
        {
            var resultado = await repo.MarcarComoFavoritoAsync(id, tipo, request.EsFavorito);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("MarcarFavorito")
        .WithTags("Canciones");

        // ==========================================
        // ASIGNACIÓN DE CANCIONES A ÁLBUMES
        // ==========================================

        // Obtener canciones disponibles para asignar (búsqueda en servidor)
        app.MapGet("/api/canciones/disponibles", async (string? filtro, int? excluirAlbumId, int? limite, bool? soloSinAlbum) =>
        {
            var canciones = await repo.ObtenerCancionesDisponiblesAsync(filtro, excluirAlbumId, limite ?? 200, soloSinAlbum ?? false);
            return Results.Ok(canciones);
        })
        .WithName("ObtenerCancionesDisponibles")
        .WithTags("Álbumes");

        // Asignar canciones a un álbum
        app.MapPost("/api/albumes/{id:int}/canciones", async (int id, AsignarCancionesRequest request) =>
        {
            var resultado = await repo.AsignarCancionesAlbumAsync(id, request);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("AsignarCancionesAlbum")
        .WithTags("Álbumes");

        // Asignar una sola canción a un álbum (o quitarla si idAlbum es null)
        app.MapPost("/api/albumes/asignar-cancion", async (AsignarCancionSimpleRequest request) =>
        {
            var resultado = await repo.AsignarCancionAAlbumAsync(request.IdCancion, request.Tipo, request.IdAlbum);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("AsignarCancionSimple")
        .WithTags("Álbumes");

        // Quitar canción de un álbum
        app.MapDelete("/api/albumes/{albumId:int}/canciones/{cancionId:int}", async (int albumId, int cancionId, string tipo) =>
        {
            var resultado = await repo.QuitarCancionDeAlbumAsync(cancionId, tipo);
            return resultado.Exito ? Results.Ok(resultado) : Results.NotFound(resultado);
        })
        .WithName("QuitarCancionDeAlbum")
        .WithTags("Álbumes");

        // ==========================================
        // NOTIFICACIONES (DATA HYGIENE)
        // ==========================================

        app.MapGet("/api/notificaciones", async () =>
        {
            try
            {
                var notificaciones = await repo.ObtenerNotificacionesAsync();
                return Results.Ok(new { total = notificaciones.Count, items = notificaciones });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] /api/notificaciones: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return Results.Problem($"Error: {ex.Message}");
            }
        })
        .WithName("ObtenerNotificaciones")
        .WithTags("Sistema");

        // ==========================================
        // CANCIONES DUPLICADAS
        // ==========================================

        app.MapGet("/api/duplicados", async (string? tipo) =>
        {
            var duplicados = await repo.ObtenerDuplicadosAsync(tipo);
            return Results.Ok(duplicados);
        })
        .WithName("ObtenerDuplicados")
        .WithTags("Sistema");

        app.MapGet("/api/duplicados/estadisticas", async () =>
        {
            var stats = await repo.ObtenerEstadisticasDuplicadosAsync();
            return Results.Ok(stats);
        })
        .WithName("ObtenerEstadisticasDuplicados")
        .WithTags("Sistema");

        // Obtener grupo de duplicados específico por ID
        app.MapGet("/api/duplicados/{grupoId}", async (string grupoId) =>
        {
            var grupo = await repo.ObtenerGrupoDuplicadoPorIdAsync(grupoId);
            return grupo is null ? Results.NotFound() : Results.Ok(grupo);
        })
        .WithName("ObtenerGrupoDuplicado")
        .WithTags("Sistema");

        // Marcar artista como original en un grupo de duplicados
        app.MapPost("/api/duplicados/{grupoId}/artista-original", async (string grupoId, MarcarArtistaOriginalRequest request) =>
        {
            var resultado = await repo.MarcarArtistaOriginalAsync(grupoId, request.IdInterprete);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("MarcarArtistaOriginal")
        .WithTags("Sistema");

        // Buscar artistas que tienen una canción con el mismo nombre (para sugerir artista original)
        app.MapGet("/api/canciones/artistas-para-cover", async (string tema, int? excluirIdInterprete) =>
        {
            var artistas = await repo.BuscarArtistasParaCoverAsync(tema, excluirIdInterprete);
            return Results.Ok(artistas);
        })
        .WithName("BuscarArtistasParaCover")
        .WithTags("Canciones");

        // ==========================================
        // PERFIL UNIFICADO DE CANCIÓN
        // ==========================================

        app.MapGet("/api/cancion/perfil", async (string? tema, string? artista, string? grupo) =>
        {
            var perfil = await repo.ObtenerPerfilCancionAsync(tema, artista, grupo);
            if (perfil == null)
                return Results.NotFound(new { error = "Canción no encontrada" });
            return Results.Ok(perfil);
        })
        .WithName("ObtenerPerfilCancion")
        .WithTags("Temas");

        // Perfil multi-artista para canciones con múltiples versiones
        app.MapGet("/api/cancion/perfil-multiartista", async (string grupo) =>
        {
            var perfil = await repo.ObtenerPerfilMultiArtistaAsync(grupo);
            if (perfil == null)
                return Results.NotFound(new { error = "Grupo no encontrado" });
            return Results.Ok(perfil);
        })
        .WithName("ObtenerPerfilMultiArtista")
        .WithTags("Temas");

        // ==========================================
        // REPRODUCTOR - POOL DE CANCIONES CON AUDIO
        // ==========================================

        app.MapGet("/api/canciones/con-audio", async () =>
        {
            try
            {
                var canciones = await repo.ObtenerCancionesConAudioAsync();
                return Results.Ok(canciones);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] /api/canciones/con-audio: {ex.Message}");
                return Results.Problem(ex.Message);
            }
        })
        .WithName("ObtenerCancionesConAudio")
        .WithTags("Reproductor");

        // ==========================================
        // MANTENIMIENTO
        // ==========================================

        app.MapPost("/api/mantenimiento/sincronizar", async () =>
        {
            var resultado = await repo.SincronizarAlbumesCoversAsync();
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("SincronizarAlbumes")
        .WithTags("Sistema");

        // ==========================================
        // COMPOSICIONES (Agrupar versiones/covers)
        // ==========================================

        // Crear nueva composición
        app.MapPost("/api/composiciones", async (ComposicionRequest request) =>
        {
            var resultado = await repo.CrearComposicionAsync(request);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("CrearComposicion")
        .WithTags("Composiciones");

        // Listar composiciones
        app.MapGet("/api/composiciones", async () =>
        {
            var composiciones = await repo.ListarComposicionesAsync();
            return Results.Ok(composiciones);
        })
        .WithName("ListarComposiciones")
        .WithTags("Composiciones");

        // Obtener canciones de una composición
        app.MapGet("/api/composiciones/{id:int}/canciones", async (int id) =>
        {
            var canciones = await repo.ObtenerCancionesDeComposicionAsync(id);
            return Results.Ok(canciones);
        })
        .WithName("ObtenerCancionesDeComposicion")
        .WithTags("Composiciones");

        // Separar canción de su composición (hacerla independiente)
        app.MapPost("/api/composiciones/separar", async (SepararCancionRequest request) =>
        {
            var resultado = await repo.SepararDeComposicionAsync(request);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("SepararDeComposicion")
        .WithTags("Composiciones");

        // Unir canciones a una composición
        app.MapPost("/api/composiciones/unir", async (UnirCancionesRequest request) =>
        {
            var resultado = await repo.UnirAComposicionAsync(request);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("UnirAComposicion")
        .WithTags("Composiciones");
    }
}
