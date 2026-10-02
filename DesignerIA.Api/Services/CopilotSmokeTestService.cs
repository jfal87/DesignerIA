using System.Diagnostics;
using DesignerIA.Contracts;
using GitHub.Copilot;

namespace DesignerIA.Api.Services;

/// <summary>
/// Prueba mínima y controlada del GitHub Copilot SDK. Reutiliza el usuario ya
/// autenticado en Copilot CLI, no expone tools ni permisos automáticos, y usa
/// un límite bajo de AI Credits. La sesión es corta y se libera al terminar.
/// </summary>
public class CopilotSmokeTestService
{
    private const string FixedPrompt = "Responde exactamente: DesignerIA Copilot SDK OK";

    // El runtime de Copilot CLI rechaza sesiones con un límite menor a 30 AI Credits
    // ("Minimum session limit is 30 AI credits"), por lo que se usa el mínimo permitido
    // como protección de costo para este smoke test.
    private const double MaxAiCredits = 30;

    private readonly ILogger<CopilotSmokeTestService> _logger;

    public CopilotSmokeTestService(ILogger<CopilotSmokeTestService> logger)
    {
        _logger = logger;
    }

    public async Task<CopilotTestResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Copilot smoke test started");

        try
        {
            await using var client = new CopilotClient(new CopilotClientOptions
            {
                UseLoggedInUser = true,
            });
            await client.StartAsync();

            await using var session = await client.CreateSessionAsync(new SessionConfig
            {
                AvailableTools = new List<string>(),
#pragma warning disable GHCP001 // SessionLimits está en evaluación en el SDK; se usa deliberadamente como protección de costo (AGENTS.md).
                SessionLimits = new SessionLimitsConfig
                {
                    MaxAiCredits = MaxAiCredits,
                },
#pragma warning restore GHCP001
            });

            var message = await session.SendAndWaitAsync(new MessageOptions
            {
                Prompt = FixedPrompt,
            });

            stopwatch.Stop();

            if (message is null)
            {
                _logger.LogWarning("Copilot smoke test completed in {ElapsedMs} ms without a response", stopwatch.ElapsedMilliseconds);
                return new CopilotTestResult("error", null, "Copilot no devolvió respuesta.");
            }

            _logger.LogInformation("Copilot smoke test completed in {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
            return new CopilotTestResult("ok", message.Data.Content, null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "Copilot smoke test failed after {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
            return new CopilotTestResult("error", null, "No se pudo completar la prueba de Copilot.");
        }
    }
}
