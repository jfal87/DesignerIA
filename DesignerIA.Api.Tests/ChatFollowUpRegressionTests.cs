using DesignerIA.Api.Services;
using DesignerIA.Contracts;
using Xunit;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

public sealed partial class PlannerFailureRegressionTests
{
    // Documented exactly like this in mail, spreadsheet and a real export; it is not strict JSON.
    private const string LegacySyntax = "{\"JsonColumnasYRenglones\":\"[{Columna:\"Todas\",Renglon:\"Todos\"}]\"}";
    private const string OtherSubjectJson = "{\"Columnas\":\"Col3|Col4|Col5\", \"Filas\":\"Todas\"}";
    private static readonly ActiveSubject CommentsTopic = new("Topic", "comentarios en grilla");
    private static readonly ActiveSubject AdvancedHandler = new("Handler", "HTMLEnCeldaAdvanced");

    // Planner outputs recorded by the SDK for the two 502 responses (InvalidPlanValue) of the captured session.
    private const string FirstExampleProposalJson =
        "{\"intent\":\"KnowledgeQuestion\",\"topic\":\"comentarios en grilla\",\"searchQueries\":[],\"resource\":null,\"operation\":null,"
        + "\"search\":null,\"referencesPreviousTopic\":true,\"handler\":null,\"responseMode\":\"json\",\"generalAnswer\":null,"
        + "\"subjectKind\":\"Topic\",\"subjectName\":null,\"subjectReference\":\"previous\",\"requestKind\":\"Examples\","
        + "\"reuseGroundedArtifact\":true,\"forbiddenAction\":false,\"goal\":null}";
    private const string ChartCommentsProposalJson =
        "{\"intent\":\"KnowledgeQuestion\",\"topic\":\"comentarios en gráfico\",\"searchQueries\":[\"comentarios en gráfico\","
        + "\"chart annotation comment\",\"configurar comentario serie grafico\"],\"resource\":null,\"operation\":null,\"search\":null,"
        + "\"referencesPreviousTopic\":false,\"handler\":null,\"responseMode\":\"explanation\",\"generalAnswer\":null,"
        + "\"subjectKind\":\"Topic\",\"subjectName\":\"comentarios en gráfico\",\"subjectReference\":null,"
        + "\"requestKind\":\"Configuration\",\"reuseGroundedArtifact\":false,\"forbiddenAction\":false,\"goal\":null}";

    private static GroundedArtifact DocumentedSyntax() => new(GroundedArtifactKind.Snippet, LegacySyntax, "Knowledge",
        "Comentarios en las grillas.msg: Comentarios en las grillas", CommentsTopic.Name, "Comentarios en las grillas");

    private static GroundedArtifact OtherSubjectArtifact() => new(GroundedArtifactKind.Json, OtherSubjectJson, "Live",
        "Handlers: 423580", AdvancedHandler.Name, AdvancedHandler.Name, AdvancedHandler);

    private static Action<ConversationLease> Focus(bool topicIsCurrent, params GroundedArtifact[] artifacts) => stored =>
    {
        var topic = new ResolvedConversationSubject(CommentsTopic, "KnowledgeTopic");
        var handler = SubjectCatalogFixture.Handler(AdvancedHandler.Name, 600);
        stored.SetResolvedSubject(topicIsCurrent ? handler : topic);
        stored.SetResolvedSubject(topicIsCurrent ? topic : handler);
        stored.AddTurn("ahora como puedo agregar comentarios a una grilla", "respuesta documental previa");
        stored.SetGroundedArtifacts(artifacts);
    };

