using DesignerIA.Api.Services;
using DesignerIA.Contracts;
using Xunit;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

public sealed class ConversationSubjectResolverTests
{
    private static SemanticProposal Proposal(string? name = null, string? kind = "Handler", string? reference = null,
        string intent = "HandlerQuestion", string? operation = "Parameters") =>
        new(intent, null, reference is not null, null, "explanation", ["specific operation query"],
            null, null, null, SubjectKind: kind, SubjectName: name, SubjectReference: reference, RequestKind: operation);

    private static ConversationSubjectSnapshot Empty => new(null, null, [], []);

    [Theory]
    [InlineData("HandlerA", "Handler")]
    [InlineData("handlera", "Handler")]
    [InlineData("HandlerA", "Function")]
    [InlineData("handlera", "Property")]
    public async Task ExactCatalogueMatchIsCanonicalDespitePlannerTypeAsync(string name, string kind)
    {
        var catalog = new SubjectCatalogFixture { Handlers = [SubjectCatalogFixture.Handler("HandlerA", 42)] };
        var proposal = Proposal(name, kind);
        var resolution = await new ConversationSubjectResolver(catalog).ResolveAsync(proposal, Empty, "query " + name);
        var validation = ValidatedConversationPlan.Validate(proposal, resolution);

        Assert.Equal(SubjectResolutionStatus.Resolved, resolution.Status);
        Assert.Equal(new ActiveSubject("Handler", "HandlerA"), resolution.Identity?.Subject);
        Assert.True(resolution.Identity?.IsLive);
        Assert.Equal("AccionesHandler: 42", resolution.Identity?.Confirmation);
        Assert.NotNull(validation.Plan);
        Assert.Equal("Handler", validation.Plan.SubjectKind);
        Assert.Equal("HandlerA", validation.Plan.Handler);
        Assert.Equal(proposal.SearchQueries, validation.Plan.SearchQueries);
        Assert.Equal(0, catalog.DocumentLookups);
    }

    [Fact]
    public async Task ExplicitIdentifierOverridesContradictoryNamesAndReferenceAsync()
    {
        var catalog = new SubjectCatalogFixture { Handlers = [SubjectCatalogFixture.Handler("HandlerA", 42)] };
        var proposal = Proposal("OtherFunction", "Function", "previous") with { Handler = "ThirdName" };
        var snapshot = new ConversationSubjectSnapshot(
            new(new ActiveSubject("Handler", "CurrentHandler"), null, null, []),
            new(new ActiveSubject("Handler", "PreviousHandler"), null, null, []), [], []);

        var resolution = await new ConversationSubjectResolver(catalog).ResolveAsync(proposal, snapshot, "handlera");
        var validation = ValidatedConversationPlan.Validate(proposal, resolution);

        Assert.True(resolution.ExplicitSubject);
        Assert.NotNull(validation.Plan);
        Assert.Equal("HandlerA", validation.Plan.SubjectName);
    }

    [Fact]
    public async Task ApproximateCandidateIsNotConfirmedEvenWithHighestRankAsync()
    {
        var catalog = new SubjectCatalogFixture { Handlers = [SubjectCatalogFixture.Handler("HandlerAdvanced", 42)] };
        var proposal = Proposal("HandlerAdv");

        var resolution = await new ConversationSubjectResolver(catalog).ResolveAsync(proposal, Empty, "HandlerAdv");

        Assert.Equal(SubjectResolutionStatus.NotFound, resolution.Status);
        Assert.Null(resolution.Identity);
        Assert.Single(resolution.Candidates!);
        Assert.Equal("ApproximateCandidate", resolution.Reason);
        Assert.Null(ValidatedConversationPlan.Validate(proposal, resolution).Plan);
    }

    [Fact]
    public async Task MultipleApproximateCandidatesAreAmbiguousNotRankSelectedAsync()
    {
        var catalog = new SubjectCatalogFixture
        {
            Handlers = [SubjectCatalogFixture.Handler("HandlerAdvanced"), SubjectCatalogFixture.Handler("HandlerAdvancedOther", 2)]
        };
        var resolution = await new ConversationSubjectResolver(catalog).ResolveAsync(Proposal("HandlerAdv"), Empty, "HandlerAdv");

        Assert.Equal(SubjectResolutionStatus.Ambiguous, resolution.Status);
        Assert.Null(resolution.Identity);
        Assert.Equal(2, resolution.Candidates?.Count);
    }

