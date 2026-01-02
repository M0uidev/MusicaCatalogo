using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MusicaCatalogo.Services;

/// <summary>
/// Servicio para obtener información de red local.
/// </summary>
public static class ServicioRed
{
    /// <summary>
    /// Obtiene todas las direcciones IP locales de la máquina.
    /// </summary>
    public static List<string> ObtenerIPsLocales()
    {
        var ips = new List<string>();

        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var props = ni.GetIPProperties();
                foreach (var addr in props.UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ips.Add(addr.Address.ToString());
                    }
                }
            }
        }
        catch
        {
            // Fallback: usar DNS
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        ips.Add(ip.ToString());
                    }
                }
            }
            catch { }
        }

        return ips.Distinct().ToList();
    }

    /// <summary>
    /// Imprime las URLs de acceso en la consola.
    /// </summary>
    public static void ImprimirURLsAcceso(int puerto)
    {
        Console.WriteLine();
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║           CATÁLOGO DE MÚSICA - SERVIDOR INICIADO             ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine($"║  Acceso local:     http://localhost:{puerto}                     ║");
        
        var ips = ObtenerIPsLocales();
        if (ips.Count > 0)
        {
            Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
            Console.WriteLine("║  Acceso desde otros dispositivos (misma red WiFi):          ║");
            foreach (var ip in ips)
            {
                var url = $"http://{ip}:{puerto}";
                Console.WriteLine($"║    → {url,-52} ║");
            }
        }

        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  NOTA: Para acceder desde el celular, asegúrate de:          ║");
        Console.WriteLine("║    1. Estar conectado a la misma red WiFi                    ║");
        Console.WriteLine("║    2. Permitir el acceso en el Firewall de Windows           ║");
        Console.WriteLine("╠══════════════════════════════════════════════════════════════╣");
        Console.WriteLine("║  Presiona Ctrl+C para detener el servidor                    ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
    }
}
