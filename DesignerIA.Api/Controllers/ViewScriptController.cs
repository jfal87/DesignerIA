using DesignerIA.Api.Services.ViewScripts;
using DesignerIA.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DesignerIA.Api.Controllers;

/// <summary>
/// Genera un script SQL de sólo texto a partir de un <see cref="ViewSpec"/> ya
/// construido, mediante código C# determinístico, y permite crear la vista cuando
/// la creación está habilitada. No interviene el GitHub Copilot SDK en este
/// controller y ningún endpoint acepta SQL del cliente.
/// </summary>
[ApiController]
[Route("api/view-script")]
[Authorize]
public class ViewScriptController : ControllerBase
{
    private readonly ILogger<ViewScriptController> _logger;
    private readonly ViewCreationService _creationService;

    public ViewScriptController(
        ILogger<ViewScriptController> logger,
        ViewCreationService creationService)
    {
        _logger = logger;
        _creationService = creationService;
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
        var auditUser = identity is { IsAuthenticated: true } ? identity.Name : null;
        var result = await _creationService.GenerateAsync(spec, auditUser, cancellationToken);
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

    /// <summary>
    /// Crea la vista. No recibe el token de la petición a propósito: cerrar el
    /// navegador no cancela una creación en curso.
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] ViewSpec? spec)
    {
        if (spec is null)
        {
            return BadRequest(new { message = "El cuerpo debe incluir un ViewSpec." });
        }

        var result = await _creationService.CreateAsync(spec, User);
        return result.Status switch
        {
            ViewCreationStatus.NotAllowed => StatusCode(StatusCodes.Status403Forbidden, result),
            ViewCreationStatus.Busy => Conflict(result),
            _ => Ok(result)
        };
    }
}
