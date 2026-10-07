using System.Text.RegularExpressions;

namespace DesignerIA.Api.Services.Knowledge;

internal static class RetrievalQueryExpansion
{
    // Retrieval vocabulary only: these variants never establish that an API exists.
    private static readonly (string Pattern, string[] Verbs)[] Operations =
    [
        (@"\b(refresc\w*|refresh\w*|recarg\w*|reload\w*|actualiz\w*)\b", ["recargar", "refresh"]),
        (@"\b(ocult\w*|hide|escond\w*)\b", ["ocultar", "hide"]),
        (@"\b(mostr\w*|show|visualiz\w*)\b", ["mostrar", "show"])
    ];

    public static IReadOnlyList<string> Expand(string message)
    {
        var operation = Operations.FirstOrDefault(o => Regex.IsMatch(message, o.Pattern, RegexOptions.IgnoreCase));
        if (operation.Verbs is null) { return []; }

        var queries = new List<string>();
        foreach (Match entity in Regex.Matches(message, @"\b[A-Z][a-z]+(?:[A-Z][A-Za-z0-9]*)+\b"))
        {
            var words = Regex.Matches(entity.Value, @"[A-Z]+(?=[A-Z][a-z]|\b)|[A-Z][a-z0-9]+")
                .Select(word => word.Value).ToArray();
            if (words.Length < 2) { continue; }
            var acronym = string.Concat(words.Skip(words.Length > 2 ? 1 : 0).Select(word => word[0]));
            foreach (var verb in operation.Verbs)
            {
                queries.Add($"{verb} {acronym}");
            }
            queries.Add($"{operation.Verbs[0]} {entity.Value}");
        }
        return queries.Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
    }
}
