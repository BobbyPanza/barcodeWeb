using BollaImpianto.Web.Models;

namespace BollaImpianto.Web.Services;

public interface IBollaImpiantoRepository
{
    Task<OperatoreAuth?> ValidateOperatoreAsync(string opCod, string password, CancellationToken cancellationToken);
    Task<IReadOnlyList<PianoLavoroRow>> GetPianiAsync(string? search, bool onlyMine, string? operatore, CancellationToken cancellationToken);
    Task<string> ExecuteBollaImpiantoAsync(string bolla, string impianto, string operatore, CancellationToken cancellationToken);
    Task UpdatePianoLavoroAsync(int idNes, DateTime? dataAssegnazione, string? note, CancellationToken cancellationToken);
    /// <summary>Imposta macchina generica plasma (MACOD) sul piano.</summary>
    Task SetMacchinaGenericaPlasmaAsync(int idNes, CancellationToken cancellationToken);
    Task<IReadOnlyList<ListaPrelievoHeader>> GetListePrelievoAsync(bool hideCompleted, CancellationToken cancellationToken);
    Task<bool> DeleteListaPrelievoAsync(int idLista, string operatore, CancellationToken cancellationToken);
    Task<int> CreateListaPrelievoAsync(string operatore, string stato, CancellationToken cancellationToken);
    Task<IReadOnlyList<ListaPrelievoPiano>> GetPianiAssegnatiListaAsync(int idLista, CancellationToken cancellationToken);
    Task<IReadOnlyList<ListaPrelievoPiano>> GetPianiNonAssegnatiListaAsync(int idLista, string? search, CancellationToken cancellationToken);
    Task<bool> AddPianoToListaByBollaAsync(int idLista, string bolla, CancellationToken cancellationToken);
    Task AddPianoToListaAsync(int idLista, int idNes, CancellationToken cancellationToken);
    Task RemovePianoFromListaAsync(int idLista, int idNes, CancellationToken cancellationToken);
}
