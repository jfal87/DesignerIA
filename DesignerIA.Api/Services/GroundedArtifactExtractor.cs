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
        string response, IReadOnlyList<KnowledgeSearchResultItem> evidence, string topic,
        ActiveSubject? subject = null, IReadOnlyList<ActiveSubject>? knownSubjects = null)
    {
        evidence = evidence.Where(item => item.SourceType is "Document" or "Curated"
            && !string.IsNullOrWhiteSpace(item.SourceFile) && !string.IsNullOrWhiteSpace(item.RelativePath)).ToArray();
        var artifacts = new List<GroundedArtifact>();
        var identities = (knownSubjects ?? []).Concat(subject is null ? [] : new[] { subject })
            .Where(identity => identity.Kind != "General").GroupBy(identity => identity.Kind)
            .SelectMany(group => group.DistinctBy(identity => identity.Name, StringComparer.OrdinalIgnoreCase)).ToArray();
        GroundedArtifact Create(GroundedArtifactKind kind, string content, KnowledgeSearchResultItem source, string entity)
        {
            var mentioned = identities.Where(identity => source.Title.Equals(identity.Name, StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(source.Snippet, $@"(?<![\p{{L}}\p{{N}}_$]){Regex.Escape(identity.Name)}(?![\p{{L}}\p{{N}}_$])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)).ToArray();
            var scope = kind is GroundedArtifactKind.Json or GroundedArtifactKind.Snippet
                ? FindDocumentedOwner(source, content, identities) : (HasScope: false, Owner: (ActiveSubject?)null);
            // A record overrides a generic title. Unscoped multi-subject mentions remain ambiguous.
            var owner = scope.HasScope ? scope.Owner : mentioned.Length == 1
                && (source.Title.Equals(mentioned[0].Name, StringComparison.OrdinalIgnoreCase)
                    || kind is GroundedArtifactKind.Variable or GroundedArtifactKind.TechnicalName
                        && entity.Equals(mentioned[0].Name, StringComparison.OrdinalIgnoreCase))
                ? mentioned[0] : null;
            return new(kind, content, "Knowledge", $"{source.SourceFile}: {source.Title}", topic,
                kind is GroundedArtifactKind.Json or GroundedArtifactKind.Snippet ? owner?.Name ?? source.Title : entity, owner);
        }
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
                declarations.Add(Create(GroundedArtifactKind.Variable, content, item, name));
            }
        }

        foreach (var group in declarations.GroupBy(a => a.Entity))
        {
            var variants = group.DistinctBy(a => a.Content).ToArray();
            if (variants.Length > 1)
            {
                artifacts.AddRange(group.Where(a => response.Contains(a.Content, StringComparison.Ordinal))
                    .DistinctBy(a => (a.Content, a.OriginatingSubject, a.Source)));
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

            artifacts.AddRange(group.DistinctBy(a => (a.Content, a.OriginatingSubject, a.Source)));
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
            artifacts.Add(Create(kind, response.Trim(), exactResponseSource, topic));
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

            artifacts.Add(Create(kind, code, source, topic));
        }

        // Scan evidence, not model prose; retain only objects displayed verbatim.
        foreach (var source in evidence)
        {
            var offset = 0;
            while (offset < source.Snippet.Length)
            {
                var json = FirstJsonObject(source.Snippet[offset..]);
                if (json is null)
                {
                    break;
                }

                offset = source.Snippet.IndexOf(json, offset, StringComparison.Ordinal) + json.Length;
                using var document = System.Text.Json.JsonDocument.Parse(json);
                var candidate = Create(GroundedArtifactKind.Json, json, source, topic);
                if (!response.Contains(json, StringComparison.Ordinal)
                    || artifacts.Any(a => a.Source == candidate.Source && a.OriginatingSubject == candidate.OriginatingSubject
                        && (a.Content == json || a.Kind == GroundedArtifactKind.Json
                            && a.Content.Contains(json, StringComparison.Ordinal))))
                {
                    continue;
                }

                artifacts.Add(candidate);
            }
        }

        // Legacy parameter syntax (an object that is not strict JSON) is kept as an exact snippet when displayed verbatim.
        foreach (var source in evidence)
        {
            foreach (Match start in Regex.Matches(source.Snippet, @"\{\s*""[^""\r\n]{1,80}""\s*:"))
            {
                var end = FindBalancedBraceEnd(source.Snippet, start.Index);
                if (end < 0) { continue; }
                var syntax = source.Snippet[start.Index..(end + 1)];
                if (FirstJsonObject(syntax) == syntax || !response.Contains(syntax, StringComparison.Ordinal)
                    || artifacts.Any(a => a.Content.Contains(syntax, StringComparison.Ordinal)))
                {
                    continue;
                }
                artifacts.Add(Create(GroundedArtifactKind.Snippet, syntax, source, topic));
            }
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
                artifacts.Add(Create(GroundedArtifactKind.TechnicalName, name, source, name));
            }
        }

        foreach (var source in evidence)
        {
            foreach (Match label in Regex.Matches(source.Snippet, @"\b(?<name>[A-Z][A-Z_]+)\s*:\s*-?\d+\b"))
            {
                var name = label.Groups["name"].Value;
                if (!artifacts.Any(a => a.Entity == name) && Regex.IsMatch(response, $@"\b{Regex.Escape(name)}\b"))
                {
                    artifacts.Insert(0, Create(GroundedArtifactKind.TechnicalName, name, source, name));
                }
            }
        }

        return (response, artifacts);
    }


    private static (bool HasScope, ActiveSubject? Owner) FindDocumentedOwner(
        KnowledgeSearchResultItem source, string content, IReadOnlyList<ActiveSubject> identities)
    {
        // These are documentary record boundaries, not guesses from the current focus.
        var records = new List<(string Name, int Start, int End)>();
        foreach (Match row in Regex.Matches(source.Snippet,
            @"(?m)^[ \t]*(?:\d+[ \t]*\|[ \t]*)?(?<name>[\p{L}_$][\p{L}\p{N}_$]*)[ \t]*\|"))
        {
            var end = source.Snippet.IndexOf('\n', row.Index);
            records.Add((row.Groups["name"].Value, row.Index, end < 0 ? source.Snippet.Length : end));
        }
        foreach (Match row in Regex.Matches(source.Snippet,
            @"(?m)^[ \t]*\(\d+\s*,\s*N?'(?<name>[\p{L}_$][\p{L}\p{N}_$]*)'\s*,\s*N?'(?<description>(?:''|[^'])*)'"))
        {
            var description = row.Groups["description"];
            records.Add((row.Groups["name"].Value, description.Index, description.Index + description.Length));
        }
        var headings = Regex.Matches(source.Snippet,
            @"(?m)^[ \t]*(?<name>[\p{L}_$][\p{L}\p{N}_$]*)[ \t]*\r?\n[ \t]*Descripci[oó]n y ejemplo\b").Cast<Match>().ToArray();
        for (var index = 0; index < headings.Length; index++)
        {
            records.Add((headings[index].Groups["name"].Value, headings[index].Index,
                index + 1 < headings.Length ? headings[index + 1].Index : source.Snippet.Length));
        }

        var scopes = new List<string?>();
        var offset = 0;
        while ((offset = source.Snippet.IndexOf(content, offset, StringComparison.Ordinal)) >= 0)
        {
            var names = records.Where(record => record.Start <= offset && offset + content.Length <= record.End)
                .Select(record => record.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            scopes.Add(names.Length == 1 ? names[0] : null);
            offset += content.Length;
        }
        if (scopes.All(name => name is null)) { return (false, null); }
        if (scopes.Any(name => name is null) || scopes.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
        {
            return (true, null);
        }
        var owners = identities.Where(identity => identity.Name.Equals(scopes[0], StringComparison.OrdinalIgnoreCase)).ToArray();
        return (true, owners.Length == 1 ? owners[0] : null);
    }

    // Quotes are deliberately ignored: the legacy syntax nests unescaped quotes, so only braces delimit it.
    private static int FindBalancedBraceEnd(string text, int start)
    {
        var depth = 0;
        for (var i = start; i < text.Length && i - start <= 600 && text[i] is not ('\r' or '\n'); i++)
        {
            if (text[i] == '{') { depth++; }
            else if (text[i] == '}' && --depth == 0) { return i; }
        }
        return -1;
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
