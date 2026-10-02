using UglyToad.PdfPig;

namespace DesignerIA.Api.Services.Knowledge.Extractors;

/// <summary>
/// Extractor para PDFs que ya contienen texto (sin OCR, sin interpretación de
/// layout complejo). Cada página con texto útil se convierte en una sección; las
/// páginas sin texto (por ejemplo, escaneadas como imagen) simplemente se omiten.
/// Si el PDF no aporta ninguna página con texto útil, se devuelve una lista vacía
/// y el archivo se contabiliza como no soportado/fallido en el índice.
/// </summary>
public static class PdfKnowledgeExtractor
{
    public static IReadOnlyList<KnowledgeSection> Extract(string filePath)
    {
        var sourceDate = TryGetLastWriteDate(filePath);
        var sections = new List<KnowledgeSection>();

        using var document = PdfDocument.Open(filePath);
        foreach (var page in document.GetPages())
        {
            var text = page.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            sections.Add(new KnowledgeSection(Heading: $"Página {page.Number}", Content: text, SourceDate: sourceDate));
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
