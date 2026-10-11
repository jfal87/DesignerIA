using DesignerIA.Api.Services.ViewSpecs;
using DesignerIA.Api.Services.ViewTemplates;
using DesignerIA.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DesignerIA.Api.Controllers;

[ApiController]
[Route("api/view-templates")]
[Authorize]
public class ViewTemplateController : ControllerBase
{
    [HttpPost("grid-query")]
    public IActionResult GridQuery([FromBody] GridQueryTemplateRequest? request)
    {
        if (request is null)
        {
            return BadRequest(new { message = "El cuerpo debe incluir los datos de la plantilla." });
        }
        var spec = ViewSpecNormalizer.Normalize(GridQueryTemplate.Create(request));
        return Ok(new ViewSpecResult(spec, ViewSpecValidator.Validate(spec)));
    }
}
