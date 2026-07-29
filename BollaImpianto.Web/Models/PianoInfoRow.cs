namespace BollaImpianto.Web.Models;

public sealed class PianoInfoRow
{
    public int IdNes { get; set; }
    public string Bolla { get; set; } = string.Empty;
    public string CodiceNesting { get; set; } = string.Empty;
    public string Macchina { get; set; } = string.Empty;
    public DateTime? DataPrevista { get; set; }
    public int? IdListaCollegata { get; set; }
    public string? ListaOperatore { get; set; }
    public string? ListaStato { get; set; }
    public DateTime? ListaDataCreazione { get; set; }
}
