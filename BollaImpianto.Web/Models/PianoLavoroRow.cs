namespace BollaImpianto.Web.Models;

public sealed class PianoLavoroRow
{
    public int IdNes { get; set; }
    /// <summary>Codice bolla (OLCOD).</summary>
    public string Bolla { get; set; } = string.Empty;
    public string NsDsc { get; set; } = string.Empty;
    public string MaDsc { get; set; } = string.Empty;
    public DateTime? DtExp { get; set; }
    public string? NsNot { get; set; }
    public decimal? Tempo { get; set; }
    public int? Ripet { get; set; }
    public string? Placche { get; set; }
}
