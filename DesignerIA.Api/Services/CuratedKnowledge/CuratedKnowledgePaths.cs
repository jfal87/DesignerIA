namespace DesignerIA.Api.Services.CuratedKnowledge;

/// <summary>
/// Resuelve las rutas físicas de almacenamiento de Curated Knowledge, separadas por
/// Status. Vive dentro de DesignerIA.Api\App_Data, nunca dentro de KnowledgeSource
/// (Pending no debe poder entrar accidentalmente al índice documental).
/// </summary>
public static class CuratedKnowledgePaths
{
    public static string GetRoot(IHostEnvironment environment)
    {
        return Path.Combine(environment.ContentRootPath, "App_Data", "CuratedKnowledge");
    }

    public static string GetFolder(IHostEnvironment environment, Contracts.CuratedKnowledgeStatus status)
    {
        return Path.Combine(GetRoot(environment), status.ToString());
    }
}
