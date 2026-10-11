using System.Text.RegularExpressions;
using DesignerIA.Api.Services.Knowledge;
using DesignerIA.Contracts;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Services;

internal enum SubjectResolutionStatus { Resolved, Ambiguous, NotFound }

internal sealed record ResolvedConversationSubject(
    ActiveSubject Subject, string Source, string? Confirmation = null, bool IsLive = false,
    string? ConfirmationSource = null)
{
    public bool IsDocumentary => Source == "Documentary" || ConfirmationSource == "Documentary";
}

internal sealed record ConversationSubjectSnapshot(
    ConversationFocus? Current, ConversationFocus? Previous,
    IReadOnlyList<ActiveSubject> Recent, IReadOnlyList<ActiveSubject> Candidates);

internal sealed record ConversationSubjectResolution(
    SubjectResolutionStatus Status, ResolvedConversationSubject? Identity = null,
    IReadOnlyList<ResolvedConversationSubject>? Candidates = null, string? Reason = null,
    bool ExplicitSubject = false);

// This boundary permits deterministic fixtures without opening SQL connections or reading Knowledge.
internal interface IConversationSubjectCatalog
{
    Task<IReadOnlyList<ResolvedConversationSubject>> FindHandlersAsync(string name, CancellationToken cancellationToken);
    IReadOnlyList<KnowledgeSearchResultItem> FindDocuments(string name);
}

internal sealed class ConversationSubjectCatalog(
    GestionEngineMetadataService metadata, KnowledgeSearchService knowledge) : IConversationSubjectCatalog
{
    public async Task<IReadOnlyList<ResolvedConversationSubject>> FindHandlersAsync(string name, CancellationToken cancellationToken)
    {
        // The existing catalogue search retains multiple exact matches; FindActionAsync uses TOP (1).
        var result = await metadata.QueryAsync("HandlerActions", "Search", name, cancellationToken);
        return result.Items.Select(item => new ResolvedConversationSubject(
            new ActiveSubject("Handler", item.Name), "Live", $"AccionesHandler: {item.Id}", true)).ToArray();
    }

    public IReadOnlyList<KnowledgeSearchResultItem> FindDocuments(string name) => knowledge.Search(name);
}