    [Fact]
    public async Task DuplicateExactCatalogueRowsDoNotSelectLowestIdAsync()
    {
        var catalog = new SubjectCatalogFixture
        {
            Handlers = [SubjectCatalogFixture.Handler("HandlerA", 1), SubjectCatalogFixture.Handler("HandlerA", 2)]
        };
        var resolution = await new ConversationSubjectResolver(catalog).ResolveAsync(Proposal("HandlerA"), Empty, "HandlerA");

        Assert.Equal(SubjectResolutionStatus.Ambiguous, resolution.Status);
        Assert.Equal(2, resolution.Candidates?.Count);
        Assert.Null(resolution.Identity);
    }

    [Fact]
    public async Task UnknownIdentifierIsNotAHandlerJustBecauseItIsCamelCaseAsync()
    {
        var resolution = await new ConversationSubjectResolver(new SubjectCatalogFixture())
            .ResolveAsync(Proposal("UnknownCamelCase"), Empty, "UnknownCamelCase");

        Assert.Equal(SubjectResolutionStatus.NotFound, resolution.Status);
        Assert.Null(resolution.Identity);
    }

    [Fact]
    public async Task DocumentaryMentionIsATopicNotLiveHandlerAsync()
    {
        var catalog = new SubjectCatalogFixture
        {
            Documents = [new("A guide", "guide.md", "guide.md", "The identifier DocHandler uses this documented JSON.", 100)]
        };
        var proposal = Proposal("DocHandler");
        var resolution = await new ConversationSubjectResolver(catalog).ResolveAsync(proposal, Empty, "DocHandler");
        var validation = ValidatedConversationPlan.Validate(proposal, resolution);

        Assert.Equal(SubjectResolutionStatus.Resolved, resolution.Status);
        Assert.Equal("Topic", resolution.Identity?.Subject.Kind);
        Assert.Equal("Documentary", resolution.Identity?.Source);
        Assert.False(resolution.Identity?.IsLive);
        Assert.Equal("guide.md", resolution.Identity?.Confirmation);
        Assert.NotNull(validation.Plan);
        Assert.Equal("Topic", validation.Plan.SubjectKind);
        Assert.Equal("DocHandler", validation.Plan.Handler);
    }

    [Fact]
    public async Task FunctionDeclarationConfirmsDocumentaryFunctionNotLiveHandlerAsync()
    {
        var catalog = new SubjectCatalogFixture
        {
            Documents = [new("Functions", "functions.md", "functions.md", "function RealFunction(value) { return value; }", 100)]
        };
        var proposal = Proposal("realfunction", "Handler", intent: "KnowledgeQuestion");
        var resolution = await new ConversationSubjectResolver(catalog).ResolveAsync(proposal, Empty, "realfunction");
        var plan = ValidatedConversationPlan.Validate(proposal, resolution).Plan;

        Assert.Equal(new ActiveSubject("Function", "RealFunction"), resolution.Identity?.Subject);
        Assert.False(resolution.Identity?.IsLive);
        Assert.NotNull(plan);
        Assert.Null(plan.Handler);
        Assert.Equal("KnowledgeQuestion", plan.Intent);
    }

    [Fact]
    public async Task GenericDomainTopicDoesNotRequireLiveIdentityAsync()
    {
        var catalog = new SubjectCatalogFixture();
        var proposal = Proposal("Estado de GestionEngine", "Property", intent: "KnowledgeQuestion", operation: "Explanation");
        var resolution = await new ConversationSubjectResolver(catalog).ResolveAsync(proposal, Empty, "que es un Estado de GestionEngine");

        Assert.Equal("Topic", resolution.Identity?.Subject.Kind);
        Assert.Equal("KnowledgeTopic", resolution.Identity?.Source);
        Assert.False(resolution.Identity?.IsLive);
        Assert.Equal(0, catalog.LiveLookups);
        Assert.NotNull(ValidatedConversationPlan.Validate(proposal, resolution).Plan);
    }

