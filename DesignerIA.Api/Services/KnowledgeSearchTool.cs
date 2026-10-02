using System.ComponentModel;
using DesignerIA.Api.Services.Knowledge;
using DesignerIA.Contracts;
using GitHub.Copilot;
using Microsoft.Extensions.AI;

namespace DesignerIA.Api.Services;

/// <summary>
/// Fábrica única de la custom tool "search_knowledge". Recibe exclusivamente un
/// argumento de texto libre (query); no acepta rutas, nombres de archivo, comandos
/// ni SQL. Llama exclusivamente a <see cref="KnowledgeSearchService"/>, que a su vez
/// sólo lee el índice local generado desde KnowledgeSource. Copilot nunca recibe
/// acceso directo al sistema de archivos.
/// </summary>
public static class KnowledgeSearchTool
{
    public const string Name = "search_knowledge";

    public static AIFunctionDeclaration Create(
        KnowledgeSearchService knowledgeSearchService,
        ILogger logger,
        Action<IReadOnlyList<KnowledgeSearchResultItem>> onInvoked,
        CancellationToken cancellationToken)
    {
        return CopilotTool.DefineTool(
            (
                [Description("Consulta en texto libre sobre documentación interna de DesignerIA/GestionEngine, por ejemplo el nombre de un handler o una funcionalidad.")]
                string query) =>
            {
                var results = knowledgeSearchService.Search(query);
                onInvoked(results);
                logger.LogInformation(
                    "Custom tool {ToolName} invoked. ResultsCount: {ResultsCount}",
                    Name,
                    results.Count);

                // Sólo se entregan al modelo los fragmentos relevantes ya recortados,
                // nunca el documento completo.
                return results
                    .Select(r => new
                    {
                        title = r.Title,
                        sourceFile = r.SourceFile,
                        snippet = r.Snippet,
                        sourceType = r.SourceType,
                        contributor = r.Contributor,
                    })
                    .ToList();
            },
            toolOptions: new CopilotToolOptions
            {
                // Tool de solo lectura sobre un índice local controlado, sin efectos secundarios.
                SkipPermission = true,
            },
            factoryOptions: new AIFunctionFactoryOptions
            {
                Name = Name,
                Description = "Busca fragmentos relevantes de documentación interna de DesignerIA en el índice local de KnowledgeSource. Recibe únicamente 'query' (texto libre). No acepta rutas, nombres de archivo, comandos ni SQL.",
            });
    }
}
