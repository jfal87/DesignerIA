using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Services;

internal enum ConversationExecutor
{
    None, GeneralAnswer, ConversationResponse, GroundedKnowledge,
    HandlerEvidence, MetadataQuery, ViewCount, ArtifactReuse, ViewSpecPreparation
}

internal enum ConversationCapabilityEffect { None, ReadOnly, Preparation }

internal sealed record ConversationCapabilityDecision(
    ConversationExecutor Executor,
    ConversationCapabilityEffect Effect,
    ResolvedConversationSubject? Identity,
    string? RequestKind,
    string ResponseMode,
    IReadOnlyList<string> SearchQueries)
{
    public string? Resource { get; init; }
    public string? ResourceOperation { get; init; }
    public string? Search { get; init; }
    public string? Answer { get; init; }
    public IReadOnlyList<GroundedArtifact> Artifacts { get; init; } = [];
    public string? Reason { get; init; }
    public bool Rejected { get; init; }
    public bool CanCommitContext { get; init; }
    public bool AllowKnowledgeTool { get; init; }
}

/// <summary>
/// The executable allowlist: local answers, scoped artifacts, documentary retrieval,
/// handler evidence, catalogue queries, view count and ViewSpec preparation.
/// There is no external-write executor. Intent identifies a semantic family, never a tool name.
/// Both plan compatibility and execution selection use this same matrix.
/// </summary>
internal static class ConversationCapabilityRouter
{
    public static ConversationCapabilityDecision Route(
        ValidatedConversationPlan plan,
        IReadOnlyList<GroundedArtifact> artifacts,
        bool minimalViewPreparation = false,
        int? artifactChoice = null)
    {
        var decision = Select(plan);
        if (decision.Executor == ConversationExecutor.ViewSpecPreparation)
        {
            return decision with { AllowKnowledgeTool = !minimalViewPreparation };
        }
        if (decision.Executor != ConversationExecutor.ArtifactReuse) { return decision; }

        var subject = decision.Identity?.Subject;
        var matches = artifacts.Where(artifact => subject is not null
                && artifact.Origin is "Knowledge" or "Live"
                && !string.IsNullOrWhiteSpace(artifact.Source)
                && SameSubject(artifact.OriginatingSubject, subject)
                && IsCompatibleArtifact(artifact.Kind, decision.ResponseMode))
            .DistinctBy(artifact => artifact.Content).ToArray();
        if (matches.Length == 0)
        {
            // Documented syntax that is not strict JSON is explained as-is; it is never repaired or delivered as JSON.
            IReadOnlyList<GroundedArtifact> documented = decision.ResponseMode == "json" ? FindDocumentedSyntax(artifacts, subject) : [];
            if (documented.Count > 0)
            {
                return Stop(plan, "GroundedArtifactNotJson", DescribeDocumentedSyntax(subject!, documented)) with
                {
                    CanCommitContext = true
                };
            }
            return Reject(plan, "GroundedArtifactMissing",
                "No tengo un artefacto grounded de ese tipo y sujeto en la conversación. Indica cuál necesitas.");
        }
        if (matches.Length > 1 && decision.ResponseMode != "names")
        {
            // The user answered the numbered options below; the choice only picks among this subject's own artifacts.
            if (artifactChoice is { } choice && choice >= 1 && choice <= matches.Length)
            {
                return decision with { Artifacts = Array.AsReadOnly(new[] { matches[choice - 1] }) };
            }
            return Reject(plan, "GroundedArtifactAmbiguous", DescribeArtifactOptions(subject!, matches));
        }
        return decision with { Artifacts = Array.AsReadOnly(matches) };
    }

