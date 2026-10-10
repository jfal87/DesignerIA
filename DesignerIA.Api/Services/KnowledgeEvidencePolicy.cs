using System.Text.RegularExpressions;
using DesignerIA.Contracts;
using DesignerIA.Api.Services.Knowledge;

namespace DesignerIA.Api.Services;

internal static class KnowledgeEvidencePolicy
{
    private static readonly Regex Scalar = new(
        @"(?<key>[A-Za-z_][\w.]*)[""”]?\s*[:=]\s*(?:[""“](?<value>[A-Za-z0-9_.-]{1,40})[""”]|(?<value>-?\d+(?:\.\d+)?|true|false)\b)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool IsInternalWikiTitle(string title) => title.StartsWith("$:/", StringComparison.Ordinal);

    public static IReadOnlyList<KnowledgeSearchResultItem> Select(
        IEnumerable<KnowledgeSearchResultItem> results, string? subject, string question, out string? conflict, bool requireSubjectMatch = false, bool allowSubjectMembers = false,
        string? coverageHints = null, IReadOnlyDictionary<string, double>? termWeights = null, string? preferredTerm = null, string? topic = null)
    {
        var candidates = results.Where(item => item.SourceType is "Document" or "Curated"
            && !IsInternalWikiTitle(item.Title)).ToArray();
        var exact = string.IsNullOrWhiteSpace(subject) ? [] : candidates.Where(item =>
            ContainsExactSubject(item.Title, subject, allowSubjectMembers) || ContainsExactSubject(item.SourceFile, subject, allowSubjectMembers)
                || ContainsExactSubject(item.Snippet, subject, allowSubjectMembers)).ToArray();
        // Deterministic search vocabulary (for example marker names for "bolitas") counts as part of the question.
        var coverage = QuestionCoverage(candidates, question + " " + coverageHints, termWeights, topic);
        // A long conversational message ("mmm, ahora que insistes, quiero...") adds words no document has; they must
        // not hide a fragment that covers most of the topic itself.
        bool OnTopic(KnowledgeSearchResultItem item) => coverage.Present[item] >= MinimumQuestionCoverage
            || coverage.TopicPresent.TryGetValue(item, out var share) && share >= MinimumTopicCoverage;
        // A confirmed subject (handler, function, documented term) restricts evidence to fragments that name it.
        // A topic phrased by the planner is only a hint: demanding its literal words would discard the real source.
        // Only a single-term subject (an identifier) is demanded literally; a documented multi-word topic rarely
        // repeats word for word in the fragment that answers it.
        var literalSubject = requireSubjectMatch && !string.IsNullOrWhiteSpace(subject) && !subject.Trim().Contains(' ');
        var scoped = literalSubject ? exact : Focus(candidates, question);
        var operation = scoped.Where(item => RetrievalQueryExpansion.MatchesRequestedOperation(
            question, item.Title + " " + item.SourceFile + " " + item.Snippet)).ToArray();
        var relevant = (operation.Length > 0 ? operation : scoped)
            .GroupBy(item => (item.SourceType, item.RelativePath, item.Title, Snippet: item.Snippet.Trim()))
            .Select(group => group.OrderByDescending(item => item.Score).First())
            .ToArray();
        conflict = null;
        // Scores of different queries are not comparable: order by how much of the current question each fragment
        // covers, and leave out fragments that cover far less than the best one instead of citing another topic.
        var best = relevant.Length == 0 ? 0 : relevant.Max(item => coverage[item]);
        // Fragments that name a confirmed subject are already on topic; the thresholds only guard planner-phrased topics.
        var guardRelevance = !literalSubject;
        if (guardRelevance && !relevant.Any(OnTopic)) { return []; }
        var selected = new List<KnowledgeSearchResultItem>();
        var reachedOnTopic = false;
        var countsBySource = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var countsByTitle = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        // A documented multi-word topic is not demanded literally, but fragments that do name it go first.
        var naming = literalSubject || !requireSubjectMatch ? [] : exact;
        if (!string.IsNullOrWhiteSpace(preferredTerm))
        {
            // The identifier a short follow-up is about (taken from the planner's wording) only counts where a
            // fragment literally contains it; an identifier no source has changes nothing.
            naming = naming.Concat(candidates.Where(item => (item.Title + " " + item.Snippet)
                .Contains(preferredTerm, StringComparison.OrdinalIgnoreCase))).ToArray();
        }
        foreach (var item in relevant.OrderByDescending(item => naming.Contains(item)).ThenByDescending(item => coverage[item])
            .ThenByDescending(item => item.Score).ThenBy(item => item.RelativePath, StringComparer.Ordinal))
        {
            if (selected.Count == 5) { break; }
            // The absolute share only removes fragments ranked below one that reaches it: the fragment that covers the
            // question best is never dropped in favour of weaker ones because of a word form ("inicio" / "iniciar").
            var onTopic = OnTopic(item);
            if (guardRelevance && !naming.Contains(item) && !onTopic && reachedOnTopic) { continue; }
            reachedOnTopic |= onTopic;
            // Each cited fragment must itself reach the minimum share of the question, not only the best one.
            if (guardRelevance && !naming.Contains(item)
                && coverage[item] < best * 0.4) { continue; }
            var count = countsBySource.GetValueOrDefault(item.RelativePath);
            // The same entry published in several files (Wiki versions) may fill at most two places.
            var sameTitle = countsByTitle.GetValueOrDefault(item.Title);
            if (count == 3 || sameTitle >= 2 && selected.All(chosen => chosen.RelativePath != item.RelativePath)) { continue; }
            selected.Add(item);
            countsBySource[item.RelativePath] = count + 1;
            countsByTitle[item.Title] = sameTitle + 1;
        }
        // For a confirmed subject every fragment that names it is checked before the display budget can discard a
        // conflicting claim. For a planner-phrased topic only the fragments actually used can contradict each other.
        conflict = FindConflict(literalSubject ? relevant : selected, question);
        return selected;
    }

    // Share of the question's weight (rarity of its terms) that the best fragment must contain. Below it the fragment
    // is about something else: citing it would present another topic as support. Presence only: a title that repeats
    // one word of the question does not make the fragment cover more of it.
    internal const double MinimumQuestionCoverage = 0.2;

    // Alternative for a resolved topic: the fragment contains at least half of the topic's own weight.
    internal const double MinimumTopicCoverage = 0.5;

    // Share of the question weight present in the best candidate (diagnostics and tests).
    internal static double BestCoverageShare(IReadOnlyList<KnowledgeSearchResultItem> candidates, string question,
        IReadOnlyDictionary<string, double>? termWeights = null)
    {
        var coverage = QuestionCoverage(candidates, question, termWeights);
        return coverage.Present.Count == 0 ? 0 : coverage.Present.Values.Max();
    }

    private sealed class CoverageMap() : Dictionary<KnowledgeSearchResultItem, double>(ReferenceEqualityComparer.Instance)
    {
        // Share (0..1) of the question weight whose terms appear in the candidate, without ranking bonuses.
        public Dictionary<KnowledgeSearchResultItem, double> Present { get; } = new(ReferenceEqualityComparer.Instance);
        // Same share measured over the terms of the resolved topic only; empty when there is no topic.
        public Dictionary<KnowledgeSearchResultItem, double> TopicPresent { get; } = new(ReferenceEqualityComparer.Instance);
    }

    private static CoverageMap QuestionCoverage(
        IReadOnlyList<KnowledgeSearchResultItem> candidates, string question, IReadOnlyDictionary<string, double>? termWeights = null,
        string? topic = null)
    {
        // Same measure the search uses, applied to the whole question: each term weighs by its rarity, and counts more
        // when it names the fragment (title or file) or repeats in it. The planner's own query words are not counted:
        // they already brought their candidates, and a rare word of theirs must not outweigh what the user asked.
        var terms = KnowledgeSearchService.Tokenize(KnowledgeSearchService.Normalize(question));
        var names = candidates.Select(item => KnowledgeSearchService.Normalize(item.Title + " " + item.SourceFile)).ToArray();
        var bodies = candidates.Select(item => KnowledgeSearchService.Normalize(item.Snippet)).ToArray();
        var weights = terms.Select(term => termWeights is not null && termWeights.TryGetValue(term, out var weight) ? weight
            : Math.Log(1 + candidates.Count / (1.0 + Enumerable.Range(0, candidates.Count).Count(index =>
                KnowledgeSearchService.CountTerm(names[index], term) > 0 || KnowledgeSearchService.CountTerm(bodies[index], term) > 0)))).ToArray();
        var coverage = new CoverageMap();
        var total = weights.Sum();
        var topicTerms = KnowledgeSearchService.Tokenize(KnowledgeSearchService.Normalize(topic ?? string.Empty)).ToHashSet(StringComparer.Ordinal);
        var topicTotal = terms.Select((term, position) => topicTerms.Contains(term) ? weights[position] : 0).Sum();
        for (var index = 0; index < candidates.Count; index++)
        {
            double score = 0;
            double present = 0;
            double topicPresent = 0;
            for (var position = 0; position < terms.Count; position++)
            {
                var named = KnowledgeSearchService.CountTerm(names[index], terms[position]) > 0;
                var occurrences = KnowledgeSearchService.CountTerm(bodies[index], terms[position]);
                if (!named && occurrences == 0) { continue; }
                present += weights[position];
                if (topicTerms.Contains(terms[position])) { topicPresent += weights[position]; }
                score += weights[position] * (1 + (named ? 1 : 0) + Math.Min(occurrences, 3) * 0.25);
            }
            coverage[candidates[index]] = score;
            coverage.Present[candidates[index]] = total <= 0 ? 0 : present / total;
            if (topicTotal > 0) { coverage.TopicPresent[candidates[index]] = topicPresent / topicTotal; }
        }
        return coverage;
    }

    private static bool ContainsExactSubject(string text, string subject, bool allowMembers)
    {
        // Accents and a plural ending do not make it a different subject ("gráfico" / "graficos").
        var compare = System.Globalization.CultureInfo.InvariantCulture.CompareInfo;
        const System.Globalization.CompareOptions options =
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace;
        var offset = 0;
        while (offset < text.Length && (offset = compare.IndexOf(text, subject, offset, options)) >= 0)
        {
            var end = Math.Min(text.Length, offset + subject.Length);
            if (end < text.Length && text[end] is 's' or 'S' && (end + 1 == text.Length || !IsIdentifierCharacter(text[end + 1]))) { end++; }
            // CamelCase membership scopes documentary topics only; it never confirms artifact ownership.
            var startsMember = allowMembers && offset > 0 && char.IsLower(text[offset - 1]) && char.IsUpper(text[offset]);
            var endsMember = allowMembers && end < text.Length && char.IsLower(text[end - 1]) && char.IsUpper(text[end]);
            if ((offset == 0 || !IsIdentifierCharacter(text[offset - 1]) || startsMember)
                && (end == text.Length || !IsIdentifierCharacter(text[end]) || endsMember))
            {
                return true;
            }
            offset = Math.Max(end, offset + 1);
        }
        return false;
    }

    private static bool IsIdentifierCharacter(char character) => char.IsLetterOrDigit(character) || character is '_' or '$';

    /// <summary>
    /// Comments on charts and comments on grids are different features: a question about one leaves out fragments
    /// that belong to the other, also when nothing else was found (the answer is then "no evidence", not the other
    /// feature). A fragment belongs to a feature by its title or file, or because its text names the feature's own
    /// identifiers (a mail titled "Edición comentarios" that configures ComentariosEnCelda is about grids).
    /// Nothing else is filtered here; relevance is decided by question coverage.
    /// </summary>
    public static IReadOnlyList<KnowledgeSearchResultItem> Focus(
        IReadOnlyList<KnowledgeSearchResultItem> evidence, string question)
    {
        var requestedTopic = Regex.Replace(question,
            @"\b(?:no|ni|excepto)\s+(?:(?:los|las|el|la|de|en|comentarios|sobre)\s+)*(?:gr[aá]ficos?|celdas?|grillas?)\b",
            string.Empty, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static bool AboutCharts(KnowledgeSearchResultItem item) =>
            Regex.IsMatch(item.Title + " " + item.SourceFile, @"gr[aá]fico|chart", RegexOptions.IgnoreCase)
            || Regex.IsMatch(item.Snippet, @"comentariosEnGrafico|ChartComments|personalizarJsonGrafico|MarkerType|MarkerSize|CIRCLE");
        static bool AboutGrids(KnowledgeSearchResultItem item) =>
            Regex.IsMatch(item.Title + " " + item.SourceFile, @"\b(celdas?|grillas?)\b", RegexOptions.IgnoreCase)
            || Regex.IsMatch(item.Snippet, @"ComentariosEnCelda|GridComments|JsonColumnasYRenglones");
        var asksCharts = Regex.IsMatch(requestedTopic, @"gr[aá]fico", RegexOptions.IgnoreCase);
        var asksGrids = Regex.IsMatch(requestedTopic, @"\b(celdas?|grillas?)\b", RegexOptions.IgnoreCase);
        if (asksCharts == asksGrids) { return evidence; }
        var kept = asksCharts
            ? evidence.Where(item => !AboutGrids(item) || AboutCharts(item)).ToArray()
            : evidence.Where(item => !AboutCharts(item) || AboutGrids(item)).ToArray();
        return kept;
    }

    private static bool IsAnotherEnumeration(string left, string right, string except)
    {
        static Dictionary<string, string> Members(string fragment) => Scalar.Matches(fragment).Cast<Match>()
            .Select(match => (Key: match.Groups["key"].Value.Split('.').Last(), Value: match.Groups["value"].Value.ToLowerInvariant()))
            .Where(member => Regex.IsMatch(member.Key, @"^[A-Z][A-Z_]+$"))
            .GroupBy(member => member.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.OrdinalIgnoreCase);
        var first = Members(left);
        var second = Members(right);
        var agreeing = first.Where(member => !member.Key.Equals(except, StringComparison.OrdinalIgnoreCase)
            && second.TryGetValue(member.Key, out var value) && value == member.Value).ToArray();
        // Values are unique inside one enumeration. Where the disputed member repeats the value of a member both
        // fragments agree on, it belongs to another list of that fragment (LINE: 0 next to CIRCLE: 0).
        bool OutsideTheSharedList(Dictionary<string, string> members) =>
            members.TryGetValue(except, out var disputed) && agreeing.Any(member => member.Value == disputed);
        return agreeing.Length >= 3 && (OutsideTheSharedList(first) || OutsideTheSharedList(second));
    }

    public static string? FindConflict(IReadOnlyList<KnowledgeSearchResultItem> evidence, string question)
    {
        // Two example configurations that set a property differently do not contradict each other when the question
        // is how to configure it; they do when it asks which value or type applies.
        var asksValue = Regex.IsMatch(question, @"\b(valor|valores|tipos?|enumeraci[oó]n|defecto|default|cu[aá]nto|cu[aá]l|cu[aá]les)\b", RegexOptions.IgnoreCase);
        var claims = evidence.SelectMany(item => Scalar.Matches(item.Snippet).Cast<Match>()
            .Select(match => new
            {
                Key = match.Groups["key"].Value.Split('.').Last(),
                Value = match.Groups["value"].Value.ToLowerInvariant(),
                Source = $"{item.SourceFile}: {item.Title}",
                Fragment = item.Snippet
            }))
            .Where(claim => asksValue && (Regex.IsMatch(question, $@"\b{Regex.Escape(claim.Key)}\b", RegexOptions.IgnoreCase)
                || Regex.IsMatch(claim.Key, @"^[A-Z][A-Z_]+$")))
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
            // A fragment that agrees on other members but gives the disputed one a value already taken by them is
            // describing a different enumeration that reuses the name (series types and marker types both have LINE).
            // Two versions of the same enumeration keep their values unique, so a differing value is a contradiction.
            if (Regex.IsMatch(property.Key, @"^[A-Z][A-Z_]+$") && assertions.All(left => assertions.All(right =>
                    left.Value == right.Value || IsAnotherEnumeration(left.Fragment, right.Fragment, property.Key))))
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
