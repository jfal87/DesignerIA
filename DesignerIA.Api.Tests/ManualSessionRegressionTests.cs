using DesignerIA.Api.Services;
using DesignerIA.Api.Services.Knowledge;
using DesignerIA.Contracts;
using Xunit;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

// Manual session of 2026-10-10 (HAR + pasted conversation). Planner proposals are the ones the SDK recorded for it.
public sealed partial class PlannerFailureRegressionTests
{
    private sealed class CorpusOnlyCatalog(KnowledgeSearchService knowledge) : IConversationSubjectCatalog
    {
        public Task<IReadOnlyList<ResolvedConversationSubject>> FindHandlersAsync(string name, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ResolvedConversationSubject>>([]);
        public IReadOnlyList<KnowledgeSearchResultItem> FindDocuments(string name) => knowledge.Search(name);
    }

    // ---- 1. "Lista de handlers" repeated eight names.
    private static GestionEngineMetadataResult Catalogue(int read, int? total) => new("HandlerActions", "List", null, total,
        Enumerable.Range(1, read).Select(id => new GestionEngineMetadataItem(id, $"Handler{id:000}", "descripción", null, 4)).ToArray());

    [Fact]
    public void TheNamesAnswerListsEveryActionThatWasReadAndSaysWhenThatIsNotAll()
    {
        var complete = CopilotChatService.FormatMetadataNames(Catalogue(126, 126)).Split('\n');
        Assert.Equal(126, complete.Length);
        Assert.Equal("Handler001", complete[0]);
        Assert.Equal("Handler126", complete[^1]);
        var partial = CopilotChatService.FormatMetadataNames(Catalogue(50, 300)).Split('\n');
        Assert.Equal(51, partial.Length);
        Assert.Equal("(lista parcial: 50 de 300)", partial[^1]);
    }

    [Fact]
    public void AListSaysWhetherItIsTheWholeCatalogueAndAPageIsNeverPresentedAsAllOfIt()
    {
        var complete = CopilotChatService.FormatMetadataResult(Catalogue(126, 126));
        Assert.Contains("Lista completa de acciones/handlers disponibles: 126", complete);
        Assert.Equal(126, complete.Split('\n').Count(line => line.StartsWith("- Handler", StringComparison.Ordinal)));
        Assert.DoesNotContain("máximo 50", complete);
        var partial = CopilotChatService.FormatMetadataResult(Catalogue(50, 300));
        Assert.Contains("Lista parcial de acciones/handlers disponibles: se muestran 50 de 300", partial);
        Assert.DoesNotContain("completa", partial);
    }

    [Fact]
    public async Task TheListAnswerIsNotCutToTheEightArtifactsTheConversationRemembers()
    {
        // Recorded: {"intent":"GestionEngineMetadata","resource":"HandlerActions","operation":"List","responseMode":"names"}
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var proposal = new SemanticProposal("GestionEngineMetadata", null, false, null, "names", [], "HandlerActions", "List", null,
            Goal: "Listar los handlers disponibles en GestionEngine");
        var catalogue = Catalogue(126, 126);
        var result = await fixture.RunAsync(proposal, "pasame una lista de los HAndlers disppinibles", (_, staged) =>
        {
            var answer = CopilotChatService.FormatMetadataNames(catalogue);
            staged.SetGroundedArtifacts(CopilotChatService.GetDisplayedLiveNames(catalogue, answer));
            return Task.FromResult(new ChatResult("ok", answer, true, null));
        }, isPlannerOutput: true);
        Assert.Equal("ok", result.Outcome.Result.Status);
        Assert.Equal(126, result.Outcome.Result.Response!.Split('\n').Length);
        Assert.InRange((await fixture.StateAsync()).Artifacts.Count, 1, 8);
    }

    // ---- 3. The modal message: documented functionality, unconfirmed name.
    private static SemanticProposal ModalProposal(string kind, string name, string topic, string requestKind, params string[] queries) =>
        new("KnowledgeQuestion", topic, true, null, "explanation", queries, null, null, null,
            SubjectKind: kind, SubjectName: name, RequestKind: requestKind);

    private static readonly SemanticProposal MensageModal = ModalProposal("Function", "MensageModal", "modal popup al iniciar la vista", "Search",
        "MensajeModal popup inicio vista", "modal popup al cargar vista GestionEngine", "MessageModal funcion mostrar modal");