/// <summary>
/// Binds identity, never capability or operation. Lexical scores can order candidates,
/// but only exact catalogue matches, scoped context or documentary declarations confirm identity.
/// </summary>
internal sealed class ConversationSubjectResolver(IConversationSubjectCatalog catalog)
{
    public async Task<ConversationSubjectResolution> ResolveAsync(
        SemanticProposal proposal, ConversationSubjectSnapshot snapshot, string message,
        CancellationToken cancellationToken = default)
    {
        static ConversationSubjectResolution Missing(string reason) => new(SubjectResolutionStatus.NotFound, Reason: reason);
        static ConversationSubjectResolution Found(ResolvedConversationSubject identity) => new(SubjectResolutionStatus.Resolved, identity);
        static ConversationSubjectResolution Ambiguous(IEnumerable<ResolvedConversationSubject> candidates) =>
            new(SubjectResolutionStatus.Ambiguous, Candidates: candidates.ToArray(), Reason: "SubjectAmbiguous");

        if (proposal.Intent is "ViewCreation" or "UnsupportedAction" or "Unknown")
        {
            return Missing("NoSubject");
        }
        if (proposal.Intent == "Conversation" && !proposal.ReuseGroundedArtifact
            && (proposal.SubjectKind is null or "General"
                || proposal.SubjectName is null && proposal.Handler is null && proposal.SubjectReference is null))
        {
            return Missing("ConversationUnbound");
        }
        if (proposal.Intent == "GeneralQuestion" && proposal.SubjectReference is null)
        {
            return Found(new(new ActiveSubject("General", message), "GeneralSubject"));
        }
        if (proposal.Intent is not ("HandlerQuestion" or "KnowledgeQuestion" or "MixedQuestion"
            or "GestionEngineMetadata" or "Conversation" or "GeneralQuestion"))
        {
            return Missing("CapabilityNotAllowed");
        }
        if (proposal.Intent == "GestionEngineMetadata" && proposal.SubjectReference is null)
        {
            // The registered catalogue operations act on a resource/filter, not on a canonical individual.
            return Missing("NoSubject");
        }

        var name = proposal.SubjectName?.Trim() ?? proposal.Handler?.Trim()
            ?? (proposal.SubjectReference is null ? proposal.Topic?.Trim() : null);
        var literal = message.Trim();
        var explicitIdentifier = IsIdentifier(literal) && (proposal.SubjectReference is null || name is not null
            || new[] { snapshot.Current?.Subject, snapshot.Previous?.Subject }.OfType<ActiveSubject>()
                .Concat(snapshot.Recent).Concat(snapshot.Candidates)
                .Any(subject => subject.Name.Equals(literal, StringComparison.OrdinalIgnoreCase)));
        if (explicitIdentifier) { name = literal; }
        if (name is { Length: > 250 } || name is not null && string.IsNullOrWhiteSpace(name))
        {
            return Missing("InvalidSubjectName");
        }

        if (!explicitIdentifier && proposal.SubjectReference is { } reference)
        {
            // "The last handler we talked about" sometimes arrives as "previous" when the conversation holds a single
            // confirmed subject. Only when the planner itself names that subject is the reference taken as "current";
            // nothing is inferred from an unnamed or different subject.
            if (reference == "previous" && snapshot.Previous is null && snapshot.Current?.Subject is { } only
                && (proposal.SubjectKind is null || proposal.SubjectKind == only.Kind)
                && new[] { proposal.SubjectName, proposal.Handler, proposal.Topic }.Any(given =>
                    given is not null && given.Trim().Equals(only.Name, StringComparison.OrdinalIgnoreCase)))
            {
                reference = "current";
            }
            var subject = reference switch
            {
                "current" => snapshot.Current?.Subject,
                "previous" => snapshot.Previous?.Subject,
                "first" => snapshot.Candidates.FirstOrDefault(),
                "handler" => snapshot.Recent.LastOrDefault(subject => subject.Kind == "Handler"),
                _ => null
            };
            if (subject is null) { return Missing("SubjectReferenceMissing"); }
            if (name is not null && !name.Equals(subject.Name, StringComparison.OrdinalIgnoreCase))
            {
                return Missing("ConflictingSubjectReference");
            }
            var priorIdentity = reference == "current" ? snapshot.Current?.Identity
                : reference == "previous" ? snapshot.Previous?.Identity : null;
            return Found(new(subject, reference, priorIdentity?.Confirmation, priorIdentity?.IsLive ?? false,
                priorIdentity?.ConfirmationSource ?? priorIdentity?.Source));
        }
        if (name is null)
        {
            return Missing(proposal.ReferencesPreviousTopic ? "SubjectReferenceMissing" : "SubjectNotFound");
        }

        var known = new[] { snapshot.Current?.Subject, snapshot.Previous?.Subject }.OfType<ActiveSubject>()
            .Concat(snapshot.Recent).Concat(snapshot.Candidates)
            .Where(subject => subject.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();
        if (known.Select(subject => subject.Kind).Distinct().Count() > 1)
        {
            return Ambiguous(known.Select(subject => new ResolvedConversationSubject(subject, "Context")));
        }
        if (known.Length > 0)
        {
            var identity = snapshot.Current?.Subject == known[0] ? snapshot.Current.Identity
                : snapshot.Previous?.Subject == known[0] ? snapshot.Previous.Identity : null;
            return Found(new(known[0], "NamedSubject", identity?.Confirmation, identity?.IsLive ?? false,
                identity?.ConfirmationSource ?? identity?.Source)) with { ExplicitSubject = explicitIdentifier };
        }

        IReadOnlyList<ResolvedConversationSubject> handlers = [];
        if (IsIdentifier(name) || proposal.Intent == "HandlerQuestion" || proposal.Handler is not null)
        {
            handlers = await catalog.FindHandlersAsync(name, cancellationToken);
            var exact = handlers.Where(candidate => candidate.Subject.Kind == "Handler" && candidate.IsLive
                && candidate.Subject.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Distinct().ToArray();
            if (exact.Length > 1) { return Ambiguous(exact); }
            if (exact.Length == 1) { return Found(exact[0]) with { ExplicitSubject = explicitIdentifier }; }
        }

        var documents = catalog.FindDocuments(name);
        var functions = documents.SelectMany(document => Regex.Matches(document.Snippet,
                @"\bfunction\s+(?<name>[\p{L}_$][\p{L}\p{N}_$]*)\s*\(", RegexOptions.CultureInvariant)
            .Cast<Match>().Where(match => match.Groups["name"].Value.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(match => new ResolvedConversationSubject(new ActiveSubject("Function", match.Groups["name"].Value),
                "Documentary", document.SourceFile)))
            .DistinctBy(identity => identity.Subject).ToArray();
        if (functions.Length > 1) { return Ambiguous(functions); }
        if (functions.Length == 1) { return Found(functions[0]); }
        var documentary = documents.FirstOrDefault(document => document.Title.Equals(name, StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(document.Snippet, $@"(?<![\p{{L}}\p{{N}}_$]){Regex.Escape(name)}(?![\p{{L}}\p{{N}}_$])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
        if (documentary is not null)
        {
            // A mention confirms a documentary topic, not a Handler, Property or View in GestionEngine.
            var canonical = documentary.Title.Equals(name, StringComparison.OrdinalIgnoreCase) ? documentary.Title : name;
            return Found(new(new ActiveSubject("Topic", canonical), "Documentary", documentary.SourceFile));
        }
        if (handlers.Count > 0)
        {
            return handlers.Count > 1 ? Ambiguous(handlers)
                : new(SubjectResolutionStatus.NotFound, Candidates: handlers, Reason: "ApproximateCandidate");
        }
        if (!IsIdentifier(name) && proposal.Intent is ("KnowledgeQuestion" or "MixedQuestion"))
        {
            return Found(new(new ActiveSubject("Topic", name), "KnowledgeTopic"));
        }
        return Missing("SubjectNotFound");
    }

    internal static bool IsIdentifier(string value) =>
        value.Length is > 0 and <= 250 && (char.IsLetter(value[0]) || value[0] is '_' or '$')
        && value.All(character => char.IsLetterOrDigit(character) || character is '_' or '$' or '.');
}
