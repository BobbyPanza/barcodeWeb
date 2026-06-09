namespace BollaImpianto.Web.Models;

public sealed class BollaPageViewModel
{
    public string Bolla { get; set; } = string.Empty;
    public string Impianto { get; set; } = string.Empty;
    public string? Search { get; set; }
    public bool OnlyMine { get; set; }
    public string? LastResult { get; set; }
    public IReadOnlyList<PianoLavoroRow> Rows { get; set; } = [];
}
