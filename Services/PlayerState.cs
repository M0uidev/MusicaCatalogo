namespace MusicaCatalogo.Services;

/// <summary>
/// Estado del player móvil para persistencia
/// </summary>
public class PlayerState
{
    public int? CancionId { get; set; }
    public string? CancionTipo { get; set; }
    public double PosicionSegundos { get; set; }
    public string? UltimaActualizacion { get; set; }
    public string? PlaylistJson { get; set; }
    public bool Shuffle { get; set; }
    public string RepeatMode { get; set; } = "off";
}
