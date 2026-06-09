using System.Globalization;
using System.Text;
using BollaImpianto.Web.Models;
using BollaImpianto.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BollaImpianto.Web.Controllers;

 [Authorize]
public sealed class BollaController(IBollaImpiantoRepository repository) : Controller
{
    private const string SoloAssegnazioniMieCookieName = "BollaImpianto.SoloAssegnazioniMie";

    [HttpGet]
    public async Task<IActionResult> Index(string? search, bool? onlyMine, CancellationToken cancellationToken)
    {
        bool onlyMineEffettivo;
        if (onlyMine.HasValue)
        {
            onlyMineEffettivo = onlyMine.Value;
            WriteSoloAssegnazioniMieCookie(onlyMineEffettivo);
        }
        else
        {
            onlyMineEffettivo = ReadSoloAssegnazioniMieCookie() ?? false;
        }

        var operatore = User.Identity?.Name;
        var model = new BollaPageViewModel
        {
            Search = search,
            OnlyMine = onlyMineEffettivo,
            Rows = await repository.GetPianiAsync(search, onlyMineEffettivo, operatore, cancellationToken),
            LastResult = TempData["ResultMessage"]?.ToString()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(BollaPageViewModel model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model.Bolla) || string.IsNullOrWhiteSpace(model.Impianto))
        {
            TempData["ResultMessage"] = "Inserire sia bolla che impianto.";
            return RedirectToAction(nameof(Index), new { search = model.Search, onlyMine = model.OnlyMine });
        }

        var operatore = User.Identity?.Name ?? string.Empty;
        var result = await repository.ExecuteBollaImpiantoAsync(model.Bolla, model.Impianto, operatore, cancellationToken);
        TempData["ResultMessage"] = result;
        return RedirectToAction(nameof(Index), new { search = model.Search, onlyMine = model.OnlyMine });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(int idNes, string? dtExp, string? nsNot, string? search, bool onlyMine, CancellationToken cancellationToken)
    {
        DateTime? parsedDate = null;
        if (!string.IsNullOrWhiteSpace(dtExp))
        {
            var formats = new[] { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm" };
            if (!DateTime.TryParseExact(dtExp, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var localDate))
            {
                TempData["ResultMessage"] = "Formato data non valido.";
                return RedirectToAction(nameof(Index), new { search, onlyMine });
            }

            parsedDate = localDate;
        }

        await repository.UpdatePianoLavoroAsync(idNes, parsedDate, nsNot, cancellationToken);
        TempData["ResultMessage"] = $"Piano {idNes} aggiornato.";
        return RedirectToAction(nameof(Index), new { search, onlyMine });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ClearDate(int idNes, string? nsNot, string? search, bool onlyMine, CancellationToken cancellationToken)
    {
        await repository.UpdatePianoLavoroAsync(idNes, null, nsNot, cancellationToken);
        TempData["ResultMessage"] = $"Data svuotata per ID {idNes}.";
        return RedirectToAction(nameof(Index), new { search, onlyMine });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImpostaMacchinaGenerica(int idNes, string? search, bool onlyMine, CancellationToken cancellationToken)
    {
        await repository.SetMacchinaGenericaPlasmaAsync(idNes, cancellationToken);
        TempData["ResultMessage"] = $"Macchina impostata su DLG-ALL_PLASMA per piano {idNes}.";
        return RedirectToAction(nameof(Index), new { search, onlyMine });
    }

    [HttpGet]
    public async Task<IActionResult> ExportCsv(string? search, CancellationToken cancellationToken)
    {
        var rows = await repository.GetPianiAsync(search, false, User.Identity?.Name, cancellationToken);
        var csv = new StringBuilder();
        csv.AppendLine("Bolla;IDNES;NSDSC;MADSC;DTEXP;NSNOT;TEMPO;RIPET");
        foreach (var row in rows)
        {
            csv.AppendLine(
                $"{Escape(row.Bolla)};{row.IdNes};{Escape(row.NsDsc)};{Escape(row.MaDsc)};{row.DtExp:dd/MM/yyyy HH:mm:ss};{Escape(row.NsNot)};{row.Tempo};{row.Ripet}");
        }

        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"piani-lavoro-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var sanitized = value.Replace("\"", "\"\"");
        return $"\"{sanitized}\"";
    }

    private bool? ReadSoloAssegnazioniMieCookie()
    {
        if (!Request.Cookies.TryGetValue(SoloAssegnazioniMieCookieName, out var raw) || string.IsNullOrWhiteSpace(raw))
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

    private void WriteSoloAssegnazioniMieCookie(bool onlyMine)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            MaxAge = TimeSpan.FromDays(400),
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps
        };
        Response.Cookies.Append(SoloAssegnazioniMieCookieName, onlyMine ? "1" : "0", options);
    }
}
