namespace BollaImpianto.Web.Models;

public sealed class ListaPrelievoPageViewModel
{
    public IReadOnlyList<ListaPrelievoHeader> Liste { get; set; } = [];
    /// <summary>Se true, le liste con stato "completata/chiusa" non compaiono nel menu.</summary>
    public bool NascondiListeCompletate { get; set; } = true;
    /// <summary>True se l'utente corrente può eliminare la lista selezionata (solo creatore).</summary>
    public bool CanEliminareListaSelezionata { get; set; }
    public int? ListaSelezionataId { get; set; }
    public string OperatoreNuovaLista { get; set; } = string.Empty;
    public string StatoNuovaLista { get; set; } = "Aperta";
    public string? BarcodeBolla { get; set; }
    public string? SearchNonAssigned { get; set; }
    public IReadOnlyList<ListaPrelievoPiano> PianiAssegnati { get; set; } = [];
    public IReadOnlyList<ListaPrelievoPiano> PianiNonAssegnati { get; set; } = [];
    public IReadOnlyList<string> StampantiDisponibili { get; set; } = [];
    public string? PrinterName { get; set; }
    public string? LastResult { get; set; }
}
