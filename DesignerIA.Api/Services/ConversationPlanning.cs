using System.Text.Json;
using System.Text.Json.Serialization;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Services;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed record SemanticProposal(
    string Intent,
    string? Topic,
    bool ReferencesPreviousTopic,
    string? Handler,
    string ResponseMode,
    IReadOnlyList<string>? SearchQueries,
    string? Resource,
    string? Operation,
    string? Search,
    string? GeneralAnswer = null,
    string? SubjectKind = null,
    string? SubjectName = null,
    string? SubjectReference = null,
    string? RequestKind = null,
    bool ForbiddenAction = false,
    string? Goal = null,
    bool ReuseGroundedArtifact = false)
{
    internal static SemanticProposal Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) { throw new JsonException("Expected a proposal object."); }
        var fields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!fields.Add(property.Name)) { throw new JsonException("Duplicate proposal field."); }
        }
        if (!new[] { "intent", "responseMode", "referencesPreviousTopic", "searchQueries" }.All(fields.Contains))
        {
            throw new JsonException("Missing required proposal fields.");
        }
        return JsonSerializer.Deserialize<SemanticProposal>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new JsonException("Empty proposal.");
    }

    private static readonly System.Text.RegularExpressions.Regex AsksSomething = new(
        @"^(?:(?:y|pero|entonces|oye|hola|ahora)\s+)*(?:se puede|se pueden|se podria|puedo|podemos|es posible|existe|existen|hay|tiene|tienen|permite|soporta|"
        + @"que|como|cual|cuales|cuando|donde|por que|para que|quien|cuanto|cuantos|cuantas)\b",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// A documentary question whose operation the planner left empty. Asking "configure or use?" only helps when the
    /// message just names a subject. A message that already asks something (question mark, interrogative word, or a
    /// possibility/existence verb) is answered with what the documentation says: a read-only explanation. It does
    /// not confirm the subject nor authorize anything; handlers and artifact requests keep their own rules.
    /// </summary>
    internal SemanticProposal? RecoverQuestionKind(string message)
    {
        if (RequestKind is not null || Intent != "KnowledgeQuestion" || SubjectKind == "Handler"
            || ReuseGroundedArtifact || ForbiddenAction || ResponseMode != "explanation" || !string.IsNullOrWhiteSpace(Handler))
        {
            return null;
        }
        var normalized = Knowledge.KnowledgeSearchService.Normalize(message).Trim();
        return message.Contains('?') || AsksSomething.IsMatch(normalized) ? this with { RequestKind = "Explanation" } : null;
    }

    /// <summary>
    /// An exact artifact can only be reused in an exact format (json, code, variable, names). When the planner sets
    /// the reuse flag on an explanation there is nothing exact to deliver: the message is an ordinary read-only
    /// question about the same subject, so the flag is dropped and no artifact is reused.
    /// </summary>
    internal SemanticProposal? AsExplanationWithoutReuse() =>
        ReuseGroundedArtifact && !ForbiddenAction && ResponseMode is "explanation" or "summary"
        && Intent is "KnowledgeQuestion" or "HandlerQuestion" or "MixedQuestion"
            ? this with { ReuseGroundedArtifact = false } : null;

    /// <summary>
    /// A request to reuse an exact artifact does not search: when the planner also sends queries with it, they are
    /// dropped and only what was already verified for the subject can be delivered (or offered, or reported missing).
    /// </summary>
    internal SemanticProposal? AsReuseWithoutQueries() =>
        ReuseGroundedArtifact && !ForbiddenAction && ResponseMode is "json" or "code" or "variable" or "names"
        && Intent is "KnowledgeQuestion" or "HandlerQuestion" && (SearchQueries ?? []).Any(query => !string.IsNullOrWhiteSpace(query))
            ? this with { SearchQueries = [] } : null;

    /// <summary>
    /// The same question about the topic the planner described, for when the function or property it named is not
    /// confirmed anywhere ("MensageModal" for the modal message whose real suffix is _MensajeModal). Nothing confirms
    /// the name: the topic is searched as documentation and the answer says the name was not confirmed.
    /// </summary>
    internal SemanticProposal? AsDocumentaryTopic() =>
        Intent == "KnowledgeQuestion" && SubjectKind is "Function" or "Property"
        && !string.IsNullOrWhiteSpace(SubjectName) && !string.IsNullOrWhiteSpace(Topic)
        && !Topic.Trim().Equals(SubjectName.Trim(), StringComparison.OrdinalIgnoreCase)
        && SubjectReference is null && !ReuseGroundedArtifact && !ForbiddenAction && ResponseMode is "explanation" or "summary"
            ? this with { SubjectKind = "Topic", SubjectName = Topic.Trim(), RequestKind = RequestKind is null or "Search" ? "Explanation" : RequestKind }
            : null;

    /// <summary>
    /// A planner Conversation that only names the identifier typed by the user is a technical subject without an operation.
    /// The user's literal replaces every model-supplied name and text; catalogue and documents decide identity afterwards.
    /// </summary>
    internal SemanticProposal? RecoverIdentifierSubject(string message)
    {
        var literal = message.Trim();
        var names = new[] { SubjectName, Topic }.Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
        if (Intent != "Conversation" || ReuseGroundedArtifact || ForbiddenAction
            || !ConversationSubjectResolver.IsIdentifier(literal)
            || names.Length == 0 || names.Any(name => !name!.Trim().Equals(literal, StringComparison.OrdinalIgnoreCase))
            || SubjectKind is not (null or "General") || !string.IsNullOrWhiteSpace(Handler)
            || SubjectReference is not null || ReferencesPreviousTopic || RequestKind is not null
            || (SearchQueries ?? []).Any(query => !string.IsNullOrWhiteSpace(query))
            || ResponseMode != "explanation" || Resource is not null || Operation is not null || Search is not null)
        {
            return null;
        }
        return this with
        {
            Intent = "KnowledgeQuestion", SubjectName = literal, Topic = null, SubjectKind = null,
            GeneralAnswer = null, Goal = null, SearchQueries = []
        };
    }
}

