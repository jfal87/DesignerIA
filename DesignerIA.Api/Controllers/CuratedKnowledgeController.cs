using DesignerIA.Api.Services.CuratedKnowledge;
using DesignerIA.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace DesignerIA.Api.Controllers;

/// <summary>
/// "Enseñar a DesignerIA": endpoints delgados sobre <see cref="CuratedKnowledgeService"/>.
/// Crear siempre produce Pending; sólo Approved participa en search_knowledge (a
/// través de KnowledgeSearchService, sin una tool nueva de Copilot).
/// </summary>
[ApiController]
[Route("api/knowledge/curated")]
public class CuratedKnowledgeController : ControllerBase
{
    private readonly CuratedKnowledgeService _curatedKnowledgeService;

    public CuratedKnowledgeController(CuratedKnowledgeService curatedKnowledgeService)
    {
        _curatedKnowledgeService = curatedKnowledgeService;
    }

    [HttpPost]
    public IActionResult Create([FromBody] CuratedKnowledgeCreateRequest? request)
    {
        if (request is null)
        {
            return BadRequest(new CuratedKnowledgeResult("error", null, "El cuerpo de la solicitud es requerido."));
        }

        var result = _curatedKnowledgeService.Create(request);
        return result.Status == "ok" ? Ok(result) : BadRequest(result);
    }

    [HttpGet]
    public IActionResult List([FromQuery] string status = "Pending")
    {
        if (!Enum.TryParse<CuratedKnowledgeStatus>(status, ignoreCase: true, out var parsedStatus))
        {
            return BadRequest(new CuratedKnowledgeListResult("error", [], "status debe ser Pending, Approved o Rejected."));
        }

        return Ok(_curatedKnowledgeService.List(parsedStatus));
    }

    [HttpPost("{id}/approve")]
    public IActionResult Approve(string id, [FromBody] CuratedKnowledgeReviewRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ReviewedBy))
        {
            return BadRequest(new CuratedKnowledgeResult("error", null, "ReviewedBy es requerido."));
        }

        var result = _curatedKnowledgeService.Approve(id, request.ReviewedBy);
        return result.Status == "ok" ? Ok(result) : BadRequest(result);
    }

    [HttpPost("{id}/reject")]
    public IActionResult Reject(string id, [FromBody] CuratedKnowledgeReviewRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ReviewedBy))
        {
            return BadRequest(new CuratedKnowledgeResult("error", null, "ReviewedBy es requerido."));
        }

        var result = _curatedKnowledgeService.Reject(id, request.ReviewedBy);
        return result.Status == "ok" ? Ok(result) : BadRequest(result);
    }
}
