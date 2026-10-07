using DesignerIA.Api.Services;
using DesignerIA.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace DesignerIA.Api.Controllers;

[ApiController]
[Route("api/copilot")]
public class CopilotController : ControllerBase
{
    private const int MaxMessageLength = 500;

    private readonly CopilotSmokeTestService _copilotSmokeTestService;
    private readonly CopilotMetadataTestService _copilotMetadataTestService;
    private readonly CopilotChatService _copilotChatService;

    public CopilotController(
        CopilotSmokeTestService copilotSmokeTestService,
        CopilotMetadataTestService copilotMetadataTestService,
        CopilotChatService copilotChatService)
    {
        _copilotSmokeTestService = copilotSmokeTestService;
        _copilotMetadataTestService = copilotMetadataTestService;
        _copilotChatService = copilotChatService;
    }

    [HttpPost("test")]
    public async Task<IActionResult> Test(CancellationToken cancellationToken)
    {
        var result = await _copilotSmokeTestService.RunAsync(cancellationToken);

        if (result.Status != "ok")
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, result);
        }

        return Ok(result);
    }

    [HttpPost("metadata-test")]
    public async Task<IActionResult> MetadataTest(CancellationToken cancellationToken)
    {
        var result = await _copilotMetadataTestService.RunAsync(cancellationToken);

        if (result.Status != "ok")
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, result);
        }

        return Ok(result);
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest? request, CancellationToken cancellationToken)
    {
        var message = request?.Message?.Trim();

        if (string.IsNullOrWhiteSpace(message))
        {
            return BadRequest(new ChatResult("error", null, false, "El mensaje es obligatorio."));
        }

        if (message.Length > MaxMessageLength)
        {
            return BadRequest(new ChatResult("error", null, false, $"El mensaje no puede superar {MaxMessageLength} caracteres."));
        }

        var conversationId = request?.ConversationId?.Trim();
        if (!Guid.TryParse(conversationId, out _))
        {
            return BadRequest(new ChatResult("error", null, false, "El identificador de conversación no es válido."));
        }

        var result = await _copilotChatService.RunAsync(message, conversationId, cancellationToken);

        if (result.Status != "ok")
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, result);
        }

        return Ok(result);
    }
}
