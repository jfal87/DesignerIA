using Ganss.Xss;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.AspNetCore.Components;

namespace DesignerIA.Web;

/// <summary>
/// Convierte las respuestas normales de DesignerIA a HTML limitado y saneado para el chat.
/// </summary>
public static class MarkdownChatRenderer
{
    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .DisableHtml()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    public static MarkupString Render(string markdown)
    {
        var document = Markdown.Parse(markdown, MarkdownPipeline);
        KeepSeparatorAsterisks(document);
        return new MarkupString(Sanitizer.Sanitize(document.ToHtml(MarkdownPipeline)));
    }

    /// <summary>
    /// En parámetros como "1|*|2|*|3" los asteriscos son datos, pero Markdown los empareja como énfasis y desaparecen
    /// de la pantalla y de lo que se copia. Se corrige sobre el documento ya analizado, no sobre el texto: sólo se
    /// deshace un énfasis de un asterisco cuyo contenido empieza o termina en "|", y se devuelven sus dos asteriscos.
    /// El código (cercado, indentado o en línea) nunca es un énfasis, así que no se toca; el énfasis normal tampoco.
    /// </summary>
    private static void KeepSeparatorAsterisks(MarkdownDocument document)
    {
        foreach (var emphasis in document.Descendants<EmphasisInline>().ToList())
        {
            if (emphasis.DelimiterChar != '*' || emphasis.DelimiterCount != 1
                || !(StartsWithPipe(emphasis.FirstChild) || EndsWithPipe(emphasis.LastChild)))
            {
                continue;
            }

            emphasis.InsertBefore(new LiteralInline("*"));
            var child = emphasis.FirstChild;
            while (child is not null)
            {
                var next = child.NextSibling;
                child.Remove();
                emphasis.InsertBefore(child);
                child = next;
            }
            emphasis.InsertBefore(new LiteralInline("*"));
            emphasis.Remove();
        }
    }

    private static bool StartsWithPipe(Inline? inline) =>
        inline is LiteralInline literal && literal.Content.ToString().StartsWith('|');

    private static bool EndsWithPipe(Inline? inline) =>
        inline is LiteralInline literal && literal.Content.ToString().EndsWith('|');

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[] { "a", "br", "code", "em", "h1", "h2", "h3", "h4", "h5", "h6", "li", "ol", "p", "pre", "strong", "ul" })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedAttributes.Add("href");

        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowedSchemes.Add("http");
        sanitizer.AllowedSchemes.Add("https");

        return sanitizer;
    }
}
