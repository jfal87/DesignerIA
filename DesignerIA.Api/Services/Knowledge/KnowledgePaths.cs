namespace DesignerIA.Api.Services.Knowledge;

/// <summary>
/// Resuelve las rutas de la fuente de conocimiento autorizada y del índice local
/// generado a partir de ella, evitando duplicar esta lógica en el indexador y en el
/// buscador.
/// </summary>
public static class KnowledgePaths
{
    public static string GetSourceRoot(IHostEnvironment environment)
    {
        return Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "KnowledgeSource"));
    }

    public static string GetIndexFilePath(IHostEnvironment environment)
    {
        return Path.Combine(environment.ContentRootPath, "Data", "Knowledge", "knowledge-index.jsonl");
    }
}