    [Fact]
    public async Task RecordedFirstExampleProposalWithTopicKindExplainsTheDocumentedSyntaxInsteadOfFailing()
    {
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(FirstExampleProposalJson), "quiero el JSON del primer ejemplo",
            seed: false, arrange: Focus(topicIsCurrent: false, DocumentedSyntax()));
        Assert.Equal(200, outcome.Http);
        Assert.Equal("ok", outcome.Result.Status);
        Assert.Equal(1, outcome.Chat.Calls);
        Assert.Contains(LegacySyntax, outcome.Result.Response);
        Assert.Contains("no es JSON válido", outcome.Result.Response);
        Assert.Contains("Comentarios en las grillas.msg", outcome.Result.Response);
        Assert.DoesNotContain("Col3", outcome.Result.Response);
        Assert.DoesNotContain(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Reason") as string == "InvalidPlanValue");
        var finalized = Assert.Single(outcome.Logger.Entries, entry => entry.Fields.ContainsKey("ContextDisposition"));
        Assert.Equal("Committed", finalized.Fields["ContextDisposition"]);
        Assert.Equal("GroundedArtifactNotJson", finalized.Fields["Reason"]);
        await using var stored = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.Equal(AdvancedHandler, stored.GetCurrentFocus()?.Subject);
        Assert.Equal(CommentsTopic, stored.GetPreviousFocus()?.Subject);
        Assert.Single(stored.GetGroundedArtifacts());
    }

    [Fact]
    public async Task RecordedChartCommentsProposalWithTopicKindReachesDocumentaryRetrieval()
    {
        var fixture = new ConversationIntegrationFixture(new SubjectCatalogFixture());
        var result = await fixture.RunAsync(SemanticProposal.Parse(ChartCommentsProposalJson), "como agregar comentarios a un grafico",
            (decision, _) =>
            {
                Assert.Equal(ConversationExecutor.GroundedKnowledge, decision.Executor);
                Assert.Equal("Configuration", decision.RequestKind);
                return Task.FromResult(new ChatResult("ok", "Respuesta documental", true, null));
            }, isPlannerOutput: true);
        Assert.NotNull(result.Validation.Plan);
        Assert.Equal(new ActiveSubject("Topic", "comentarios en gráfico"), result.Decision?.Identity?.Subject);
        Assert.Single(fixture.ExternalExecutions);
    }

    [Theory]
    [InlineData("Tema")]
    [InlineData("topic")]
    [InlineData("Executor")]
    public void SubjectKindRemainsAClosedList(string kind)
    {
        var proposal = SemanticProposal.Parse(ChartCommentsProposalJson) with { SubjectKind = kind };
        var validation = ValidatedConversationPlan.Validate(proposal,
            new ConversationSubjectResolution(SubjectResolutionStatus.NotFound, Reason: "SubjectNotFound"), isPlannerOutput: true);
        Assert.Null(validation.Plan);
        Assert.Equal("InvalidPlanValue", validation.Reason);
    }

