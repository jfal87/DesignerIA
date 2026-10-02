namespace DesignerIA.Contracts;

/// <summary>
/// Resumen de una reconstrucción del índice local de KnowledgeSource. Nunca incluye
/// contenido de documentos, únicamente conteos.
/// </summary>
public record KnowledgeRebuildResult(
    string Status,
    int FilesScanned,
    int FilesIndexed,
    int FilesExcluded,
    int FilesUnsupported,
    int ChunksCreated,
    long DurationMs,
    int MsgIndexed = 0,
    int PdfIndexed = 0,
    int MsgFailed = 0,
    int PdfFailed = 0,
    string? Error = null);