    [Theory]
    [InlineData("current", "HandlerB")]
    [InlineData("previous", "HandlerA")]
    [InlineData("first", "HandlerC")]
    public async Task ReferenceUsesOnlyItsScopedSnapshotWithoutCatalogueLookupAsync(string reference, string expected)
    {
        var catalog = new SubjectCatalogFixture();
        await using var context = await new ConversationContextStore().AcquireAsync("reference", default);
        context.SetResolvedSubject(SubjectCatalogFixture.Handler("HandlerA", 1));
        context.SetResolvedSubject(SubjectCatalogFixture.Handler("HandlerB", 2));
        context.SetLastCandidateSet([new ActiveSubject("Handler", "HandlerC")]);
        var snapshot = context.GetSubjectSnapshot();
        context.SetActiveHandler("AfterSnapshot");
        context.SetLastCandidateSet([new ActiveSubject("Handler", "AfterSnapshotCandidate")]);

        var resolution = await new ConversationSubjectResolver(catalog)
            .ResolveAsync(Proposal(kind: "Function", reference: reference), snapshot, "dame sus parametros");

        Assert.Equal(expected, resolution.Identity?.Subject.Name);
        Assert.Equal("Handler", resolution.Identity?.Subject.Kind);
        Assert.Equal(reference, resolution.Identity?.Source);
        Assert.Equal(0, catalog.LiveLookups);
        Assert.Equal(0, catalog.DocumentLookups);
        if (reference != "first") { Assert.True(resolution.Identity?.IsLive); }
    }

    [Theory]
    [InlineData("previous")]
    [InlineData("first")]
    public async Task MissingReferenceDoesNotUseCurrentOrRecentSubjectsAsync(string reference)
    {
        var current = new ConversationFocus(new ActiveSubject("Handler", "HandlerA"), null, null, []);
        var snapshot = new ConversationSubjectSnapshot(current, null, [current.Subject], []);

        var resolution = await new ConversationSubjectResolver(new SubjectCatalogFixture())
            .ResolveAsync(Proposal(reference: reference), snapshot, "dame sus parametros");

        Assert.Equal(SubjectResolutionStatus.NotFound, resolution.Status);
        Assert.Equal("SubjectReferenceMissing", resolution.Reason);
        Assert.Null(resolution.Identity);
    }

