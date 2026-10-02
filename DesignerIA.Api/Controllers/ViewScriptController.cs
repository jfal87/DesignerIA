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
        ViewScriptGenerationResult result;

        if (!viewSpecValidation.IsValid)
        {
            result = ViewScriptGenerator.Generate(normalizedSpec, viewSpecValidation, auditUser);
        }
        else
        {
            var plan = ViewCreationPlanFactory.Create(normalizedSpec);
            var preflight = await _preflightService.CheckNameAsync(normalizedSpec.Name!, cancellationToken);
            if (!preflight.IsAvailable)
            {
                result = new ViewScriptGenerationResult(
                    CanGenerate: false,
                    Script: null,
                    Gaps: [new ViewScriptGenerationGap(
                        "Nombre de vista existente",
                        "Ya existe una vista con el nombre indicado; no se generó un script que pueda sobrescribirla.")],
                    Summary: BuildSummary(normalizedSpec),
                    Preflight: preflight);
            }
            else
            {
                result = ViewScriptGenerator.Generate(plan, BuildSummary(normalizedSpec), auditUser) with { Preflight = preflight };
            }
        }
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

    private static ViewScriptGenerationSummary BuildSummary(ViewSpec spec)
    {
        var states = spec.States ?? [];
        var rows = states.SelectMany(state => state.Rows ?? []).ToList();
        var areas = rows.SelectMany(row => row.Areas ?? []).ToList();
        var handlers = areas.SelectMany(area => area.Handlers ?? []).ToList();
        return new ViewScriptGenerationSummary(1, states.Count, rows.Count, areas.Count, handlers.Count);
    }
}