    /// <summary>Several verified artifacts of one subject, told apart by their exact content and provenance.</summary>
    internal static string DescribeArtifactOptions(ActiveSubject subject, IReadOnlyList<GroundedArtifact> options)
    {
        var text = new System.Text.StringBuilder()
            .Append("Tengo ").Append(options.Count).Append(" ejemplos verificados de «").Append(subject.Name)
            .AppendLine("» y no voy a elegir uno por ti. Indica cuál necesitas (por ejemplo «el 2»):");
        for (var index = 0; index < options.Count; index++)
        {
            text.AppendLine().Append(index + 1).Append(". Fuente: ").Append(options[index].Origin == "Live" ? "GestionEngine real, " : "documentación, ")
                .AppendLine(options[index].Source).AppendLine("```").AppendLine(options[index].Content).AppendLine("```");
        }
        return text.ToString().TrimEnd();
    }

    /// <summary>
    /// Configuration-like snippets shown for the subject that are exact documentary text but not valid JSON.
    /// An unowned snippet counts only when it was displayed in an answer about that same subject.
    /// </summary>
    internal static IReadOnlyList<GroundedArtifact> FindDocumentedSyntax(
        IReadOnlyList<GroundedArtifact> artifacts, ActiveSubject? subject) => subject is null ? [] : artifacts
        .Where(artifact => artifact.Kind == GroundedArtifactKind.Snippet && artifact.Origin == "Knowledge"
            && !string.IsNullOrWhiteSpace(artifact.Source)
            && (SameSubject(artifact.OriginatingSubject, subject)
                || artifact.OriginatingSubject is null && artifact.Topic.Equals(subject.Name, StringComparison.OrdinalIgnoreCase))
            && artifact.Content.TrimStart() is { Length: > 0 } content && content[0] is '{' or '[' && !IsJson(content))
        .DistinctBy(artifact => artifact.Content).ToArray();

    private static bool IsJson(string content)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(content);
            return true;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Answer to a JSON request built only from retrieved text: objects documented for that subject are reproduced
    /// with their source; syntax that is not valid JSON is shown as-is with a warning; otherwise the gap is stated.
    /// </summary>
    internal static string DescribeDocumentedJson(ActiveSubject subject, IReadOnlyList<GroundedArtifact> artifacts)
    {
        var valid = artifacts.Where(artifact => artifact.Kind == GroundedArtifactKind.Json && artifact.Origin == "Knowledge"
            && !string.IsNullOrWhiteSpace(artifact.Source) && SameSubject(artifact.OriginatingSubject, subject))
            .DistinctBy(artifact => artifact.Content).Take(3).ToArray();
        var syntax = FindDocumentedSyntax(artifacts, subject);
        if (valid.Length == 0)
        {
            return syntax.Count > 0 ? DescribeDocumentedSyntax(subject, syntax)
                : $"No encontré un JSON documentado para «{subject.Name}» en la evidencia consultada, y no voy a inventarlo. "
                    + "Puedo explicar cómo se configura con la documentación disponible o buscar ejemplos reales.";
        }
        var response = new System.Text.StringBuilder()
            .Append("JSON documentado para «").Append(subject.Name).AppendLine("», reproducido de la fuente sin modificar:");
        foreach (var artifact in valid)
        {
            response.AppendLine().Append("Fuente: ").AppendLine(artifact.Source)
                .AppendLine("```json").AppendLine(artifact.Content).AppendLine("```");
        }
        return response.ToString().TrimEnd();
    }

    private static string DescribeDocumentedSyntax(ActiveSubject subject, IReadOnlyList<GroundedArtifact> documented)
    {
        var response = new System.Text.StringBuilder()
            .Append("No tengo un JSON válido y verificado para «").Append(subject.Name).AppendLine("».")
            .AppendLine(documented.Count == 1
                ? "La documentación consultada muestra esta sintaxis, que no es JSON válido tal como está escrita; la reproduzco sin corregirla ni completarla:"
                : "La documentación consultada muestra estas sintaxis, que no son JSON válido tal como están escritas; las reproduzco sin corregirlas ni completarlas:");
        for (var index = 0; index < documented.Count; index++)
        {
            response.AppendLine().Append(documented.Count == 1 ? string.Empty : $"{index + 1}. ")
                .Append("Fuente: ").AppendLine(documented[index].Source)
                .AppendLine("```").AppendLine(documented[index].Content).AppendLine("```");
        }
        return response.AppendLine()
            .Append("Puedo explicar cómo se configura o buscar ejemplos reales de uso, pero no inventar una versión corregida.").ToString();
    }

