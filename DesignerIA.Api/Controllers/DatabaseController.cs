using DesignerIA.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace DesignerIA.Api.Controllers;

[ApiController]
[Route("api/database")]
public class DatabaseController : ControllerBase
{
    private readonly GestionEngineHealthService _healthService;
    private readonly ILogger<DatabaseController> _logger;

    public DatabaseController(GestionEngineHealthService healthService, ILogger<DatabaseController> logger)
    {
        _healthService = healthService;
        _logger = logger;
    }

    [HttpGet("health")]
    public async Task<IActionResult> GetHealth(CancellationToken cancellationToken)
    {
        try
        {
            var status = await _healthService.CheckHealthAsync(cancellationToken);
            return Ok(status);
        }
        catch (SqlException ex)
        {
            _logger.LogWarning(ex, "No se pudo conectar a GestionEngine local.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "error", message = "Base de datos no disponible." });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Configuración inválida para GestionEngine local.");
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { status = "error", message = "Base de datos no disponible." });
        }
    }
}
