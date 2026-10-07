using System.Text.RegularExpressions;
using DesignerIA.Contracts;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Services;

internal static class GroundedArtifactExtractor
{
    private static readonly Regex Declaration = new(@"\b(?:var|let|const)\s+(?<name>[$A-Za-z_][$\w]*)\s*=", RegexOptions.CultureInvariant);

    public static string? FirstJsonObject(string text)
    {
        foreach (Match start in Regex.Matches(text, @"\{"))
        {
            try
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(text[start.Index..]);
                var reader = new System.Text.Json.Utf8JsonReader(bytes);
                using var document = System.Text.Json.JsonDocument.ParseValue(ref reader);
                if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    return System.Text.Encoding.UTF8.GetString(bytes.AsSpan(0, checked((int)reader.BytesConsumed)));
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Prose and legacy parameter syntax can contain braces without being JSON.
            }
        }
        return null;
    }

    public static (string Response, IReadOnlyList<GroundedArtifact> Artifacts) FromKnowledge(
        string response, IReadOnlyList<KnowledgeSearchResultItem> evidence, string topic)
    {
        var artifacts = new List<GroundedArtifact>();
        var exactResponseSource = evidence.FirstOrDefault(item =>
            response.Trim().Length > 0 && item.Snippet.Contains(response.Trim(), StringComparison.Ordinal));
        var declarations = new List<GroundedArtifact>();
        foreach (var item in evidence)
        {
            foreach (Match declaration in Declaration.Matches(item.Snippet))
            {
                var end = FindStatementEnd(item.Snippet, declaration.Index + declaration.Length);
                if (end < 0)
                {
                    continue;
                }

                var name = declaration.Groups["name"].Value;
                if (!Regex.IsMatch(response, $@"(?<![$\w]){Regex.Escape(name)}(?![$\w])"))
                {
                    continue;
                }

                var content = item.Snippet[declaration.Index..(end + 1)];
                declarations.Add(new GroundedArtifact(GroundedArtifactKind.Variable, content, "Knowledge",
                    $"{item.SourceFile}: {item.Title}", topic, name));
            }
        }

        foreach (var group in declarations.GroupBy(a => a.Entity))
        {
            var variants = group.DistinctBy(a => a.Content).ToArray();
            if (variants.Length > 1)
            {
                artifacts.AddRange(variants.Where(a => response.Contains(a.Content, StringComparison.Ordinal)));
                continue;
            }

            var artifact = variants[0];
            var name = artifact.Entity;
            var content = artifact.Content;
            // The retained declaration always comes from the retrieved source, never the model.
            var displayedDeclaration = Declaration.Matches(response)
                .Cast<Match>().FirstOrDefault(m => m.Groups["name"].Value == name);
            var displayedEnd = displayedDeclaration is null ? -1
                : FindStatementEnd(response, displayedDeclaration.Index + displayedDeclaration.Length);
            if (displayedDeclaration is not null && displayedEnd >= 0)
            {
                response = response[..displayedDeclaration.Index] + content + response[(displayedEnd + 1)..];
            }
            else if (!response.Contains(content, StringComparison.Ordinal))
            {
                response += $"\n\n## Fragmento documental\n```javascript\n{content}\n```";
            }

            artifacts.Add(artifact);
        }

        if (exactResponseSource is not null && !artifacts.Any(a => a.Content == response.Trim()))
        {
            var kind = GroundedArtifactKind.Snippet;
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(response.Trim());
                kind = GroundedArtifactKind.Json;
            }
            catch (System.Text.Json.JsonException)
            {
                // Exact source text need not be JSON to be a grounded snippet.
            }
            artifacts.Add(new GroundedArtifact(kind, response.Trim(), "Knowledge",
                $"{exactResponseSource.SourceFile}: {exactResponseSource.Title}", topic, topic));
        }

        foreach (Match block in Regex.Matches(response, @"```[^\r\n]*\r?\n(?<code>[\s\S]*?)```"))
        {
            var code = block.Groups["code"].Value.TrimEnd('\r', '\n');
            var source = evidence.FirstOrDefault(e => e.Snippet.Contains(code, StringComparison.Ordinal));
            if (string.IsNullOrWhiteSpace(code) || source is null || artifacts.Any(a => a.Content == code))
            {
                continue;
            }

            var kind = GroundedArtifactKind.Snippet;
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(code);
                kind = GroundedArtifactKind.Json;
            }
            catch (System.Text.Json.JsonException)
            {
                // A non-JSON block can still be an exact documentary snippet.
            }

            artifacts.Add(new GroundedArtifact(kind, code, "Knowledge",
                $"{source.SourceFile}: {source.Title}", topic, topic));
        }

        foreach (Match identifier in Regex.Matches(response, @"`(?<name>[$A-Za-z_][$\w]*)`"))
        {
            var name = identifier.Groups["name"].Value;
            if (name is "var" or "let" or "const" or "return" or "true" or "false" or "null"
                || artifacts.Any(a => a.Entity == name))
            {
                continue;
            }

            var source = evidence.FirstOrDefault(e => Regex.IsMatch(e.Snippet, $@"(?<![$\w]){Regex.Escape(name)}(?![$\w])"));
            if (source is not null)
            {
                artifacts.Add(new GroundedArtifact(GroundedArtifactKind.TechnicalName, name, "Knowledge",
                    $"{source.SourceFile}: {source.Title}", topic, name));
            }
        }

        foreach (var source in evidence)
        {
            foreach (Match label in Regex.Matches(source.Snippet, @"\b(?<name>[A-Z][A-Z_]+)\s*:\s*-?\d+\b"))
            {
                var name = label.Groups["name"].Value;
                if (!artifacts.Any(a => a.Entity == name) && Regex.IsMatch(response, $@"\b{Regex.Escape(name)}\b"))
                {
                    artifacts.Insert(0, new GroundedArtifact(GroundedArtifactKind.TechnicalName, name, "Knowledge",
                        $"{source.SourceFile}: {source.Title}", topic, name));
                }
            }
        }

        return (response, artifacts);
    }

    private static int FindStatementEnd(string text, int start)
    {
        var depth = 0;
        var quote = '\0';
        for (var i = start; i < text.Length; i++)
        {
            var character = text[i];
            if (quote != '\0')
            {
                if (character == '\\') { i++; }
                else if (character == quote) { quote = '\0'; }
                continue;
            }

            if (character == '/' && i + 1 < text.Length && text[i + 1] == '/')
            {
                var newline = text.IndexOf('\n', i + 2);
                if (newline < 0) { return -1; }
                i = newline;
                continue;
            }
            if (character == '/' && i + 1 < text.Length && text[i + 1] == '*')
            {
                var commentEnd = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                if (commentEnd < 0) { return -1; }
                i = commentEnd + 1;
                continue;
            }

            if (character is '\'' or '"' or '`') { quote = character; }
            else if (character is '{' or '[' or '(') { depth++; }
            else if (character is '}' or ']' or ')') { depth--; }
            else if (character == ';' && depth == 0) { return i; }
            if (depth < 0) { return -1; }
        }

        return -1;
    }
}
