namespace DesignerIA.Contracts;

/// <summary>
/// Referencia mínima a una fuente utilizada para responder una pregunta. No expone
/// rutas absolutas ni contenido completo. <paramref name="SourceType"/> es "Document"
/// para un archivo de KnowledgeSource o "Curated" para conocimiento del equipo ya
/// Approved; <paramref name="Contributor"/> sólo aplica a este último caso.
/// </summary>
public record KnowledgeSourceReference(
    string Title,
    string SourceFile,
    string SourceType = "Document",
    string? Contributor = null);
