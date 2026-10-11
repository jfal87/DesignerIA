using DesignerIA.Api.Services;
using DesignerIA.Api.Services.Knowledge;
using DesignerIA.Contracts;
using Xunit;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

// Causes confirmed in artifacts\eval-100-20261010-final (G08, F11, G20) and in the weak-evidence review.
public sealed partial class PlannerFailureRegressionTests
{
    // Recorded for evaluation G08: the planner returned no requestKind and the chat asked "configure or use?".
    private static SemanticProposal ReferenceCommentsProposal(string? requestKind = null) => new(
        "KnowledgeQuestion", "comentarios en columna de referencia de grilla", false, null, "explanation",
        ["comentarios columna de referencia grilla", "ComentariosEnCelda columna referencia", "configurar comentarios en columna especifica grilla"],
        null, null, null, SubjectKind: "Topic", SubjectName: "comentarios en columna de referencia de grilla", RequestKind: requestKind);

    [Theory]
    [InlineData("se pueden poner comentarios en la columna de referencia de una grilla?")]
    [InlineData("se puede comentar sobre la referencia de una grilla")]
    [InlineData("es posible agregar comentarios en el reference")]
    [InlineData("y hay comentarios para la columna de referencia")]
    [InlineData("como funcionan los comentarios en la columna de referencia")]
    [InlineData("comentarios en la columna de referencia?")]
    public void AQuestionWithoutAnOperationIsTakenAsAnExplanationRequest(string message)
    {
        Assert.Equal("Explanation", ReferenceCommentsProposal().RecoverQuestionKind(message)?.RequestKind);
    }

    [Theory]
    [InlineData("comentarios en la columna de referencia")]
    [InlineData("la columna de referencia de una grilla")]
    [InlineData("el primero")]
    public void AMessageThatOnlyNamesASubjectStillNeedsItsOperation(string message)
    {
        Assert.Null(ReferenceCommentsProposal().RecoverQuestionKind(message));
    }

    [Fact]
    public void RecoveringTheOperationNeverTouchesHandlersArtifactsOrAnOperationThePlannerGave()
    {
        const string question = "se pueden poner comentarios en la columna de referencia de una grilla?";
        Assert.Null(ReferenceCommentsProposal("Configuration").RecoverQuestionKind(question));
        Assert.Null((ReferenceCommentsProposal() with { SubjectKind = "Handler" }).RecoverQuestionKind(question));
        // Real run (2026-10-10): for "es posible agregar comentarios en la columna reference" the planner named a Property.
        Assert.Equal("Explanation", (ReferenceCommentsProposal() with { SubjectKind = "Property" }).RecoverQuestionKind(question)?.RequestKind);
        Assert.Null((ReferenceCommentsProposal() with { Intent = "HandlerQuestion" }).RecoverQuestionKind(question));
        Assert.Null((ReferenceCommentsProposal() with { ReuseGroundedArtifact = true }).RecoverQuestionKind(question));
        Assert.Null((ReferenceCommentsProposal() with { ResponseMode = "json" }).RecoverQuestionKind(question));
        Assert.Null((ReferenceCommentsProposal() with { ForbiddenAction = true }).RecoverQuestionKind(question));
        var proposal = ReferenceCommentsProposal();
        Assert.Equal(proposal with { RequestKind = "Explanation" }, proposal.RecoverQuestionKind(question));
    }

