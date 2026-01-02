using MusicaCatalogo.Data;
using MusicaCatalogo.Endpoints;
using MusicaCatalogo.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;

// ============================================
// CONSTANTES Y CONFIGURACIÓN
// ============================================

const int PUERTO = 5179;
const int SW_HIDE = 0;
const int SW_SHOW = 5;
const int SW_SHOWMINIMIZED = 2;

// ============================================
// IMPORTACIONES DE WINDOWS API
// ============================================

[DllImport("kernel32.dll")]
static extern IntPtr GetConsoleWindow();

[DllImport("user32.dll")]
static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

[DllImport("user32.dll")]
static extern bool IsWindowVisible(IntPtr hWnd);

[DllImport("user32.dll")]
static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

// Para deshabilitar el botón de cerrar
[DllImport("user32.dll")]
static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);

[DllImport("user32.dll")]
static extern bool DeleteMenu(IntPtr hMenu, uint uPosition, uint uFlags);

const uint SC_CLOSE = 0xF060;
const uint MF_BYCOMMAND = 0x00000000;

// ============================================
// VARIABLES GLOBALES
// ============================================

var consoleWindow = GetConsoleWindow();
NotifyIcon? trayIcon = null;

// Deshabilitar el botón X de la consola (para que no se pueda cerrar accidentalmente)
var sysMenu = GetSystemMenu(consoleWindow, false);
DeleteMenu(sysMenu, SC_CLOSE, MF_BYCOMMAND);

// ============================================
// FUNCIONES DEL SYSTEM TRAY
// ============================================

void ConfigurarSystemTray()
{
    // Ejecutar en un thread separado con su propio message loop
    var trayThread = new Thread(() =>
    {
        // Crear menú contextual
        var trayMenu = new System.Windows.Forms.ContextMenuStrip();
        
        var menuMostrar = new System.Windows.Forms.ToolStripMenuItem("📺 Mostrar consola");
        menuMostrar.Click += (s, e) => {
            ShowWindow(consoleWindow, SW_SHOW);
        };
        
        var menuAbrir = new System.Windows.Forms.ToolStripMenuItem("🌐 Abrir en navegador");
        menuAbrir.Click += (s, e) => {
            try { Process.Start(new ProcessStartInfo($"http://localhost:{PUERTO}") { UseShellExecute = true }); }
            catch { }
        };
        
        var menuSeparador = new System.Windows.Forms.ToolStripSeparator();
        
        var menuSalir = new System.Windows.Forms.ToolStripMenuItem("❌ Cerrar servidor");
        menuSalir.Click += (s, e) => {
            trayIcon?.Dispose();
            Environment.Exit(0);
        };
        
        trayMenu.Items.Add(menuMostrar);
        trayMenu.Items.Add(menuAbrir);
        trayMenu.Items.Add(menuSeparador);
        trayMenu.Items.Add(menuSalir);
        
        // Crear icono
        trayIcon = new NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Catálogo de Música - Servidor",
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        
        // Doble clic para mostrar/ocultar consola
        trayIcon.DoubleClick += (s, e) => {
            if (IsWindowVisible(consoleWindow))
                ShowWindow(consoleWindow, SW_HIDE);
            else
                ShowWindow(consoleWindow, SW_SHOW);
        };
        
        // Timer para detectar minimizado
        var timer = new System.Windows.Forms.Timer { Interval = 500 };
        bool estabaVisible = true;
        
        timer.Tick += (s, e) => {
            var placement = new WINDOWPLACEMENT();
            placement.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
            GetWindowPlacement(consoleWindow, ref placement);
            
            if (placement.showCmd == SW_SHOWMINIMIZED && estabaVisible)
            {
                ShowWindow(consoleWindow, SW_HIDE);
                trayIcon?.ShowBalloonTip(2000, "Catálogo de Música", 
                    "El servidor sigue ejecutándose.\nDoble clic en el icono para mostrar.", 
                    ToolTipIcon.Info);
                estabaVisible = false;
            }
            else if (IsWindowVisible(consoleWindow))
            {
                estabaVisible = true;
            }
        };
        timer.Start();
        
        // Ejecutar el message loop de Windows Forms
        System.Windows.Forms.Application.Run();
    });
    
    trayThread.SetApartmentState(ApartmentState.STA);
    trayThread.IsBackground = true;
    trayThread.Start();
}

// ============================================
// INICIO DEL PROGRAMA
// ============================================

// Determinar rutas
var rutaEjecutable = AppContext.BaseDirectory;
var rutaDb = Path.Combine(rutaEjecutable, "catalogo.db");

