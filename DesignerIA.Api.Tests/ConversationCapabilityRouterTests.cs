using DesignerIA.Api.Services;
using Xunit;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

public sealed class ConversationCapabilityRouterTests
{
    private static SemanticProposal Proposal(
        string intent = "HandlerQuestion", string? operation = "Parameters", string? kind = "Handler",
        string? name = "HandlerA", bool reuse = false, string mode = "explanation") =>
        new(intent, null, false, null, mode, reuse ? [] : ["specific operation query"], null, null, null,
            GeneralAnswer: "answer", SubjectKind: kind, SubjectName: name, RequestKind: operation, ReuseGroundedArtifact: reuse);

    private static ConversationSubjectResolution Confirmed(
        string kind = "Handler", string name = "HandlerA", string source = "Live") =>
        new(SubjectResolutionStatus.Resolved,
            new(new ActiveSubject(kind, name), source,
                source is "Live" or "Documentary" ? "confirmed source: 42" : null, source == "Live"));

    private static ValidatedConversationPlan Validate(SemanticProposal proposal, ConversationSubjectResolution resolution)
    {
        var validation = ValidatedConversationPlan.Validate(proposal, resolution);
        Assert.Null(validation.Reason);
        return Assert.IsType<ValidatedConversationPlan>(validation.Plan);
    }

    // Manual test 2026-10-10: "HTMLEnCeldaAdvanced, ¿cómo lo puedo implementar?" returned a dump of fragments and SQL rows.
    [Fact]
    public void HowToImplementAConfirmedHandlerIsExplainedAndAnExactFormatKeepsTheDeterministicAnswer()
    {
        var explained = ConversationCapabilityRouter.Route(Validate(Proposal(operation: "Configuration"), Confirmed()), []);
        Assert.Equal(ConversationExecutor.GroundedKnowledge, explained.Executor);
        Assert.Equal(ConversationCapabilityEffect.ReadOnly, explained.Effect);
        Assert.Equal("Configuration", explained.RequestKind);
        var exact = ConversationCapabilityRouter.Route(Validate(Proposal(operation: "Configuration") with { ResponseMode = "json" }, Confirmed()), []);
        Assert.Equal(ConversationExecutor.HandlerEvidence, exact.Executor);
    }

    [Theory]
    [InlineData("Parameters")]
    [InlineData("Examples")]
    [InlineData("Evidence")]
    [InlineData("Search")]
    public void ConfirmedHandlerUsesSpecializedReadOnlyEvidence(string operation)
    {
        var plan = Validate(Proposal(operation: operation), Confirmed());
        var decision = ConversationCapabilityRouter.Route(plan, []);

        Assert.Equal(ConversationExecutor.HandlerEvidence, decision.Executor);
        Assert.Equal(ConversationCapabilityEffect.ReadOnly, decision.Effect);
        Assert.Equal(operation, decision.RequestKind);
        Assert.Same(plan.Identity, decision.Identity);
        Assert.Equal(plan.SearchQueries, decision.SearchQueries);
        Assert.False(decision.AllowKnowledgeTool);
    }

    [Theory]
    [InlineData("Use", "HandlerQuestion")]
    [InlineData("Explanation", "HandlerQuestion")]
    [InlineData("Use", "KnowledgeQuestion")]
    [InlineData("Explanation", "KnowledgeQuestion")]
    [InlineData("Use", "MixedQuestion")]
    [InlineData("Explanation", "MixedQuestion")]
    public void CompatibleSemanticFamiliesCannotChangeHandlerDocumentaryExecutor(string operation, string intent)
    {
        var plan = Validate(Proposal(intent, operation), Confirmed());
        var decision = ConversationCapabilityRouter.Route(plan, []);

        Assert.Equal(ConversationExecutor.GroundedKnowledge, decision.Executor);
        Assert.Equal(operation, decision.RequestKind);
    }

    [Fact]
    public void MixedIntentCannotRedirectHandlerParameters()
    {
        var validation = ValidatedConversationPlan.Validate(Proposal("MixedQuestion"), Confirmed());

        Assert.Null(validation.Plan);
        Assert.Equal("HandlerOperationCapabilityMismatch", validation.Reason);
    }