    // Two recorded proposals for the same message. With the first the long conversational message left the best
    // fragment at 12% of the question and nothing was cited; with the second ("inicio" instead of "iniciar") that
    // fragment was dropped and the answer was only about abrirPopUp. Both must select the mail that explains the steps.
    [Theory]
    [InlineData("modal popup al iniciar la vista", "modal popup inicio de vista", "ventana emergente al cargar vista", "mostrar popup al entrar a la vista", "modalActivo")]
    [InlineData("modal popup al inicio de la vista", "modal popup inicio vista GestionEngine", "mostrar ventana modal al cargar vista", "popup automático al entrar a la vista", "modalActivo")]
    public void TheFunctionalQuestionAboutAModalOnOpeningAViewReachesItsDocumentation(string topic, string first, string second, string third, string marker)
    {
        const string message = "mmmmm si ahora que insistes, quiero meter un modal al incicio de mi vista que cuando entren me salga un modal popUp, sabes si tenemos algo de eso?";
        string[] planner = [first, second, third];
        var corpus = new ReadOnlyKnowledgeFixture();
        var queries = CopilotChatService.BuildKnowledgeQueries(message, planner, topic, new ActiveSubject("Topic", topic));
        var selected = corpus.Chat(new ConversationContextStore()).SelectEvidence(queries.SelectMany(query => corpus.Search.SearchWide(query)),
            queries, planner, false, topic, topic, message, requireSubjectMatch: false, allowSubjectMembers: true, out _);
        Assert.Contains(selected, item => item.Snippet.Contains("_MensajeModal", StringComparison.Ordinal)
            && item.Snippet.Contains(marker, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("algo como MensageModal ?")]
    [InlineData("si es algo MesageModal que le asocias un SP pero no recuerdo como actiuvaarlo")]
    [InlineData("quiero meter un modal al inicio de mi vista")]
    public void TheModalIsSearchedByTheIdentifiersItsDocumentationUses(string message)
    {
        var reworded = Assert.Single(RetrievalQueryExpansion.Reword(message));
        Assert.Contains("MensajeModal modalActivo", reworded);
        var results = new ReadOnlyKnowledgeFixture().Search.SearchWide(reworded);
        Assert.Contains(results, item => item.SourceFile == "Detalle mensaje modal.msg" && item.Snippet.Contains("modalActivo", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("como abro un popup con otra vista")]
    [InlineData("que modalidad de cache existe")]
    public void OtherQuestionsAreNotRewordedAsTheModal(string message)
    {
        Assert.Empty(RetrievalQueryExpansion.Reword(message));
    }

    [Fact]
    public void ATopicItsFragmentsBarelyTouchIsStillNotEvidence()
    {
        var onlyTheCommonWord = new KnowledgeSearchResultItem("Vista", "vista.msg", "vista.msg", "la vista se abre desde el menu de la vista", 9);
        Assert.Empty(KnowledgeEvidencePolicy.Select([onlyTheCommonWord], null,
            "teletransporte cuántico de una vista mmm ahora que insistes quiero saber si tenemos algo de eso", out _,
            topic: "teletransporte cuántico de una vista"));
    }

    [Fact]
    public void AnUnconfirmedFunctionOrPropertyBecomesItsTopicAndNothingElseDoes()
    {
        var asTopic = MensageModal.AsDocumentaryTopic()!;
        Assert.Equal(("Topic", "modal popup al iniciar la vista", "Explanation"), (asTopic.SubjectKind, asTopic.SubjectName, asTopic.RequestKind));
        Assert.Equal(MensageModal.SearchQueries, asTopic.SearchQueries);
        var property = ModalProposal("Property", "MessageModal", "MessageModal con SP asociado - activación", "Use", "MessageModal SP asociado configuracion");
        Assert.Equal(("Topic", "Use"), (property.AsDocumentaryTopic()!.SubjectKind, property.AsDocumentaryTopic()!.RequestKind));
        Assert.Null((MensageModal with { SubjectKind = "Handler" }).AsDocumentaryTopic());
        Assert.Null((MensageModal with { Topic = "mensagemodal" }).AsDocumentaryTopic());
        Assert.Null((MensageModal with { Topic = null }).AsDocumentaryTopic());
        Assert.Null((MensageModal with { ResponseMode = "json" }).AsDocumentaryTopic());
        Assert.Null((MensageModal with { ReuseGroundedArtifact = true }).AsDocumentaryTopic());
        Assert.Null((MensageModal with { Intent = "HandlerQuestion" }).AsDocumentaryTopic());
    }

    [Fact]
    public async Task AnApproximateNameIsAnsweredFromTheDocumentedTopicWithoutConfirmingTheName()
    {
        var knowledge = new ReadOnlyKnowledgeFixture().Search;
        var prompt = string.Empty;
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(MensageModal)), "algo como MensageModal ?",
            catalog: new CorpusOnlyCatalog(knowledge), seed: false,
            answer: received => { prompt = received; return "FUENTES: 1\nEl modal se activa con el sufijo _MensajeModal."; });
        Assert.Equal(200, outcome.Http);
        Assert.Equal(1, outcome.Chat.AnswerCalls);
        Assert.Contains("_MensajeModal", prompt);
        Assert.StartsWith("No pude confirmar «MensageModal» como handler, función ni propiedad con ese nombre exacto.", outcome.Result.Response);
        Assert.Contains("El modal se activa con el sufijo _MensajeModal.", outcome.Result.Response);
        Assert.NotEmpty(outcome.Result.Sources!);
        await using var stored = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.Equal(new ActiveSubject("Topic", "modal popup al iniciar la vista"), stored.GetCurrentFocus()?.Subject);
        Assert.DoesNotContain(stored.GetGroundedArtifacts(), artifact => artifact.OriginatingSubject?.Name == "MensageModal");
    }

    [Fact]
    public async Task AReuseFlagOnAnExplanationIsAnsweredAsAQuestionInsteadOfFailingTheContract()
    {
        // Recorded on the real SDK for "algo como MensageModal ?" (HTTP 502, ArtifactRequestIncompatible).
        var recorded = new SemanticProposal("KnowledgeQuestion", "modal popup al iniciar la vista", true, null, "explanation", [], null, null, null,
            SubjectReference: "current", RequestKind: "Parameters", ReuseGroundedArtifact: true);
        var topic = new ActiveSubject("Topic", "modal popup al iniciar la vista");
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(recorded)), "algo como MensageModal ?", seed: false,
            arrange: stored =>
            {
                stored.SetResolvedSubject(new ResolvedConversationSubject(topic, "KnowledgeTopic"));
                stored.AddTurn("quiero meter un modal al inicio de mi vista", "respuesta documental previa");
            },
            answer: _ => "FUENTES: 1\nEl SP lleva el sufijo _MensajeModal.");
        Assert.Equal(200, outcome.Http);
        Assert.Equal("ok", outcome.Result.Status);
        Assert.Equal(1, outcome.Chat.AnswerCalls);
        Assert.Contains("_MensajeModal", outcome.Result.Response);
        Assert.DoesNotContain(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Reason") as string == "ArtifactRequestIncompatible");
    }

    [Fact]
    public void AReuseFlagIsOnlyDroppedForAnExplanation()
    {
        var reuse = new SemanticProposal("KnowledgeQuestion", null, true, null, "json", [], null, null, null,
            SubjectReference: "current", ReuseGroundedArtifact: true);
        Assert.Null(reuse.AsExplanationWithoutReuse());
        Assert.Null((reuse with { ResponseMode = "names" }).AsExplanationWithoutReuse());
        Assert.Null((reuse with { ResponseMode = "explanation", Intent = "GeneralQuestion" }).AsExplanationWithoutReuse());
        Assert.Null((reuse with { ResponseMode = "explanation", ForbiddenAction = true }).AsExplanationWithoutReuse());
        Assert.False((reuse with { ResponseMode = "explanation" }).AsExplanationWithoutReuse()!.ReuseGroundedArtifact);
    }

    [Fact]
    public async Task AHandlerNameThatExistsNowhereStillCannotBeConfirmed()
    {
        // Evaluation G24: nothing to fall back to, so no explanation is produced.
        var invented = new SemanticProposal("HandlerQuestion", "pintarCeldaArcoiris", false, null, "explanation", ["pintarCeldaArcoiris"], null, null, null,
            SubjectKind: "Handler", SubjectName: "pintarCeldaArcoiris", RequestKind: "Explanation");
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(invented)), "que hace el handler pintarCeldaArcoiris",
            catalog: new CorpusOnlyCatalog(new ReadOnlyKnowledgeFixture().Search), seed: false);
        Assert.Equal(0, outcome.Chat.AnswerCalls);
        Assert.Contains("No pude confirmar el sujeto", outcome.Result.Response);
    }