    [Theory]
    [InlineData("HandlerA", "Handler")]
    [InlineData("handlera", "Handler")]
    [InlineData("HandlerA", "Function")]
    [InlineData("handlera", "Function")]
    public async Task CasingAndKindCorrectionsAndOperationOnlyChangesDoNotDisplaceFocusAsync(string name, string kind)
    {
        await using var context = await new ConversationContextStore().AcquireAsync("stable", default);
        context.SetResolvedSubject(SubjectCatalogFixture.Handler("PreviousHandler"));
        context.SetResolvedSubject(SubjectCatalogFixture.Handler("HandlerA", 42));
        context.SetRequestKind("Explanation");
        var previous = context.GetPreviousFocus();
        var proposal = Proposal(name, kind);
        var resolution = await new ConversationSubjectResolver(new SubjectCatalogFixture())
            .ResolveAsync(proposal, context.GetSubjectSnapshot(), "query " + name);
        var plan = ValidatedConversationPlan.Validate(proposal, resolution).Plan;
        Assert.NotNull(plan);
        await using var staged = context.BeginUpdate();
        plan.StageSubject(staged);
        plan.StageOperation(staged);
        staged.Commit();

        Assert.Equal("HandlerA", context.GetActiveHandler());
        Assert.Equal("Parameters", context.GetCurrentFocus()?.RequestKind);
        Assert.Equal(previous, context.GetPreviousFocus());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrAmbiguousResolutionCannotMutateContextAsync(bool ambiguous)
    {
        await using var context = await new ConversationContextStore().AcquireAsync("failure", default);
        context.SetResolvedSubject(SubjectCatalogFixture.Handler("OriginalHandler"));
        context.SetRequestKind("Examples");
        context.SetGroundedArtifacts([new(GroundedArtifactKind.Json, "{}", "Live", "source", "OriginalHandler", "OriginalHandler")]);
        var original = context.GetCurrentFocus();
        var artifacts = context.GetGroundedArtifacts().ToArray();
        var catalog = new SubjectCatalogFixture
        {
            Handlers = ambiguous ? [SubjectCatalogFixture.Handler("OtherCandidate"), SubjectCatalogFixture.Handler("AnotherCandidate", 2)] : []
        };
        var proposal = Proposal("UnknownHandler");
        var resolution = await new ConversationSubjectResolver(catalog)
            .ResolveAsync(proposal, context.GetSubjectSnapshot(), "UnknownHandler");
        var validation = ValidatedConversationPlan.Validate(proposal, resolution);

        Assert.Null(validation.Plan);
        Assert.Equal(original, context.GetCurrentFocus());
        Assert.Null(context.GetPreviousFocus());
        Assert.Equal(artifacts, context.GetGroundedArtifacts());
    }

    [Fact]
    public async Task CreateMinimumContractRemainsUnboundAndDoesNotLookupSubjectsAsync()
    {
        var catalog = new SubjectCatalogFixture();
        var proposal = new SemanticProposal("ViewCreation", null, false, null, "explanation", [], null, null, null);
        var resolution = await new ConversationSubjectResolver(catalog).ResolveAsync(proposal, Empty, "crea una vista minima");
        var plan = ValidatedConversationPlan.Validate(proposal, resolution).Plan;

        Assert.NotNull(plan);
        Assert.Equal("ViewCreation", plan.Intent);
        Assert.Null(plan.Subject);
        Assert.Empty(plan.SearchQueries);
        Assert.Equal(0, catalog.LiveLookups);
        Assert.Equal(0, catalog.DocumentLookups);
    }

    [Fact]
    public async Task ArtifactContractKeepsOriginAndDoesNotLookupLiveAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("artifact", default);
        context.SetResolvedSubject(SubjectCatalogFixture.Handler("HandlerA", 42));
        context.SetGroundedArtifacts([new(GroundedArtifactKind.Json, "{\"value\":1}", "Live", "source", "HandlerA", "HandlerA", context.GetActiveSubject())]);
        var catalog = new SubjectCatalogFixture();
        var proposal = Proposal(reference: "current", operation: "Examples") with
        {
            ResponseMode = "json", SearchQueries = [], ReuseGroundedArtifact = true
        };
        var resolution = await new ConversationSubjectResolver(catalog)
            .ResolveAsync(proposal, context.GetSubjectSnapshot(), "dame solo el JSON del ejemplo");
        var plan = ValidatedConversationPlan.Validate(proposal, resolution).Plan;

        Assert.NotNull(plan);
        Assert.Equal(context.GetGroundedArtifacts()[0].OriginatingSubject, plan.Subject);
        Assert.True(plan.ReuseGroundedArtifact);
        Assert.Empty(plan.SearchQueries);
        Assert.Equal(0, catalog.LiveLookups);
        Assert.Equal(0, catalog.DocumentLookups);
    }

    [Fact]
    public async Task DocumentaryTopicRetainsProvenanceAcrossFollowUpsAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("documentary", default);
        context.SetResolvedSubject(new(new ActiveSubject("Topic", "DocHandler"), "Documentary", "guide.md"));
        var proposal = Proposal(reference: "current");
        var resolution = await new ConversationSubjectResolver(new SubjectCatalogFixture())
            .ResolveAsync(proposal, context.GetSubjectSnapshot(), "dame sus parametros");

