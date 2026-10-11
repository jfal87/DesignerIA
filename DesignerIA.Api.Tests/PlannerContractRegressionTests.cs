using System.Text.Json;
using DesignerIA.Api.Services;
using Xunit;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

public sealed class PlannerContractRegressionTests
{
    private const string Handler = "HTMLEnCeldaAdvanced";
    private const string Variant = "HTMLEnCeldaAdvanced_ControlRoom";
    private static SemanticProposal Proposal(string? reference = null) =>
        new("HandlerQuestion", null, false, null, "explanation", ["handler parameters"], null, null, null,
            SubjectKind: "Function", SubjectName: reference is null ? Handler : null,
            SubjectReference: reference, RequestKind: "Parameters");

    [Theory]
    [InlineData("executor")]
    [InlineData("metadataResource")]
    [InlineData("isLive")]
    [InlineData("isPlannerOutput")]
    public void UnknownPlannerFieldsAreRejectedRatherThanSilentlyIgnored(string field)
    {
        var json = "{\"intent\":\"HandlerQuestion\",\"responseMode\":\"json\",\"referencesPreviousTopic\":false,"
            + "\"searchQueries\":[],\"" + field + "\":\"Untrusted\"}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SemanticProposal>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Theory]
    [InlineData("HandlerActions", "List", "acciones de handler")]
    [InlineData("HandlerActions", "Count", "HandlerActions")]
    [InlineData("HandlerUsages", "Count", "handlers")]
    [InlineData("HandlerUsages", "List", "usos de handlers")]
    [InlineData("HandlerActions", "Search", "catalogo")]
    [InlineData(null, null, "Vistas")]
    public async Task GlobalMetadataDoesNotRequireOrCommitAnIndividualIdentity(string? resource, string? operation, string name)
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler));
        var before = await fixture.StateAsync();
        var proposal = Proposal() with
        {
            Intent = "GestionEngineMetadata", Resource = resource, Operation = operation,
            Search = operation == "Search" ? "HTML" : null, SubjectName = name, RequestKind = "Search"
        };
        var result = await fixture.RunAsync(proposal, "consulta global de metadata", (decision, _) =>
        {
            Assert.Equal(resource is null ? ConversationExecutor.ViewCount : ConversationExecutor.MetadataQuery, decision.Executor);
            Assert.Null(decision.Identity);
            return Task.FromResult(new DesignerIA.Contracts.ChatResult("ok", "Resultado del catalogo", true, null));
        });
        Assert.NotNull(result.Validation.Plan);
        Assert.Equal(0, fixture.Catalog.LiveLookups);
        Assert.Equal(0, fixture.Catalog.DocumentLookups);
        Assert.Equal(before.Current, (await fixture.StateAsync()).Current);
        Assert.Equal(before.Previous, (await fixture.StateAsync()).Previous);
    }

    [Fact]
    public async Task ConversationCannotHideAnExplicitConfirmedTechnicalSubject()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler));
        var before = await fixture.StateAsync();
        var result = await fixture.RunAsync(Proposal() with
        {
            Intent = "Conversation", RequestKind = null, SearchQueries = [], GeneralAnswer = "Quedan 30 creditos"
        }, "pregunta sobre el sujeto explicito", isPlannerOutput: true);
        Assert.Null(result.Validation.Plan);
        Assert.Equal("ConversationSubjectIncompatible", result.Validation.Reason);
        Assert.Empty(fixture.ExternalExecutions);
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
        Assert.Equal(before.Prompt, (await fixture.StateAsync()).Prompt);
    }

    [Fact]
    public void MetadataSearchCannotBeSmuggledOutsideItsSemanticFamily()
    {
        var proposal = Proposal() with { Search = "unused metadata filter" };
        var resolution = new ConversationSubjectResolution(SubjectResolutionStatus.Resolved, SubjectCatalogFixture.Handler(Handler));
        Assert.Null(ValidatedConversationPlan.Validate(proposal, resolution).Plan);
    }

    [Fact]
    public void UnusedGeneralAnswerIsNotAnAuthorizationAndIsRemovedFromTechnicalPlan()
    {
        var resolution = new ConversationSubjectResolution(SubjectResolutionStatus.Resolved, SubjectCatalogFixture.Handler(Handler));
        var plan = ValidatedConversationPlan.Validate(Proposal() with { GeneralAnswer = "Incidental answer" }, resolution).Plan;
        Assert.NotNull(plan);
        Assert.Null(plan.GeneralAnswer);
        Assert.Equal(ConversationExecutor.HandlerEvidence, ConversationCapabilityRouter.Route(plan, []).Executor);
    }

    [Theory]
    [InlineData("Quedan 30 creditos")]
    [InlineData("Sere breve")]
    [InlineData("Evitare exploracion opcional")]
    [InlineData("An arbitrary unrelated response")]
    public async Task UnboundModelConversationCannotPublishContentOrCommitIt(string answer)
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler));
        var before = await fixture.StateAsync();
        var proposal = new SemanticProposal("Conversation", null, false, null, "summary", [], null, null, null,
            GeneralAnswer: answer, SubjectKind: "General", SubjectName: "session_limits_status");
        var result = await fixture.RunAsync(proposal, "consulta tecnica", isPlannerOutput: true);
        Assert.Equal("UntrustedPlannerConversation", result.Validation.Reason);
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
        Assert.DoesNotContain(answer, result.Outcome.Result.Response);
        Assert.Equal(before.Prompt, (await fixture.StateAsync()).Prompt);
        Assert.Empty(fixture.ExternalExecutions);
    }

    [Theory]
    [InlineData("{\"intent\":\"Conversation\",\"Intent\":\"GeneralQuestion\",\"responseMode\":\"summary\",\"referencesPreviousTopic\":false,\"searchQueries\":[]}")]
    [InlineData("{\"intent\":\"GeneralQuestion\",\"responseMode\":\"summary\"}")]
    [InlineData("{\"intent\":\"GeneralQuestion\",\"responseMode\":\"summary\",\"referencesPreviousTopic\":\"yes\",\"searchQueries\":[]}")]
    [InlineData("{\"intent\":\"GeneralQuestion\",\"responseMode\":\"summary\",\"referencesPreviousTopic\":false,\"searchQueries\":[],\"executor\":\"ExecuteSql\"}")]
    public void MalformedPlannerContractIsRejected(string json)
    {
        Assert.Throws<JsonException>(() => SemanticProposal.Parse(json));
    }

    [Fact]
    public void OmittedRequestKindRemainsAmbiguousRatherThanInvented()
    {
        var proposal = SemanticProposal.Parse("{\"intent\":\"HandlerQuestion\",\"responseMode\":\"explanation\",\"referencesPreviousTopic\":false,\"searchQueries\":[],\"subjectName\":\"HandlerA\"}");
        Assert.Null(proposal.RequestKind);
        var plan = ValidatedConversationPlan.Validate(proposal,
            new(SubjectResolutionStatus.Resolved, SubjectCatalogFixture.Handler("HandlerA"))).Plan;
        Assert.NotNull(plan);
        Assert.Equal("RequestKindAmbiguous", ConversationCapabilityRouter.Route(plan, []).Reason);
    }

    [Fact]
    public async Task ConfirmedHandlerParametersAndContradictoryFunctionHintUseEvidenceOnce()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var first = await fixture.RunAsync(Proposal(), "pregunta sobre " + Handler, ConversationIntegrationFixture.HandlerEvidence);
        Assert.Equal(ConversationExecutor.HandlerEvidence, first.Decision?.Executor);
        Assert.Equal("Handler", first.Decision?.Identity?.Subject.Kind);
        var lookups = fixture.Catalog.LiveLookups;
        var second = await fixture.RunAsync(Proposal("current"), "dame sus parametros", ConversationIntegrationFixture.HandlerEvidence);
        Assert.Equal(lookups, fixture.Catalog.LiveLookups);
        Assert.Equal("Parameters", second.Decision?.RequestKind);
        Assert.Equal(Handler, second.Decision?.Identity?.Subject.Name);
    }

    [Fact]
    public async Task PreviousReferenceIsExclusiveAndDoesNotResolveIdentityAgain()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler), SubjectCatalogFixture.Handler(Variant));
        var result = await fixture.RunAsync(Proposal("previous"), "dame los parametros del anterior",
            ConversationIntegrationFixture.HandlerEvidence);
        Assert.Equal(Handler, result.Decision?.Identity?.Subject.Name);
        Assert.Equal("previous", result.Decision?.Identity?.Source);
        Assert.Equal(0, fixture.Catalog.LiveLookups);
        Assert.Equal(0, fixture.Catalog.DocumentLookups);
    }

    [Fact]
    public async Task NameAndPreviousReferenceConflictRejectsWithoutContextMutation()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler), SubjectCatalogFixture.Handler(Variant));
        var before = await fixture.StateAsync();
        var result = await fixture.RunAsync(Proposal("previous") with { SubjectName = Variant }, "pregunta con referencia contradictoria");
        Assert.Equal("ConflictingSubjectReference", result.Validation.Reason);
        Assert.Empty(fixture.ExternalExecutions);
        Assert.Equal(before.Current, (await fixture.StateAsync()).Current);
        Assert.Equal(before.Previous, (await fixture.StateAsync()).Previous);
        Assert.Equal(before.Prompt, (await fixture.StateAsync()).Prompt);
    }

    [Fact]
    public async Task NewParameterQueryCannotAlsoClaimArtifactReuse()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler));
        var result = await fixture.RunAsync(Proposal("current") with { ReuseGroundedArtifact = true, ResponseMode = "json" },
            "dame sus parametros");
        Assert.Equal("ArtifactRequestIncompatible", result.Validation.Reason);
        Assert.Empty(fixture.ExternalExecutions);
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
    }

    [Fact]
    public async Task ReuseWithoutCompatibleEvidenceDoesNotBecomeALiveQuery()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler));
        var result = await fixture.RunAsync(Proposal("current") with
        {
            ReuseGroundedArtifact = true, SearchQueries = [], ResponseMode = "json"
        }, "dame sus parametros");
        Assert.Equal("GroundedArtifactMissing", result.Decision?.Reason);
        Assert.Empty(fixture.ExternalExecutions);
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
    }

    [Fact]
    public async Task UnknownIntentCannotExecuteOrConfirmAnything()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var result = await fixture.RunAsync(Proposal() with { Intent = "session_limits_status" }, "consulta tecnica");
        Assert.Equal("CapabilityNotAllowed", result.Validation.Reason);
        Assert.Empty(fixture.ExternalExecutions);
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
    }

    [Fact]
    public async Task MissingOperationKeepsConfirmedIdentityButDoesNotGuessExecutor()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var result = await fixture.RunAsync(Proposal() with { RequestKind = null }, Handler);
        Assert.Equal("RequestKindAmbiguous", result.Decision?.Reason);
        Assert.Equal(Handler, result.Decision?.Identity?.Subject.Name);
        Assert.Empty(fixture.ExternalExecutions);
    }

    [Fact]
    public async Task ExplicitGeneralTopicChangeIsIndependentFromDomainArtifacts()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler));
        var before = await fixture.StateAsync();
        var result = await fixture.RunAsync(Proposal() with
        {
            Intent = "GeneralQuestion", SubjectKind = "General", SubjectName = "Python",
            RequestKind = "Examples", GeneralAnswer = "sum(numbers)", ResponseMode = "code"
        }, "Ahora en Python");
        Assert.Equal(ConversationExecutor.GeneralAnswer, result.Decision?.Executor);
        Assert.Equal("sum(numbers)", result.Outcome.Result.Response);
        Assert.Equal(before.Current, (await fixture.StateAsync()).Current);
        Assert.Empty(fixture.ExternalExecutions);
    }

    [Theory]
    [InlineData("HandlerUsages", "Count", "previous")]
    [InlineData("HandlerUsages", "Count", "current")]
    [InlineData("HandlerUsages", "Count", "first")]
    [InlineData("HandlerUsages", "Count", "handler")]
    [InlineData("HandlerActions", "Count", "previous")]
    [InlineData("HandlerActions", "List", "previous")]
    [InlineData("HandlerActions", "Search", "previous")]
    [InlineData(null, null, "previous")]
    public async Task GlobalMetadataRejectsIndividualReferencesWithoutExecutionOrContextChanges(
        string? resource, string? operation, string reference)
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler), SubjectCatalogFixture.Handler(Variant));
        await using (var stored = await fixture.Store.AcquireAsync("integration", default))
        {
            stored.SetLastCandidateSet([new ActiveSubject("Handler", Handler)]);
        }
        var before = await fixture.StateAsync();
        var proposal = Proposal(reference) with
        {
            Intent = "GestionEngineMetadata", Resource = resource, Operation = operation,
            Search = operation == "Search" ? "HTML" : null, RequestKind = "Search", SearchQueries = []
        };
        var result = await fixture.RunAsync(proposal, "cuantos hay del anterior",
            (_, _) => Task.FromResult(new DesignerIA.Contracts.ChatResult("ok", "Conteo global: 42", true, null)),
            isPlannerOutput: true);

        Assert.Null(result.Validation.Plan);
        Assert.Equal("MetadataSubjectReferenceIncompatible", result.Validation.Reason);
        Assert.Empty(fixture.ExternalExecutions);
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
        Assert.Contains("global", result.Outcome.Result.Response);
        Assert.Equal(0, fixture.Catalog.LiveLookups);
        Assert.Equal(0, fixture.Catalog.DocumentLookups);
        var after = await fixture.StateAsync();
        Assert.Equal(before.Current, after.Current);
        Assert.Equal(before.Previous, after.Previous);
        Assert.Equal(before.Candidates, after.Candidates);
        Assert.Equal(before.Artifacts, after.Artifacts);
        Assert.Equal(before.Prompt, after.Prompt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GlobalMetadataCannotCarryAnIndividualIdentityEvenThroughArtifactReuse(bool reuse)
    {
        var proposal = Proposal() with
        {
            Intent = "GestionEngineMetadata", Resource = "HandlerUsages", Operation = "Count",
            RequestKind = "Search", SearchQueries = [], ReuseGroundedArtifact = reuse,
            ResponseMode = reuse ? "json" : "explanation"
        };
        var resolution = new ConversationSubjectResolution(SubjectResolutionStatus.Resolved,
            SubjectCatalogFixture.Handler(Handler));
        var validation = ValidatedConversationPlan.Validate(proposal, resolution, isPlannerOutput: true);

        Assert.Null(validation.Plan);
        Assert.Equal("MetadataSubjectReferenceIncompatible", validation.Reason);
    }

    [Theory]
    [InlineData("PlannerNoResponse")]
    [InlineData("PlannerNoJson")]
    [InlineData("InvalidProposalJson")]
    [InlineData("PlannerFailure")]
    public async Task PlannerFailuresRetainDiagnosticReasonWhileDiscardingUntrustedConversation(string failure)
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler), SubjectCatalogFixture.Handler(Variant));
        var before = await fixture.StateAsync();
        const string untrustedAnswer = "Contenido arbitrario del modelo sobre creditos";
        var proposal = new SemanticProposal("Conversation", null, false, null, "explanation", [], null, null, null,
            GeneralAnswer: untrustedAnswer, RequestKind: "Parameters");
        var result = await fixture.RunAsync(proposal, "consulta tecnica",
            isPlannerOutput: true, plannerFailureReason: failure);

        Assert.Null(result.Validation.Plan);
        Assert.Equal("UntrustedPlannerConversation", result.Validation.Reason);
        Assert.Equal(failure, result.Outcome.FallbackReason);
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
        Assert.DoesNotContain(untrustedAnswer, result.Outcome.Result.Response);
        Assert.Empty(fixture.ExternalExecutions);
        Assert.False(CopilotChatService.CanCommitContext(new("ok", "respuesta", false, null), true, failure));
        var after = await fixture.StateAsync();
        Assert.Equal(before.Current, after.Current);
        Assert.Equal(before.Previous, after.Previous);
        Assert.Equal(before.Artifacts, after.Artifacts);
        Assert.Equal(before.Candidates, after.Candidates);
        Assert.Equal(before.Prompt, after.Prompt);
    }

    [Fact]
    public void ArbitraryDiagnosticTextCannotReplaceAValidationRejection()
    {
        var validation = new ConversationPlanValidation(null, "UntrustedPlannerConversation", "Aclaracion local");

        Assert.Equal("UntrustedPlannerConversation", validation.GetDiagnosticReason("Texto externo arbitrario"));
        Assert.Equal("UntrustedPlannerConversation", validation.GetDiagnosticReason(null));
    }

    [Theory]
    [InlineData("KnowledgeQuestion", null)]
    [InlineData("MixedQuestion", "console.log(id);")]
    public async Task DocumentaryExecutorReceivesGeneralAnswerOnlyForMixedQuestions(string intent, string? expectedAnswer)
    {
        const string function = "DocumentedFunction";
        var document = new DesignerIA.Contracts.KnowledgeSearchResultItem(
            function, "function.md", "function.md", "function DocumentedFunction(id) { return id; }", 1);
        var fixture = new ConversationIntegrationFixture(new SubjectCatalogFixture { Documents = [document] });
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler));
        var proposal = Proposal() with
        {
            Intent = intent, SubjectKind = "Function", SubjectName = function, RequestKind = "Explanation",
            SearchQueries = [function], GeneralAnswer = "console.log(id);"
        };
        var result = await fixture.RunAsync(proposal, "explica la funcion documentada",
            (decision, staged) =>
            {
                Assert.Equal(ConversationExecutor.GroundedKnowledge, decision.Executor);
                Assert.Equal(expectedAnswer, decision.Answer);
                return ConversationIntegrationFixture.Knowledge(decision, staged, document.Snippet, document);
            }, isPlannerOutput: true);

        Assert.Equal(expectedAnswer, result.Validation.Plan?.GeneralAnswer);
        Assert.Equal("ok", result.Outcome.Result.Status);
        Assert.Equal("Committed", result.Outcome.ContextDisposition);
        Assert.Equal(new ActiveSubject("Function", function), (await fixture.StateAsync()).Current?.Subject);
        Assert.Single(fixture.ExternalExecutions);
    }
}