    // ---- 4. Several verified JSON of the same handler.
    private const string TextHandler = "AgregarTextoEnValorAdvanced";
    private static readonly string[] TextExamples =
    [
        "{\"Texto\":\"%\",\"Posicion\":\"I\",\"Columnas\":\"1|*|2|*|3\", \"Filas\":\"fila-1|*|fila-2|*|fila-3\"}",
        "{\"Texto\":\"%\",\"Posicion\":\"I\",\"Columnas\":\"col-1|*| col-2|*| col-3\", \"Filas\":\"fila-1|*|fila-2|*|fila-3\"}",
        "{\"Texto\":\"%\",\"Posicion\":\"I\",\"Columnas\":\"Todas\", \"Filas\":\"Todas\"}"
    ];

    private static Action<ConversationLease> ThreeExamplesShown() => stored =>
    {
        stored.SetResolvedSubject(SubjectCatalogFixture.Handler(TextHandler, 591));
        stored.AddTurn("este para que es AgregarTextoEnValorAdvanced tienes info de esto?", "respuesta con la descripción del catálogo");
        stored.SetGroundedArtifacts(TextExamples.Select(json => new GroundedArtifact(GroundedArtifactKind.Json, json, "Live",
            "AccionesHandler: 591", TextHandler, TextHandler, new ActiveSubject("Handler", TextHandler))));
    };

