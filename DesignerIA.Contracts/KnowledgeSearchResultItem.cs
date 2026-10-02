namespace DesignerIA.Contracts;

/// <summary>
/// Resultado de una búsqueda léxica en el índice local de KnowledgeSource, o en una
/// entrada de <see cref="CuratedKnowledgeEntry"/> ya Approved. Contiene únicamente un
/// fragmento relevante, nunca el documento completo. <paramref name="SourceType"/> es
/// "Document" para KnowledgeSource o "Curated" para conocimiento aportado por el
/// equipo; <paramref name="Contributor"/> sólo aplica a este último caso.
/// </summary>
public record KnowledgeSearchResultItem(
    string Title,
    string SourceFile,
    string RelativePath,
    string Snippet,
    double Score,
    string SourceType = "Document",
    string? Contributor = null);