internal sealed record ConversationPlanValidation(
    ValidatedConversationPlan? Plan, string? Reason = null, string? Clarification = null)
{
    internal string? GetDiagnosticReason(string? plannerFailureReason) =>
        plannerFailureReason is "PlannerNoResponse" or "PlannerNoJson" or "InvalidProposalJson" or "PlannerFailure"
            ? plannerFailureReason : Reason;
}

/// <summary>
/// Only structural validation and context binding can create an executable plan.
/// Validation reads the context; execution works on a staged lease until Finish commits it.
/// </summary>
internal sealed class ValidatedConversationPlan
{
    private readonly SemanticProposal _proposal;

    private ValidatedConversationPlan(SemanticProposal proposal, ResolvedConversationSubject? identity, string resolution)
    {
        _proposal = proposal;
        Identity = identity;
        ContextResolution = resolution;
    }

    public string Intent => _proposal.Intent;
    public string? Topic => _proposal.Topic;
    public string? Handler => _proposal.Handler;
    public string ResponseMode => _proposal.ResponseMode;
    public IReadOnlyList<string> SearchQueries => _proposal.SearchQueries ?? [];
    public string? Resource => _proposal.Resource;
    public string? Operation => _proposal.Operation;
    public string? Search => _proposal.Search;
    public string? GeneralAnswer => _proposal.GeneralAnswer;
    public string? SubjectKind => Subject?.Kind;
    public string? SubjectName => Subject?.Name;
    public string? RequestKind => _proposal.RequestKind;
    public bool ForbiddenAction => _proposal.ForbiddenAction;
    public bool ReuseGroundedArtifact => _proposal.ReuseGroundedArtifact;
    public ResolvedConversationSubject? Identity { get; }
    public ActiveSubject? Subject => Identity?.Subject;
    public string ContextResolution { get; }

