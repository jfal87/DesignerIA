using DesignerIA.Api.Services;
using DesignerIA.Contracts;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

internal sealed class ConversationIntegrationFixture(SubjectCatalogFixture catalog)
{
    public ConversationContextStore Store { get; } = new();
    public SubjectCatalogFixture Catalog { get; } = catalog;
    public List<ConversationCapabilityDecision> ExternalExecutions { get; } = [];

    public async Task<ConversationIntegrationResult> RunAsync(
        SemanticProposal proposal, string message,
        Func<ConversationCapabilityDecision, ConversationLease, Task<ChatResult>>? externalExecutor = null,
        bool isPlannerOutput = false, string? plannerFailureReason = null)
    {
        await using var stored = await Store.AcquireAsync("integration", default);
        await using var staged = stored.BeginUpdate();
        var resolution = await new ConversationSubjectResolver(Catalog)
            .ResolveAsync(proposal, staged.GetSubjectSnapshot(), message);
        var validation = ValidatedConversationPlan.Validate(proposal, resolution, isPlannerOutput);
        if (validation.Plan is not { } plan)
        {
            var publish = validation.Reason is "SubjectAmbiguous" or "ApproximateCandidate"
                && resolution.Candidates is { Count: > 0 };
            if (publish)
            {
                var subjects = resolution.Candidates!.Select(candidate => candidate.Subject).ToArray();
                staged.SetLastCandidateSet(subjects.Distinct().Count() == subjects.Length ? subjects : []);
            }
            var stopped = ConversationTurnCompleter.Finish(new("ok", validation.Clarification, false, null),
                staged, new(message, null, null, false, publish, FallbackReason: validation.GetDiagnosticReason(plannerFailureReason)));
            return new(validation, null, stopped);
        }

        var decision = ConversationCapabilityRouter.Route(plan, staged.GetGroundedArtifacts());
        if (decision.CanCommitContext) { plan.StageSubject(staged); }
        var result = CopilotChatService.ExecuteLocalCapability(decision);
        if (result is null)
        {
            ExternalExecutions.Add(decision);
            if (externalExecutor is null) { throw new InvalidOperationException("An external executor fixture is required."); }
            result = await externalExecutor(decision, staged);
        }
        var reason = decision.Reason ?? (decision.Executor == ConversationExecutor.GeneralAnswer && result.Status != "ok"
            ? "GeneralAnswerMissingOrInvalid" : null);
        var outcome = ConversationTurnCompleter.Finish(result, staged,
            new(message, plan, decision, decision.CanCommitContext, ForbiddenAction: plan.ForbiddenAction, FallbackReason: reason));
        return new(validation, decision, outcome);
    }

    public async Task<ConversationIntegrationState> StateAsync()
    {
        await using var stored = await Store.AcquireAsync("integration", default);
        return new(stored.GetCurrentFocus(), stored.GetPreviousFocus(),
            stored.GetGroundedArtifacts().ToArray(), stored.GetLastCandidateSet().ToArray(), stored.BuildPrompt("next"));
    }

    public async Task SeedAsync(params ResolvedConversationSubject[] subjects)
    {
        await using var stored = await Store.AcquireAsync("integration", default);
        foreach (var subject in subjects) { stored.SetResolvedSubject(subject); }
    }

    public static Task<ChatResult> HandlerEvidence(ConversationCapabilityDecision decision, ConversationLease staged)
    {
        var subject = decision.Identity?.Subject ?? throw new InvalidOperationException("Missing canonical handler.");
        var json = "{\"handler\":\"" + subject.Name + "\"}";
        staged.SetGroundedArtifacts([new(GroundedArtifactKind.Json, json, "Live", "AccionesHandler: 1",
            subject.Name, subject.Name, subject)]);
        return Task.FromResult(new ChatResult("ok", "Parametros respaldados: " + json, true, null));
    }

    public static Task<ChatResult> Knowledge(
        ConversationCapabilityDecision decision, ConversationLease staged,
        string response, params KnowledgeSearchResultItem[] evidence)
    {
        var subject = decision.Identity?.Subject ?? throw new InvalidOperationException("Missing canonical subject.");
        var grounded = CopilotChatService.ExtractKnowledgeArtifacts(response, evidence, subject.Name, staged);
        staged.SetActiveKnowledge(subject.Name, decision.SearchQueries);
        staged.SetGroundedArtifacts(grounded.Artifacts);
        return Task.FromResult(new ChatResult("ok", grounded.Response, true, null));
    }
}

internal sealed record ConversationIntegrationResult(
    ConversationPlanValidation Validation, ConversationCapabilityDecision? Decision, ConversationTurnOutcome Outcome);

internal sealed record ConversationIntegrationState(
    ConversationFocus? Current, ConversationFocus? Previous, IReadOnlyList<GroundedArtifact> Artifacts,
    IReadOnlyList<ActiveSubject> Candidates, string Prompt);
