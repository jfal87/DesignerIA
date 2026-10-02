using System.Text;
using System.Text.RegularExpressions;

namespace DesignerIA.Api.Services.Knowledge.Extractors;

/// <summary>
/// Extractor para archivos Markdown (.md). Respeta los encabezados (# .. ######)
/// como límites de sección, conservando el heading como título del fragmento.
/// </summary>
public static partial class MarkdownKnowledgeExtractor
{
    public static IReadOnlyList<KnowledgeSection> Extract(string filePath)
    {
        var text = File.ReadAllText(filePath);
        var sourceDate = TryGetLastWriteDate(filePath);
        var lines = text.Replace("\r\n", "\n").Split('\n');

        var sections = new List<KnowledgeSection>();
        string? currentHeading = null;
        var currentContent = new StringBuilder();

        void Flush()
        {
            var content = currentContent.ToString().Trim();
            if (content.Length > 0)
            {
                sections.Add(new KnowledgeSection(currentHeading, content, sourceDate));
            }
            currentContent.Clear();
        }

        foreach (var line in lines)
        {
            var headingMatch = HeadingRegex().Match(line);
            if (headingMatch.Success)
            {
                Flush();
                currentHeading = headingMatch.Groups["text"].Value.Trim();
                continue;
            }

            currentContent.AppendLine(line);
        }

        Flush();

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

    [GeneratedRegex(@"^\s{0,3}#{1,6}\s+(?<text>.+?)\s*#*\s*$")]
    private static partial Regex HeadingRegex();
}