    public static ConversationPlanValidation Validate(
        SemanticProposal proposal, ConversationSubjectResolution resolution, bool isPlannerOutput = false)
    {
        static ConversationPlanValidation Reject(string reason, string? clarification = null) => new(
            null, reason, clarification ?? "El sujeto y la operación propuesta no son compatibles. Indica qué necesitas consultar.");

        ConversationPlanValidation Approve(ResolvedConversationSubject? identity, string source)
        {
            var plan = new ValidatedConversationPlan(proposal, identity, source);
            return ConversationCapabilityRouter.GetIncompatibility(plan) is { } reason
                ? Reject(reason) : new(plan);
        }

        if (proposal.Intent is not ("GeneralQuestion" or "MixedQuestion" or "KnowledgeQuestion" or "HandlerQuestion"
            or "GestionEngineMetadata" or "ViewCreation" or "Conversation" or "Unknown" or "UnsupportedAction"))
        {
            return Reject("CapabilityNotAllowed", "Esa capacidad no está disponible en DesignerIA.");
        }
        if (proposal.ResponseMode is not ("explanation" or "json" or "code" or "variable" or "summary" or "names")
            || proposal.RequestKind is not (null or "Explanation" or "Configuration" or "Use" or "Examples"
                or "Parameters" or "Evidence" or "Search")
            // "Topic" is the kind C# itself assigns to documentary subjects and shows to the planner as context.
            || proposal.SubjectKind is not (null or "Handler" or "Function" or "Property" or "View" or "General" or "Topic")
            || proposal.SubjectReference is not (null or "current" or "previous" or "first" or "handler"))
        {
            return Reject("InvalidPlanValue");
        }
        if (proposal.SubjectName is { } suppliedName && (string.IsNullOrWhiteSpace(suppliedName) || suppliedName.Length > 250))
        {
            return Reject("InvalidSubjectName");
        }

        var queries = (proposal.SearchQueries ?? [])
            .Where(query => !string.IsNullOrWhiteSpace(query) && query.Length <= 250)
            .Select(query => query.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
        proposal = proposal with
        {
            Topic = string.IsNullOrWhiteSpace(proposal.Topic) ? null : proposal.Topic.Trim(),
            Handler = string.IsNullOrWhiteSpace(proposal.Handler) ? null : proposal.Handler.Trim(),
            SubjectName = proposal.SubjectName?.Trim(),
            Search = string.IsNullOrWhiteSpace(proposal.Search) ? null : proposal.Search.Trim(),
            SearchQueries = Array.AsReadOnly(queries),
            GeneralAnswer = proposal.Intent is "GeneralQuestion" or "MixedQuestion"
                || proposal.Intent == "Conversation" && !isPlannerOutput ? proposal.GeneralAnswer : null,
            Goal = proposal.Goal is { Length: > 0 and <= 500 } ? proposal.Goal.Trim() : null
        };
        if (proposal.Handler is not null && proposal.SubjectName is not null
            && !proposal.Handler.Equals(proposal.SubjectName, StringComparison.OrdinalIgnoreCase) && !resolution.ExplicitSubject)
        {
            return Reject("ConflictingSubjectNames");
        }
        if (proposal.Intent != "GestionEngineMetadata"
            && (proposal.Resource is not null || proposal.Operation is not null || proposal.Search is not null))
        {
            return Reject("MetadataFieldsOutsideCapability");
        }
        if (proposal.Intent == "GestionEngineMetadata")
        {
            if ((proposal.Resource is null) != (proposal.Operation is null)
                || proposal.Resource is not null && !GestionEngineMetadataService.IsSupported(proposal.Resource, proposal.Operation))
            {
                return Reject("MetadataCapabilityNotAllowed");
            }
            if (proposal.Operation == "Search" && proposal.Search is null && queries.Length == 0)
            {
                return Reject("MetadataOperationIncompatible");
            }
            proposal = proposal with { Search = proposal.Search ?? queries.FirstOrDefault() };
        }
        if (proposal.ReuseGroundedArtifact && (queries.Length != 0
            || proposal.ResponseMode is not ("json" or "code" or "variable" or "names")
            || proposal.Intent is "GeneralQuestion" or "ViewCreation" or "Unknown" or "UnsupportedAction"))
        {
            return Reject("ArtifactRequestIncompatible");
        }
        if (proposal.ForbiddenAction && (proposal.Intent is "Unknown" or "Conversation"
            || proposal.Intent == "KnowledgeQuestion" && proposal.RequestKind is null))
        {
            proposal = proposal with { Intent = "UnsupportedAction", ReuseGroundedArtifact = false };
        }
        if (proposal.Intent is "UnsupportedAction" or "ViewCreation" or "Unknown")
        {
            return Approve(null, "NoSubject");
        }

        if (proposal.Intent is "Conversation" && !proposal.ReuseGroundedArtifact)
        {
            if (isPlannerOutput && (resolution.Identity?.Subject.Kind is { } kind && kind != "General"
                || proposal.SubjectKind is "Handler" or "Function" or "Property" or "View"
                    && (proposal.SubjectName is not null || proposal.Handler is not null || proposal.SubjectReference is not null)))
            {
                return Reject("ConversationSubjectIncompatible", "El sujeto técnico requiere una operación de consulta. Indica qué necesitas saber.");
            }
            if (isPlannerOutput)
            {
                if (resolution.Status != SubjectResolutionStatus.NotFound || resolution.Identity is not null
                    || proposal.SubjectKind is not (null or "General") || proposal.SubjectName is not null
                    || proposal.Handler is not null || proposal.RequestKind is not null || queries.Length != 0
                    || proposal.ResponseMode != "explanation")
                {
                    return Reject("UntrustedPlannerConversation");
                }
                // Authorize only an unbound clarification; neither model text nor a technical subject can be committed.
                // Pointers to the conversation context (topic, reference, previous-topic flag) request no capability:
                // they are discarded with the rest of the model text instead of failing a social remark.
                proposal = proposal with
                {
                    GeneralAnswer = "Necesito un poco más de detalle. ¿Qué quieres consultar sobre programación o GestionEngine?",
                    Goal = null, Topic = null, SubjectReference = null, ReferencesPreviousTopic = false
                };
            }
            // Social responses do not establish technical identity or displace the user's focus.
            return Approve(null, isPlannerOutput ? "PlannerClarification" : "ConversationUnbound");
        }
        if (proposal.Intent == "GeneralQuestion")
        {
            return Approve(resolution.Identity, "GeneralSubject");
        }
        if (resolution.Status == SubjectResolutionStatus.Ambiguous)
        {
            return Reject("SubjectAmbiguous", "Hay varios candidatos; indica cuál necesitas: "
                + string.Join("; ", (resolution.Candidates ?? []).Select(candidate =>
                    $"{candidate.Subject.Name} ({candidate.Subject.Kind}, {candidate.Source}, {candidate.Confirmation ?? "referencia de contexto"})")));
        }
        if (resolution.Status == SubjectResolutionStatus.NotFound
            && !(proposal.Intent == "GestionEngineMetadata" && resolution.Reason == "NoSubject"))
        {
            var reason = resolution.Reason ?? "SubjectNotFound";
            return Reject(reason, "No pude confirmar el sujeto. Indica su nombre"
                + (resolution.Candidates is { Count: > 0 } candidates
                    ? " o confirma el candidato: " + string.Join("; ", candidates.Select(candidate => candidate.Subject.Name))
                    : " o precisa el tema documental que necesitas."));
        }
        if (proposal.Intent == "GestionEngineMetadata" && proposal.SubjectReference is not null)
        {
            return Reject("MetadataSubjectReferenceIncompatible",
                "Esta consulta de metadata es global y no puede limitarse al sujeto referido. Precisa si necesitas el catálogo global o información de ese sujeto.");
        }
        var subject = resolution.Identity?.Subject;
        if (proposal.RequestKind == "Parameters" && subject is null && proposal.Intent != "GestionEngineMetadata")
        {
            return Reject("ParametersSubjectMissing", "¿De qué objeto necesitas los parámetros? Indica su nombre.");
        }
        if (subject is null && proposal.Intent is ("KnowledgeQuestion" or "MixedQuestion"))
        {
            return Reject("KnowledgeSubjectMissing", "Indica el sujeto o tema que necesitas consultar.");
        }
        // Evidence decides the kind: a HandlerQuestion whose subject is documented as something else is a documentary question.
        if (proposal.Intent == "HandlerQuestion" && subject is { Kind: "Function" or "Property" or "View" }
            && resolution.Identity?.IsDocumentary == true)
        {
            proposal = proposal with { Intent = "KnowledgeQuestion" };
        }
        // The same rule in the other direction: an operation on a confirmed handler uses handler evidence,
        // never a documentary lookup, even when the model called it a knowledge question.
        if (proposal.Intent == "KnowledgeQuestion" && subject is { Kind: "Handler" }
            && proposal.RequestKind is not (null or "Use" or "Explanation") && !proposal.ReuseGroundedArtifact)
        {
            proposal = proposal with { Intent = "HandlerQuestion" };
        }
        var documentaryHandlerTopic = subject?.Kind == "Topic" && resolution.Identity?.IsDocumentary == true;
        if (proposal.ReuseGroundedArtifact && subject is null)
        {
            return Reject("ArtifactSubjectMissing");
        }
        proposal = proposal with
        {
            Handler = subject?.Kind == "Handler" || proposal.Intent == "HandlerQuestion" && documentaryHandlerTopic ? subject?.Name : null,
            SearchQueries = queries.Length > 0 || proposal.ReuseGroundedArtifact || subject is null
                ? proposal.SearchQueries : Array.AsReadOnly(new[] { subject.Name })
        };
        return Approve(resolution.Identity, resolution.Identity?.Source ?? "NoSubject");
    }

    public void StageSubject(ConversationLease conversation)
    {
        if (Subject is null || Subject.Kind == "General" || ReuseGroundedArtifact) { return; }
        if (ContextResolution == "first") { conversation.SelectCandidate(Subject); }
        if (Identity is not null)
        {
            conversation.SetResolvedSubject(Identity);
            // Candidates were an offer to pick a subject; a confirmed subject consumes them.
            conversation.ClearCandidateSet();
        }
    }

    public void StageOperation(ConversationLease conversation)
    {
        if (Subject is { Kind: not "General" } && SameSubject(conversation.GetActiveSubject(), Subject)
            && Intent is not ("UnsupportedAction" or "ViewCreation" or "Unknown")
            && (Intent != "Conversation" || ReuseGroundedArtifact))
        {
            conversation.SetRequestKind(RequestKind);
        }
    }
}
