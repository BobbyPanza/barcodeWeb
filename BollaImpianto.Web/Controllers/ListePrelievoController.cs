using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Drawing.Printing;
using Microsoft.AspNetCore.Http;
using BollaImpianto.Web.Models;
using BollaImpianto.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace BollaImpianto.Web.Controllers;

[Authorize]
public sealed class ListePrelievoController(
    IBollaImpiantoRepository repository,
    IOptions<PrinterManagerOptions> printerManagerOptions,
    IHttpClientFactory httpClientFactory) : Controller
{
    private const string ListeFilterCookieName = "BollaImpianto.ListeNascondiCompletate";

    private readonly PrinterManagerOptions _printerOptions = printerManagerOptions.Value;

    [HttpGet]
    public async Task<IActionResult> Index(int? listaId, string? search, string? printerName, bool? nascondiCompletate, CancellationToken cancellationToken = default)
    {
        bool nascondiEffettivo;
        if (nascondiCompletate.HasValue)
        {
            nascondiEffettivo = nascondiCompletate.Value;
            WriteListeFilterCookie(nascondiEffettivo);
        }
        else
        {
            nascondiEffettivo = ReadListeFilterCookie() ?? true;
        }

        var liste = await repository.GetListePrelievoAsync(nascondiEffettivo, cancellationToken);
        var selected = listaId;
        if (selected.HasValue && liste.All(l => l.Id != selected.Value))
        {
            selected = null;
        }

        var currentUser = User.Identity?.Name?.Trim() ?? string.Empty;
        var operatoreLista = selected.HasValue
            ? liste.FirstOrDefault(l => l.Id == selected.Value)?.Operatore?.Trim()
            : null;
        var canEliminare = selected.HasValue
            && !string.IsNullOrEmpty(currentUser)
            && string.Equals(operatoreLista, currentUser, StringComparison.OrdinalIgnoreCase);

        var stampanti = GetInstalledPrinters();

        var model = new ListaPrelievoPageViewModel
        {
            Liste = liste,
            NascondiListeCompletate = nascondiEffettivo,
            CanEliminareListaSelezionata = canEliminare,
            ListaSelezionataId = selected,
            OperatoreNuovaLista = User.Identity?.Name
                ?? User.FindFirst("operator_description")?.Value
                ?? string.Empty,
            SearchNonAssigned = search,
            PrinterName = printerName,
            StampantiDisponibili = stampanti,
            LastResult = TempData["ResultMessage"]?.ToString()
        };

        if (selected.HasValue)
        {
            model.PianiAssegnati = await repository.GetPianiAssegnatiListaAsync(selected.Value, cancellationToken);
            model.PianiNonAssegnati = await repository.GetPianiNonAssegnatiListaAsync(selected.Value, search, cancellationToken);
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NuovaLista(string operatore, string? stato, bool nascondiCompletate, CancellationToken cancellationToken)
    {
        operatore = string.IsNullOrWhiteSpace(operatore)
            ? (User.Identity?.Name ?? string.Empty)
            : operatore.Trim();

        if (string.IsNullOrWhiteSpace(operatore))
        {
            TempData["ResultMessage"] = "Operatore obbligatorio.";
            return RedirectToAction(nameof(Index), new { nascondiCompletate });
        }

        var id = await repository.CreateListaPrelievoAsync(operatore, string.IsNullOrWhiteSpace(stato) ? "Aperta" : stato, cancellationToken);
        TempData["ResultMessage"] = $"Lista {id} creata.";
        return RedirectToAction(nameof(Index), new { listaId = id, nascondiCompletate });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssegnaDaBarcode(int listaId, string barcodeBolla, string? search, string? printerName, bool nascondiCompletate, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(barcodeBolla))
        {
            TempData["ResultMessage"] = "Barcode/bolla obbligatorio.";
            return RedirectToAction(nameof(Index), new { listaId, search, printerName, nascondiCompletate });
        }

        var ok = await repository.AddPianoToListaByBollaAsync(listaId, barcodeBolla, cancellationToken);
        TempData["ResultMessage"] = ok
            ? $"Bolla {barcodeBolla} assegnata alla lista {listaId}."
            : $"Nessun piano trovato per bolla {barcodeBolla}.";
        return RedirectToAction(nameof(Index), new { listaId, search, printerName, nascondiCompletate });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssegnaManuale(int listaId, int idNes, string? search, string? printerName, bool nascondiCompletate, CancellationToken cancellationToken)
    {
        await repository.AddPianoToListaAsync(listaId, idNes, cancellationToken);
        TempData["ResultMessage"] = $"Piano {idNes} assegnato.";
        return RedirectToAction(nameof(Index), new { listaId, search, printerName, nascondiCompletate });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disabbina(int listaId, int idNes, string? search, string? printerName, bool nascondiCompletate, CancellationToken cancellationToken)
    {
        await repository.RemovePianoFromListaAsync(listaId, idNes, cancellationToken);
        TempData["ResultMessage"] = $"Piano {idNes} disabbinato.";
        return RedirectToAction(nameof(Index), new { listaId, search, printerName, nascondiCompletate });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AggiornaPianoLista(int listaId, int idNes, string? dtExp, string? nsNot, string? search, string? printerName, bool nascondiCompletate, CancellationToken cancellationToken)
    {
        DateTime? parsedDate = null;
        if (!string.IsNullOrWhiteSpace(dtExp))
        {
            var formats = new[] { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm" };
            if (!DateTime.TryParseExact(dtExp, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var localDate))
            {
                TempData["ResultMessage"] = "Formato data non valido.";
                return RedirectToAction(nameof(Index), new { listaId, search, printerName, nascondiCompletate });
            }

            parsedDate = localDate;
        }

        await repository.UpdatePianoLavoroAsync(idNes, parsedDate, nsNot, cancellationToken);
        TempData["ResultMessage"] = $"Piano {idNes} aggiornato.";
        return RedirectToAction(nameof(Index), new { listaId, search, printerName, nascondiCompletate });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EliminaLista(int listaId, string? search, string? printerName, bool nascondiCompletate, CancellationToken cancellationToken)
    {
        var user = User.Identity?.Name ?? string.Empty;
        var ok = await repository.DeleteListaPrelievoAsync(listaId, user, cancellationToken);
        TempData["ResultMessage"] = ok
            ? $"Lista {listaId} eliminata (righe e testata)."
            : "Impossibile eliminare la lista: solo il creatore può eliminarla, oppure lista non trovata.";
        return RedirectToAction(nameof(Index), new { listaId = (int?)null, search, printerName, nascondiCompletate });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StampaLista(int listaId, string? search, string? printerName, bool nascondiCompletate, CancellationToken cancellationToken)
    {
        var ok = await SendPrintAsync(
            _printerOptions.NomeReportLista,
            new { IDLista = listaId },
            $"Stampa lista prelievo {listaId}",
            printerName,
            cancellationToken);

        TempData["ResultMessage"] = ok ? $"Stampa lista {listaId} inviata." : "Errore stampa lista.";
        return RedirectToAction(nameof(Index), new { listaId, search, printerName, nascondiCompletate });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StampaEtichette(int listaId, string? search, string? printerName, bool nascondiCompletate, CancellationToken cancellationToken)
    {
        var ok = await SendPrintAsync(
            _printerOptions.NomeReportEtichetta,
            new { IDLista = listaId },
            $"Stampa etichette lista {listaId}",
            printerName,
            cancellationToken);

        TempData["ResultMessage"] = ok ? $"Stampa etichette lista {listaId} inviata." : "Errore stampa etichette lista.";
        return RedirectToAction(nameof(Index), new { listaId, search, printerName, nascondiCompletate });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> StampaEtichettaSingola(int listaId, int idNes, string? search, string? printerName, bool nascondiCompletate, CancellationToken cancellationToken)
    {
        var ok = await SendPrintAsync(
            _printerOptions.NomeReportEtichettaSingola,
            new { IDLista = listaId, IDNesting = idNes },
            $"Stampa etichetta singola nesting {idNes}",
            printerName,
            cancellationToken);

        TempData["ResultMessage"] = ok ? $"Stampa etichetta nesting {idNes} inviata." : $"Errore stampa etichetta nesting {idNes}.";
        return RedirectToAction(nameof(Index), new { listaId, search, printerName, nascondiCompletate });
    }

    private async Task<bool> SendPrintAsync(
        string printModelCode,
        object printParameters,
        string description,
        string? printerName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_printerOptions.IndirizzoPrinterManager) || string.IsNullOrWhiteSpace(printModelCode))
        {
            return false;
        }

        var userCode = User.Identity?.Name ?? "WEB";
        var requestPayload = new
        {
            opts = new
            {
                copyQuantities = 1,
                jsonParams = JsonSerializer.Serialize(printParameters),
                userCode,
                printModelCode,
                printerName = string.IsNullOrWhiteSpace(printerName) ? null : printerName.Trim(),
                description_1 = description
            }
        };

        var httpClient = httpClientFactory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Post, $"{_printerOptions.IndirizzoPrinterManager.TrimEnd('/')}/PrintController");
        request.Headers.Add("x-http-method-override", "PrintReport");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(requestPayload), Encoding.UTF8, "application/json");

        try
        {
            var response = await httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static IReadOnlyList<string> GetInstalledPrinters()
    {
        try
        {
            var printers = new List<string>();
            foreach (string printer in PrinterSettings.InstalledPrinters)
            {
                printers.Add(printer);
            }

            return printers.OrderBy(x => x).ToList();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Ricorda l'ultima scelta "nascondi completate" / "mostra tutte" tra una visita e l'altra (es. link dal menu).</summary>
    private bool? ReadListeFilterCookie()
    {
        if (!Request.Cookies.TryGetValue(ListeFilterCookieName, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (raw == "1" || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (raw == "0" || string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return null;
    }

    private void WriteListeFilterCookie(bool nascondiCompletate)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            MaxAge = TimeSpan.FromDays(400),
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps
        };
        Response.Cookies.Append(ListeFilterCookieName, nascondiCompletate ? "1" : "0", options);
    }
}
