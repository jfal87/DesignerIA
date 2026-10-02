namespace DesignerIA.Contracts;

/// <summary>
/// Resultado de GET /api/knowledge/curated?status=... .
/// </summary>
public record CuratedKnowledgeListResult(string Status, IReadOnlyList<CuratedKnowledgeEntry> Entries, string? Error);
