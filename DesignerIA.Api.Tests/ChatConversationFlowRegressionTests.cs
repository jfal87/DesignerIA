using DesignerIA.Api.Services;
using DesignerIA.Contracts;
using Xunit;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

// Cases taken from the captured 23-message session; planner outputs are the ones the SDK recorded.
public sealed partial class PlannerFailureRegressionTests
{
    private static Task<string?> PlannerMustNotBeCalled(CopilotChatService.ChatTimings timings, CancellationToken token) =>
        throw new InvalidOperationException("Planner must not be called");

    private static string General(string answer, string mode = "explanation", bool previous = false) => ProposalJson(
        new SemanticProposal("GeneralQuestion", null, previous, null, mode, [], null, null, null,
            GeneralAnswer: answer, SubjectKind: "General"));

    private static async Task AssertSeededFocusIsIntact(ConversationContextStore store, string id)
    {
        await using var stored = await store.AcquireAsync(id, default);
        Assert.Equal("HandlerB", stored.GetCurrentFocus()?.Subject.Name);
        Assert.Equal("Examples", stored.GetCurrentFocus()?.RequestKind);
        Assert.Equal("HandlerA", stored.GetPreviousFocus()?.Subject.Name);
        Assert.Equal(2, stored.GetLastCandidateSet().Count);
        Assert.Equal("{}", Assert.Single(stored.GetGroundedArtifacts()).Content);
    }