    [Fact]
    public void KnowledgeIntentOnAConfirmedHandlerUsesHandlerEvidenceNeverADocumentaryLookup()
    {
        var plan = Validate(Proposal("KnowledgeQuestion"), Confirmed());
        var decision = ConversationCapabilityRouter.Route(plan, []);

        Assert.Equal("HandlerQuestion", plan.Intent);
        Assert.Equal(ConversationExecutor.HandlerEvidence, decision.Executor);
        Assert.Equal("Parameters", decision.RequestKind);
    }

    [Theory]
    [InlineData("KnowledgeQuestion", null)]
    [InlineData("MixedQuestion", "answer")]
    public void DocumentaryFunctionUsesKnowledgeAndKeepsGeneralAnswerOnlyForMixedQuestions(string intent, string? expectedAnswer)
    {
        var plan = Validate(Proposal(intent, "Explanation", "Function", "RealFunction"),
            Confirmed("Function", "RealFunction", "Documentary"));
        var decision = ConversationCapabilityRouter.Route(plan, []);

        Assert.Equal(ConversationExecutor.GroundedKnowledge, decision.Executor);
        Assert.Equal(expectedAnswer, decision.Answer);
        Assert.Equal("RealFunction", decision.Identity?.Subject.Name);
    }

    [Theory]
    [InlineData("Parameters")]
    [InlineData("Examples")]
    [InlineData("Configuration")]
    public void DocumentaryHandlerTopicCannotTriggerLiveLookup(string operation)
    {
        var plan = Validate(Proposal(operation: operation, kind: "Handler", name: "DocHandler"),
            Confirmed("Topic", "DocHandler", "Documentary"));
        var decision = ConversationCapabilityRouter.Route(plan, []);

        Assert.Equal(ConversationExecutor.GroundedKnowledge, decision.Executor);
        Assert.False(decision.Identity?.IsLive);
    }

    [Fact]
    public async Task UnknownOrAmbiguousSubjectCannotProduceAnExecutionDecisionAsync()
    {
        foreach (var status in new[] { SubjectResolutionStatus.NotFound, SubjectResolutionStatus.Ambiguous })
        {
            await using var context = await new ConversationContextStore().AcquireAsync(status.ToString(), default);
            context.SetActiveHandler("OriginalHandler");
            var focus = context.GetCurrentFocus();
            var validation = ValidatedConversationPlan.Validate(Proposal(),
                new(status, Reason: "SubjectNotFound", Candidates: [SubjectCatalogFixture.Handler("Candidate")]));
            var dispatches = 0;
            if (validation.Plan is { } executable)
            {
                if (ConversationCapabilityRouter.Route(executable, []).Executor != ConversationExecutor.None) { dispatches++; }
            }

            Assert.Null(validation.Plan);
            Assert.Equal(0, dispatches);
            Assert.Equal(focus, context.GetCurrentFocus());
        }
    }

    [Theory]
    [InlineData("json", GroundedArtifactKind.Json)]
    [InlineData("code", GroundedArtifactKind.Snippet)]
    [InlineData("variable", GroundedArtifactKind.Variable)]
    [InlineData("names", GroundedArtifactKind.TechnicalName)]
    public void CompatibleScopedArtifactSelectsReuseWithoutQueries(string mode, GroundedArtifactKind kind)
    {
        var plan = Validate(Proposal(operation: "Examples", reuse: true, mode: mode), Confirmed());
        var artifact = new GroundedArtifact(kind, "content", "Knowledge", "guide.md", "HandlerA", "HandlerA", plan.Subject);
        var decision = ConversationCapabilityRouter.Route(plan, [artifact]);

        Assert.Equal(ConversationExecutor.ArtifactReuse, decision.Executor);
        Assert.Equal(ConversationCapabilityEffect.ReadOnly, decision.Effect);
        Assert.Same(artifact, Assert.Single(decision.Artifacts));
        Assert.Empty(decision.SearchQueries);
    }

