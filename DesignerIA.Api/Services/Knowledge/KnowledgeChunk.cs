namespace DesignerIA.Api.Services.Knowledge;

/// <summary>
/// Representación mínima y explícita de un fragmento documental indexado desde
/// KnowledgeSource. Se persiste en el índice local (JSONL); nunca se envía completo
/// a Copilot, sólo lo estrictamente necesario a través de search_knowledge.
/// </summary>
public record KnowledgeChunk(
    string Id,
    string SourceFile,
    string RelativePath,
    string SourceType,
    string Title,
    string Content,
    int ChunkIndex,
    string? SourceDate,
    string? Heading);