        Assert.True(resolution.Identity?.IsDocumentary);
        Assert.False(resolution.Identity?.IsLive);
        Assert.NotNull(ValidatedConversationPlan.Validate(proposal, resolution).Plan);
    }

    [Fact]
    public async Task GeneralProgrammingCannotBindIncidentalTechnicalSubjectAsync()
    {
        var catalog = new SubjectCatalogFixture();
        var proposal = Proposal("HandlerA", "Handler", intent: "GeneralQuestion", operation: "Explanation") with
        {
            ResponseMode = "code", GeneralAnswer = "int Sum(int a, int b) => a + b;"
        };
        const string message = "volvamos a C#: dame solo el codigo";
        var resolution = await new ConversationSubjectResolver(catalog).ResolveAsync(proposal, Empty, message);
        var plan = ValidatedConversationPlan.Validate(proposal, resolution).Plan;

        Assert.NotNull(plan);
        Assert.Equal(new ActiveSubject("General", message), plan.Subject);
        Assert.Equal("code", plan.ResponseMode);
        Assert.Equal(0, catalog.LiveLookups);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void EmptySuccessfulResponseCannotCommitContext(string? response)
    {
        Assert.False(CopilotChatService.CanCommitContext(new("ok", response, false, null), true, null));
    }

    [Theory]
    [InlineData("GroundedArtifactMissing")]
    [InlineData("GroundedArtifactAmbiguous")]
    [InlineData("EvidenceConflict")]
    [InlineData("UnsafeGroundedOutput")]
    [InlineData("UnresolvedCapability")]
    [InlineData("PlannerFailure")]
    public void FailureFallbackCannotCommitContext(string failure)
    {
        Assert.False(CopilotChatService.CanCommitContext(new("ok", "response", true, null), true, failure));
    }

    [Fact]
    public void FailedFormatOrUnauthorizedPlanCannotCommitContext()
    {
        var formatted = ChatResponseFormat.Apply(new("ok", "no artifact", true, null), "json", []);
        Assert.False(CopilotChatService.CanCommitContext(formatted, true, null));
        Assert.False(CopilotChatService.CanCommitContext(new("ok", "response", false, null), false, null));
        Assert.True(CopilotChatService.CanCommitContext(new("ok", "response", true, null), true, null));
    }

    [Fact]
    public async Task LiveCandidatesAreNotArtifactsOfThePreviousFocusAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("candidate-artifacts", default);
        context.SetActiveHandler("PreviousHandler");
        var result = new GestionEngineMetadataResult("HandlerActions", "Search", "Handler", null,
            [new(42, "HandlerA", null, null, null)]);
        var artifacts = CopilotChatService.GetDisplayedLiveNames(result, "- HandlerA (Id: 42)");
        context.SetGroundedArtifacts(artifacts);

        Assert.Equal(new ActiveSubject("Handler", "HandlerA"), context.GetGroundedArtifacts()[0].OriginatingSubject);
        Assert.Equal("PreviousHandler", context.GetActiveHandler());
    }

    [Fact]
    public async Task ExplicitKnownIdentifierOverridesContradictoryProposalNamesAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("explicit-known", default);
        context.SetResolvedSubject(SubjectCatalogFixture.Handler("HandlerA", 42));
        var proposal = Proposal("OtherFunction", "Function") with { Handler = "ThirdName" };
        var resolution = await new ConversationSubjectResolver(new SubjectCatalogFixture())
            .ResolveAsync(proposal, context.GetSubjectSnapshot(), "handlera");
        var plan = ValidatedConversationPlan.Validate(proposal, resolution).Plan;

        Assert.NotNull(plan);
        Assert.Equal("HandlerA", plan.SubjectName);
        Assert.Equal("Handler", plan.SubjectKind);
    }

    [Fact]
    public async Task GeneralClassificationCannotReinterpretScopedTechnicalReferenceAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("general-reference", default);
        context.SetResolvedSubject(SubjectCatalogFixture.Handler("HandlerA", 42));
        var proposal = Proposal(reference: "current", intent: "GeneralQuestion") with { GeneralAnswer = "invented general answer" };
        var resolution = await new ConversationSubjectResolver(new SubjectCatalogFixture())
            .ResolveAsync(proposal, context.GetSubjectSnapshot(), "dame sus parametros");
        var validation = ValidatedConversationPlan.Validate(proposal, resolution);

        Assert.Null(validation.Plan);
        Assert.Equal("GeneralSubjectIncompatible", validation.Reason);
        Assert.Equal("HandlerA", context.GetActiveHandler());
    }

    [Fact]
    public void InvalidCreateResultCannotCommitDespiteSuccessfulTransportStatus()
    {
        var spec = new ViewSpec("ViewA", null, null, []);
        var invalidResult = new ChatResult("ok", "invalid view", false, null, ViewSpecResult:
            new(spec, new(false, ["validation error"], [])));
        var validResult = invalidResult with { ViewSpecResult = new(spec, new(true, [], ["missing stored procedure"])) };

        Assert.False(CopilotChatService.CanCommitContext(invalidResult, true, null));
        Assert.True(CopilotChatService.CanCommitContext(validResult, true, null));
    }

    [Fact]
    public async Task MetadataCannotIgnoreFailedSubjectReferenceAsync()
    {
        var proposal = Proposal(reference: "previous", intent: "GestionEngineMetadata", operation: "Search") with
        {
            Resource = "HandlerActions", Operation = "Search", Search = "HTML"
        };
        var resolution = await new ConversationSubjectResolver(new SubjectCatalogFixture())
            .ResolveAsync(proposal, Empty, "dame datos del anterior");
        var validation = ValidatedConversationPlan.Validate(proposal, resolution);

        Assert.Null(validation.Plan);
        Assert.Equal("SubjectReferenceMissing", validation.Reason);
    }

    [Fact]
    public async Task CatalogueFailurePropagatesWithoutMutatingContextAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("catalogue-failure", default);
        context.SetResolvedSubject(SubjectCatalogFixture.Handler("OriginalHandler"));
        var focus = context.GetCurrentFocus();
        var resolver = new ConversationSubjectResolver(new FailingCatalog());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync(Proposal("NewHandler"), context.GetSubjectSnapshot(), "NewHandler"));

        Assert.Equal(focus, context.GetCurrentFocus());
        Assert.Null(context.GetPreviousFocus());
    }

    [Fact]
    public async Task BareSemanticReferenceDoesNotBecomeAnIdentifierLookupAsync()
    {
        var catalog = new SubjectCatalogFixture();
        var snapshot = new ConversationSubjectSnapshot(null,
            new(new ActiveSubject("Handler", "HandlerA"), null, null, []), [], []);
        var resolution = await new ConversationSubjectResolver(catalog)
            .ResolveAsync(Proposal(reference: "previous"), snapshot, "anterior");

        Assert.Equal("HandlerA", resolution.Identity?.Subject.Name);
        Assert.Equal("previous", resolution.Identity?.Source);
        Assert.Equal(0, catalog.LiveLookups);
        Assert.Equal(0, catalog.DocumentLookups);
    }

    [Fact]
    public async Task ConflictingConfirmedContextKindsAreAmbiguousWithoutSelectingPlannerKindAsync()
    {
        var snapshot = new ConversationSubjectSnapshot(
            new(new ActiveSubject("Handler", "SharedName"), null, null, []),
            new(new ActiveSubject("Function", "SharedName"), null, null, []), [], []);
        var resolution = await new ConversationSubjectResolver(new SubjectCatalogFixture())
            .ResolveAsync(Proposal("SharedName", "Handler"), snapshot, "consulta SharedName");

        Assert.Equal(SubjectResolutionStatus.Ambiguous, resolution.Status);
        Assert.Null(resolution.Identity);
        Assert.Equal(2, resolution.Candidates?.Count);
    }

    [Fact]
    public async Task PublishingDisplayedCandidatesDoesNotCommitFailedFocusOrArtifactsAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("published-candidates", default);
        context.SetResolvedSubject(SubjectCatalogFixture.Handler("OriginalHandler"));
        context.SetRequestKind("Examples");
        context.SetLastCandidateSet([new ActiveSubject("Handler", "OldCandidate")]);
        context.SetGroundedArtifacts([new(GroundedArtifactKind.Json, "{}", "Live", "source", "OriginalHandler", "OriginalHandler")]);
        var focus = context.GetCurrentFocus();
        var artifacts = context.GetGroundedArtifacts().ToArray();
        await using var staged = context.BeginUpdate();
        staged.SetActiveHandler("UnconfirmedHandler");
        staged.SetRequestKind("Parameters");
        staged.SetGroundedArtifacts([]);
        staged.SetLastCandidateSet([new ActiveSubject("Handler", "DisplayedCandidate")]);
        staged.CommitCandidateSet();
        var resolution = await new ConversationSubjectResolver(new SubjectCatalogFixture())
            .ResolveAsync(Proposal(reference: "first"), context.GetSubjectSnapshot(), "dame sus parametros");

        Assert.Equal("DisplayedCandidate", resolution.Identity?.Subject.Name);
        Assert.Equal(focus, context.GetCurrentFocus());
        Assert.Null(context.GetPreviousFocus());
        Assert.Equal(artifacts, context.GetGroundedArtifacts());
    }

    private sealed class FailingCatalog : IConversationSubjectCatalog
    {
        public Task<IReadOnlyList<ResolvedConversationSubject>> FindHandlersAsync(string name, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Fixture catalogue unavailable.");

        public IReadOnlyList<KnowledgeSearchResultItem> FindDocuments(string name) =>
            throw new InvalidOperationException("Fixture catalogue unavailable.");
    }
}
