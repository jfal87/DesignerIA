using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace DesignerIA.Api.Services;

/// <summary>
/// Fábrica única de la custom tool "get_gestionengine_summary". Todos los flujos de
/// Copilot (metadata-test y chat) reutilizan esta misma definición para evitar
/// duplicar la consulta SQL o exponer una segunda versión de la tool.
/// La tool no acepta parámetros y llama exclusivamente al servicio C# de solo
/// lectura ya validado (GestionEngineHealthService -> SELECT COUNT(*) FROM dbo.Vistas).
/// </summary>
public static class GestionEngineSummaryTool
{
    public const string Name = "get_gestionengine_summary";

    public static AIFunctionDeclaration Create(
        GestionEngineHealthService gestionEngineHealthService,
        ILogger logger,
        Action<int> onInvoked,
        CancellationToken cancellationToken)
    {
        return CopilotTool.DefineTool(
            async () =>
            {
                var status = await gestionEngineHealthService.CheckHealthAsync(cancellationToken);
                onInvoked(status.ViewsCount);
                logger.LogInformation(
                    "Custom tool {ToolName} invoked. ViewsCount: {ViewsCount}",
                    Name,
                    status.ViewsCount);
                return new { database = status.Database, viewsCount = status.ViewsCount };
            },
            toolOptions: new CopilotToolOptions
            {
                // Tool de solo lectura, sin efectos secundarios y completamente controlada
                // por DesignerIA: puede omitir el prompt de permiso manual.
                SkipPermission = true,
            },
            factoryOptions: new AIFunctionFactoryOptions
            {
                Name = Name,
                Description = "Obtiene el resumen actual de GestionEngine (base de datos y cantidad de vistas). No acepta parámetros.",
            });
    }
}
