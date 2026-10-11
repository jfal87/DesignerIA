using DesignerIA.Api.Services;
using Xunit;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

public sealed class ConversationPlanningTests
{
    private static SemanticProposal HandlerProposal(string? name = null, string? reference = null, string? operation = "Parameters") =>
        new("HandlerQuestion", null, reference is not null, null, "explanation", ["operation-specific query"],
            null, null, null, SubjectKind: "Handler", SubjectName: name, SubjectReference: reference, RequestKind: operation);

    private static ValidatedConversationPlan Approve(SemanticProposal proposal, ConversationLease conversation, string message = "request")
    {
        var validation = Validate(proposal, conversation, message);
        Assert.Null(validation.Reason);
        return Assert.IsType<ValidatedConversationPlan>(validation.Plan);
    }

    private static ConversationPlanValidation Validate(SemanticProposal proposal, ConversationLease conversation, string message)
    {
        // The existing tests use "request" as a transport placeholder, not an explicit identifier.
        var resolution = new ConversationSubjectResolver(SubjectCatalogFixture.ExistingHandlers())
            .ResolveAsync(proposal, conversation.GetSubjectSnapshot(), message == "request" ? "test request" : message)
            .GetAwaiter().GetResult();
        return ValidatedConversationPlan.Validate(proposal, resolution);
    }

    [Fact]
    public async Task ValidProposalProducesReadOnlyPlanWithoutChangingContextAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("valid", default);
        context.SetActiveHandler("HandlerA");
        context.SetRequestKind("Explanation");
        var before = context.GetCurrentFocus();
        var proposal = HandlerProposal(reference: "current");

        var plan = Approve(proposal, context);

