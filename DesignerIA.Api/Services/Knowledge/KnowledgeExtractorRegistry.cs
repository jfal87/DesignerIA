using DesignerIA.Api.Services.Knowledge.Extractors;

namespace DesignerIA.Api.Services.Knowledge;

/// <summary>
/// Punto único de despacho entre una extensión de archivo y su extractor. Agregar
/// un formato nuevo más adelante sólo requiere un extractor nuevo y una entrada aquí,
/// sin una arquitectura de plugins.
/// </summary>
public static class KnowledgeExtractorRegistry
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".txt", ".sql", ".js", ".json", ".md", ".docx", ".xlsx", ".pptx", ".msg", ".pdf", ".cs",
    };

    public static bool IsSupported(string extension) => SupportedExtensions.Contains(extension);

    public static IReadOnlyList<KnowledgeSection> Extract(string filePath, string extension) => extension.ToLowerInvariant() switch
    {
        ".html" or ".htm" => HtmlKnowledgeExtractor.Extract(filePath),
        ".txt" or ".sql" or ".js" or ".json" or ".cs" => PlainTextKnowledgeExtractor.Extract(filePath),
        ".md" => MarkdownKnowledgeExtractor.Extract(filePath),
        ".docx" => WordKnowledgeExtractor.Extract(filePath),
        ".xlsx" => ExcelKnowledgeExtractor.Extract(filePath),
        ".pptx" => PowerPointKnowledgeExtractor.Extract(filePath),
        ".msg" => MsgKnowledgeExtractor.Extract(filePath),
        ".pdf" => PdfKnowledgeExtractor.Extract(filePath),
        _ => throw new NotSupportedException($"Extension '{extension}' is not supported by any knowledge extractor."),
    };
}
