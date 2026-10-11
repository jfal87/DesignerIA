using System.ComponentModel;
using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace DesignerIA.Api.Services;

/// <summary>Controlled Copilot tool for read-only real handler examples.</summary>
public static class HandlerExamplesTool
{
    public const string Name = "find_handler_examples";

    public static AIFunctionDeclaration Create(HandlerExamplesService handlerExamplesService, ILogger logger, Action onInvoked, CancellationToken cancellationToken)
    {
        return CopilotTool.DefineTool(
            async (
                [Description("Nombre exacto de la acción/handler ya identificado; no acepta nombres aproximados.")] string handler,
                [Description("Cantidad máxima de ejemplos; el límite interno es 3.")] int? maxResults = null,
                [Description("Marcador opcional de configuración real: MultiHandler o Conditional.")] string? configurationMarker = null) =>
            {
                var result = await handlerExamplesService.FindAsync(handler, maxResults, configurationMarker, cancellationToken);
                onInvoked();
                logger.LogInformation("Custom tool {ToolName} invoked. RequestedHandler: {RequestedHandler}, ActionFound: {ActionFound}, ExamplesCount: {ExamplesCount}", Name, handler, result.ActionFound, result.Examples.Count);
                return result;
            },
            toolOptions: new CopilotToolOptions { SkipPermission = true },
            factoryOptions: new AIFunctionFactoryOptions
            {
                Name = Name,
                Description = "Busca hasta 3 ejemplos reales de solo lectura de un handler en GestionEngine. configurationMarker sólo admite MultiHandler o Conditional y filtra los parámetros del mismo handler/acción; no realiza búsquedas globales. No acepta SQL, tablas, filtros, comandos ni conexiones.",
            });
    }
}
