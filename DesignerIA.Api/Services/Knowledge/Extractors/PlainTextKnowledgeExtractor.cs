namespace DesignerIA.Api.Services.Knowledge.Extractors;

/// <summary>
/// Extractor para archivos de texto plano sin estructura de encabezados
/// (.txt, .sql, .js, .json). Divide el contenido por párrafos (líneas en blanco)
/// para mantener secciones razonablemente pequeñas antes del chunking final.
/// </summary>
public static class PlainTextKnowledgeExtractor
{
    public static IReadOnlyList<KnowledgeSection> Extract(string filePath)
    {
        var text = File.ReadAllText(filePath);
        var sourceDate = TryGetLastWriteDate(filePath);

        var paragraphs = text
            .Replace("\r\n", "\n")
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        if (paragraphs.Count == 0)
        {
            return text.Trim().Length == 0
                ? Array.Empty<KnowledgeSection>()
                : [new KnowledgeSection(Heading: null, Content: text.Trim(), SourceDate: sourceDate)];
        }

        return paragraphs
            .Select(p => new KnowledgeSection(Heading: null, Content: p, SourceDate: sourceDate))
            .ToList();
    }

    private static string? TryGetLastWriteDate(string filePath)
    {
        try
        {
            return File.GetLastWriteTimeUtc(filePath).ToString("yyyy-MM-dd");
        }
        catch
        {
            return null;
        }
    }
}