    [Theory]
    [InlineData(GroundedArtifactKind.Snippet, "Knowledge", "guide.md", "HandlerA")]
    [InlineData(GroundedArtifactKind.Json, "Model", "guide.md", "HandlerA")]
    [InlineData(GroundedArtifactKind.Json, "Knowledge", "", "HandlerA")]
    [InlineData(GroundedArtifactKind.Json, "Knowledge", "guide.md", "OtherHandler")]
    public void IncompatibleOrUngroundedArtifactCannotFallbackToAnotherExecutor(
        GroundedArtifactKind kind, string origin, string source, string subject)
    {
        var plan = Validate(Proposal(operation: "Examples", reuse: true, mode: "json"), Confirmed());
        var artifact = new GroundedArtifact(kind, "{}", origin, source, subject, subject, new("Handler", subject));
        var decision = ConversationCapabilityRouter.Route(plan, [artifact]);

        Assert.Equal(ConversationExecutor.None, decision.Executor);
        Assert.Equal("GroundedArtifactMissing", decision.Reason);
        Assert.True(decision.Rejected);
        Assert.False(decision.CanCommitContext);
        Assert.Empty(decision.Artifacts);
    }

    [Fact]
    public void MultipleScopedJsonArtifactsRequireClarification()
    {
        var plan = Validate(Proposal(operation: "Examples", reuse: true, mode: "json"), Confirmed());
        var artifacts = new[]
        {
            new GroundedArtifact(GroundedArtifactKind.Json, "{\"a\":1}", "Live", "source1", "HandlerA", "HandlerA", plan.Subject),
            new GroundedArtifact(GroundedArtifactKind.Json, "{\"a\":2}", "Live", "source2", "HandlerA", "HandlerA", plan.Subject)
        };
        var decision = ConversationCapabilityRouter.Route(plan, artifacts);

        Assert.Equal(ConversationExecutor.None, decision.Executor);
        Assert.Equal("GroundedArtifactAmbiguous", decision.Reason);
    }

    [Fact]
    public void ArtifactReusePrecedesMissingOperationWithoutInventingOne()
    {
        var plan = Validate(Proposal(operation: null, reuse: true, mode: "json"), Confirmed());
        var decision = ConversationCapabilityRouter.Route(plan,
            [new(GroundedArtifactKind.Json, "{}", "Live", "source", "HandlerA", "HandlerA", plan.Subject)]);

        Assert.Equal(ConversationExecutor.ArtifactReuse, decision.Executor);
        Assert.Null(decision.RequestKind);
    }

    [Fact]
    public void GeneralCodeDoesNotRequireDomainArtifacts()
    {
        var plan = Validate(Proposal("GeneralQuestion", "Explanation", "General", "CSharp", mode: "code"),
            Confirmed("General", "CSharp", "GeneralSubject"));
        var decision = ConversationCapabilityRouter.Route(plan, []);

        Assert.Equal(ConversationExecutor.GeneralAnswer, decision.Executor);
        Assert.Equal("code", decision.ResponseMode);
        Assert.Empty(decision.Artifacts);
        Assert.False(decision.AllowKnowledgeTool);
    }

    [Fact]
    public async Task ConversationDoesNotBindIncidentalTechnicalSubjectAsync()
    {
        await using var context = await new ConversationContextStore().AcquireAsync("conversation", default);
        context.SetActiveHandler("OriginalHandler");
        var focus = context.GetCurrentFocus();
        var plan = Validate(Proposal("Conversation", null), Confirmed());
        var decision = ConversationCapabilityRouter.Route(plan, context.GetGroundedArtifacts());

        Assert.Equal(ConversationExecutor.ConversationResponse, decision.Executor);
        Assert.Null(decision.Identity);
        Assert.Equal(focus, context.GetCurrentFocus());
    }

    [Theory]
    [InlineData("HandlerActions", "Count")]
    [InlineData("HandlerActions", "List")]
    [InlineData("HandlerActions", "Search")]
    [InlineData("HandlerUsages", "Count")]
    [InlineData("HandlerUsages", "List")]
    [InlineData("HandlerUsages", "Search")]
    public void RegisteredMetadataPairsUseCatalogueExecutor(string resource, string operation)
    {
        var proposal = Proposal("GestionEngineMetadata", "Search", null, null) with
        {
            Resource = resource, Operation = operation, Search = "HTML"
        };
        var plan = Validate(proposal, new(SubjectResolutionStatus.NotFound, Reason: "NoSubject"));
        var decision = ConversationCapabilityRouter.Route(plan, []);

        Assert.Equal(ConversationExecutor.MetadataQuery, decision.Executor);
        Assert.Equal(resource, decision.Resource);
        Assert.Equal(operation, decision.ResourceOperation);
        Assert.Equal("HTML", decision.Search);
        Assert.False(decision.AllowKnowledgeTool);
    }

