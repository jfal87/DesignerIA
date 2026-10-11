using DesignerIA.Api.Services.ViewScripts;
using DesignerIA.Api.Services.ViewSpecs;
using DesignerIA.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DesignerIA.Api.Controllers;

/// <summary>
/// Genera un script SQL de sólo texto (nunca ejecutado) a partir de un
/// <see cref="ViewSpec"/> ya construido, mediante código C# determinístico. No
/// interviene el GitHub Copilot SDK en este endpoint.
/// </summary>
[ApiController]
[Route("api/view-script")]
[Authorize]
public class ViewScriptController : ControllerBase
{
    private readonly ILogger<ViewScriptController> _logger;
    private readonly ViewCreationPreflightService _preflightService;

    public ViewScriptController(
        ILogger<ViewScriptController> logger,
        ViewCreationPreflightService preflightService)
    {
        _logger = logger;
        _preflightService = preflightService;
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] ViewSpec? spec, CancellationToken cancellationToken)
    {
        var identity = User.Identity;
        _logger.LogInformation(
            "ViewScript generation request authentication. IsAuthenticated: {IsAuthenticated}. Name: {Name}. AuthenticationType: {AuthenticationType}",
            identity?.IsAuthenticated ?? false,
            identity?.Name,
            identity?.AuthenticationType);

        if (spec is null)
        {
            return BadRequest(new { message = "El cuerpo debe incluir un ViewSpec." });
        }

        var startedAt = DateTime.UtcNow;
        var normalizedSpec = ViewSpecNormalizer.Normalize(spec);
        var viewSpecValidation = ViewSpecValidator.Validate(normalizedSpec);
        var auditUser = identity is { IsAuthenticated: true } ? identity.Name : null;
        var generationGaps = ViewScriptGenerationValidator.Validate(normalizedSpec, viewSpecValidation);
        var preflight = generationGaps.Count == 0
            ? await _preflightService.CheckAsync(normalizedSpec, cancellationToken)
            : null;
        var result = ViewScriptGenerator.Generate(normalizedSpec, preflight, auditUser);
        var elapsedMs = (DateTime.UtcNow - startedAt).TotalMilliseconds;

        _logger.LogInformation(
            "ViewScript generation completed in {ElapsedMs} ms. CanGenerate: {CanGenerate}. States: {States}. Rows: {Rows}. Areas: {Areas}. Handlers: {Handlers}. Gaps: {GapCount}. ScriptLength: {ScriptLength}",
            elapsedMs,
            result.CanGenerate,
            result.Summary.States,
            result.Summary.Rows,
            result.Summary.Areas,
            result.Summary.Handlers,
            result.Gaps.Count,
            result.Script?.Length ?? 0);

        return Ok(result);
    }
}