// Si no existe la base de datos, usar la que viene con los datos originales
var rutaDbOriginal = Path.Combine(rutaEjecutable, "Data", "catalogo.db");
if (!File.Exists(rutaDb) && File.Exists(rutaDbOriginal))
{
    File.Copy(rutaDbOriginal, rutaDb);
    Console.WriteLine("[SQLite] Base de datos copiada desde instalación original.");
}

// ============================================
// MOSTRAR BANNER DE INICIO
// ============================================

Console.Clear();
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine();
Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║           CATALOGO DE MUSICA - INICIANDO...                  ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

// ============================================
// INICIALIZAR BASE DE DATOS SQLITE
// ============================================

var baseDatos = new BaseDatos(rutaDb);

try
{
    if (!baseDatos.BaseDatosExiste())
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("[!] Primera ejecución detectada - Creando base de datos...");
        Console.ResetColor();
    }
    
    await baseDatos.InicializarAsync();
    Console.WriteLine($"[SQLite] Base de datos: {rutaDb}");
    
    // Verificar si hay datos
    using var conn = baseDatos.ObtenerConexion();
    var count = await Dapper.SqlMapper.QueryFirstAsync<int>(conn, "SELECT COUNT(*) FROM interpretes");
    Console.WriteLine($"[SQLite] {count} intérpretes en la base de datos.");
}
catch (Exception ex)
{
    Console.ForegroundColor = ConsoleColor.Red;
    Console.WriteLine($"[ERROR] Error al inicializar la base de datos: {ex.Message}");
    Console.ResetColor();
    Console.WriteLine("\nPresiona cualquier tecla para salir...");
    Console.ReadKey();
    Environment.Exit(1);
}

// ============================================
// CONFIGURAR APLICACIÓN WEB
// ============================================

var builder = WebApplication.CreateBuilder(args);

// Configurar Kestrel para escuchar en todas las interfaces
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(PUERTO);
});

// Configurar logging mínimo
builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// Agregar servicios
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

// ============================================
// CONFIGURAR MIDDLEWARE
// ============================================

// Servir archivos estáticos desde la carpeta Web
var rutaWeb = Path.Combine(rutaEjecutable, "Web");
if (Directory.Exists(rutaWeb))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(rutaWeb),
        RequestPath = ""
    });

    // Redirigir raíz a index.html
    app.MapGet("/", () => Results.Redirect("/index.html"));
}
else
{
    Console.WriteLine($"[Advertencia] No se encontró la carpeta Web en: {rutaWeb}");
    app.MapGet("/", () => Results.Text("Catálogo de Música API - La interfaz web no está disponible.", "text/plain"));
}

// Mapear endpoints de la API
app.MapearEndpoints(baseDatos, rutaEjecutable);

// ============================================
// MOSTRAR INFORMACIÓN DE ACCESO
// ============================================

Console.WriteLine();
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║           CATALOGO DE MUSICA - SERVIDOR LISTO                ║");
Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
Console.ForegroundColor = ConsoleColor.White;
Console.WriteLine($"║  URL: http://localhost:{PUERTO}                                  ║");
Console.ForegroundColor = ConsoleColor.Yellow;
Console.WriteLine("║                                                              ║");
Console.WriteLine("║  >>> Ctrl+Click en el link para abrir en el navegador <<<    ║");
Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

// Obtener IPs de red para acceso desde otros dispositivos
try
{
    var ips = ServicioRed.ObtenerIPsLocales();
    if (ips.Count > 0)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("Acceso desde otros dispositivos en la misma red:");
        foreach (var ip in ips)
        {
            Console.WriteLine($"  → http://{ip}:{PUERTO}");
        }
        Console.ResetColor();
        Console.WriteLine();
    }
}
catch { /* Ignorar errores de red */ }

// ============================================
// INICIAR SYSTEM TRAY
// ============================================

Console.ForegroundColor = ConsoleColor.Magenta;
Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
Console.WriteLine("║  💡 Minimiza esta ventana para enviarla a la bandeja del     ║");
Console.WriteLine("║  sistema (icono junto al reloj). Doble clic para volver.     ║");
Console.WriteLine("║                                                              ║");
Console.WriteLine("║  ⚠️  Para cerrar el servidor: clic derecho en el icono de    ║");
Console.WriteLine("║  la bandeja → 'Cerrar servidor'                              ║");
Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

// Configurar icono en bandeja del sistema
ConfigurarSystemTray();

app.Run();

// ============================================
// ESTRUCTURAS PARA WINDOWS API
// ============================================

[StructLayout(LayoutKind.Sequential)]
struct WINDOWPLACEMENT
{
    public int length;
    public int flags;
    public int showCmd;
    public System.Drawing.Point ptMinPosition;
    public System.Drawing.Point ptMaxPosition;
    public System.Drawing.Rectangle rcNormalPosition;
}