    private static readonly SubjectCatalogFixture TextCatalog = new() { Handlers = [SubjectCatalogFixture.Handler(TextHandler, 591)] };

    [Fact]
    public async Task SeveralVerifiedJsonAreOfferedByContentAndSourceInsteadOfThreeIdenticalLabels()
    {
        // Recorded: HandlerQuestion, subjectReference current, responseMode json, requestKind Parameters, reuseGroundedArtifact true.
        var reuse = new SemanticProposal("HandlerQuestion", TextHandler, true, null, "json", [], null, null, null,
            SubjectKind: "Handler", SubjectReference: "current", RequestKind: "Parameters", ReuseGroundedArtifact: true);
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(reuse)), "dame el json del ultimo handler que pla ticamos",
            catalog: TextCatalog, seed: false, arrange: ThreeExamplesShown());
        var text = outcome.Result.Response!;
        Assert.Equal(200, outcome.Http);
        Assert.DoesNotContain("Json: AgregarTextoEnValorAdvanced; Json:", text);
        Assert.Contains("Tengo 3 ejemplos verificados de «AgregarTextoEnValorAdvanced»", text);
        for (var index = 0; index < TextExamples.Length; index++)
        {
            Assert.Contains($"{index + 1}. Fuente: GestionEngine real, AccionesHandler: 591", text);
            Assert.Contains(TextExamples[index], text);
        }
        await using var stored = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.Equal(3, stored.GetGroundedArtifacts().Count);
    }

    [Fact]
    public async Task AReuseRequestThatAlsoCarriesQueriesOffersTheVerifiedExamplesInsteadOfFailingTheContract()
    {
        // Recorded on the real SDK for the same message (HTTP 502, ArtifactRequestIncompatible): reuse + json + two queries.
        var recorded = new SemanticProposal("HandlerQuestion", TextHandler, true, null, "json",
            ["AgregarTextoEnValorAdvanced json", "AgregarTextoEnValorAdvanced parametros"], null, null, null,
            SubjectKind: "Handler", SubjectReference: "current", RequestKind: "Evidence", ReuseGroundedArtifact: true);
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(recorded)), "dame el json del ultimo handler que pla ticamos",
            catalog: TextCatalog, seed: false, arrange: ThreeExamplesShown());
        Assert.Equal(200, outcome.Http);
        Assert.Equal(0, outcome.Chat.AnswerCalls);
        Assert.Contains("Tengo 3 ejemplos verificados de «AgregarTextoEnValorAdvanced»", outcome.Result.Response);
        Assert.Contains(TextExamples[2], outcome.Result.Response);
        Assert.DoesNotContain(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Reason") as string == "ArtifactRequestIncompatible");
        Assert.Null(recorded.AsExplanationWithoutReuse());
        Assert.Null((recorded with { SearchQueries = [] }).AsReuseWithoutQueries());
        Assert.Null((recorded with { ReuseGroundedArtifact = false }).AsReuseWithoutQueries());
    }

    // Manual sequence: what is the handler for -> its JSON in MultiHandler (absence, turn discarded) -> "Hola" ->
    // "Dame el JSON del último handler que platicamos". Recorded on the real SDK: the planner referenced "previous"
    // although the handler was the only confirmed subject, and the chat answered "No pude confirmar el sujeto".
    private static readonly SemanticProposal LastHandlerAsPrevious = new("HandlerQuestion", TextHandler, true, null, "json", [], null, null, null,
        SubjectKind: "Handler", SubjectReference: "previous", RequestKind: "Evidence", ReuseGroundedArtifact: true);

    [Fact]
    public async Task AfterAnAbsenceAndAGreetingTheLastConfirmedHandlerIsStillTheOneAskedAbout()
    {
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(LastHandlerAsPrevious)), "Dame el JSON del último handler que platicamos",
            catalog: TextCatalog, seed: false, firstMessage: "Hola", arrange: ThreeExamplesShown());
        Assert.Equal(200, outcome.Http);
        Assert.Equal(1, outcome.Chat.Calls);
        Assert.DoesNotContain("No pude confirmar el sujeto", outcome.Result.Response);
        Assert.Contains("Tengo 3 ejemplos verificados de «AgregarTextoEnValorAdvanced»", outcome.Result.Response);
        foreach (var example in TextExamples) { Assert.Contains(example, outcome.Result.Response); }
        Assert.DoesNotContain(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Reason") as string == "SubjectReferenceMissing");
        await using var stored = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.Equal(new ActiveSubject("Handler", TextHandler), stored.GetCurrentFocus()?.Subject);
        Assert.Null(stored.GetPreviousFocus());
        Assert.Equal(3, stored.GetGroundedArtifacts().Count);
    }

    // The confirmed subject may be named in any of the three fields, and the kind may be omitted.
    [Theory]
    [InlineData(TextHandler, null, null, "Handler")]
    [InlineData(null, TextHandler, null, "Handler")]
    [InlineData(null, null, TextHandler, "Handler")]
    [InlineData("agregartextoenvaloradvanced", null, null, null)]
    public async Task TheConfirmedHandlerIsRecoveredHoweverThePlannerNamesIt(string? topic, string? subjectName, string? handler, string? kind)
    {
        var proposal = LastHandlerAsPrevious with { Topic = topic, SubjectName = subjectName, Handler = handler, SubjectKind = kind };
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(proposal)), "Dame el JSON del último handler que platicamos",
            catalog: TextCatalog, seed: false, arrange: ThreeExamplesShown());
        Assert.Equal(200, outcome.Http);
        Assert.Contains("Tengo 3 ejemplos verificados de «AgregarTextoEnValorAdvanced»", outcome.Result.Response);
        await using var stored = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.Equal(new ActiveSubject("Handler", TextHandler), stored.GetCurrentFocus()?.Subject);
    }

    [Fact]
    public async Task ANewConversationDoesNotInheritThatHandler()
    {
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(LastHandlerAsPrevious)), "Dame el JSON del último handler que platicamos",
            catalog: TextCatalog, seed: false);
        Assert.Contains("No pude confirmar el sujeto", outcome.Result.Response);
        Assert.DoesNotContain("Texto", outcome.Result.Response);
        await using var stored = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.Null(stored.GetCurrentFocus());
        Assert.Empty(stored.GetGroundedArtifacts());
    }

    [Theory]
    [InlineData("OtroHandler", "Handler")]
    [InlineData(null, "Handler")]
    [InlineData(TextHandler, "Function")]
    public async Task AMissingPreviousSubjectIsOnlyRecoveredWhenThePlannerNamesTheConfirmedOne(string? topic, string kind)
    {
        var proposal = LastHandlerAsPrevious with { Topic = topic, SubjectKind = kind };
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(proposal)), "Dame el JSON del último handler que platicamos",
            catalog: TextCatalog, seed: false, arrange: ThreeExamplesShown());
        Assert.Contains("No pude confirmar el sujeto", outcome.Result.Response);
        Assert.DoesNotContain(TextExamples[0], outcome.Result.Response);
    }

    [Theory]
    [InlineData("el 2", 1)]
    [InlineData("el segundo", 1)]
    [InlineData("dame el json 3", 2)]
    [InlineData("el primero", 0)]
    public async Task TheUsersChoiceReturnsExactlyThatExampleWithoutThePlanner(string message, int expected)
    {
        var outcome = await RunAsync((_, _) => throw new InvalidOperationException("Planner must not be called"), message,
            catalog: TextCatalog, seed: false, arrange: ThreeExamplesShown());
        Assert.Equal(200, outcome.Http);
        Assert.Equal(0, outcome.Chat.Calls);
        Assert.Equal(TextExamples[expected], outcome.Result.Response);
    }

    [Theory]
    [InlineData("el 7")]
    [InlineData("el segundo handler de la lista")]
    public async Task AChoiceOutsideTheOptionsOrWithOtherWordsIsNotTakenAsOne(string message)
    {
        var store = new ConversationContextStore();
        await using var lease = await store.AcquireAsync("choice", default);
        ThreeExamplesShown()(lease);
        Assert.Null(CopilotChatService.TryCreateLocalArtifactRequest(message, lease, out var choice));
        Assert.Null(choice);
    }

    [Fact]
    public async Task AMultiHandlerJsonThatNothingVerifiesIsAnAnswerAndNotAServiceFailure()
    {
        var corpus = new ReadOnlyKnowledgeFixture();
        var fixture = new ConversationIntegrationFixture(new SubjectCatalogFixture { Handlers = [SubjectCatalogFixture.Handler(TextHandler, 591)] });
        var chat = corpus.Chat(fixture.Store);
        const string message = "ok dame solo el json de este pero ojo lo quiero en Multihandler";
        var proposal = new SemanticProposal("HandlerQuestion", TextHandler, true, null, "json",
            ["AgregarTextoEnValorAdvanced MultiHandler json ejemplo"], null, null, null,
            SubjectKind: "Handler", SubjectName: TextHandler, RequestKind: "Examples");
        var live = new HandlerExamplesResult(TextHandler, true, TextHandler, "Inserta el texto deseado al valor de la celda.", null, 591, 4, 0, 0, []);
        var timings = new CopilotChatService.ChatTimings();
        ChatResult? answer = null;
        await fixture.RunAsync(proposal, message, (decision, staged) =>
        {
            answer = chat.BuildTechnicalEvidenceResult(message, TextHandler, staged, "json", live, timings, "Examples", decision.SearchQueries);
            return Task.FromResult(answer);
        });
        // The reason travels with the turn in the service; here the executor's own result is what is checked.
        Assert.Equal("ok", answer!.Status);
        Assert.Null(answer.Error);
        Assert.Contains("No tengo un JSON verificado de «AgregarTextoEnValorAdvanced» con MultiHandler", answer.Response);
        Assert.Contains("no voy a fabricarlo", answer.Response);
        Assert.DoesNotContain("{", answer.Response);
        Assert.Equal("GroundedArtifactMissing", timings.FallbackReason);
    }

    // ---- 6. Informal vocative.
    [Theory]
    [InlineData("Quien eres brooo")]
    [InlineData("quién eres bro?")]
    [InlineData("oye bro quien eres")]
    [InlineData("que sabes hacer amigo")]
    public void AnIdentityQuestionWithAVocativeIsAnsweredAsIdentity(string message)
    {
        Assert.Equal(DesignerCapabilities.Describe(), SocialConversation.Respond(message, KnowledgeSearchService.Normalize(message)));
    }

    [Theory]
    [InlineData("gracias bro", SocialConversation.Thanks)]
    [InlineData("hola broooo", SocialConversation.Greeting)]
    public void AVocativeDoesNotChangeASocialMessage(string message, string expected)
    {
        Assert.Equal(expected, SocialConversation.Respond(message, KnowledgeSearchService.Normalize(message)));
    }

    [Theory]
    [InlineData("hola bro, explícame este handler")]
    [InlineData("bro explicame HTMLEnCeldaAdvanced")]
    [InlineData("quien eres bro y que hace ColorCelda")]
    [InlineData("bro")]
    public void AVocativeNeverAbsorbsACompoundOrEmptyRequest(string message)
    {
        Assert.Null(SocialConversation.Respond(message, KnowledgeSearchService.Normalize(message)));
    }
}