    internal static string? GetIncompatibility(ValidatedConversationPlan plan)
    {
        var decision = Select(plan);
        return decision.Rejected ? decision.Reason : null;
    }

    private static ConversationCapabilityDecision Select(ValidatedConversationPlan plan)
    {
        if (plan.Intent == "UnsupportedAction")
        {
            return Stop(plan, "CapabilityNotAllowed", DesignerCapabilities.ExecutionBoundary);
        }
        if (plan.Intent == "Unknown")
        {
            return Stop(plan, "UnknownRequest",
                "No puedo responder esa consulta con la evidencia disponible de DesignerIA. Puedo ayudarte con programación, documentación y capacidades autorizadas.");
        }
        if (plan.Intent == "ViewCreation")
        {
            return Allow(plan, ConversationExecutor.ViewSpecPreparation, ConversationCapabilityEffect.Preparation);
        }
        if (plan.Identity is not null && !HasSupportedOrigin(plan.Identity))
        {
            return Reject(plan, "SubjectNotConfirmed");
        }
        if (plan.Intent == "GestionEngineMetadata" && plan.Identity is not null)
        {
            return Reject(plan, "MetadataSubjectReferenceIncompatible");
        }
        if (plan.ReuseGroundedArtifact)
        {
            if (plan.Identity is null || !HasSupportedOrigin(plan.Identity)
                || plan.ResponseMode is not ("json" or "code" or "variable" or "names"))
            {
                return Reject(plan, "ArtifactRequestIncompatible");
            }
            return Allow(plan, ConversationExecutor.ArtifactReuse);
        }
        if (plan.Intent == "GeneralQuestion")
        {
            return plan.SubjectKind == "General" && plan.Handler is null
                ? Allow(plan, ConversationExecutor.GeneralAnswer)
                : Reject(plan, "GeneralSubjectIncompatible");
        }
        if (plan.Intent == "Conversation")
        {
            return plan.Identity is null && plan.RequestKind is null
                ? Allow(plan, ConversationExecutor.ConversationResponse)
                : Reject(plan, "ConversationOperationIncompatible");
        }
        if (plan.Intent == "GestionEngineMetadata")
        {
            if (plan.RequestKind is not (null or "Explanation" or "Evidence" or "Search"))
            {
                return Reject(plan, "MetadataOperationIncompatible");
            }
            if (plan.Resource is null && plan.Operation is null)
            {
                if (plan.SubjectKind == "Handler") { return Reject(plan, "ViewCountSubjectIncompatible"); }
                return plan.SubjectKind is null or "View" or "General" or "Topic"
                    ? Allow(plan, ConversationExecutor.ViewCount) with { Resource = "Views", ResourceOperation = "Count" }
                    : Reject(plan, "MetadataSubjectIncompatible");
            }
            if (!GestionEngineMetadataService.IsSupported(plan.Resource, plan.Operation)
                || plan.Operation == "Search" && string.IsNullOrWhiteSpace(plan.Search))
            {
                return Reject(plan, "MetadataCapabilityNotAllowed");
            }
            return plan.SubjectKind is null or "Handler" or "General" or "Topic"
                ? Allow(plan, ConversationExecutor.MetadataQuery)
                : Reject(plan, "MetadataSubjectIncompatible");
        }
        if (plan.Intent is not ("HandlerQuestion" or "KnowledgeQuestion" or "MixedQuestion"))
        {
            return Reject(plan, "CapabilityNotAllowed");
        }
        if (plan.Identity is null || !HasSupportedOrigin(plan.Identity))
        {
            return Reject(plan, "SubjectNotConfirmed");
        }
        var documentaryTopic = plan.SubjectKind == "Topic" && plan.Identity.IsDocumentary;
        if (plan.Intent == "HandlerQuestion" && plan.SubjectKind != "Handler" && !documentaryTopic)
        {
            return Reject(plan, "HandlerSubjectMissing");
        }
        if (plan.RequestKind is null)
        {
            return Stop(plan, "RequestKindAmbiguous",
                "¿Quieres saber cómo configurar técnicamente la funcionalidad o cómo usarla ya implementada?") with
            {
                CanCommitContext = true
            };
        }
        if (plan.RequestKind is not ("Explanation" or "Use" or "Configuration" or "Examples" or "Parameters" or "Evidence" or "Search"))
        {
            return Reject(plan, "OperationNotRegistered");
        }
        if (plan.SubjectKind == "Handler")
        {
            // "How do I implement it?" asks for an explanation with steps; exact formats (json, names, code) and
            // parameter or example listings keep the deterministic LIVE answer.
            if (plan.RequestKind is "Use" or "Explanation" || plan.Identity.IsDocumentary
                || plan.RequestKind == "Configuration" && plan.ResponseMode is "explanation" or "summary")
            {
                return Allow(plan, ConversationExecutor.GroundedKnowledge);
            }
            return plan.Intent == "HandlerQuestion"
                ? Allow(plan, ConversationExecutor.HandlerEvidence)
                : Reject(plan, "HandlerOperationCapabilityMismatch");
        }
        if (plan.SubjectKind is "Function" or "Property" or "View" or "Topic"
            && (plan.Intent is "KnowledgeQuestion" or "MixedQuestion" || documentaryTopic))
        {
            return Allow(plan, ConversationExecutor.GroundedKnowledge);
        }
        return Reject(plan, "CombinationNotRegistered");
    }

