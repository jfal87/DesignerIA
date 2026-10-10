using System.Text.RegularExpressions;

namespace DesignerIA.Api.Services.Knowledge;

internal static class RetrievalQueryExpansion
{
    // Retrieval vocabulary only: these variants never establish that an API exists.
    private static readonly (string Pattern, string[] Verbs)[] Operations =
    [
        (@"\b(refresc\w*|refresh\w*|recarg\w*|reload\w*|actualiz\w*)\b", ["refrescar", "recargar", "refresh"]),
        (@"\b(ocult\w*|hide|escond\w*)\b", ["ocultar", "hide"]),
        (@"\b(mostr\w*|show|visualiz\w*)\b", ["mostrar", "show"])
    ];

    // The user's Spanish word against the English term the sources use for the same thing. Each pair is backed by
    // sources that describe one item both ways: the Wiki lists ColorCelda as "Cambiar el fondo del color de la celda"
    // and the action catalogue as "Setea el Background-Color de las celdas de una columna".
    private static readonly (string Pattern, string SourceTerm)[] SourceTerms =
    [
        (@"\bfondo\b", "Background-Color"),
        // The modal shown when a view opens is documented by its identifiers ("Detalle mensaje modal.msg": the SP
        // suffix _MensajeModal and the variable modalActivo). The user says "modal" or misspells the name
        // ("MensageModal", "MesageModal").
        (@"\b(?:me[ns]{1,2}a[gj]e ?)?modal\b", "MensajeModal modalActivo")
    ];

    /// <summary>The same question worded with the term the sources use; retrieval vocabulary only.</summary>
    public static IReadOnlyList<string> Reword(string message) => SourceTerms
        .Where(term => Regex.IsMatch(message, term.Pattern, RegexOptions.IgnoreCase))
        .Select(term => Regex.Replace(message, term.Pattern, term.SourceTerm, RegexOptions.IgnoreCase))
        .ToArray();

    public static bool MatchesRequestedOperation(string message, string evidence) =>
        Operations.Any(operation => Regex.IsMatch(message, operation.Pattern, RegexOptions.IgnoreCase)
            && Regex.IsMatch(evidence, operation.Pattern, RegexOptions.IgnoreCase));

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
