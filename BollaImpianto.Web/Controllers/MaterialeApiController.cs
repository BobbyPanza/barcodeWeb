using BollaImpianto.Web.Models;
using BollaImpianto.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace BollaImpianto.Web.Controllers;

[ApiKeyOrCookie]
[ApiController]
[Route("api/materiale")]
public sealed class MaterialeApiController(IBollaImpiantoRepository repository) : ControllerBase
{
    /// <summary>Anomalie, materiale minimo richiesto e lamiere compatibili disponibili.</summary>
    [HttpGet("analisi")]
    public Task<ActionResult<MaterialAnalysisResult>> Analisi(
        string? bolla,
        int? idNesting,
        string? codiceNesting,
        bool soloDisponibili = false,
        CancellationToken cancellationToken = default)
        => EseguiAsync(new MaterialAnalysisRequest
        {
            Bolla = bolla,
            IdNesting = idNesting,
            CodiceNesting = codiceNesting,
            SoloDisponibili = soloDisponibili
        }, cancellationToken);

    /// <summary>Come <see cref="Analisi"/>, ma accetta anche una lista esplicita di lavorazioni (IDLAV).</summary>
    [HttpPost("analisi")]
    public Task<ActionResult<MaterialAnalysisResult>> Analisi(
        [FromBody] MaterialAnalysisRequest request,
        CancellationToken cancellationToken = default)
        => EseguiAsync(request, cancellationToken);

    /// <summary>Verifica se una rintracciabilita' e' idonea, elencando le caratteristiche non conformi.</summary>
    [HttpGet("verifica")]
    public Task<ActionResult<MaterialAnalysisResult>> Verifica(
        string rintracciabilita,
        string? bolla,
        int? idNesting,
        string? codiceNesting,
        CancellationToken cancellationToken = default)
        => EseguiAsync(new MaterialAnalysisRequest
        {
            Bolla = bolla,
            IdNesting = idNesting,
            CodiceNesting = codiceNesting,
            Rintracciabilita = rintracciabilita
        }, cancellationToken);

    [HttpPost("verifica")]
    public Task<ActionResult<MaterialAnalysisResult>> Verifica(
        [FromBody] MaterialAnalysisRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Rintracciabilita))
        {
            return Task.FromResult<ActionResult<MaterialAnalysisResult>>(
                BadRequest(new { errore = "Rintracciabilita' obbligatoria." }));
        }

        return EseguiAsync(request, cancellationToken);
    }

    private async Task<ActionResult<MaterialAnalysisResult>> EseguiAsync(
        MaterialAnalysisRequest request,
        CancellationToken cancellationToken)
    {
        var hasInput = !string.IsNullOrWhiteSpace(request.Bolla)
            || request.IdNesting.HasValue
            || !string.IsNullOrWhiteSpace(request.CodiceNesting)
            || request.Lavorazioni is { Count: > 0 };

        if (!hasInput)
        {
            return BadRequest(new { errore = "Indicare almeno una tra: bolla, idNesting, codiceNesting, lavorazioni." });
        }

        var result = await repository.AnalizzaMaterialeAsync(request, cancellationToken);
        return Ok(result);
    }
}
