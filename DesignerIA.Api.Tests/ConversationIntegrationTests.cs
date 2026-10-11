using DesignerIA.Api.Services;
using DesignerIA.Api.Services.ViewSpecs;
using DesignerIA.Contracts;
using Xunit;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

public sealed class ConversationIntegrationTests
{
    private const string Handler = "HTMLEnCeldaAdvanced";
    private const string Variant = "HTMLEnCeldaAdvanced_ControlRoom";
    private const string Json = "{\"EstiloNivel\":\"\",\"ToolTipoNivel\":\"\",\"EstiloUltimoNivel\":\"G_GRAFICO|G_DET_FACT\",\"ToolTipoUltimoNivel\":\"Modificar Frecuencia|asas\"}";

    private static SemanticProposal Proposal(string? name = Handler, string? request = "Parameters",
        string intent = "HandlerQuestion", string? reference = null, string mode = "explanation", bool reuse = false) =>
        new(intent, null, false, null, mode, reuse ? [] : ["documented operation query"],
            null, null, null, SubjectKind: name is null ? null : "Handler", SubjectName: name,
            SubjectReference: reference, RequestKind: request, ReuseGroundedArtifact: reuse);

    private static KnowledgeSearchResultItem Document(string name, string text) =>
        new(name, name + ".md", name + ".md", text, 1);

    [Fact]
    public async Task HandlerThenParametersKeepsCanonicalFocusAndUsesSpecializedExecutor()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var first = await fixture.RunAsync(Proposal(request: null), Handler);
        Assert.Equal(ConversationExecutor.None, first.Decision?.Executor);
        Assert.Empty(fixture.ExternalExecutions);
        Assert.Equal("Committed", first.Outcome.ContextDisposition);

