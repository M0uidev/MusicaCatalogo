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

        app.MapGet("/api/formatos", async (string? tipo, int? limite) =>
        {
            var formatos = await repo.ListarFormatosAsync(tipo, limite ?? 200);
            return Results.Ok(formatos);
        })
        .WithName("ListarFormatos")
        .WithTags("Formatos");

        app.MapGet("/api/formatos/{numFormato}", async (string numFormato) =>
        {
            var formato = await repo.ObtenerFormatoAsync(numFormato);
            return formato is null ? Results.NotFound() : Results.Ok(formato);
        })
        .WithName("ObtenerFormato")
        .WithTags("Formatos");

        app.MapGet("/api/formatos/{numFormato}/temas", async (string numFormato) =>
        {
            var temas = await repo.ObtenerTemasDeFormatoAsync(numFormato);
            return Results.Ok(temas);
        })
        .WithName("ObtenerTemasDeFormato")
        .WithTags("Formatos");

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
            var opciones = await repo.ObtenerOpcionesFormularioAsync();
            return Results.Ok(opciones);
        })
        .WithName("ObtenerOpciones")
        .WithTags("CRUD");

        // ==========================================
        // CRUD - FORMATOS
        // ==========================================

        app.MapPost("/api/formatos", async (FormatoRequest request) =>
        {
            var resultado = await repo.CrearFormatoAsync(request);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("CrearFormato")
        .WithTags("CRUD");

        app.MapPut("/api/formatos/{numFormato}", async (string numFormato, FormatoRequest request) =>
        {
            var resultado = await repo.ActualizarFormatoAsync(numFormato, request);
            return resultado.Exito ? Results.Ok(resultado) : Results.BadRequest(resultado);
        })
        .WithName("ActualizarFormato")
        .WithTags("CRUD");

        app.MapDelete("/api/formatos/{numFormato}", async (string numFormato) =>
        {
            var resultado = await repo.EliminarFormatoAsync(numFormato);
            return resultado.Exito ? Results.Ok(resultado) : Results.NotFound(resultado);
        })
        .WithName("EliminarFormato")
        .WithTags("CRUD");

        // ==========================================
        // CRUD - CANCIONES
        // ==========================================

        app.MapGet("/api/formatos/{numFormato}/canciones", async (string numFormato) =>
        {
            var temas = await repo.ObtenerTemasConIdAsync(numFormato);
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
    }
}
