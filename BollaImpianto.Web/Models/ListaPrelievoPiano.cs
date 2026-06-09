namespace BollaImpianto.Web.Models;

public sealed class ListaPrelievoPiano
{
    public int IdNes { get; set; }
    public string Bolla { get; set; } = string.Empty;
    public string CodiceNesting { get; set; } = string.Empty;
    public string Macchina { get; set; } = string.Empty;
    public string Materiale { get; set; } = string.Empty;
    public decimal? Lunghezza { get; set; }
    public decimal? Larghezza { get; set; }
    public decimal? Spessore { get; set; }
    public string? Clienti { get; set; }
    public string? Attributi { get; set; }
    public string? Placche { get; set; }
    public DateTime? DataPrevista { get; set; }
    public string? Note { get; set; }
}
