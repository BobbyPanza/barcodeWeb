namespace BollaImpianto.Web.Models;

public sealed class ListaPrelievoHeader
{
    public int Id { get; set; }
    public DateTime DataCreazione { get; set; }
    public string Operatore { get; set; } = string.Empty;
    public string Stato { get; set; } = string.Empty;
}
