using Ganss.Xss;
using Markdig;
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
        var generatedHtml = Markdown.ToHtml(markdown, MarkdownPipeline);
        return new MarkupString(Sanitizer.Sanitize(generatedHtml));
    }

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