        Assert.Equal("HandlerQuestion", plan.Intent);
        Assert.Equal(context.GetActiveSubject(), plan.Subject);
        Assert.Equal("Parameters", plan.RequestKind);
        Assert.Equal(proposal.SearchQueries, plan.SearchQueries);
        Assert.Equal(before, context.GetCurrentFocus());
        Assert.Null(context.GetPreviousFocus());
        Assert.Equal("Parameters", proposal.RequestKind);
    }

    [Fact]
    public async Task IncompatibleOperationDoesNotProduceExecutablePlanAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("incompatible", default);
        var proposal = new SemanticProposal("GestionEngineMetadata", null, false, null, "explanation", [],
            "HandlerActions", "List", null, RequestKind: "Parameters");

        var validation = Validate(proposal, context, "request");

        Assert.Null(validation.Plan);
        Assert.Equal("MetadataOperationIncompatible", validation.Reason);
        Assert.NotNull(validation.Clarification);
    }

    [Theory]
    [InlineData("Function")]
    [InlineData("Property")]
    [InlineData("General")]
    public async Task ContradictoryKindCannotOverwriteKnownSubjectAsync(string kind)
    {
        await using var context = await new ConversationContextStore().AcquireAsync("identity", default);
        context.SetActiveHandler("HandlerA");
        var original = context.GetCurrentFocus();
        var proposal = HandlerProposal("HandlerA") with { Intent = "KnowledgeQuestion", SubjectKind = kind };

        var validation = Validate(proposal, context, "request");

        // The hinted kind never overwrites the known subject; the operation is routed by the confirmed handler.
        Assert.Equal(new ActiveSubject("Handler", "HandlerA"), validation.Plan?.Subject);
        Assert.Equal("HandlerQuestion", validation.Plan?.Intent);
        Assert.Equal(ConversationExecutor.HandlerEvidence, ConversationCapabilityRouter.Route(validation.Plan!, []).Executor);
        Assert.Equal(original, context.GetCurrentFocus());
    }

    [Fact]
    public async Task InvalidPlanPreservesBothFociOperationAndOtherContextAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("invalid", default);
        context.SetActiveHandler("HandlerA");
        context.SetRequestKind("Examples");
        context.SetActiveHandler("HandlerB");
        context.SetRequestKind("Explanation");
        context.SetActiveKnowledge("HandlerB", ["query"]);
        context.SetLastCandidateSet([new ActiveSubject("Handler", "HandlerC")]);
        context.SetGroundedArtifacts([new GroundedArtifact(GroundedArtifactKind.Json, "{}", "Knowledge", "source", "HandlerB", "HandlerB")]);
        var current = context.GetCurrentFocus();
        var previous = context.GetPreviousFocus();
        var recent = context.GetRecentSubjects().ToArray();
        var candidates = context.GetLastCandidateSet().ToArray();
        var artifacts = context.GetGroundedArtifacts().ToArray();
        var prompt = context.BuildPrompt("request");

        var validation = Validate(HandlerProposal("HandlerC", operation: "NotAllowed"), context, "request");

        Assert.Null(validation.Plan);
        Assert.Equal(current, context.GetCurrentFocus());
        Assert.Equal(previous, context.GetPreviousFocus());
        Assert.Equal(recent, context.GetRecentSubjects());
        Assert.Equal(candidates, context.GetLastCandidateSet());
        Assert.Equal(artifacts, context.GetGroundedArtifacts());
        Assert.Equal(prompt, context.BuildPrompt("request"));
    }

    [Fact]
    public async Task OperationOnlyUpdateDoesNotDisplacePreviousFocusAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("operation", default);
        context.SetActiveHandler("HandlerA");
        context.SetRequestKind("Examples");
        context.SetActiveHandler("HandlerB");
        context.SetRequestKind("Explanation");
        var previous = context.GetPreviousFocus();
        await using var staged = context.BeginUpdate();
        var plan = Approve(HandlerProposal(reference: "current"), staged);

        plan.StageSubject(staged);
        plan.StageOperation(staged);
        Assert.Equal("Explanation", context.GetCurrentFocus()?.RequestKind);
        staged.Commit();

        Assert.Equal("Parameters", context.GetCurrentFocus()?.RequestKind);
        Assert.Equal(previous, context.GetPreviousFocus());
        Assert.Equal("HandlerB", context.GetActiveHandler());
    }

    [Fact]
    public async Task RealSubjectChangeRetiresCurrentFocusOnlyAfterCommitAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("transition", default);
        context.SetActiveHandler("HandlerA");
        context.SetRequestKind("Examples");
        var original = context.GetCurrentFocus();
        await using var staged = context.BeginUpdate();
        var plan = Approve(HandlerProposal("HandlerB"), staged);

        plan.StageSubject(staged);
        plan.StageOperation(staged);
        Assert.Equal(original, context.GetCurrentFocus());
        Assert.Null(context.GetPreviousFocus());
        staged.Commit();

        Assert.Equal(original, context.GetPreviousFocus());
        Assert.Equal("HandlerB", context.GetActiveHandler());
        Assert.Equal("Parameters", context.GetCurrentFocus()?.RequestKind);
    }

    [Theory]
    [InlineData("session_limits_status")]
    [InlineData("NonexistentCapability")]
    public async Task UnknownCapabilityProducesNoExecutionPlanOrMutationAsync(string capability)
    {
        await using var context = await new ConversationContextStore().AcquireAsync("capability", default);
        context.SetActiveHandler("HandlerA");
        context.SetRequestKind("Explanation");
        var original = context.GetCurrentFocus();

        var validation = Validate(HandlerProposal("HandlerB") with { Intent = capability }, context, "request");

        Assert.Null(validation.Plan);
        Assert.Equal("CapabilityNotAllowed", validation.Reason);
        Assert.Equal(original, context.GetCurrentFocus());
        Assert.Null(context.GetPreviousFocus());
    }

    [Fact]
    public async Task ConversationIncidentalSubjectCannotChangeFocusOrOperationAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("social", default);
        context.SetActiveHandler("HandlerA");
        context.SetRequestKind("Examples");
        var original = context.GetCurrentFocus();
        await using var staged = context.BeginUpdate();
        var proposal = HandlerProposal("session_limits_status", operation: null) with
        {
            Intent = "Conversation", SubjectKind = "General", GeneralAnswer = "conversation response"
        };
        var plan = Approve(proposal, staged);

        Assert.Null(plan.Subject);
        plan.StageSubject(staged);
        plan.StageOperation(staged);
        staged.AddTurn("request", plan.GeneralAnswer!);
        staged.Commit();

        Assert.Equal(original, context.GetCurrentFocus());
        Assert.Null(context.GetPreviousFocus());
        Assert.Single(context.GetRecentSubjects());
    }

    [Fact]
    public async Task DiscardedExecutionDoesNotLeakAnyStagedChangesAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("discard", default);
        context.SetActiveHandler("HandlerA");
        context.SetRequestKind("Explanation");
        var original = context.GetCurrentFocus();
        var prompt = context.BuildPrompt("request");
        await using (var staged = context.BeginUpdate())
        {
            var plan = Approve(HandlerProposal("HandlerB"), staged);
            plan.StageSubject(staged);
            plan.StageOperation(staged);
            staged.SetActiveMetadataResource("HandlerActions");
            staged.SetLastCandidateSet([new ActiveSubject("Handler", "HandlerC")]);
            staged.SetActiveKnowledge("HandlerB", ["new query"]);
            staged.SetGroundedArtifacts([new GroundedArtifact(GroundedArtifactKind.Json, "{}", "LIVE", "source", "HandlerB", "HandlerB")]);
            staged.AddTurn("request", "response");
        }

        Assert.Equal(original, context.GetCurrentFocus());
        Assert.Null(context.GetPreviousFocus());
        Assert.Single(context.GetRecentSubjects());
        Assert.Empty(context.GetLastCandidateSet());
        Assert.Empty(context.GetGroundedArtifacts());
        Assert.Null(context.GetActiveMetadataResource());
        Assert.Equal(prompt, context.BuildPrompt("request"));
    }

    [Fact]
    public async Task PreviousReferenceResolvesPreviousSubjectAndPreservesQueriesAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("previous", default);
        context.SetActiveHandler("HTMLEnCeldaAdvanced");
        context.SetActiveHandler("HTMLEnCeldaAdvanced_ControlRoom");
        var plan = Approve(HandlerProposal(reference: "previous"), context);

        Assert.Equal("HTMLEnCeldaAdvanced", plan.SubjectName);
        Assert.Equal("HTMLEnCeldaAdvanced", plan.Handler);
        Assert.Equal("previous", plan.ContextResolution);
        Assert.Equal(new[] { "operation-specific query" }, plan.SearchQueries);
        Assert.Equal("HTMLEnCeldaAdvanced_ControlRoom", context.GetActiveHandler());
    }

    [Fact]
    public async Task CasingCannotCreateFalseSubjectTransitionAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("casing", default);
        context.SetActiveHandler("HandlerA");
        context.SetActiveHandler("HandlerB");
        var previous = context.GetPreviousFocus();
        await using var staged = context.BeginUpdate();
        var plan = Approve(HandlerProposal("handlerb"), staged);
        plan.StageSubject(staged);
        plan.StageOperation(staged);
        staged.Commit();

        Assert.Equal("HandlerB", context.GetActiveHandler());
        Assert.Equal(previous, context.GetPreviousFocus());
    }

    [Fact]
    public async Task ArtifactFollowUpBindsExistingSubjectWithoutNewSearchAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("artifact", default);
        context.SetActiveHandler("botonUltimoNivel");
        var plan = Approve(HandlerProposal(reference: "current", operation: "Examples") with
        {
            ResponseMode = "json", SearchQueries = [], ReuseGroundedArtifact = true
        }, context);

        Assert.Equal("botonUltimoNivel", plan.SubjectName);
        Assert.True(plan.ReuseGroundedArtifact);
        Assert.Empty(plan.SearchQueries);
    }

    [Fact]
    public async Task GeneralCodeUsesUserTopicNotIncidentalTechnicalSubjectAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("general", default);
        context.SetActiveHandler("HandlerA");
        var proposal = HandlerProposal("incidentalFunction", operation: "Explanation") with
        {
            Intent = "GeneralQuestion", SubjectKind = "Function", ResponseMode = "code",
            GeneralAnswer = "int Sum(int a, int b) => a + b;"
        };
        const string message = "volvamos a C#: dame solo el codigo";
        var plan = Approve(proposal, context, message);

        Assert.Equal(new ActiveSubject("General", message), plan.Subject);
        Assert.Equal("code", plan.ResponseMode);
        Assert.False(plan.ReuseGroundedArtifact);
        Assert.Equal("HandlerA", context.GetActiveHandler());
    }

    [Theory]
    [InlineData("first")]
    [InlineData("previous")]
    public async Task MissingReferenceCannotFallBackToCurrentSubjectAsync(string reference)
    {
        await using var context = await new ConversationContextStore().AcquireAsync("missing", default);
        context.SetActiveHandler("HandlerA");
        var validation = Validate(HandlerProposal(reference: reference), context, "request");

        Assert.Null(validation.Plan);
        Assert.Equal("SubjectReferenceMissing", validation.Reason);
        Assert.Equal("HandlerA", context.GetActiveHandler());
    }

    [Fact]
    public async Task NameAndReferenceCannotSelectDifferentSubjectsAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("conflict", default);
        context.SetActiveHandler("HandlerA");
        context.SetActiveHandler("HandlerB");
        var validation = Validate(HandlerProposal("HandlerB", "previous"), context, "request");

        Assert.Null(validation.Plan);
        Assert.Equal("ConflictingSubjectReference", validation.Reason);
        Assert.Equal("HandlerB", context.GetActiveHandler());
    }

    [Fact]
    public async Task FirstCandidateIsSelectedOnlyOnCommittedUpdateAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("candidate", default);
        context.SetActiveHandler("HandlerA");
        context.SetLastCandidateSet([new ActiveSubject("Handler", "HandlerB"), new ActiveSubject("Handler", "HandlerC")]);
        await using var staged = context.BeginUpdate();
        var plan = Approve(HandlerProposal(reference: "first"), staged);
        plan.StageSubject(staged);
        plan.StageOperation(staged);
        Assert.Null(context.GetSelectedEntity());
        staged.Commit();

        Assert.Equal(new ActiveSubject("Handler", "HandlerB"), context.GetSelectedEntity());
        Assert.Equal("HandlerA", context.GetPreviousFocus()?.Subject.Name);
    }

    [Fact]
    public async Task HandlerParametersCannotSilentlyBecomeDocumentaryLookupAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("route", default);
        context.SetActiveHandler("HandlerA");
        var validation = Validate(
            HandlerProposal(reference: "current") with { Intent = "KnowledgeQuestion" }, context, "request");

        Assert.NotNull(validation.Plan);
        Assert.Equal(ConversationExecutor.HandlerEvidence, ConversationCapabilityRouter.Route(validation.Plan, []).Executor);
    }

    [Fact]
    public async Task ViewCreationDoesNotPersistOperationOnOldSubjectAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("creation", default);
        context.SetActiveHandler("HandlerA");
        context.SetRequestKind("Explanation");
        var original = context.GetCurrentFocus();
        var proposal = new SemanticProposal("ViewCreation", null, false, null, "explanation", [], null, null, null,
            SubjectKind: "View", SubjectName: "NewView", RequestKind: "Configuration");
        await using var staged = context.BeginUpdate();
        var plan = Approve(proposal, staged);
        plan.StageSubject(staged);
        plan.StageOperation(staged);
        staged.Commit();

        Assert.Equal(original, context.GetCurrentFocus());
        Assert.Null(context.GetPreviousFocus());
    }

    [Fact]
    public async Task TopicCannotBypassKnownSubjectIdentityValidationAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("topic", default);
        context.SetActiveHandler("HandlerA");
        var proposal = HandlerProposal(operation: "Explanation") with
        {
            Intent = "KnowledgeQuestion", Topic = "HandlerA", SubjectKind = "Function"
        };

        var validation = Validate(proposal, context, "request");

        Assert.NotNull(validation.Plan);
        Assert.Equal("Handler", validation.Plan.SubjectKind);
        Assert.Equal("HandlerA", context.GetActiveHandler());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Explanation")]
    [InlineData("Configuration")]
    [InlineData("Use")]
    [InlineData("Examples")]
    [InlineData("Parameters")]
    [InlineData("Evidence")]
    [InlineData("Search")]
    public async Task AllowedHandlerOperationsRemainAvailableWithoutAutomaticInheritanceAsync(string? operation)
    {
        await using var context = await new ConversationContextStore().AcquireAsync("allowed", default);
        context.SetActiveHandler("HandlerA");
        context.SetRequestKind("Configuration");

        var plan = Approve(HandlerProposal(reference: "current", operation: operation), context);

        Assert.Equal(operation, plan.RequestKind);
        Assert.Equal("Configuration", context.GetCurrentFocus()?.RequestKind);
    }

    [Theory]
    [InlineData("UnknownResource", "List")]
    [InlineData("HandlerActions", "session_limits_status")]
    [InlineData("HandlerActions", null)]
    public async Task UnknownMetadataCapabilityNeverProducesPlanAsync(string? resource, string? operation)
    {
        await using var context = await new ConversationContextStore().AcquireAsync("metadata", default);
        context.SetActiveHandler("HandlerA");
        var proposal = new SemanticProposal("GestionEngineMetadata", null, false, null, "explanation", [],
            resource, operation, null, RequestKind: "Search");

        var validation = Validate(proposal, context, "request");

        Assert.Null(validation.Plan);
        Assert.Equal("MetadataCapabilityNotAllowed", validation.Reason);
        Assert.Equal("HandlerA", context.GetActiveHandler());
    }

    [Theory]
    [InlineData("HandlerActions", "List")]
    [InlineData("HandlerActions", "Count")]
    [InlineData("HandlerActions", "Search")]
    [InlineData("HandlerUsages", "List")]
    [InlineData("HandlerUsages", "Count")]
    [InlineData(null, null)]
    public async Task ExistingMetadataCapabilitiesRemainAvailableAsync(string? resource, string? operation)
    {
        await using var context = await new ConversationContextStore().AcquireAsync("metadata-valid", default);
        var proposal = new SemanticProposal("GestionEngineMetadata", null, false, null, "explanation", ["HTML"],
            resource, operation, operation == "Search" ? "HTML" : null, RequestKind: "Search");

        var plan = Approve(proposal, context);

        Assert.Equal(resource, plan.Resource);
        Assert.Equal(operation, plan.Operation);
        Assert.Null(plan.Subject);
    }

    [Fact]
    public async Task CommittedUpdatePersistsWhenConversationIsReacquiredAsync()
    {
        var store = new ConversationContextStore();
        await using (var context = await store.AcquireAsync("persisted", default))
        {
            context.SetActiveHandler("HandlerA");
            await using var staged = context.BeginUpdate();
            var plan = Approve(HandlerProposal("HandlerB"), staged);
            plan.StageSubject(staged);
            plan.StageOperation(staged);
            staged.AddTurn("request", "coherent response");
            staged.Commit();
        }
        await using var reopened = await store.AcquireAsync("persisted", default);

        Assert.Equal("HandlerB", reopened.GetActiveHandler());
        Assert.Equal("HandlerA", reopened.GetPreviousFocus()?.Subject.Name);
        Assert.Equal("Parameters", reopened.GetCurrentFocus()?.RequestKind);
        Assert.Contains("coherent response", reopened.BuildPrompt("next"));
    }

    [Theory]
    [InlineData("HandlerQuestion")]
    [InlineData("KnowledgeQuestion")]
    [InlineData("Conversation")]
    public async Task ExplicitArtifactReuseKeepsBoundOperationRegardlessOfDocumentaryIntentAsync(string intent)
    {
        await using var context = await new ConversationContextStore().AcquireAsync("artifact-operation", default);
        context.SetActiveHandler("HandlerA");
        context.SetRequestKind("Explanation");
        await using var staged = context.BeginUpdate();
        var plan = Approve(HandlerProposal(reference: "current", operation: "Examples") with
        {
            Intent = intent, ResponseMode = "json", SearchQueries = [], ReuseGroundedArtifact = true
        }, staged);

        plan.StageSubject(staged);
        plan.StageOperation(staged);
        staged.Commit();

        Assert.Equal("Examples", context.GetCurrentFocus()?.RequestKind);
        Assert.Equal("HandlerA", context.GetActiveHandler());
        Assert.Null(context.GetPreviousFocus());
    }

    [Fact]
    public async Task KnowledgeWithoutStructuredSubjectCannotIntroduceOneDuringExecutionAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("unbound", default);
        context.SetActiveHandler("HandlerA");
        var validation = Validate(HandlerProposal(operation: "Explanation") with
        {
            Intent = "KnowledgeQuestion", SubjectKind = null
        }, context, "request");

        Assert.Null(validation.Plan);
        Assert.Equal("SubjectNotFound", validation.Reason);
        Assert.Equal("HandlerA", context.GetActiveHandler());
    }
}
