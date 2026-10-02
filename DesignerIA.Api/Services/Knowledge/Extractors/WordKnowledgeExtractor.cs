using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace DesignerIA.Api.Services.Knowledge.Extractors;

/// <summary>
/// Extractor para documentos Word (.docx) mediante DocumentFormat.OpenXml (lectura,
/// sin macros ni automatización de Office). Los párrafos con estilo de encabezado
/// (Heading1/Heading2/...) inician una nueva sección; el resto de párrafos se
/// concatenan a la sección actual.
/// </summary>
public static class WordKnowledgeExtractor
{
    public static IReadOnlyList<KnowledgeSection> Extract(string filePath)
    {
        using var document = WordprocessingDocument.Open(filePath, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
        {
            return Array.Empty<KnowledgeSection>();
        }

        var sourceDate = TryGetLastWriteDate(filePath);
        var sections = new List<KnowledgeSection>();
        string? currentHeading = null;
        var currentContent = new System.Text.StringBuilder();

        void Flush()
        {
            var content = currentContent.ToString().Trim();
            if (content.Length > 0)
            {
                sections.Add(new KnowledgeSection(currentHeading, content, sourceDate));
            }
            currentContent.Clear();
        }

        foreach (var paragraph in body.Elements<Paragraph>())
        {
            var text = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text)).Trim();
            if (text.Length == 0)
            {
                continue;
            }

            if (IsHeading(paragraph))
            {
                Flush();
                currentHeading = text;
                continue;
            }

            currentContent.AppendLine(text);
        }

        Flush();
        return sections;
    }

    private static bool IsHeading(Paragraph paragraph)
    {
        var styleId = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        return styleId is not null && styleId.Contains("Heading", StringComparison.OrdinalIgnoreCase);
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
