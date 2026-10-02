using DesignerIA.Api.Services.Knowledge;
using Microsoft.AspNetCore.Mvc;

namespace DesignerIA.Api.Controllers;

/// <summary>
/// Operación local/de desarrollo para reconstruir el índice de KnowledgeSource bajo
/// demanda. No expone contenido de documentos, sólo un resumen de conteos.
/// </summary>
[ApiController]
[Route("api/knowledge")]
public class KnowledgeController : ControllerBase
{
    private readonly KnowledgeIndexService _knowledgeIndexService;
    private readonly KnowledgeSearchService _knowledgeSearchService;

    public KnowledgeController(KnowledgeIndexService knowledgeIndexService, KnowledgeSearchService knowledgeSearchService)
    {
        _knowledgeIndexService = knowledgeIndexService;
        _knowledgeSearchService = knowledgeSearchService;
    }

    [HttpPost("rebuild")]
    public async Task<IActionResult> Rebuild(CancellationToken cancellationToken)
    {
        var result = await _knowledgeIndexService.RebuildAsync(cancellationToken);

        if (result.Status != "ok")
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, result);
        }

        return Ok(result);
    }

    /// <summary>
    /// Endpoint local/de desarrollo para validar la búsqueda léxica sin gastar
    /// llamadas de Copilot. Mismo servicio que usa la custom tool search_knowledge.
    /// </summary>
    [HttpGet("search")]
    public IActionResult Search([FromQuery] string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return BadRequest("query es requerido.");
        }

        var results = _knowledgeSearchService.Search(query);
        return Ok(results);
    }
}
