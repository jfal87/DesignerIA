namespace DesignerIA.Contracts;

/// <summary>
/// Resultado de una operación individual sobre Curated Knowledge (crear, aprobar,
/// rechazar). <paramref name="Entry"/> es null cuando <paramref name="Status"/> es
/// "error".
/// </summary>
public record CuratedKnowledgeResult(string Status, CuratedKnowledgeEntry? Entry, string? Error);
