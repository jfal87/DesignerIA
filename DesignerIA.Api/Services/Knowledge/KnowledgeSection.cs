namespace DesignerIA.Api.Services.Knowledge;

/// <summary>
/// Sección lógica extraída de un documento fuente antes de aplicar el chunking final
/// (por ejemplo, un tiddler de una wiki, un heading de Markdown, una hoja de Excel o
/// una diapositiva). No se persiste tal cual; <see cref="KnowledgeChunker"/> la divide
/// en fragmentos de tamaño razonable si es necesario.
/// </summary>
public record KnowledgeSection(string? Heading, string Content, string? SourceDate);
