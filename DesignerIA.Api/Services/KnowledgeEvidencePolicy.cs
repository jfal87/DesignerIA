using System.Text.RegularExpressions;
using DesignerIA.Contracts;

namespace DesignerIA.Api.Services;

internal static class KnowledgeEvidencePolicy
{
    private static readonly Regex Scalar = new(
        @"(?<key>[A-Za-z_][\w.]*)[""”]?\s*[:=]\s*(?:[""“](?<value>[A-Za-z0-9_.-]{1,40})[""”]|(?<value>-?\d+(?:\.\d+)?|true|false)\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<KnowledgeSearchResultItem> Focus(
        IReadOnlyList<KnowledgeSearchResultItem> evidence, string question)
    {
        if (!Regex.IsMatch(question, @"gr[aá]fico", RegexOptions.IgnoreCase))
        {
            return evidence;
        }

        var specific = evidence.Where(e =>
            Regex.IsMatch(e.Title + " " + e.SourceFile, @"gr[aá]fico|chart", RegexOptions.IgnoreCase)
            || Regex.IsMatch(e.Snippet, @"comentariosEnGrafico|personalizarJsonGrafico|MarkerType|MarkerSize|CIRCLE")).ToArray();
        return specific.Length == 0 ? evidence : specific;
    }

    public static string? FindConflict(IReadOnlyList<KnowledgeSearchResultItem> evidence, string question)
    {
        var claims = evidence.SelectMany(item => Scalar.Matches(item.Snippet).Cast<Match>()
            .Select(match => new
            {
                Key = match.Groups["key"].Value.Split('.').Last(),
                Value = match.Groups["value"].Value.ToLowerInvariant(),
                Source = $"{item.SourceFile}: {item.Title}",
                Fragment = item.Snippet
            }))
            .Where(claim => Regex.IsMatch(question, $@"\b{Regex.Escape(claim.Key)}\b", RegexOptions.IgnoreCase)
                || Regex.IsMatch(claim.Key, @"^[A-Z][A-Z_]+$")
                    && Regex.IsMatch(question, @"\b(valor|valores|tipo|enumeraci[oó]n)\b", RegexOptions.IgnoreCase))
            .GroupBy(claim => claim.Key, StringComparer.OrdinalIgnoreCase);

        var conflicts = new List<string>();
        foreach (var property in claims)
        {
            // A source containing several values can be a list of examples, not a single assertion.
            var assertions = property.GroupBy(claim => (claim.Source, claim.Fragment))
                .Where(source => source.Select(claim => claim.Value).Distinct().Count() == 1)
                .Select(source => source.First()).ToArray();
            if (assertions.Select(claim => claim.Value).Distinct().Count() < 2)
            {
                continue;
            }

            conflicts.Add($"{property.Key}: " + string.Join("; ", assertions.Select(claim => $"{claim.Value} ({claim.Source})")));
        }

        return conflicts.Count == 0 ? null
            : "Encontré evidencia documental contradictoria con valores incompatibles para la misma propiedad/configuración:\n"
                + string.Join("\n", conflicts.Select(conflict => "- " + conflict))
                + "\nNo puedo recomendar un valor definitivo sin identificar la fuente aplicable a esta versión/configuración. "
                + "Estos fragmentos no demuestran que los valores sean equivalentes.";
    }
}