    [Theory]
    [InlineData("ok ok ok ok gracias!", SocialConversation.Thanks)]
    [InlineData("Muchas graciasss!!", SocialConversation.Thanks)]
    [InlineData("Hola???", SocialConversation.Greeting)]
    [InlineData("buenos días", SocialConversation.Greeting)]
    [InlineData("ok", SocialConversation.Acknowledgement)]
    [InlineData("Perfecto, entendido.", SocialConversation.Acknowledgement)]
    [InlineData("hola, como estas?", SocialConversation.HowAreYou)]
    public async Task SocialMessagesAreAnsweredLocallyAndLeaveTheTechnicalContextUntouched(string message, string expected)
    {
        var outcome = await RunAsync(PlannerMustNotBeCalled, message);
        Assert.Equal(200, outcome.Http);
        Assert.Equal(expected, outcome.Result.Response);
        Assert.Equal(0, outcome.Chat.Calls);
        Assert.False(outcome.Result.ToolInvoked);
        Assert.Null(outcome.Result.Sources);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("ContextDisposition") as string == "Committed");
        await AssertSeededFocusIsIntact(outcome.Store, outcome.Id);
    }

    [Theory]
    [InlineData("quien eres?")]
    [InlineData("Quien erees=")]
    [InlineData("Que haces=????")]
    [InlineData("¿cómo te llamas?")]
    public async Task IdentityQuestionsDescribeDesignerIAWithoutThePlanner(string message)
    {
        var outcome = await RunAsync(PlannerMustNotBeCalled, message);
        Assert.Equal(200, outcome.Http);
        Assert.Equal(0, outcome.Chat.Calls);
        Assert.StartsWith("Soy DesignerIA.", outcome.Result.Response);
        Assert.Contains("No puede ejecutar SQL arbitrario", outcome.Result.Response);
        await AssertSeededFocusIsIntact(outcome.Store, outcome.Id);
    }

    [Theory]
    [InlineData("gracias, ahora dime los parámetros de HTMLEnCeldaAdvanced")]
    [InlineData("ok, ejecuta este SQL")]
    [InlineData("hola();")]
    [InlineData("sí")]
    [InlineData("ok dame los ejemplos")]
    public void MessagesWithAnyNonSocialWordAreNotSocial(string message)
    {
        var normalized = new string(message.Normalize(System.Text.NormalizationForm.FormD)
            .Where(character => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : ' ').ToArray());
        Assert.Null(SocialConversation.Respond(message, normalized));
    }

    [Fact]
    public async Task ThanksWithATechnicalRequestStillReachesThePlanner()
    {
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(UnboundConversation())),
            "gracias, ahora dime los parámetros de HTMLEnCeldaAdvanced");
        Assert.Equal(1, outcome.Chat.Calls);
        Assert.NotEqual(SocialConversation.Thanks, outcome.Result.Response);
    }

    [Fact]
    public async Task AcknowledgementWithAnExecutionRequestKeepsTheExecutionBoundary()
    {
        var forbidden = ProposalJson(new SemanticProposal("UnsupportedAction", null, false, null, "explanation", [], null, null, null,
            ForbiddenAction: true));
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(forbidden), "ok, ejecuta este SQL");
        Assert.Equal(1, outcome.Chat.Calls);
        Assert.Contains(DesignerCapabilities.ExecutionBoundary, outcome.Result.Response);
        Assert.False(outcome.Result.ToolInvoked);
    }

    [Fact]
    public async Task ThanksFollowedByATechnicalFollowUpStillUsesTheSameSubjectAndArtifact()
    {
        var outcome = await RunAsync(PlannerMustNotBeCalled, "dame solo el JSON", firstMessage: "ok ok ok ok gracias!");
        Assert.Equal(200, outcome.Http);
        Assert.Equal("{}", outcome.Result.Response);
        Assert.Equal(0, outcome.Chat.Calls);
        var finalized = Assert.Single(outcome.Logger.Entries, entry => entry.Fields.ContainsKey("ContextDisposition"));
        Assert.Equal("HandlerB", finalized.Fields["SubjectName"]);
    }

    [Theory]
    [InlineData("cuanto es 24 por ocho", "192", "variable")]
    [InlineData("como que no, cuanto es 24 * 8", "24 * 8 = 192", "variable")]
    [InlineData("cuanto es 24 * 9", "216", "variable")]
    [InlineData("cuanto es tres por dos", "Tres por dos es seis.", "variable")]
    [InlineData("cuanto es tres por dos", "6", "names")]
    public async Task GeneralResultsDoNotRequireAMotorArtifactNorInheritSubjectFormatOrCandidates(string message, string answer, string mode)
    {
        // The seeded conversation has a technical focus, a JSON artifact and candidates on offer.
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(General(answer, mode)), message);
        Assert.Equal(200, outcome.Http);
        Assert.Equal("ok", outcome.Result.Status);
        Assert.Equal(answer, outcome.Result.Response);
        Assert.Equal(1, outcome.Chat.Calls);
        Assert.False(outcome.Result.ToolInvoked);
        Assert.Null(outcome.Result.Sources);
        var finalized = Assert.Single(outcome.Logger.Entries, entry => entry.Fields.ContainsKey("ContextDisposition"));
        Assert.Equal("GeneralAnswer", finalized.Fields["Executor"]?.ToString());
        Assert.Null(finalized.Fields["Reason"]);
        await AssertSeededFocusIsIntact(outcome.Store, outcome.Id);
    }

    [Fact]
    public async Task AGeneralAnswerRequestedAsJsonMustStillBeJson()
    {
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(General("ciento noventa y dos", "json")), "cuanto es 24 por ocho");
        Assert.Equal("error", outcome.Result.Status);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Reason") as string == "RequestedFormatUnavailable");
    }

    [Fact]
    public async Task AGeneralFollowUpUsesItsOwnAnswer()
    {
        var calls = 0;
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(++calls == 1
                ? General("La capital de México es Ciudad de México (CDMX).")
                : General("La capital de Brasil es Brasilia.", previous: true)),
            "de brasil", seed: false, firstMessage: "capital de méxioc?");
        Assert.Equal(200, outcome.Http);
        Assert.Equal("La capital de Brasil es Brasilia.", outcome.Result.Response);
        Assert.False(outcome.Result.ToolInvoked);
        await using var stored = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.Contains("Usuario anterior: capital de méxioc?", stored.BuildPrompt("next"));
    }

    [Fact]
    public async Task ADocumentedFunctionProposedAsHandlerBecomesADocumentaryQuestionAndTheFollowUpStaysOnIt()
    {
        const string function = "clickEnCeldaGrilla";
        var document = new KnowledgeSearchResultItem("clickEnCeldaGrilla", "DocumentacionEngineNET.html", "Wiki/DocumentacionEngineNET.html",
            "Ejemplo ``` function clickEnCeldaGrilla(obj) { var nuePar = new ParametrosPopUp(); } ```", 1);
        var fixture = new ConversationIntegrationFixture(new SubjectCatalogFixture
        {
            Handlers = [SubjectCatalogFixture.Handler("ejecutarClickEnCelda", 5)], Documents = [document]
        });
        await fixture.SeedAsync(new ResolvedConversationSubject(new ActiveSubject("Topic", "handler"), "KnowledgeTopic"));
        // Recorded proposal: the model guessed a handler for "que es clickenceldaGrilla".
        var proposal = new SemanticProposal("HandlerQuestion", function, false, null, "explanation",
            ["clickEnCeldaGrilla handler"], null, null, null, SubjectKind: "Handler", SubjectName: function, RequestKind: "Explanation");
        var first = await fixture.RunAsync(proposal, "que es clickenceldaGrilla",
            (decision, staged) => ConversationIntegrationFixture.Knowledge(decision, staged, "Es una función de la vista.", document),
            isPlannerOutput: true);
        Assert.Equal("KnowledgeQuestion", first.Validation.Plan?.Intent);
        Assert.Equal(ConversationExecutor.GroundedKnowledge, first.Decision?.Executor);
        Assert.Equal(new ActiveSubject("Function", function), first.Decision?.Identity?.Subject);
        Assert.True(first.Decision?.Identity?.IsDocumentary);
        Assert.Null(first.Validation.Plan?.Handler);
        Assert.Equal("Committed", first.Outcome.ContextDisposition);

        var followUp = new SemanticProposal("KnowledgeQuestion", null, true, null, "explanation", [], null, null, null,
            SubjectKind: "General", SubjectReference: "current", RequestKind: "Explanation");
        var second = await fixture.RunAsync(followUp, "para que es? es una funcion del motor",
            (decision, staged) => ConversationIntegrationFixture.Knowledge(decision, staged, "Sirve para abrir un popup.", document),
            isPlannerOutput: true);
        Assert.Equal(new ActiveSubject("Function", function), second.Decision?.Identity?.Subject);
        Assert.Equal(new ActiveSubject("Function", function), (await fixture.StateAsync()).Current?.Subject);
    }

    [Fact]
    public async Task ARecordedKnowledgeProposalForAConfirmedHandlerUsesHandlerEvidence()
    {
        // Recorded for "gracias, ahora dime los parámetros de HTMLEnCeldaAdvanced": knowledge intent and a Function hint.
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var proposal = new SemanticProposal("KnowledgeQuestion", "HTMLEnCeldaAdvanced", false, null, "explanation",
            ["HTMLEnCeldaAdvanced parámetros"], null, null, null,
            SubjectKind: "Function", SubjectName: "HTMLEnCeldaAdvanced", RequestKind: "Parameters");
        var result = await fixture.RunAsync(proposal, "gracias, ahora dime los parámetros de HTMLEnCeldaAdvanced",
            ConversationIntegrationFixture.HandlerEvidence, isPlannerOutput: true);
        Assert.Equal(ConversationExecutor.HandlerEvidence, result.Decision?.Executor);
        Assert.Equal(new ActiveSubject("Handler", "HTMLEnCeldaAdvanced"), result.Decision?.Identity?.Subject);
        Assert.True(result.Decision?.Identity?.IsLive);
        Assert.Equal("Committed", result.Outcome.ContextDisposition);
    }

    [Fact]
    public async Task AnUnresolvedQuestionIsRememberedWithoutChangingFocusArtifactsOrCandidates()
    {
        var unknown = ProposalJson(new SemanticProposal("KnowledgeQuestion", null, false, null, "explanation", ["FuncionInexistente"],
            null, null, null, SubjectKind: "Function", SubjectName: "FuncionInexistente", RequestKind: "Explanation"));
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(unknown), "que es FuncionInexistente", new SubjectCatalogFixture());
        Assert.Equal(200, outcome.Http);
        Assert.Contains("No pude confirmar el sujeto", outcome.Result.Response);
        var finalized = Assert.Single(outcome.Logger.Entries, entry => entry.Fields.ContainsKey("ContextDisposition"));
        Assert.Equal("TurnOnly", finalized.Fields["ContextDisposition"]);
        Assert.Equal("SubjectNotFound", finalized.Fields["Reason"]);
        await AssertSeededFocusIsIntact(outcome.Store, outcome.Id);
        await using var stored = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.Contains("Usuario anterior: que es FuncionInexistente", stored.BuildPrompt("next"));
        Assert.Contains("Respuesta anterior: No pude confirmar el sujeto", stored.BuildPrompt("next"));
    }

    [Fact]
    public async Task ATechnicalFollowUpNamingANewTopicReplacesThePreviousTopic()
    {
        var fixture = new ConversationIntegrationFixture(new SubjectCatalogFixture());
        await fixture.SeedAsync(new ResolvedConversationSubject(new ActiveSubject("Topic", "comentario en gráfico"), "KnowledgeTopic"));
        // Recorded proposal for "y a grilas?" after a documented answer about chart comments.
        var proposal = new SemanticProposal("KnowledgeQuestion", "comentario en grilla", true, null, "explanation",
            ["agregar comentario grilla", "GridComments.js configuración"], null, null, null,
            SubjectKind: "General", SubjectName: "comentario en grilla", RequestKind: "Configuration");
        var result = await fixture.RunAsync(proposal, "y a grilas?",
            (decision, _) => Task.FromResult(new ChatResult("ok", "Respuesta documental", true, null)), isPlannerOutput: true);
        Assert.Equal(ConversationExecutor.GroundedKnowledge, result.Decision?.Executor);
        Assert.Equal(new ActiveSubject("Topic", "comentario en grilla"), result.Decision?.Identity?.Subject);
        Assert.Equal("Configuration", result.Decision?.RequestKind);
    }
}