    [Theory]
    [InlineData("ok dame solo el JSON")]
    [InlineData("el primero")]
    [InlineData("quiero el JSON del primer ejemplo")]
    public async Task ExactArtifactRequestsForTheCurrentTopicExplainInvalidSyntaxWithoutThePlanner(string message)
    {
        var outcome = await RunAsync((_, _) => throw new InvalidOperationException("Planner must not be called"), message,
            seed: false, arrange: Focus(topicIsCurrent: true, DocumentedSyntax(), OtherSubjectArtifact()));
        Assert.Equal(200, outcome.Http);
        Assert.Equal(0, outcome.Chat.Calls);
        Assert.Contains(LegacySyntax, outcome.Result.Response);
        Assert.Contains("no es JSON válido", outcome.Result.Response);
        Assert.DoesNotContain("Col3", outcome.Result.Response);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("ContextDisposition") as string == "Committed");
        await using var stored = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.Equal(CommentsTopic, stored.GetCurrentFocus()?.Subject);
        Assert.Equal(AdvancedHandler, stored.GetPreviousFocus()?.Subject);
        Assert.Contains("Usuario anterior: " + message, stored.BuildPrompt("next"));
    }

    [Fact]
    public async Task ExactJsonRequestReturnsTheSingleVerifiedArtifactOfTheCurrentSubjectWithoutThePlanner()
    {
        var outcome = await RunAsync((_, _) => throw new InvalidOperationException("Planner must not be called"), "dame solo el JSON",
            seed: false, arrange: Focus(topicIsCurrent: false, DocumentedSyntax(), OtherSubjectArtifact()));
        Assert.Equal(200, outcome.Http);
        Assert.Equal(0, outcome.Chat.Calls);
        Assert.Equal(OtherSubjectJson, outcome.Result.Response);
        var finalized = Assert.Single(outcome.Logger.Entries, entry => entry.Fields.ContainsKey("ContextDisposition"));
        Assert.Equal("ArtifactReuse", finalized.Fields["Executor"]?.ToString());
    }

    [Fact]
    public async Task ArtifactRequestWithoutAnythingShownForTheCurrentSubjectStillUsesThePlannerAndNeverBorrowsAnotherSubject()
    {
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(UnboundConversation())), "ok dame solo el JSON",
            seed: false, arrange: Focus(topicIsCurrent: true, OtherSubjectArtifact()));
        Assert.Equal(1, outcome.Chat.Calls);
        Assert.DoesNotContain("Col3", outcome.Result.Response ?? string.Empty);
    }

    [Fact]
    public async Task FirstMeansACandidateOnlyWhileCandidatesAreOnOffer()
    {
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(UnboundConversation())), "el primero",
            seed: false, arrange: stored =>
            {
                Focus(topicIsCurrent: true, DocumentedSyntax())(stored);
                stored.SetLastCandidateSet([new("Handler", "HandlerA"), new("Handler", "HandlerB")]);
            });
        Assert.Equal(1, outcome.Chat.Calls);
        Assert.DoesNotContain(LegacySyntax, outcome.Result.Response ?? string.Empty);
    }

    [Fact]
    public async Task AConfirmedSubjectConsumesTheCandidatesSoALaterFirstCannotSwitchSubject()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await using (var stored = await fixture.Store.AcquireAsync("integration", default))
        {
            stored.SetLastCandidateSet([AdvancedHandler, new("Handler", "HTMLEnCeldaAdvanced_ControlRoom")]);
        }
        var chosen = new SemanticProposal("HandlerQuestion", null, false, null, "explanation", [], null, null, null,
            SubjectKind: "Handler", SubjectName: "HandlerA");
        var confirmed = await fixture.RunAsync(chosen, "HandlerA", isPlannerOutput: true);
        Assert.Equal("Committed", confirmed.Outcome.ContextDisposition);
        Assert.Empty((await fixture.StateAsync()).Candidates);

        var first = new SemanticProposal("HandlerQuestion", null, false, null, "explanation", [], null, null, null,
            SubjectKind: "Handler", SubjectReference: "first");
        var later = await fixture.RunAsync(first, "el primero", isPlannerOutput: true);
        Assert.Null(later.Validation.Plan);
        Assert.Equal("SubjectReferenceMissing", later.Validation.Reason);
        Assert.Equal("HandlerA", (await fixture.StateAsync()).Current?.Subject.Name);
    }

    [Fact]
    public void LegacyParameterSyntaxShownVerbatimIsKeptAsASnippetNeverAsJson()
    {
        var evidence = new[]
        {
            new KnowledgeSearchResultItem("Comentarios en las grillas", "Comentarios en las grillas.msg", "WikiData/Comentarios en las grillas.msg",
                "Se configura el handler ComentariosEnCelda con la siguiente sintaxis:\r\n\r\n" + LegacySyntax + "\r\n\r\nLuego hay que dar permisos.", 1),
            new KnowledgeSearchResultItem("Otro", "otro.md", "otro.md", "Ejemplo válido: {\"Filas\":\"Todas\"} y nada más.", 1)
        };
        var response = "El correo indica la sintaxis " + LegacySyntax + " y el ejemplo {\"Filas\":\"Todas\"}.";
        var grounded = GroundedArtifactExtractor.FromKnowledge(response, evidence, CommentsTopic.Name, CommentsTopic);
        var syntax = Assert.Single(grounded.Artifacts, artifact => artifact.Content == LegacySyntax);
        Assert.Equal(GroundedArtifactKind.Snippet, syntax.Kind);
        Assert.Equal("Comentarios en las grillas.msg: Comentarios en las grillas", syntax.Source);
        var valid = Assert.Single(grounded.Artifacts, artifact => artifact.Content == "{\"Filas\":\"Todas\"}");
        Assert.Equal(GroundedArtifactKind.Json, valid.Kind);
        Assert.Single(ConversationCapabilityRouter.FindDocumentedSyntax(grounded.Artifacts, CommentsTopic));
    }
}
