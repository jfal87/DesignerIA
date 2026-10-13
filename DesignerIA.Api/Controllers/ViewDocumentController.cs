using DesignerIA.Api.Services.ViewDocuments;
using DesignerIA.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DesignerIA.Api.Controllers;

/// <summary>
/// Prepara y crea una vista a partir de un documento JSONMetadata. Recibe el
/// texto del documento, nunca SQL; no interviene el GitHub Copilot SDK.
/// </summary>
[ApiController]
[Route("api/view-json")]
[Authorize]
public class ViewDocumentController : ControllerBase
{
    private readonly JsonMetadataService _service;

    public ViewDocumentController(JsonMetadataService service)
    {
        _service = service;
    }

    [HttpPost("prepare")]
    public async Task<IActionResult> Prepare([FromBody] JsonMetadataRequest? request, CancellationToken cancellationToken)
    {
        return Ok(await _service.PrepareAsync(request?.Content, User.Identity?.Name, cancellationToken));
    }

    /// <summary>
    /// Crea la vista. No recibe el token de la petición a propósito: cerrar el
    /// navegador no cancela una creación en curso.
    /// </summary>
    [HttpPost("create")]
    public async Task<IActionResult> Create([FromBody] JsonMetadataRequest? request)
    {
        var result = await _service.CreateAsync(request?.Content, User);
        return result.Status switch
        {
            ViewCreationStatus.NotAllowed => StatusCode(StatusCodes.Status403Forbidden, result),
            ViewCreationStatus.Busy => Conflict(result),
            _ => Ok(result)
        };
    }
}
