using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;

namespace DesignerIA.Api.Services;

internal static class HandlerNameMatcher
{
    public static IReadOnlyList<GestionEngineMetadataItem> Rank(
        IEnumerable<GestionEngineMetadataItem> items, string query, int limit)
    {
        var tokens = Tokens(query);
        var normalized = Normalize(query);
        if (tokens.Length == 0)
        {
            return [];
        }

        var ranked = items.Select(item =>
            {
                var name = Normalize(item.Name);
                var matches = tokens.Count(token => name.Contains(token, StringComparison.Ordinal)
                    || token.Length >= 6 && name.Contains(token[..5], StringComparison.Ordinal));
                var description = Normalize(item.Description ?? string.Empty);
                var descriptionMatches = tokens.Count(token => description.Contains(token, StringComparison.Ordinal));
                var score = name == normalized ? 1000
                    : name.Contains(normalized, StringComparison.Ordinal) ? 800
                    : matches == tokens.Length ? 600 - name.Length
                    : (matches * 100 + descriptionMatches * 30) / tokens.Length;
                if (score < 500 && tokens.Any(token => token.Length >= 8 && name.Contains(token, StringComparison.Ordinal)))
                {
                    score += 250;
                }
                if (matches == 0 && description.Contains(normalized, StringComparison.Ordinal))
                {
                    score = 10;
                }

                return (Item: item, Score: score);
            })
            .ToArray();
        var hasStrongMatches = ranked.Any(candidate => candidate.Score >= 500);
        return ranked.Where(candidate => hasStrongMatches
                ? candidate.Score >= 500
                : candidate.Score >= 50 || tokens.Length == 1 && candidate.Score == 10)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(limit)
            .Select(candidate => candidate.Item)
            .ToList();
    }

    private static string[] Tokens(string value) =>
        Regex.Split(Regex.Replace(value, @"([a-z])([A-Z])|([A-Z])([A-Z][a-z])", "$1$3 $2$4").ToLowerInvariant(), @"[^\p{L}0-9]+")
            .Select(Normalize)
            .Where(token => token.Length > 1 && token is not ("en" or "de" or "el" or "la" or "handler"))
            .Distinct().ToArray();

    private static string Normalize(string value) =>
        string.Concat(value.Normalize(NormalizationForm.FormD).Where(character =>
            CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character))).ToLowerInvariant();
}