    [Fact]
    public async Task TheRecordedReferenceQuestionReachesDocumentaryRetrievalInsteadOfAskingConfigureOrUse()
    {
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(ReferenceCommentsProposal())),
            "se pueden poner comentarios en la columna de referencia de una grilla?", seed: false);
        Assert.Equal(1, outcome.Chat.Calls);
        Assert.DoesNotContain(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Reason") as string == "RequestKindAmbiguous");
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Executor")?.ToString() == "GroundedKnowledge");
        Assert.DoesNotContain("configurar técnicamente", outcome.Result.Response ?? string.Empty);
    }

    [Fact]
    public async Task ABareSubjectWithoutAnOperationStillAsksWhatIsNeeded()
    {
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(ReferenceCommentsProposal())),
            "comentarios en la columna de referencia", seed: false);
        Assert.Contains("configurar técnicamente", outcome.Result.Response);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Reason") as string == "RequestKindAmbiguous");
    }

    // Recorded for evaluation F11: topic "series de gráfico", reference to the current subject, queries about markerSize.
    private static readonly string[] MarkerSizeQueries =
        ["markerSize tamaño marcador serie grafico", "configurar tamaño de marcador punto serie", "TerniumChartSerie markerSize"];

    [Theory]
    [InlineData("y cmo cambio el tamaño?", "current", true)]
    [InlineData("y el tamaño?", "current", true)]
    [InlineData("y como cambio el tamaño de la fuente de las etiquetas?", "current", false)]
    [InlineData("y cmo cambio el tamaño?", "Documentary", false)]
    [InlineData("y cmo cambio el tamaño?", "ExplicitOrNew", false)]
    public void OnlyAShortFollowUpOfTheCurrentSubjectBorrowsThePlannersWording(string message, string resolution, bool expected)
    {
        Assert.Equal(expected, CopilotChatService.IsEllipticalFollowUp(message, resolution, MarkerSizeQueries));
        Assert.False(CopilotChatService.IsEllipticalFollowUp(message, resolution, []));
    }

    [Fact]
    public void AShortFollowUpSearchesWithTheSubjectItContinues()
    {
        var subject = new ActiveSubject("Topic", "marcadores de series en gráfico");
        var followUp = CopilotChatService.BuildKnowledgeQueries("y cmo cambio el tamaño?", MarkerSizeQueries, "series de gráfico", subject, ellipticalFollowUp: true);
        Assert.Equal(MarkerSizeQueries[0], followUp[0]);
        Assert.Contains("MarkerSize", followUp);
        var ownWording = CopilotChatService.BuildKnowledgeQueries("y como cambio el tamaño de la fuente de las etiquetas?",
            ["FontSize etiquetas serie"], "tamaño de fuente de etiquetas", subject);
        Assert.DoesNotContain("MarkerSize", ownWording);
        Assert.DoesNotContain(ownWording, query => query.Contains("marcador", StringComparison.OrdinalIgnoreCase) && query != subject.Name);
    }

    // Real run of F11 (2026-10-10, second targeted run): active subject "series de gráfico (marcadores)". The fragment
    // that defines markerSize was tenth and the answer was about font sizes.
    [Theory]
    [InlineData("series de gráfico (marcadores)", "markerSize tamaño marcador serie", "cambiar tamaño de marcador en gráfico", "TerniumChartSerie markerSize valor")]
    [InlineData("Marcadores de serie en gráfico", "markerSize tamaño marcador serie", "configurar tamaño de marca en serie grafico", "serie1.markerSize ejemplo")]
    [InlineData("series de gráfico", "markerSize tamaño marcador serie grafico", "configurar tamaño de marcador punto serie", "TerniumChartSerie markerSize")]
    public void TheShortFollowUpAboutSizeSelectsTheFragmentsThatNameTheMarkerSize(string subject, string first, string second, string third)
    {
        const string message = "y cmo cambio el tamaño?";
        string[] planner = [first, second, third];
        var corpus = new ReadOnlyKnowledgeFixture();
        var elliptical = CopilotChatService.IsEllipticalFollowUp(message, "current", planner);
        var queries = CopilotChatService.BuildKnowledgeQueries(message, planner, subject, new ActiveSubject("Topic", subject), elliptical);
        var selected = corpus.Chat(new ConversationContextStore()).SelectEvidence(queries.SelectMany(query => corpus.Search.SearchWide(query)),
            queries, planner, elliptical, subject, subject, message, requireSubjectMatch: false, allowSubjectMembers: true, out var conflict);
        Assert.True(elliptical);
        Assert.Null(conflict);
        Assert.Contains(selected, item => item.Snippet.Contains("this.markerSize = 0", StringComparison.Ordinal));
        Assert.All(selected, item => Assert.Contains("markerSize", item.Title + " " + item.Snippet, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AnIdentifierNoSourceContainsDoesNotChangeTheSelection()
    {
        var answer = new KnowledgeSearchResultItem("Escalas", "v.html", "v.html", "AxisStart indica el inicio de escala del eje", 5);
        var selected = KnowledgeEvidencePolicy.Select([answer], null, "inicio de escala del eje", out _, preferredTerm: "AxisMinimumInventado");
        Assert.Equal([answer], selected);
    }

    [Fact]
    public async Task WithoutEvidenceTheAnswerSaysSoWithoutSourcesOrAModelCallAndKeepsVerifiedArtifacts()
    {
        var proposal = new SemanticProposal("KnowledgeQuestion", "teletransporte cuántico de una vista", false, null, "explanation",
            ["teletransporte cuantico"], null, null, null, SubjectKind: "Topic", SubjectName: "teletransporte cuántico de una vista", RequestKind: "Configuration");
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(proposal)),
            "como configuro el teletransporte cuantico de una vista", seed: false, arrange: Focus(topicIsCurrent: true, DocumentedSyntax()));
        Assert.Equal(200, outcome.Http);
        Assert.Equal(1, outcome.Chat.Calls);
        Assert.Contains("No encontré en la documentación indexada evidencia", outcome.Result.Response);
        Assert.True(outcome.Result.Sources is null or { Count: 0 });
        var timings = Assert.Single(outcome.Logger.Entries, entry => entry.Fields.ContainsKey("AnswerPromptChars"));
        Assert.Equal(0, timings.Fields["AnswerPromptChars"]);
        Assert.Equal("NoSupportingEvidence", timings.Fields["FallbackReason"]);
        await using var after = await outcome.Store.AcquireAsync(outcome.Id, default);
        var artifact = Assert.Single(after.GetGroundedArtifacts());
        Assert.Equal(LegacySyntax, artifact.Content);
        Assert.Equal("Comentarios en las grillas.msg: Comentarios en las grillas", artifact.Source);
    }

    [Fact]
    public async Task ANewConversationStartsWithoutTheSubjectOfAnotherOne()
    {
        var store = new ConversationContextStore();
        await using (var first = await store.AcquireAsync("conversation-a", default))
        {
            first.SetResolvedSubject(new ResolvedConversationSubject(new ActiveSubject("Topic", "marcadores de series en gráfico"), "KnowledgeTopic"));
            first.SetActiveKnowledge("marcadores de series en gráfico", MarkerSizeQueries);
        }
        await using var second = await store.AcquireAsync("conversation-b", default);
        Assert.Null(second.GetActiveSubject());
        Assert.Null(second.GetCurrentFocus());
        Assert.True(string.IsNullOrWhiteSpace(second.GetActiveKnowledgeTopic()));
        Assert.Empty(second.GetGroundedArtifacts());
    }

    // Real run of F11 (2026-10-10): the planner named the property and the chat answered with a "contradiction"
    // between example configurations that simply use different sizes.
    [Theory]
    [InlineData("markerSize y cmo cambio el tamaño?", false)]
    [InlineData("markerSize cual es el valor por defecto", true)]
    public void DifferentSettingsInExamplesAreAConflictOnlyWhenTheValueIsAsked(string question, bool conflict)
    {
        var one = new KnowledgeSearchResultItem("Hoja1", "Consulta Grafico.xlsx", "a.xlsx", "\"MarkerSize\": \"2\", \"MarkerType\": \"0\"", 5);
        var two = new KnowledgeSearchResultItem("Hoja1", "ConsultaTooltip.xlsx", "b.xlsx", "\"MarkerSize\": \"4\", \"MarkerType\": \"0\"", 5);
        Assert.Equal(conflict, KnowledgeEvidencePolicy.FindConflict([one, two], question) is not null);
    }

    [Fact]
    public void IdenticalTextFromSeveralFilesIsOneFragmentWithEveryProvenance()
    {
        var one = new KnowledgeSearchResultItem("Escalas", "V2.6.html", "V2.6.html", "AxisStart: inicio de escala", 3);
        var two = one with { SourceFile = "V2.8.html", RelativePath = "V2.8.html" };
        var other = new KnowledgeSearchResultItem("Gráfico", "V4.html", "V4.html", "Valor mínimo del eje", 2);
        var fragments = CopilotChatService.GroupEvidence([one, two, other]);
        Assert.Equal(2, fragments.Count);
        Assert.Equal(["V2.6.html", "V2.8.html"], fragments[0].Select(item => item.SourceFile));
    }
}
