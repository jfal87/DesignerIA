namespace DesignerIA.Contracts;

/// <summary>
/// Cuerpo de POST /api/knowledge/curated. El Id nunca lo proporciona el usuario:
/// lo genera la aplicación al crear la entrada.
/// </summary>
public record CuratedKnowledgeCreateRequest(
    string Topic,
    string Content,
    string? Example,
    string Contributor);
