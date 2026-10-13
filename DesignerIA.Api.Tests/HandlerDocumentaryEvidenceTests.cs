using DesignerIA.Api.Services;
using DesignerIA.Contracts;
using Xunit;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

public sealed class HandlerDocumentaryEvidenceTests
{
    private const string Handler = "botonUltimoNivel";
    private const string Json = "{\"EstiloNivel\":\"\",\"ToolTipoNivel\":\"\",\"EstiloUltimoNivel\":\"G_GRAFICO|G_DET_FACT\",\"ToolTipoUltimoNivel\":\"Modificar Frecuencia|asas\"}";
    private static HandlerExamplesResult NoJson() => new(Handler, true, Handler, null, null, 18, 1, 0, 0, []);

    private static async Task<(ConversationIntegrationResult Turn, ConversationIntegrationState State)> RunAsync(
        HandlerExamplesResult live, string request, string mode)
    {
        var corpus = new ReadOnlyKnowledgeFixture();
        var fixture = new ConversationIntegrationFixture(new SubjectCatalogFixture { Handlers = [SubjectCatalogFixture.Handler(Handler, 18)] });
        var chat = corpus.Chat(fixture.Store);
        var proposal = new SemanticProposal("HandlerQuestion", null, false, null, mode,
            [Handler + " acciones handler"], null, null, null, SubjectKind: "Handler", SubjectName: Handler, RequestKind: request);
        var timings = new CopilotChatService.ChatTimings();
        var turn = await fixture.RunAsync(proposal, "dame el ejemplo documentado de " + Handler,
            (decision, staged) => Task.FromResult(chat.BuildTechnicalEvidenceResult("dame el ejemplo documentado de " + Handler,
                Handler, staged, mode, live, timings, request, decision.SearchQueries)));
        return (turn, await fixture.StateAsync());
    }

    [Theory]
    [InlineData("Examples")]
    [InlineData("Parameters")]
    public async Task MissingLiveJsonCanReturnVerifiedDocumentaryJsonWithItsSource(string request)
    {
        var (turn, state) = await RunAsync(NoJson(), request, "json");
        Assert.Equal("ok", turn.Outcome.Result.Status);
        Assert.Equal(Json, turn.Outcome.Result.Response);
        var artifacts = state.Artifacts.Where(a => a.Kind == GroundedArtifactKind.Json).ToArray();
        Assert.Equal(Json, Assert.Single(artifacts.Select(a => a.Content).Distinct()));
        // Several corpus files carry the same JSON; how many are retrieved depends on the ranking, each keeps its provenance.
        Assert.InRange(artifacts.Select(a => a.Source).Distinct().Count(), 3, 5);
        Assert.All(artifacts, artifact =>
        {
            Assert.Equal("Knowledge", artifact.Origin);
            Assert.Equal(new ActiveSubject("Handler", Handler), artifact.OriginatingSubject);
        });
        Assert.Contains(artifacts, artifact => artifact.Source.Contains("DataGestionEngineDesarrollo.xlsx", StringComparison.Ordinal));
        Assert.NotEmpty(turn.Outcome.Result.Sources!);
        Assert.Contains(turn.Outcome.Result.Sources!, source => source.SourceFile == "DataGestionEngineDesarrollo.xlsx");
    }

    [Fact]
    public async Task DocumentaryJsonDisplayedInExplanationIsRegisteredWithoutBecomingLive()
    {
        var (turn, state) = await RunAsync(NoJson(), "Examples", "explanation");
        Assert.Equal("ok", turn.Outcome.Result.Status);
        Assert.Contains("Según la documentación", turn.Outcome.Result.Response);
        Assert.Contains("No encontré implementaciones actuales", turn.Outcome.Result.Response);
        Assert.Contains(state.Artifacts, a => a.Kind == GroundedArtifactKind.Json && a.Content == Json && a.Origin == "Knowledge");
        Assert.DoesNotContain(state.Artifacts, a => a.Kind == GroundedArtifactKind.Json && a.Origin == "Live");
    }

    [Fact]
    public async Task AvailableLiveJsonKeepsLiveIdentityAndIsNotReplacedByDocumentation()
    {
        var (turn, state) = await RunAsync(NoJson() with { ParameterTemplate = "{\"observed\":1}" }, "Parameters", "json");
        Assert.Equal("{\"observed\":1}", turn.Outcome.Result.Response);
        var artifact = Assert.Single(state.Artifacts);
        Assert.Equal("Live", artifact.Origin);
        Assert.Equal("AccionesHandler: 18", artifact.Source);
        Assert.Null(turn.Outcome.Result.Sources);
    }

    [Fact]
    public async Task EvidenceForAnotherLiveActionCannotBeRelabelledAsTheRequestedHandler()
    {
        var (turn, state) = await RunAsync(NoJson() with { Action = "OtherHandler", ParameterTemplate = "{\"foreign\":1}" }, "Parameters", "json");
        Assert.Equal("error", turn.Outcome.Result.Status);
        Assert.Empty(state.Artifacts);
        Assert.Null(state.Current);
    }

    [Fact]
    public async Task AForeignLiveExampleCannotBecomeTheRequestedHandlersObservedParameters()
    {
        var example = new HandlerExample(1, 99, "OtherHandler", null, null, 1, "{\"foreign\":1}", null, null,
            false, false, 1, null, 1, null, 1, null, 1, null, "OtherView", null);
        var (turn, state) = await RunAsync(NoJson() with { Examples = [example] }, "Parameters", "json");
        Assert.Equal("error", turn.Outcome.Result.Status);
        Assert.Equal("Discarded", turn.Outcome.ContextDisposition);
        Assert.Empty(state.Artifacts);
        Assert.Null(state.Current);
    }

    [Fact]
    public async Task DocumentaryJsonAmbiguityRemainsAClarificationAndDoesNotCommitContext()
    {
        var catalog = new SubjectCatalogFixture { Handlers = [SubjectCatalogFixture.Handler(Handler, 18)] };
        var store = new ConversationContextStore();
        await using var stored = await store.AcquireAsync("documentary-ambiguity", default);
        await using var staged = stored.BeginUpdate();
        var proposal = new SemanticProposal("HandlerQuestion", null, false, null, "json", [Handler],
            null, null, null, SubjectKind: "Handler", SubjectName: Handler, RequestKind: "Examples");
        var resolution = await new ConversationSubjectResolver(catalog).ResolveAsync(proposal, staged.GetSubjectSnapshot(), Handler);
        var plan = Assert.IsType<ValidatedConversationPlan>(ValidatedConversationPlan.Validate(proposal, resolution).Plan);
        var decision = ConversationCapabilityRouter.Route(plan, []);
        plan.StageSubject(staged);
        const string clarification = "Hay varios JSON documentados compatibles; indica qué ejemplo necesitas.";
        var outcome = ConversationTurnCompleter.Finish(new ChatResult("ok", clarification, true, null), staged,
            new(Handler, plan, decision, true, FallbackReason: "GroundedArtifactAmbiguous"));
        Assert.Equal("ok", outcome.Result.Status);
        Assert.Equal(clarification, outcome.Result.Response);
        Assert.Equal("GroundedArtifactAmbiguous", outcome.FallbackReason);
        Assert.Equal("Discarded", outcome.ContextDisposition);
        Assert.Null(stored.GetCurrentFocus());
        Assert.Null(stored.GetPreviousFocus());
        Assert.Empty(stored.GetGroundedArtifacts());
    }
}