        var next = await fixture.RunAsync(Proposal(null, reference: "current"), "dame sus parametros",
            ConversationIntegrationFixture.HandlerEvidence);
        var state = await fixture.StateAsync();
        Assert.Equal(ConversationExecutor.HandlerEvidence, next.Decision?.Executor);
        Assert.Equal(Handler, next.Decision?.Identity?.Subject.Name);
        Assert.Equal("Parameters", next.Decision?.RequestKind);
        Assert.Equal(Handler, state.Current?.Subject.Name);
        Assert.Equal("Parameters", state.Current?.RequestKind);
        Assert.Null(state.Previous);
        Assert.True(state.Current?.Identity?.IsLive);
        Assert.Equal(state.Current?.Subject, Assert.Single(state.Artifacts).OriginatingSubject);
        Assert.Single(fixture.ExternalExecutions);
        Assert.Equal(1, fixture.Catalog.LiveLookups);
        Assert.Equal(0, fixture.Catalog.DocumentLookups);
    }

    [Fact]
    public async Task PreviousParametersUsesOnlyPreviousIdentity()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.RunAsync(Proposal(request: null), Handler);
        await fixture.RunAsync(Proposal(Variant, request: null), Variant);
        var before = await fixture.StateAsync();
        Assert.Equal(Handler, before.Previous?.Subject.Name);
        var result = await fixture.RunAsync(Proposal(null, reference: "previous"), "dame los parametros del anterior",
            ConversationIntegrationFixture.HandlerEvidence);
        var after = await fixture.StateAsync();
        Assert.Equal(Handler, result.Decision?.Identity?.Subject.Name);
        Assert.Equal(Handler, after.Current?.Subject.Name);
        Assert.Equal(Variant, after.Previous?.Subject.Name);
        Assert.Single(fixture.ExternalExecutions);
        Assert.Equal(2, fixture.Catalog.LiveLookups);
    }

    [Fact]
    public async Task DocumentedInlineJsonSurvivesRegistrationAndExactReuse()
    {
        const string name = "botonUltimoNivel";
        var document = Document(name, name + " usa " + Json);
        var fixture = new ConversationIntegrationFixture(new() { Documents = [document] });
        var response = await fixture.RunAsync(Proposal(name, "Examples", "KnowledgeQuestion"), name,
            (decision, staged) => ConversationIntegrationFixture.Knowledge(decision, staged, "Ejemplo: " + Json, document));
        Assert.Equal(ConversationExecutor.GroundedKnowledge, response.Decision?.Executor);
        var before = await fixture.StateAsync();
        var artifact = Assert.Single(before.Artifacts, artifact => artifact.Kind == GroundedArtifactKind.Json);
        Assert.Equal(Json, artifact.Content);
        Assert.Equal(before.Current?.Subject, artifact.OriginatingSubject);
        Assert.Equal("Knowledge", artifact.Origin);
        Assert.Contains(name + ".md", artifact.Source);

        var result = await fixture.RunAsync(Proposal(null, "Examples", "KnowledgeQuestion", "current", "json", true),
            "dame solo el JSON del ejemplo");
        var after = await fixture.StateAsync();
        Assert.Equal(ConversationExecutor.ArtifactReuse, result.Decision?.Executor);
        Assert.Equal(Json, result.Outcome.Result.Response);
        Assert.Equal(before.Current, after.Current);
        Assert.Equal(before.Previous, after.Previous);
        Assert.Equal(before.Artifacts.ToArray(), after.Artifacts.ToArray());
        Assert.Single(fixture.ExternalExecutions);
        Assert.Equal(1, fixture.Catalog.LiveLookups);
        Assert.Equal(1, fixture.Catalog.DocumentLookups);
    }

    [Fact]
    public async Task GeneralCodeDoesNotConsumeDomainArtifactsOrDisplaceTechnicalFocus()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.RunAsync(Proposal(), Handler, ConversationIntegrationFixture.HandlerEvidence);
        var before = await fixture.StateAsync();
        var result = await fixture.RunAsync(Proposal("CSharp", "Explanation", "GeneralQuestion", mode: "code") with
        {
            SubjectKind = "General", GeneralAnswer = "int Sum(int a, int b) => a + b;"
        }, "volvamos a C#: dame solo el codigo");
        var after = await fixture.StateAsync();
        Assert.Equal(ConversationExecutor.GeneralAnswer, result.Decision?.Executor);
        Assert.Equal("int Sum(int a, int b) => a + b;", result.Outcome.Result.Response);
        Assert.Equal(before.Current, after.Current);
        Assert.Equal(before.Previous, after.Previous);
        Assert.Equal(before.Artifacts.ToArray(), after.Artifacts.ToArray());
        Assert.Single(fixture.ExternalExecutions);
        Assert.Contains("volvamos a C#", after.Prompt);
    }

    [Fact]
    public async Task ContradictoryFunctionClassificationCannotChangeLiveHandlerIdentity()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var result = await fixture.RunAsync(Proposal() with { SubjectKind = "Function" }, Handler,
            ConversationIntegrationFixture.HandlerEvidence);
        Assert.Equal("Handler", result.Decision?.Identity?.Subject.Kind);
        Assert.Equal(ConversationExecutor.HandlerEvidence, result.Decision?.Executor);
        Assert.Equal("Live", result.Decision?.Identity?.Source);
        Assert.Equal("Handler", (await fixture.StateAsync()).Current?.Subject.Kind);
        Assert.Single(fixture.ExternalExecutions);
    }

    [Theory]
    [InlineData("session_limits_status")]
    [InlineData("UnsupportedAction")]
    public async Task UnknownOrUnsupportedCapabilityCannotExecuteOrCommit(string intent)
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler));
        var before = await fixture.StateAsync();
        var result = await fixture.RunAsync(Proposal(intent: intent), "request");
        var after = await fixture.StateAsync();
        Assert.Empty(fixture.ExternalExecutions);
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
        Assert.Equal(before.Current, after.Current);
        Assert.Equal(before.Previous, after.Previous);
        Assert.Equal(before.Prompt, after.Prompt);
    }

    [Fact]
    public async Task AmbiguousLiveIdentityCannotChooseCandidateOrConfirmFocus()
    {
        var fixture = new ConversationIntegrationFixture(new()
        {
            Handlers = [SubjectCatalogFixture.Handler(Handler, 1), SubjectCatalogFixture.Handler(Handler, 2)]
        });
        await fixture.SeedAsync(SubjectCatalogFixture.Handler("Original"));
        var before = await fixture.StateAsync();
        var result = await fixture.RunAsync(Proposal(), Handler);
        var after = await fixture.StateAsync();
        Assert.Equal("SubjectAmbiguous", result.Validation.Reason);
        Assert.Null(result.Decision);
        Assert.Empty(fixture.ExternalExecutions);
        Assert.Equal(before.Current, after.Current);
        Assert.Equal(before.Previous, after.Previous);
        Assert.Empty(after.Candidates);
        Assert.Equal(before.Prompt, after.Prompt);
    }

    [Theory]
    [InlineData("error", null)]
    [InlineData("ok", "")]
    [InlineData("ok", " ")]
    [InlineData("ok", null)]
    public async Task FailedOrEmptyExecutionDiscardsAllProvisionalChanges(string status, string? response)
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler("Original"));
        var before = await fixture.StateAsync();
        var result = await fixture.RunAsync(Proposal(), Handler, (decision, staged) =>
        {
            staged.SetGroundedArtifacts([new(GroundedArtifactKind.Json, "{}", "Live", "source", Handler, Handler, decision.Identity?.Subject)]);
            return Task.FromResult(new ChatResult(status, response, true, status == "error" ? "execution error" : null));
        });
        var after = await fixture.StateAsync();
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
        Assert.Equal(before.Current, after.Current);
        Assert.Equal(before.Previous, after.Previous);
        Assert.Equal(before.Artifacts.ToArray(), after.Artifacts.ToArray());
        Assert.Equal(before.Prompt, after.Prompt);
        Assert.Single(fixture.ExternalExecutions);
    }

    [Fact]
    public async Task ExecutionExceptionDisposesStagedContextWithoutCommit()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler("Original"));
        var before = await fixture.StateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.RunAsync(Proposal(), Handler, (_, staged) =>
        {
            staged.SetLastCandidateSet([new("Handler", "LeakedCandidate")]);
            throw new InvalidOperationException("External fixture failed.");
        }));
        var after = await fixture.StateAsync();
        Assert.Equal(before.Current, after.Current);
        Assert.Equal(before.Previous, after.Previous);
        Assert.Equal(before.Candidates.ToArray(), after.Candidates.ToArray());
        Assert.Equal(before.Prompt, after.Prompt);
        Assert.Single(fixture.ExternalExecutions);
    }

    [Fact]
    public async Task InvalidCreateCannotCommitPreparationOrAffectFocus()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler));
        var before = await fixture.StateAsync();
        var spec = new ViewSpec(null, null, "missing", []);
        var validation = ViewSpecValidator.Validate(spec);
        Assert.False(validation.IsValid);
        var result = await fixture.RunAsync(Proposal(null, null, "ViewCreation"), "crear vista", (decision, _) =>
        {
            Assert.Equal(ConversationCapabilityEffect.Preparation, decision.Effect);
            return Task.FromResult(new ChatResult("ok", "Invalid preparation", false, null,
                ViewSpecResult: new(spec, validation)));
        });
        var after = await fixture.StateAsync();
        Assert.Equal(ConversationExecutor.ViewSpecPreparation, result.Decision?.Executor);
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
        Assert.Equal(before.Current, after.Current);
        Assert.Equal(before.Previous, after.Previous);
        Assert.Equal(before.Prompt, after.Prompt);
        Assert.Equal(0, fixture.Catalog.LiveLookups);
        Assert.Equal(0, fixture.Catalog.DocumentLookups);
    }

    [Fact]
    public async Task ArtifactFromVariantCannotBeAssignedToCurrentHandler()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Variant), SubjectCatalogFixture.Handler(Handler));
        var evidence = Document(Variant, Variant + " usa " + Json);
        await fixture.RunAsync(Proposal(request: "Use"), "como se usa", (decision, staged) =>
            ConversationIntegrationFixture.Knowledge(decision, staged, "Ejemplo: " + Json, evidence));
        var before = await fixture.StateAsync();
        var json = Assert.Single(before.Artifacts, artifact => artifact.Kind == GroundedArtifactKind.Json);
        Assert.Equal(new ActiveSubject("Handler", Variant), json.OriginatingSubject);
        Assert.Equal(Variant, json.Entity);

        var result = await fixture.RunAsync(Proposal(null, "Examples", reference: "current", mode: "json", reuse: true), "JSON actual");
        Assert.Equal(ConversationExecutor.None, result.Decision?.Executor);
        Assert.Equal("GroundedArtifactMissing", result.Decision?.Reason);
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
        Assert.Equal(before.Current, (await fixture.StateAsync()).Current);
        Assert.Single(fixture.ExternalExecutions);
    }

    [Fact]
    public async Task ReuseOfPreviousArtifactDoesNotSwitchFocusOrReduceAvailableArtifacts()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Variant), SubjectCatalogFixture.Handler(Handler));
        var one = Document(Handler, Handler + " usa {\"base\":1}");
        var two = Document(Variant, Variant + " usa {\"variant\":2}");
        await fixture.RunAsync(Proposal(request: "Use"), "mostrar ambos ejemplos", (decision, staged) =>
            ConversationIntegrationFixture.Knowledge(decision, staged, "{\"base\":1}\n{\"variant\":2}", one, two));
        var before = await fixture.StateAsync();
        Assert.Equal(2, before.Artifacts.Count(artifact => artifact.Kind == GroundedArtifactKind.Json));
        var result = await fixture.RunAsync(Proposal(null, "Examples", reference: "previous", mode: "json", reuse: true), "JSON anterior");
        var after = await fixture.StateAsync();
        Assert.Equal(ConversationExecutor.ArtifactReuse, result.Decision?.Executor);
        Assert.Equal("{\"variant\":2}", result.Outcome.Result.Response);
        Assert.Equal(before.Current, after.Current);
        Assert.Equal(before.Previous, after.Previous);
        Assert.Equal(before.Artifacts.ToArray(), after.Artifacts.ToArray());
        Assert.Single(fixture.ExternalExecutions);
    }

    [Fact]
    public async Task MultipleCompatibleJsonArtifactsRequireClarification()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var document = Document(Handler, Handler + " ejemplos {\"a\":1} y {\"a\":2}");
        await fixture.RunAsync(Proposal(request: "Use"), Handler, (decision, staged) =>
            ConversationIntegrationFixture.Knowledge(decision, staged, "Ejemplos {\"a\":1} y {\"a\":2}", document));
        var before = await fixture.StateAsync();
        var result = await fixture.RunAsync(Proposal(null, "Examples", reference: "current", mode: "json", reuse: true), "JSON");
        Assert.Equal("GroundedArtifactAmbiguous", result.Decision?.Reason);
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
        Assert.Equal(before.Current, (await fixture.StateAsync()).Current);
        Assert.Single(fixture.ExternalExecutions);
    }

    [Fact]
    public async Task StoreCannotInferOwnerFromCurrentFocusOrMergeOwnersByContent()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler));
        await fixture.RunAsync(Proposal(), Handler, (_, staged) =>
        {
            staged.SetGroundedArtifacts([
                new(GroundedArtifactKind.Json, "{}", "Knowledge", "other.md", Variant, Variant),
                new(GroundedArtifactKind.Json, "{\"shared\":1}", "Knowledge", "base.md", Handler, Handler, new("Handler", Handler)),
                new(GroundedArtifactKind.Json, "{\"shared\":1}", "Knowledge", "variant.md", Variant, Variant, new("Handler", Variant))
            ]);
            return Task.FromResult(new ChatResult("ok", "Backed examples", true, null));
        });
        var state = await fixture.StateAsync();
        Assert.Null(state.Artifacts.Single(artifact => artifact.Content == "{}").OriginatingSubject);
        Assert.Equal(2, state.Artifacts.Count(artifact => artifact.Content == "{\"shared\":1}"));
    }

    [Fact]
    public async Task ExecutionOfDifferentSubjectCannotCommitSuccessfulResponse()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var result = await fixture.RunAsync(Proposal(), Handler, (_, staged) =>
        {
            staged.SetResolvedSubject(SubjectCatalogFixture.Handler(Variant));
            return Task.FromResult(new ChatResult("ok", "Wrong subject", true, null));
        });
        Assert.Equal("error", result.Outcome.Result.Status);
        Assert.Equal("ExecutedSubjectMismatch", result.Outcome.FallbackReason);
        Assert.Null((await fixture.StateAsync()).Current);
    }

    [Fact]
    public async Task CasingOnlyCorrectionCannotCreatePreviousFocus()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler));
        var result = await fixture.RunAsync(Proposal(), Handler, (decision, staged) =>
        {
            staged.SetResolvedSubject(decision.Identity! with { Subject = new("Handler", Handler.ToUpperInvariant()) });
            return Task.FromResult(new ChatResult("ok", "Case correction", true, null));
        });
        Assert.Equal("ok", result.Outcome.Result.Status);
        Assert.Null((await fixture.StateAsync()).Previous);
    }

    [Fact]
    public async Task InventedValidJsonCannotBypassGroundingAtFinalFormatting()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var evidence = Document(Handler, Handler + " usa {\"real\":1}");
        var result = await fixture.RunAsync(Proposal(request: "Use", mode: "json"), Handler, (decision, staged) =>
            ConversationIntegrationFixture.Knowledge(decision, staged, "{\"invented\":2}", evidence));
        Assert.Equal("error", result.Outcome.Result.Status);
        Assert.Equal("RequestedFormatUnavailable", result.Outcome.FallbackReason);
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
        Assert.Null((await fixture.StateAsync()).Current);
        Assert.Empty((await fixture.StateAsync()).Artifacts);
        Assert.Single(fixture.ExternalExecutions);
    }

    [Fact]
    public async Task IdenticalJsonFromDifferentOwnersKeepsBothProvenances()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Variant), SubjectCatalogFixture.Handler(Handler));
        var one = Document(Handler, Handler + " usa " + Json);
        var two = Document(Variant, Variant + " usa " + Json);
        await fixture.RunAsync(Proposal(request: "Use"), Handler, (decision, staged) =>
            ConversationIntegrationFixture.Knowledge(decision, staged, "Ejemplo: " + Json, one, two));
        var state = await fixture.StateAsync();
        var artifacts = state.Artifacts.Where(artifact => artifact.Kind == GroundedArtifactKind.Json).ToArray();
        Assert.Equal(2, artifacts.Length);
        Assert.Contains(artifacts, artifact => artifact.OriginatingSubject?.Name == Handler);
        Assert.Contains(artifacts, artifact => artifact.OriginatingSubject?.Name == Variant);
        Assert.Equal(2, artifacts.Select(artifact => artifact.Source).Distinct().Count());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RegisteredMetadataJsonDoesNotRequireDocumentaryArtifacts(bool catalogue)
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler));
        var before = await fixture.StateAsync();
        var proposal = Proposal(null, "Search", "GestionEngineMetadata", mode: "json") with
        {
            Resource = catalogue ? "HandlerActions" : null, Operation = catalogue ? "Count" : null
        };
        var result = await fixture.RunAsync(proposal, "cuantas hay", (_, _) =>
            Task.FromResult(new ChatResult("ok", "{\"count\":3}", true, null)));
        Assert.Equal(catalogue ? ConversationExecutor.MetadataQuery : ConversationExecutor.ViewCount, result.Decision?.Executor);
        Assert.Equal("ok", result.Outcome.Result.Status);
        Assert.Equal("{\"count\":3}", result.Outcome.Result.Response);
        Assert.Equal(ConversationCapabilityEffect.ReadOnly, result.Decision?.Effect);
        Assert.Equal(before.Current, (await fixture.StateAsync()).Current);
        Assert.Single(fixture.ExternalExecutions);
        Assert.Equal(0, fixture.Catalog.LiveLookups);
        Assert.Equal(0, fixture.Catalog.DocumentLookups);
    }

    [Fact]
    public async Task InvalidMetadataOperationCannotDispatchOrChangeFocus()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler));
        var before = await fixture.StateAsync();
        var result = await fixture.RunAsync(Proposal(null, "Search", "GestionEngineMetadata") with
        {
            Resource = "HandlerActions", Operation = "Execute"
        }, "unsupported operation");
        Assert.Null(result.Decision);
        Assert.NotNull(result.Validation.Reason);
        Assert.Empty(fixture.ExternalExecutions);
        Assert.Equal(before.Current, (await fixture.StateAsync()).Current);
        Assert.Equal(before.Prompt, (await fixture.StateAsync()).Prompt);
    }

    [Fact]
    public async Task LocalConversationPreservesTechnicalFocusWithoutExternalCalls()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler));
        var before = await fixture.StateAsync();
        var result = await fixture.RunAsync(Proposal(null, null, "Conversation") with { GeneralAnswer = "Hola." }, "hola");
        Assert.Equal(ConversationExecutor.ConversationResponse, result.Decision?.Executor);
        Assert.Equal("Hola.", result.Outcome.Result.Response);
        Assert.Equal(before.Current, (await fixture.StateAsync()).Current);
        Assert.Equal(before.Previous, (await fixture.StateAsync()).Previous);
        Assert.Empty(fixture.ExternalExecutions);
        Assert.Equal(0, fixture.Catalog.LiveLookups);
        Assert.Equal(0, fixture.Catalog.DocumentLookups);
    }

    [Theory]
    [InlineData("previous")]
    [InlineData("first")]
    public async Task MissingReferenceCannotFallBackToCurrent(string reference)
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler));
        var before = await fixture.StateAsync();
        var result = await fixture.RunAsync(Proposal(null, reference: reference), "dame parametros de esa referencia");
        Assert.Null(result.Decision);
        Assert.Empty(fixture.ExternalExecutions);
        Assert.Equal(before.Current, (await fixture.StateAsync()).Current);
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
        Assert.Equal(0, fixture.Catalog.LiveLookups);
        Assert.Equal(0, fixture.Catalog.DocumentLookups);
    }

    [Fact]
    public async Task AmbiguousMultiSubjectEvidenceCannotAcquireAnOwnerFromFocus()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Variant), SubjectCatalogFixture.Handler(Handler));
        var source = Document("Comparacion", Handler + " y " + Variant + " usan " + Json);
        await fixture.RunAsync(Proposal(request: "Use"), Handler, (decision, staged) =>
            ConversationIntegrationFixture.Knowledge(decision, staged, "Ejemplo: " + Json, source));
        var state = await fixture.StateAsync();
        Assert.Null(Assert.Single(state.Artifacts).OriginatingSubject);
        var result = await fixture.RunAsync(Proposal(null, "Examples", reference: "current", mode: "json", reuse: true), "solo JSON");
        Assert.Equal("GroundedArtifactMissing", result.Decision?.Reason);
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
        Assert.Single(fixture.ExternalExecutions);
    }

    [Fact]
    public async Task InvalidJsonIsNeitherRepairedNorCommittedAsJson()
    {
        const string invalid = "{\"invalid\":}";
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var source = Document(Handler, Handler + " muestra " + invalid);
        var result = await fixture.RunAsync(Proposal(request: "Use", mode: "json"), Handler, (decision, staged) =>
            ConversationIntegrationFixture.Knowledge(decision, staged, invalid, source));
        Assert.Equal("error", result.Outcome.Result.Status);
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
        Assert.Empty((await fixture.StateAsync()).Artifacts);
        Assert.Null((await fixture.StateAsync()).Current);
    }

    [Fact]
    public async Task ContradictoryClassificationOnFollowUpCannotCreateSubjectTransition()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.RunAsync(Proposal(request: null), Handler);
        var result = await fixture.RunAsync(Proposal(Handler.ToUpperInvariant()) with { SubjectKind = "Function" },
            "dame sus parametros", ConversationIntegrationFixture.HandlerEvidence);
        var state = await fixture.StateAsync();
        Assert.Equal(ConversationExecutor.HandlerEvidence, result.Decision?.Executor);
        Assert.Equal(new ActiveSubject("Handler", Handler), state.Current?.Subject);
        Assert.Equal("Parameters", state.Current?.RequestKind);
        Assert.Null(state.Previous);
        Assert.Equal(1, fixture.Catalog.LiveLookups);
        Assert.Single(fixture.ExternalExecutions);
    }

    [Fact]
    public async Task MetadataNamesUseDisplayedLiveEvidenceWithoutTakingTechnicalFocus()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler(Handler));
        var proposal = Proposal(null, "Search", "GestionEngineMetadata", mode: "names") with
        {
            Resource = "HandlerActions", Operation = "List"
        };
        var result = await fixture.RunAsync(proposal, "lista nombres", (_, staged) =>
        {
            var metadata = new GestionEngineMetadataResult("HandlerActions", "List", null, null,
                [new(1, Handler, null, null, null), new(2, Variant, null, null, null)]);
            var display = "- " + Handler + " (Id: 1)\n- " + Variant + " (Id: 2)";
            staged.SetGroundedArtifacts(CopilotChatService.GetDisplayedLiveNames(metadata, display));
            // The names answer is written from the catalogue that was read, not from the artifacts kept in memory.
            return Task.FromResult(new ChatResult("ok", CopilotChatService.FormatMetadataNames(metadata), true, null));
        });
        Assert.Equal("ok", result.Outcome.Result.Status);
        Assert.Equal(Handler + "\n" + Variant, result.Outcome.Result.Response);
        Assert.Equal(Handler, (await fixture.StateAsync()).Current?.Subject.Name);
        Assert.Single(fixture.ExternalExecutions);
    }
}
