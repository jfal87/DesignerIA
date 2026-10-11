using DesignerIA.Api.Services;
using DesignerIA.Contracts;
using Xunit;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

// Causes confirmed in artifacts\eval-100-20261009 (S14, S15, F13, G12); retrieval causes live in KnowledgeEvidenceSelectionRegressionTests.
public sealed partial class PlannerFailureRegressionTests
{
    [Theory]
    [InlineData("ok, y qué más sabes hacer?", "Soy DesignerIA.")]
    [InlineData("y qué más puedes hacer", "Soy DesignerIA.")]
    [InlineData("ok gracias, eso era todo", SocialConversation.Thanks)]
    [InlineData("eso es todo por ahora", SocialConversation.Closing)]
    [InlineData("nada más, hasta luego", SocialConversation.Closing)]
    public async Task CapabilityQuestionsAndClosingsAreAnsweredLocally(string message, string expectedStart)
    {
        var outcome = await RunAsync(PlannerMustNotBeCalled, message);
        Assert.Equal(200, outcome.Http);
        Assert.Equal(0, outcome.Chat.Calls);
        Assert.StartsWith(expectedStart, outcome.Result.Response);
        await AssertSeededFocusIsIntact(outcome.Store, outcome.Id);
    }

    [Theory]
    [InlineData("ok, y qué más sabes hacer con HTMLEnCeldaAdvanced?")]
    [InlineData("eso es todo lo que hace el handler?")]
    [InlineData("nada más ejecuta este SQL")]
    [InlineData("gracias, y ahora los parámetros de HTMLEnCeldaAdvanced")]
    public void CompoundRequestsAreStillNotSocial(string message)
    {
        var normalized = new string(message.Normalize(System.Text.NormalizationForm.FormD)
            .Where(character => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ').ToArray());
        Assert.Null(SocialConversation.Respond(message, normalized));
    }

    [Fact]
    public async Task ALiveCountIsDeliveredEvenWhenThePlannerAsksForAVariableFormat()
    {
        // Recorded for "y cuántas acciones de handler existen actualmente?" (evaluation F13, HTTP 503).
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var proposal = new SemanticProposal("GestionEngineMetadata", null, false, null, "variable", [], "HandlerActions", "Count", null,
            Goal: "saber cuántas acciones de handler existen actualmente");
        var result = await fixture.RunAsync(proposal, "y cuántas acciones de handler existen actualmente?",
            (decision, _) =>
            {
                Assert.Equal(ConversationExecutor.MetadataQuery, decision.Executor);
                return Task.FromResult(new ChatResult("ok", "En GestionEngine real existen 123 acciones de handler.", true, null));
            }, isPlannerOutput: true);
        Assert.Equal("ok", result.Outcome.Result.Status);
        Assert.Contains("123", result.Outcome.Result.Response);
        Assert.NotEqual("RequestedFormatUnavailable", result.Outcome.FallbackReason);
    }

    [Fact]
    public async Task ADocumentaryAnswerStillNeedsAVerifiedArtifactForAVariableFormat()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var proposal = new SemanticProposal("HandlerQuestion", null, false, null, "variable", ["HandlerA"], null, null, null,
            SubjectKind: "Handler", SubjectName: "HandlerA", RequestKind: "Explanation");
        var result = await fixture.RunAsync(proposal, "dame solo la variable de HandlerA",
            (_, _) => Task.FromResult(new ChatResult("ok", "var inventada = 1;", true, null)), isPlannerOutput: true);
        Assert.Equal("error", result.Outcome.Result.Status);
        Assert.Equal("RequestedFormatUnavailable", result.Outcome.FallbackReason);
    }

    [Fact]
    public void ADocumentedJsonIsReproducedWithItsSourceAndOnlyForItsOwnSubject()
    {
        var subject = new ActiveSubject("Handler", "HandlerA");
        GroundedArtifact Json(string content, ActiveSubject? owner) =>
            new(GroundedArtifactKind.Json, content, "Knowledge", "Handlers.xlsx: Hoja1", "HandlerA", "HandlerA", owner);
        var text = ConversationCapabilityRouter.DescribeDocumentedJson(subject,
            [Json("{\"Columnas\":\"Todas\"}", subject), Json("{\"Otro\":1}", new ActiveSubject("Handler", "HandlerB")), Json("{\"SinDueño\":1}", null)]);
        Assert.Contains("{\"Columnas\":\"Todas\"}", text);
        Assert.Contains("Handlers.xlsx: Hoja1", text);
        Assert.DoesNotContain("Otro", text);
        Assert.DoesNotContain("SinDueño", text);
    }

    [Fact]
    public void WithoutAnyDocumentedJsonTheGapIsStatedInsteadOfInventingOne()
    {
        var text = ConversationCapabilityRouter.DescribeDocumentedJson(new ActiveSubject("Topic", "tema sin json"), []);
        Assert.Contains("No encontré un JSON documentado", text);
        Assert.Contains("no voy a inventarlo", text);
    }

    [Fact]
    public async Task ANewJsonRequestWithoutAVerifiedArtifactShowsTheDocumentedSyntaxWithWarningAndSource()
    {
        // Recorded for "dame un JSON válido para configurar ComentariosEnCelda en todas las columnas" (evaluation G12):
        // the planner asked to reuse an artifact that was never shown. The corpus only documents a syntax that is not JSON.
        var topic = new ActiveSubject("Topic", "ComentariosEnCelda");
        var recorded = ProposalJson(new SemanticProposal("KnowledgeQuestion", "ComentariosEnCelda", true, null, "json", [], null, null, null,
            SubjectReference: "current", RequestKind: "Parameters", ReuseGroundedArtifact: true));
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(recorded),
            "dame un JSON válido para configurar ComentariosEnCelda en todas las columnas", seed: false,
            arrange: stored =>
            {
                stored.SetResolvedSubject(new ResolvedConversationSubject(topic, "KnowledgeTopic"));
                stored.AddTurn("como configuro comentarios en celdas", "respuesta documental previa");
            });
        Assert.Equal(200, outcome.Http);
        Assert.Equal(1, outcome.Chat.Calls);
        Assert.Contains("No tengo un JSON válido y verificado", outcome.Result.Response);
        Assert.Contains("JSON válido tal como est", outcome.Result.Response);
        Assert.Contains("{\"JsonColumnasYRenglones\":\"[{Columna:\"Todas\",Renglon:\"Todos\"}]\"}", outcome.Result.Response);
        Assert.Contains("Fuente:", outcome.Result.Response);
        Assert.DoesNotContain("artefacto grounded", outcome.Result.Response);
        Assert.DoesNotContain("\\\"Todas\\\"", outcome.Result.Response);
        Assert.NotEmpty(outcome.Result.Sources!);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("ContextDisposition") as string == "Committed");
        await using var after = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.Equal(topic, after.GetCurrentFocus()?.Subject);
    }

    [Fact]
    public async Task AJsonRequestAboutAnotherSubjectWithoutAVerifiedArtifactKeepsTheRefusal()
    {
        // The documentary search runs on the active subject; a request that points at the previous one must not be
        // answered with whatever that search finds for the current focus.
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(FirstExampleProposalJson), "quiero el JSON del primer ejemplo",
            seed: false, arrange: Focus(topicIsCurrent: false));
        Assert.Equal(1, outcome.Chat.Calls);
        var text = outcome.Result.Response + outcome.Result.Error;
        Assert.Contains("No tengo un artefacto grounded", text);
        Assert.DoesNotContain("JsonColumnasYRenglones", text);
        Assert.True(outcome.Result.Sources is null or { Count: 0 });
        await using var after = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.Equal(AdvancedHandler, after.GetCurrentFocus()?.Subject);
        Assert.Equal(CommentsTopic, after.GetPreviousFocus()?.Subject);
    }
}