    [Fact]
    public void ViewCountHasItsOwnReadOnlyExecutor()
    {
        var plan = Validate(Proposal("GestionEngineMetadata", "Search", null, null),
            new(SubjectResolutionStatus.NotFound, Reason: "NoSubject"));
        var decision = ConversationCapabilityRouter.Route(plan, []);

        Assert.Equal(ConversationExecutor.ViewCount, decision.Executor);
        Assert.Equal(ConversationCapabilityEffect.ReadOnly, decision.Effect);
        Assert.Equal("Views", decision.Resource);
        Assert.Equal("Count", decision.ResourceOperation);
    }

    [Theory]
    [InlineData("OtherResource", "Search")]
    [InlineData("HandlerActions", "Execute")]
    [InlineData("HandlerActions", null)]
    public void UnregisteredMetadataHasNoPlan(string resource, string? operation)
    {
        var validation = ValidatedConversationPlan.Validate(Proposal("GestionEngineMetadata", "Search", null, null) with
        {
            Resource = resource, Operation = operation
        }, new(SubjectResolutionStatus.NotFound, Reason: "NoSubject"));

        Assert.Null(validation.Plan);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ViewCreationIsOnlyPreparationWithFixedKnowledgeAllowlist(bool minimal)
    {
        var plan = Validate(Proposal("ViewCreation", "Configuration", "View", "ViewA"),
            new(SubjectResolutionStatus.NotFound, Reason: "NoSubject"));
        var decision = ConversationCapabilityRouter.Route(plan, [], minimal);

        Assert.Equal(ConversationExecutor.ViewSpecPreparation, decision.Executor);
        Assert.Equal(ConversationCapabilityEffect.Preparation, decision.Effect);
        Assert.Equal(!minimal, decision.AllowKnowledgeTool);
        Assert.Null(decision.Resource);
        Assert.Null(decision.ResourceOperation);
    }

    [Theory]
    [InlineData("UnsupportedAction")]
    [InlineData("Unknown")]
    public void UnsupportedAndUnknownRequestsHaveNoExecutor(string intent)
    {
        var plan = Validate(Proposal(intent, null, null, null),
            new(SubjectResolutionStatus.NotFound, Reason: "NoSubject"));
        var decision = ConversationCapabilityRouter.Route(plan, []);

        Assert.Equal(ConversationExecutor.None, decision.Executor);
        Assert.Equal(ConversationCapabilityEffect.None, decision.Effect);
        Assert.False(decision.CanCommitContext);
        Assert.False(decision.AllowKnowledgeTool);
    }

    [Theory]
    [InlineData("session_limits_status")]
    [InlineData("ExecuteSql")]
    [InlineData("UpdateView")]
    public void SuggestedCapabilityCannotProduceValidatedPlan(string intent)
    {
        var validation = ValidatedConversationPlan.Validate(Proposal(intent), Confirmed());

        Assert.Null(validation.Plan);
        Assert.Equal("CapabilityNotAllowed", validation.Reason);
    }

    [Fact]
    public async Task RouterDoesNotMutateContextOrQueryCataloguesAsync()
    {
        var catalog = new SubjectCatalogFixture { Handlers = [SubjectCatalogFixture.Handler("HandlerA")] };
        await using var context = await new ConversationContextStore().AcquireAsync("pure-router", default);
        context.SetActiveHandler("OriginalHandler");
        context.SetRequestKind("Explanation");
        var focus = context.GetCurrentFocus();
        var proposal = Proposal();
        var resolution = await new ConversationSubjectResolver(catalog)
            .ResolveAsync(proposal, context.GetSubjectSnapshot(), "query HandlerA");
        var plan = Validate(proposal, resolution);
        var liveLookups = catalog.LiveLookups;
        var documentLookups = catalog.DocumentLookups;

        for (var iteration = 0; iteration < 5; iteration++)
        {
            Assert.Equal(ConversationExecutor.HandlerEvidence,
                ConversationCapabilityRouter.Route(plan, context.GetGroundedArtifacts()).Executor);
        }

        Assert.Equal(focus, context.GetCurrentFocus());
        Assert.Null(context.GetPreviousFocus());
        Assert.Equal(liveLookups, catalog.LiveLookups);
        Assert.Equal(documentLookups, catalog.DocumentLookups);
    }

    [Theory]
    [InlineData("HandlerA")]
    [InlineData("handlera")]
    [InlineData("HANDLERA")]
    public void CasingDoesNotChangeRegisteredExecutor(string name)
    {
        var plan = Validate(Proposal(name: name), Confirmed(name: name));

        Assert.Equal(ConversationExecutor.HandlerEvidence, ConversationCapabilityRouter.Route(plan, []).Executor);
    }

    [Fact]
    public void UnregisteredSubjectCombinationDoesNotFallbackToKnowledge()
    {
        var validation = ValidatedConversationPlan.Validate(
            Proposal("KnowledgeQuestion", "Parameters", "General", "topic"),
            Confirmed("General", "topic", "GeneralSubject"));

        Assert.Null(validation.Plan);
        Assert.Equal("CombinationNotRegistered", validation.Reason);
    }

    [Fact]
    public void MissingOperationClarifiesWithoutSelectingAService()
    {
        var plan = Validate(Proposal(operation: null), Confirmed());
        var decision = ConversationCapabilityRouter.Route(plan, []);

        Assert.Equal(ConversationExecutor.None, decision.Executor);
        Assert.Equal("RequestKindAmbiguous", decision.Reason);
        Assert.True(decision.CanCommitContext);
    }

    [Fact]
    public void ForbiddenMechanismDoesNotExpandAllowedPreparationTools()
    {
        var plan = Validate(Proposal("ViewCreation", "Configuration", "View", "ViewA") with
        {
            ForbiddenAction = true, Goal = "ExecuteSql; shell; filesystem; create view directly"
        }, new(SubjectResolutionStatus.NotFound, Reason: "NoSubject"));
        var decision = ConversationCapabilityRouter.Route(plan, []);

        Assert.Equal(ConversationExecutor.ViewSpecPreparation, decision.Executor);
        Assert.Equal(ConversationCapabilityEffect.Preparation, decision.Effect);
        Assert.True(decision.AllowKnowledgeTool);
        Assert.Null(decision.Resource);
        Assert.Null(decision.ResourceOperation);
    }

    [Fact]
    public void UnknownIdentityProvenanceCannotAuthorizeAService()
    {
        var validation = ValidatedConversationPlan.Validate(Proposal(), Confirmed(source: "ModelSuggested"));

        Assert.Null(validation.Plan);
        Assert.Equal("SubjectNotConfirmed", validation.Reason);
    }

    [Fact]
    public void NameOnlyArtifactCannotProveSubjectKindOrProvenance()
    {
        var plan = Validate(Proposal(operation: "Examples", reuse: true, mode: "json"), Confirmed());
        var decision = ConversationCapabilityRouter.Route(plan,
            [new(GroundedArtifactKind.Json, "{}", "Live", "source", "HandlerA", "HandlerA")]);

        Assert.Equal(ConversationExecutor.None, decision.Executor);
        Assert.Equal("GroundedArtifactMissing", decision.Reason);
    }

    [Fact]
    public void ArtifactScopeComparisonPreservesCanonicalCasing()
    {
        var plan = Validate(Proposal(operation: "Examples", reuse: true, mode: "json"), Confirmed());
        var artifact = new GroundedArtifact(GroundedArtifactKind.Json, "{}", "Live", "source", "HandlerA", "HandlerA",
            new("Handler", "handlera"));
        var decision = ConversationCapabilityRouter.Route(plan, [artifact]);

        Assert.Equal(ConversationExecutor.ArtifactReuse, decision.Executor);
        Assert.Equal("HandlerA", decision.Identity?.Subject.Name);
    }

    [Fact]
    public void ArtifactNamesMayReturnMultipleScopedNamesWithoutChoosingOne()
    {
        var plan = Validate(Proposal(operation: "Examples", reuse: true, mode: "names"), Confirmed());
        var artifacts = new[]
        {
            new GroundedArtifact(GroundedArtifactKind.TechnicalName, "NameA", "Knowledge", "source1", "HandlerA", "NameA", plan.Subject),
            new GroundedArtifact(GroundedArtifactKind.TechnicalName, "NameB", "Knowledge", "source2", "HandlerA", "NameB", plan.Subject)
        };
        var decision = ConversationCapabilityRouter.Route(plan, artifacts);

        Assert.Equal(ConversationExecutor.ArtifactReuse, decision.Executor);
        Assert.Equal(2, decision.Artifacts.Count);
    }
}
