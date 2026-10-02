using DesignerIA.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DesignerIA.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public ActionResult<HealthStatus> Get()
    {
        return Ok(new HealthStatus("ok", "DesignerIA.Api"));
    }

    [HttpGet("identity")]
    [Authorize]
    public ActionResult<IdentityStatus> GetIdentity()
    {
        return Ok(new IdentityStatus(
            User.Identity?.IsAuthenticated ?? false,
            User.Identity?.Name,
            User.Identity?.AuthenticationType));
    }
}
