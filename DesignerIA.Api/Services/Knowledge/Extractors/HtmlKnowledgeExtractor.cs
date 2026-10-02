using System.Net;
using System.Text.RegularExpressions;

namespace DesignerIA.Api.Services.Knowledge.Extractors;

/// <summary>
/// Extractor para archivos .html/.htm. Los archivos de Wiki en KnowledgeSource son
/// exportaciones de TiddlyWiki: cada tema real vive en un
/// &lt;div created="..." modified="..." tags="..." title="..."&gt;...&lt;/div&gt;. Se aprovecha
/// esa estructura para obtener un Title y una fecha reales por fragmento. Si un
/// archivo .html no tiene esa estructura, se aplica un fallback simple que limpia
/// las etiquetas y trata el archivo completo como una única sección.
/// </summary>
public static partial class HtmlKnowledgeExtractor
{
    public static IReadOnlyList<KnowledgeSection> Extract(string filePath)
    {
        var html = File.ReadAllText(filePath);
        var matches = TiddlerDivRegex().Matches(html);

        if (matches.Count == 0)
        {
            var plainText = CleanHtml(html);
            return plainText.Length == 0
                ? Array.Empty<KnowledgeSection>()
                : [new KnowledgeSection(Heading: null, Content: plainText, SourceDate: null)];
        }

        var sections = new List<KnowledgeSection>(matches.Count);
        foreach (Match match in matches)
        {
            var title = WebUtility.HtmlDecode(match.Groups["title"].Value).Trim();
            var content = CleanHtml(match.Groups["body"].Value);
            if (content.Length == 0)
            {
                continue;
            }

            var modifiedMatch = ModifiedAttributeRegex().Match(match.Groups["attrs"].Value);
            var modified = modifiedMatch.Success ? modifiedMatch.Groups["value"].Value : null;
            sections.Add(new KnowledgeSection(Heading: title, Content: content, SourceDate: NormalizeTiddlyDate(modified)));
        }

        return sections;
    }

    private static string CleanHtml(string fragment)
    {
        var withoutTags = HtmlTagRegex().Replace(fragment, " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        var collapsed = WhitespaceRegex().Replace(decoded, " ").Trim();
        return collapsed;
    }

    /// <summary>
    /// Utilidad reutilizada por otros extractores (por ejemplo Msg) cuando sólo
    /// disponen de un cuerpo HTML y necesitan el mismo saneamiento simple de tags.
    /// </summary>
    public static string StripHtml(string html) => CleanHtml(html);

    /// <summary>
    /// TiddlyWiki almacena la fecha "modified" como yyyyMMddHHmmssfff. Se convierte a
    /// yyyy-MM-dd cuando el formato es reconocible; si no, se descarta en lugar de
    /// inventar una fecha.
    /// </summary>
    private static string? NormalizeTiddlyDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length < 8)
        {
            return null;
        }

        var datePart = raw[..8];
        return DateTime.TryParseExact(datePart, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var date)
            ? date.ToString("yyyy-MM-dd")
            : null;
    }

    [GeneratedRegex("""<div\s+(?<attrs>[^>]*?title="(?<title>[^"]*)"[^>]*)>(?<body>.*?)</div>""", RegexOptions.Singleline)]
    private static partial Regex TiddlerDivRegex();

    [GeneratedRegex("modified=\"(?<value>[^\"]*)\"")]
    private static partial Regex ModifiedAttributeRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
