namespace DesignerIA.Contracts;

/// <summary>
/// Cuerpo de POST /api/knowledge/curated/{id}/approve y .../reject. ReviewedBy es
/// texto declarado por la persona que revisa, requerido en ambas acciones.
/// </summary>
public record CuratedKnowledgeReviewRequest(string ReviewedBy);
