using System.Diagnostics;
using DesignerIA.Contracts;
using GitHub.Copilot;

namespace DesignerIA.Api.Services;

/// <summary>
/// Prueba controlada del patrón Copilot -> custom tool -> servicio C# -> GestionEngine.
/// Copilot NUNCA recibe acceso SQL directo: la única capacidad expuesta es la tool
/// "get_gestionengine_summary" (definida una única vez en <see cref="GestionEngineSummaryTool"/>),
/// que no acepta parámetros y llama al servicio de solo lectura ya validado en el
/// checkpoint 2. La sesión se crea con una allowlist que contiene únicamente esa tool
/// (todas las tools integradas del CLI quedan deshabilitadas), no se usa
/// PermissionHandler.ApproveAll, y se mantiene el mismo límite mínimo de AI Credits ya
/// validado en el checkpoint 3.
/// </summary>
public class CopilotMetadataTestService
{
    private const string FixedPrompt =
        $"Usa obligatoriamente la herramienta {GestionEngineSummaryTool.Name} para obtener el dato actual. No inventes el valor.\n" +
        "Responde únicamente con el formato:\n" +
        "GestionEngine tiene N vistas.";

    // Mismo mínimo aceptado por el runtime del SDK ya validado en el checkpoint 3.
    private const double MaxAiCredits = 30;

    private readonly GestionEngineHealthService _gestionEngineHealthService;
    private readonly ILogger<CopilotMetadataTestService> _logger;

    public CopilotMetadataTestService(GestionEngineHealthService gestionEngineHealthService, ILogger<CopilotMetadataTestService> logger)
    {
        _gestionEngineHealthService = gestionEngineHealthService;
        _logger = logger;
    }

    public async Task<CopilotMetadataTestResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var toolInvoked = false;
        int? viewsCount = null;
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Copilot + GestionEngine metadata test started");

        try
        {
            var summaryTool = GestionEngineSummaryTool.Create(
                _gestionEngineHealthService,
                _logger,
                onInvoked: count =>
                {
                    toolInvoked = true;
                    viewsCount = count;
                },
                cancellationToken);

            await using var client = new CopilotClient(new CopilotClientOptions
            {
                UseLoggedInUser = true,
            });
            await client.StartAsync();

            await using var session = await client.CreateSessionAsync(new SessionConfig
            {
                Tools = [summaryTool],
                // Allowlist explícita: únicamente nuestra custom tool está disponible.
                // Todas las tools integradas del CLI permanecen deshabilitadas.
                AvailableTools = new List<string> { GestionEngineSummaryTool.Name },
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
                _logger.LogWarning(
                    "Copilot + GestionEngine metadata test completed in {ElapsedMs} ms without a response. ToolInvoked: {ToolInvoked}",
                    stopwatch.ElapsedMilliseconds,
                    toolInvoked);
                return new CopilotMetadataTestResult("error", null, toolInvoked, viewsCount, "Copilot no devolvió respuesta.");
            }

            _logger.LogInformation(
                "Copilot + GestionEngine metadata test completed in {ElapsedMs} ms. ToolInvoked: {ToolInvoked}, ViewsCount: {ViewsCount}",
                stopwatch.ElapsedMilliseconds,
                toolInvoked,
                viewsCount);

            return new CopilotMetadataTestResult("ok", message.Data.Content, toolInvoked, viewsCount, null);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "Copilot + GestionEngine metadata test failed after {ElapsedMs} ms. ToolInvoked: {ToolInvoked}",
                stopwatch.ElapsedMilliseconds,
                toolInvoked);
            return new CopilotMetadataTestResult("error", null, toolInvoked, viewsCount, "No se pudo completar la prueba de Copilot + GestionEngine.");
        }
    }
}