    private static bool HasSupportedOrigin(ResolvedConversationSubject identity) => identity.Source switch
    {
        "Live" => identity.IsLive && !string.IsNullOrWhiteSpace(identity.Confirmation),
        "Documentary" => !string.IsNullOrWhiteSpace(identity.Confirmation) && !identity.IsLive,
        "NamedSubject" or "current" or "previous" or "first" or "handler" or "KnowledgeTopic" or "GeneralSubject" => true,
        _ => false
    };

    private static bool IsCompatibleArtifact(GroundedArtifactKind kind, string responseMode) => responseMode switch
    {
        "json" => kind == GroundedArtifactKind.Json,
        "code" => kind is GroundedArtifactKind.Snippet or GroundedArtifactKind.Variable,
        "variable" => kind == GroundedArtifactKind.Variable,
        "names" => kind is GroundedArtifactKind.TechnicalName or GroundedArtifactKind.Variable,
        _ => false
    };

    private static ConversationCapabilityDecision Allow(
        ValidatedConversationPlan plan, ConversationExecutor executor,
        ConversationCapabilityEffect effect = ConversationCapabilityEffect.ReadOnly) =>
        new(executor, effect, plan.Identity, plan.RequestKind, plan.ResponseMode, plan.SearchQueries)
        {
            Resource = plan.Resource,
            ResourceOperation = plan.Operation,
            Search = plan.Search,
            Answer = plan.GeneralAnswer,
            CanCommitContext = true
        };

    private static ConversationCapabilityDecision Stop(ValidatedConversationPlan plan, string reason, string answer) =>
        new(ConversationExecutor.None, ConversationCapabilityEffect.None, plan.Identity, plan.RequestKind,
            "explanation", plan.SearchQueries) { Reason = reason, Answer = answer };

    private static ConversationCapabilityDecision Reject(
        ValidatedConversationPlan plan, string reason,
        string answer = "Esa combinación de sujeto, operación y capacidad no está registrada. Precisa qué necesitas consultar.") =>
        Stop(plan, reason, answer) with { Rejected = true };
}
