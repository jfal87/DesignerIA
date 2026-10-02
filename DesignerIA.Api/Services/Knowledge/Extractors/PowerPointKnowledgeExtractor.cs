using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Presentation;
using A = DocumentFormat.OpenXml.Drawing;

namespace DesignerIA.Api.Services.Knowledge.Extractors;

/// <summary>
/// Extractor para presentaciones PowerPoint (.pptx) mediante DocumentFormat.OpenXml.
/// Cada diapositiva se convierte en una sección (heading = "Diapositiva N"),
/// concatenando el texto de sus formas.
/// </summary>
public static class PowerPointKnowledgeExtractor
{
    public static IReadOnlyList<KnowledgeSection> Extract(string filePath)
    {
        using var document = PresentationDocument.Open(filePath, false);
        var presentationPart = document.PresentationPart;
        if (presentationPart is null)
        {
            return Array.Empty<KnowledgeSection>();
        }

        var sourceDate = TryGetLastWriteDate(filePath);
        var sections = new List<KnowledgeSection>();
        var slideIndex = 0;

        foreach (var slidePart in presentationPart.SlideParts)
        {
            slideIndex++;
            var texts = slidePart.Slide.Descendants<A.Text>().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t));
            var content = string.Join(Environment.NewLine, texts).Trim();
            if (content.Length > 0)
            {
                sections.Add(new KnowledgeSection($"Diapositiva {slideIndex}", content, sourceDate));
            }
        }

        return sections;
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
