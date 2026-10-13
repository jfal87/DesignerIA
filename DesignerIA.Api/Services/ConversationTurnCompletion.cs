using System.Text.Json;
using DesignerIA.Api.Services.ViewSpecs;
using DesignerIA.Contracts;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Services;

internal sealed record ConversationTurnCompletion(
    string Message,
    ValidatedConversationPlan? Plan,
    ConversationCapabilityDecision? Capability,
    bool CanCommit,
    bool PublishCandidates = false,
    bool ForbiddenAction = false,
    string? FallbackReason = null,
    bool RecordClarification = false);

internal sealed record ConversationTurnOutcome(
    ChatResult Result, string? FallbackReason, string ContextDisposition);

/// <summary>Shared finalization after execution; failed or mismatched turns never commit their staged focus.</summary>
internal static class ConversationTurnCompleter
{
    public static ConversationTurnOutcome Finish(
        ChatResult result, ConversationLease conversation, ConversationTurnCompletion turn)
    {
        var mode = turn.Capability?.ResponseMode ?? "explanation";
        var reason = turn.FallbackReason;
        if (PlannerFailurePolicy.Error(reason) is { } plannerError)
        {
            return new(plannerError, reason, "Discarded");
        }
        if (turn.ForbiddenAction && result.Status == "ok" && mode is "explanation" or "summary"
            && result.Response?.Contains(DesignerCapabilities.ExecutionBoundary, StringComparison.Ordinal) != true)
        {
            result = result with { Response = result.Response + "\n\n" + DesignerCapabilities.ExecutionBoundary };
        }
        if (reason == "EvidenceConflict" && mode is "json" or "code" or "variable" or "names")
        {
            return new(new ChatResult("error", null, result.ToolInvoked,
                "La evidencia es contradictoria; no puedo proporcionar un artefacto definitivo en el formato solicitado."),
                reason, "Discarded");
        }

        var isPreparation = turn.Capability?.Executor == ConversationExecutor.ViewSpecPreparation;
        IReadOnlyList<GroundedArtifact> selectedArtifacts = isPreparation ? []
            : turn.Capability?.Executor == ConversationExecutor.ArtifactReuse
            ? turn.Capability.Artifacts
            : conversation.GetGroundedArtifacts().Where(artifact =>
                turn.Capability?.Executor == ConversationExecutor.MetadataQuery
                    ? artifact.Origin == "Live" && artifact.Kind == GroundedArtifactKind.TechnicalName
                        && !string.IsNullOrWhiteSpace(artifact.Source)
                    : SameSubject(artifact.OriginatingSubject, turn.Capability?.Identity?.Subject)).ToArray();
        var formatted = isPreparation ? FormatPreparation(result, mode)
            // A general answer is not a Motor artifact: artifact-bound formats do not apply to it (JSON is still checked).
            : result.Status == "ok" && turn.Capability?.Executor == ConversationExecutor.GeneralAnswer
                && mode is "code" or "variable" or "names"
            // A LIVE count or catalogue listing is produced by C#: a variable/code format has no artifact to demand, and
            // a list of names is the whole catalogue, not the few names the conversation keeps in memory.
            || result.Status == "ok" && mode is "variable" or "code" or "names"
                && turn.Capability?.Executor is ConversationExecutor.MetadataQuery or ConversationExecutor.ViewCount
            ? result
            : result.Status == "ok" && reason is not ("GroundedArtifactMissing" or "GroundedArtifactAmbiguous" or "EvidenceConflict")
                ? ChatResponseFormat.Apply(result, mode, result.ToolInvoked
                    || turn.Capability?.Executor == ConversationExecutor.ArtifactReuse ? selectedArtifacts : [])
                : result;
        if (formatted.Status != "ok" && result.Status == "ok")
        {
            reason = isPreparation ? "InvalidViewSpecPreparation" : "RequestedFormatUnavailable";
        }
        if (formatted.Status == "ok" && mode == "json"
            && reason is not ("GroundedArtifactMissing" or "GroundedArtifactAmbiguous" or "EvidenceConflict")
            && turn.Capability?.Executor is ConversationExecutor.GroundedKnowledge
                or ConversationExecutor.HandlerEvidence or ConversationExecutor.ArtifactReuse
            && !selectedArtifacts.Any(artifact => artifact.Kind == GroundedArtifactKind.Json
                && artifact.Content.Trim().Equals(formatted.Response?.Trim(), StringComparison.Ordinal)))
        {
            formatted = new("error", null, result.ToolInvoked, "No tengo un JSON verificado para el sujeto autorizado.");
            reason = "RequestedFormatUnavailable";
        }

        if (formatted.Status == "ok" && mode == "json" && result.Sources is not null)
        {
            var sources = result.Sources.Where(source => selectedArtifacts.Any(artifact =>
                artifact.Kind == GroundedArtifactKind.Json && artifact.Origin == "Knowledge"
                && artifact.Content.Trim().Equals(formatted.Response?.Trim(), StringComparison.Ordinal)
                && artifact.Source == $"{source.SourceFile}: {source.Title}")).ToArray();
            if (sources.Length > 0) { formatted = formatted with { Sources = sources }; }
        }

        var expected = turn.Capability?.Identity?.Subject;
        if (turn.CanCommit && formatted.Status == "ok" && expected is { Kind: not "General" }
            && turn.Capability?.Executor != ConversationExecutor.ArtifactReuse
            && turn.Plan?.ReuseGroundedArtifact != true
            && !SameSubject(conversation.GetActiveSubject(), expected))
        {
            return new(new ChatResult("error", null, result.ToolInvoked,
                "La respuesta no corresponde al sujeto autorizado."), "ExecutedSubjectMismatch", "Discarded");
        }
        if (CopilotChatService.CanCommitContext(formatted, turn.CanCommit, reason)
            && formatted.Response is { } response)
        {
            turn.Plan?.StageOperation(conversation);
            conversation.AddTurn(turn.Message, response);
            conversation.Commit();
            return new(formatted, reason, "Committed");
        }
        if (turn.PublishCandidates && formatted.Status == "ok" && !string.IsNullOrWhiteSpace(formatted.Response))
        {
            conversation.CommitCandidateSet();
            return new(formatted, reason, "CandidatesOnly");
        }
        // A clarification written by C# keeps only the exchange, so a follow-up can refer to the unresolved question.
        if (turn.RecordClarification && formatted.Status == "ok" && formatted.Response is { Length: > 0 } clarification)
        {
            conversation.CommitTurnOnly(turn.Message, clarification);
            return new(formatted, reason, "TurnOnly");
        }
        return new(formatted, reason, "Discarded");
    }

    // Only the authorized preparation executor can format this typed result without documentary artifacts.
    private static ChatResult FormatPreparation(ChatResult result, string mode)
    {
        if (result.Status != "ok") { return result; }
        if (result.ViewSpecResult is not { } preparation)
        {
            return result with
            {
                Status = "error", Response = null,
                Error = "No se recibió una estructura de vista preparada."
            };
        }
        var validation = ViewSpecValidator.Validate(preparation.Spec);
        if (!validation.IsValid) { preparation = preparation with { Validation = validation }; }
        if (!preparation.Validation.IsValid)
        {
            return result with
            {
                Status = "error", Response = null, ViewSpecResult = preparation,
                Error = "No pude construir una estructura de vista válida:\n- "
                    + string.Join("\n- ", preparation.Validation.Errors)
            };
        }
        return mode == "json"
            ? result with { Response = JsonSerializer.Serialize(preparation, new JsonSerializerOptions(JsonSerializerDefaults.Web)) }
            : ChatResponseFormat.Apply(result, mode, []);
    }
}
