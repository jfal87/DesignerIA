using System.Text.Json;
using DesignerIA.Api.Services;
using DesignerIA.Contracts;
using Xunit;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

public sealed partial class PlannerFailureRegressionTests
{
    private const string Clarification = "Necesito un poco más de detalle. ¿Qué quieres consultar sobre programación o GestionEngine?";

    private static SemanticProposal UnboundConversation() =>
        new("Conversation", null, false, null, "explanation", [], null, null, null);

    private static string ProposalJson(SemanticProposal proposal) =>
        JsonSerializer.Serialize(proposal, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Theory]
    [InlineData("HTMLAdvanced")]
    [InlineData("OtherTechnicalIdentifier")]
    [InlineData("No sé por dónde empezar")]
    public async Task UnboundPlannerProposalReturnsControlledClarificationThroughHttpAndCommitsOnlyTheTurn(string message)
    {
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(UnboundConversation())), message);
        Assert.Equal(200, outcome.Http);
        Assert.Equal("ok", outcome.Result.Status);
        Assert.Null(outcome.Result.ErrorKind);
        Assert.Equal(Clarification, outcome.Result.Response);
        Assert.False(outcome.Result.ToolInvoked);
        Assert.Equal(1, outcome.Chat.Calls);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Status")?.ToString() == "NotFound");
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Accepted") is true);
        var finalized = Assert.Single(outcome.Logger.Entries, entry => entry.Fields.ContainsKey("ContextDisposition"));
        Assert.Equal("Committed", finalized.Fields["ContextDisposition"]);
        Assert.Equal("ConversationResponse", finalized.Fields["Executor"]?.ToString());
        await using var stored = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.Equal("HandlerB", stored.GetCurrentFocus()?.Subject.Name);
        Assert.Equal("Examples", stored.GetCurrentFocus()?.RequestKind);
        Assert.Equal("HandlerA", stored.GetPreviousFocus()?.Subject.Name);
        Assert.Equal(new ActiveSubject("Handler", "HandlerB"), stored.GetSelectedEntity());
        Assert.Equal(2, stored.GetLastCandidateSet().Count);
        Assert.Equal("{}", Assert.Single(stored.GetGroundedArtifacts()).Content);
        Assert.Null(stored.GetActiveKnowledgeTopic());
        Assert.Null(stored.GetActiveMetadataResource());
        Assert.Contains("Usuario anterior: " + message, stored.BuildPrompt("next"));
        Assert.Contains("Respuesta anterior: " + Clarification, stored.BuildPrompt("next"));
    }

    [Theory]
    [InlineData("Texto arbitrario que afirma haber ejecutado SQL")]
    [InlineData(Sensitive)]
    public async Task PlannerTextIsNeitherPublishedLoggedNorCommittedInAnAuthorizedClarification(string arbitrary)
    {
        var proposal = UnboundConversation() with { GeneralAnswer = arbitrary, Goal = arbitrary };
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(proposal)), "Necesito orientación");
        Assert.Equal(200, outcome.Http);
        Assert.Equal(Clarification, outcome.Result.Response);
        Assert.All(outcome.Logger.Entries, entry => Assert.DoesNotContain(arbitrary, entry.Text));
        await using var stored = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.DoesNotContain(arbitrary, State(stored));
    }

    [Theory]
    [InlineData("Hola")]
    [InlineData("Gracias")]
    [InlineData("Tengo una duda")]
    public async Task LegitimateLocalConversationStillCommitsWithoutCallingThePlanner(string message)
    {
        var outcome = await RunAsync((_, _) => throw new InvalidOperationException("Planner must not be called"), message);
        Assert.Equal(200, outcome.Http);
        Assert.Equal(0, outcome.Chat.Calls);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("ContextDisposition") as string == "Committed");
    }

    [Theory]
    [InlineData("subject", "ConversationSubjectIncompatible")]
    [InlineData("operation", "UntrustedPlannerConversation")]
    [InlineData("queries", "UntrustedPlannerConversation")]
    [InlineData("format", "UntrustedPlannerConversation")]
    [InlineData("metadata", "MetadataFieldsOutsideCapability")]
    public async Task ConversationCannotHideTechnicalFieldsOrContaminateContext(string field, string reason)
    {
        var proposal = field switch
        {
            "subject" => UnboundConversation() with { SubjectKind = "Handler", SubjectName = "HandlerB" },
            "operation" => UnboundConversation() with { RequestKind = "Parameters" },
            "reference" => UnboundConversation() with { SubjectReference = "current" },
            "previous" => UnboundConversation() with { ReferencesPreviousTopic = true },
            "queries" => UnboundConversation() with { SearchQueries = ["HandlerB"] },
            "topic" => UnboundConversation() with { Topic = "HandlerB" },
            "format" => UnboundConversation() with { ResponseMode = "json" },
            _ => UnboundConversation() with { Resource = "HandlerActions" }
        };
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(proposal)), "Dame los parámetros de HandlerB");
        Assert.Equal(502, outcome.Http);
        Assert.Equal("semantic", outcome.Result.ErrorKind);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Reason") as string == reason);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("ContextDisposition") as string == "Discarded");
        Assert.Equal(1, outcome.Chat.Calls);
    }

    // Planner output recorded by the SDK for "Que te paso ?" after a documentary answer: a social remark decorated with context pointers.
    private const string SocialRemarkProposalJson =
        "{\"intent\":\"Conversation\",\"topic\":\"comentarios en grilla\",\"searchQueries\":[],\"resource\":null,\"operation\":null,"
        + "\"search\":null,\"referencesPreviousTopic\":true,\"handler\":null,\"responseMode\":\"explanation\",\"generalAnswer\":null,"
        + "\"subjectKind\":null,\"subjectName\":null,\"subjectReference\":\"previous\",\"requestKind\":null,"
        + "\"reuseGroundedArtifact\":false,\"forbiddenAction\":false,\"goal\":null}";

    [Theory]
    [InlineData("recorded")]
    [InlineData("reference")]
    [InlineData("previous")]
    [InlineData("topic")]
    public async Task ContextPointersOnAPlannerConversationAreDiscardedAndOnlyTheFixedClarificationIsPublished(string field)
    {
        const string arbitrary = "Texto arbitrario del modelo";
        var proposal = (field switch
        {
            "recorded" => SemanticProposal.Parse(SocialRemarkProposalJson),
            "reference" => UnboundConversation() with { SubjectReference = "current" },
            "previous" => UnboundConversation() with { ReferencesPreviousTopic = true },
            _ => UnboundConversation() with { Topic = "HandlerB" }
        }) with { GeneralAnswer = arbitrary, Goal = arbitrary };
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(proposal)), "Que te paso ?");
        Assert.Equal(200, outcome.Http);
        Assert.Equal(Clarification, outcome.Result.Response);
        Assert.Equal(1, outcome.Chat.Calls);
        var finalized = Assert.Single(outcome.Logger.Entries, entry => entry.Fields.ContainsKey("ContextDisposition"));
        Assert.Equal("Committed", finalized.Fields["ContextDisposition"]);
        Assert.Equal("ConversationResponse", finalized.Fields["Executor"]?.ToString());
        Assert.All(outcome.Logger.Entries, entry => Assert.DoesNotContain(arbitrary, entry.Text));
        await using var stored = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.Equal("HandlerB", stored.GetCurrentFocus()?.Subject.Name);
        Assert.Equal("Examples", stored.GetCurrentFocus()?.RequestKind);
        Assert.Equal("HandlerA", stored.GetPreviousFocus()?.Subject.Name);
        Assert.Equal(2, stored.GetLastCandidateSet().Count);
        Assert.Equal("{}", Assert.Single(stored.GetGroundedArtifacts()).Content);
        Assert.DoesNotContain(arbitrary, State(stored));
    }

    [Fact]
    public async Task GeneralTopicChangeUsesItsAuthorizedAnswerAndPreservesTechnicalFocus()
    {
        var proposal = UnboundConversation() with { Intent = "GeneralQuestion", SubjectKind = "General", GeneralAnswer = "Una lista almacena elementos ordenados." };
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(proposal)), "Cambiemos de tema: explica List en C#");
        Assert.Equal(200, outcome.Http);
        Assert.Equal(proposal.GeneralAnswer, outcome.Result.Response);
        await using var stored = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.Equal("HandlerB", stored.GetCurrentFocus()?.Subject.Name);
        Assert.Contains(proposal.GeneralAnswer, stored.BuildPrompt("next"));
    }

    [Theory]
    [InlineData("texto sin JSON", "PlannerNoJson")]
    [InlineData("{invalid}", "InvalidProposalJson")]
    [InlineData("{\"intent\":\"Conversation\"}", "InvalidProposalJson")]
    [InlineData("{\"intent\":\"Conversation\",\"responseMode\":\"explanation\",\"referencesPreviousTopic\":false,\"searchQueries\":[],\"executor\":\"ExecuteSql\"}", "InvalidProposalJson")]
    public async Task ClarificationDoesNotTurnParsingFailuresIntoSuccess(string output, string reason)
    {
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(output), "HTMLAdvanced");
        Assert.Equal(502, outcome.Http);
        Assert.Null(outcome.Result.Response);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Reason") as string == reason);
    }

    // Planner output recorded by the SDK for the incident: new conversation, "buenas" then "HTMLAdvanced".
    private const string HistoricalIdentifier = "HTMLAdvanced";
    private const string HistoricalProposalJson =
        "{\"intent\":\"Conversation\",\"topic\":\"HTMLAdvanced\",\"searchQueries\":[],\"resource\":null,\"operation\":null,"
        + "\"search\":null,\"referencesPreviousTopic\":false,\"handler\":null,\"responseMode\":\"explanation\",\"generalAnswer\":null,"
        + "\"subjectKind\":null,\"subjectName\":\"HTMLAdvanced\",\"subjectReference\":null,\"requestKind\":null,"
        + "\"reuseGroundedArtifact\":false,\"forbiddenAction\":false,\"goal\":null}";

    private static SemanticProposal HistoricalProposal() => SemanticProposal.Parse(HistoricalProposalJson);

    private static SubjectCatalogFixture ApproximateCatalog() => new()
    {
        Handlers = [SubjectCatalogFixture.Handler("HTMLEnCeldaAdvanced", 600), SubjectCatalogFixture.Handler("HTMLEnCeldaAdvanced_ControlRoom", 621)]
    };

    private static bool WasRecovered(RecordingLogger logger) =>
        logger.Entries.Any(entry => entry.Text.Contains("recovered as identifier subject", StringComparison.Ordinal));

    [Fact]
    public async Task HistoricalConversationNamingTheUserIdentifierAsksWithCatalogCandidatesWithoutConfirmingIdentity()
    {
        var catalog = ApproximateCatalog();
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(HistoricalProposalJson), HistoricalIdentifier,
            catalog, seed: false, firstMessage: "buenas");
        Assert.Equal(200, outcome.Http);
        Assert.Equal("ok", outcome.Result.Status);
        Assert.Null(outcome.Result.ErrorKind);
        Assert.Contains("HTMLEnCeldaAdvanced (", outcome.Result.Response);
        Assert.Contains("HTMLEnCeldaAdvanced_ControlRoom", outcome.Result.Response);
        Assert.False(outcome.Result.ToolInvoked);
        Assert.Equal(1, outcome.Chat.Calls);
        Assert.Equal(1, catalog.LiveLookups);
        Assert.True(WasRecovered(outcome.Logger));
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Reason") as string == "SubjectAmbiguous");
        var finalized = Assert.Single(outcome.Logger.Entries, entry => entry.Fields.ContainsKey("ContextDisposition"));
        Assert.Equal("CandidatesOnly", finalized.Fields["ContextDisposition"]);
        Assert.Null(finalized.Fields["Executor"]);
        await using var stored = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.Null(stored.GetCurrentFocus());
        Assert.Null(stored.GetActiveSubject());
        Assert.Null(stored.GetSelectedEntity());
        Assert.Empty(stored.GetGroundedArtifacts());
        Assert.Equal([new ActiveSubject("Handler", "HTMLEnCeldaAdvanced"), new ActiveSubject("Handler", "HTMLEnCeldaAdvanced_ControlRoom")],
            stored.GetLastCandidateSet());
        Assert.DoesNotContain("Usuario anterior: " + HistoricalIdentifier, stored.BuildPrompt("next"));
    }

    [Fact]
    public async Task RecoveredIdentifierNeverPublishesLogsOrCommitsPlannerText()
    {
        const string arbitrary = "Texto arbitrario que afirma haber ejecutado SQL";
        var proposal = HistoricalProposal() with { GeneralAnswer = arbitrary, Goal = arbitrary };
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(proposal)), HistoricalIdentifier,
            ApproximateCatalog(), seed: false);
        Assert.Equal(200, outcome.Http);
        Assert.True(WasRecovered(outcome.Logger));
        Assert.DoesNotContain(arbitrary, outcome.Result.Response);
        Assert.All(outcome.Logger.Entries, entry => Assert.DoesNotContain(arbitrary, entry.Text));
        await using var stored = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.DoesNotContain(arbitrary, State(stored));
    }

    [Theory]
    [InlineData("operation", "UntrustedPlannerConversation")]
    [InlineData("reference", "UntrustedPlannerConversation")]
    [InlineData("previous", "UntrustedPlannerConversation")]
    [InlineData("queries", "UntrustedPlannerConversation")]
    [InlineData("format", "UntrustedPlannerConversation")]
    [InlineData("handler", "UntrustedPlannerConversation")]
    [InlineData("otherName", "UntrustedPlannerConversation")]
    [InlineData("topicMismatch", "UntrustedPlannerConversation")]
    [InlineData("metadata", "MetadataFieldsOutsideCapability")]
    [InlineData("technicalKind", "ConversationSubjectIncompatible")]
    [InlineData("reuse", "ArtifactRequestIncompatible")]
    public async Task IncompatibleFieldsDisableIdentifierRecoveryAndKeepTheRejection(string field, string reason)
    {
        var proposal = field switch
        {
            "operation" => HistoricalProposal() with { RequestKind = "Parameters" },
            "reference" => HistoricalProposal() with { SubjectReference = "current" },
            "previous" => HistoricalProposal() with { ReferencesPreviousTopic = true },
            "queries" => HistoricalProposal() with { SearchQueries = [HistoricalIdentifier] },
            "format" => HistoricalProposal() with { ResponseMode = "json" },
            "handler" => HistoricalProposal() with { Handler = HistoricalIdentifier },
            "otherName" => HistoricalProposal() with { SubjectName = "HTMLEnCeldaAdvanced", Topic = null },
            "topicMismatch" => HistoricalProposal() with { Topic = "HTMLEnCeldaAdvanced" },
            "metadata" => HistoricalProposal() with { Resource = "HandlerActions" },
            "reuse" => HistoricalProposal() with { ReuseGroundedArtifact = true },
            _ => HistoricalProposal() with { SubjectKind = "Handler" }
        };
        var catalog = ApproximateCatalog();
        // RunAsync also asserts that the seeded conversation state is unchanged after an error.
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(proposal)), HistoricalIdentifier, catalog);
        Assert.Equal(502, outcome.Http);
        Assert.Equal("semantic", outcome.Result.ErrorKind);
        Assert.Null(outcome.Result.Response);
        Assert.False(WasRecovered(outcome.Logger));
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Reason") as string == reason);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("ContextDisposition") as string == "Discarded");
        if (field is not ("technicalKind" or "reuse"))
        {
            // A technical subjectKind or a reuse request already reaches the catalogue in the existing resolver; only unbound forms skip it.
            Assert.Equal(0, catalog.LiveLookups);
            Assert.Equal(0, catalog.DocumentLookups);
        }
    }

    [Theory]
    [InlineData("caseAndSpaces")]
    [InlineData("generalKind")]
    [InlineData("topicOnly")]
    [InlineData("blankHandlerAndQueries")]
    public async Task EquivalentNamingsOfTheUserIdentifierAreRecoveredWithTheUserLiteral(string form)
    {
        var proposal = form switch
        {
            "caseAndSpaces" => HistoricalProposal() with { SubjectName = " htmladvanced ", Topic = null },
            "generalKind" => HistoricalProposal() with { SubjectKind = "General" },
            "topicOnly" => HistoricalProposal() with { SubjectName = null },
            _ => HistoricalProposal() with { Handler = " ", SearchQueries = [" "] }
        };
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(proposal)), HistoricalIdentifier,
            ApproximateCatalog(), seed: false);
        Assert.Equal(200, outcome.Http);
        Assert.True(WasRecovered(outcome.Logger));
        Assert.Contains("HTMLEnCeldaAdvanced_ControlRoom", outcome.Result.Response);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("ContextDisposition") as string == "CandidatesOnly");
        await using var stored = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.Null(stored.GetCurrentFocus());
    }

    [Fact]
    public async Task ForbiddenActionIsNotRecoveredAsAnIdentifierSubject()
    {
        var catalog = ApproximateCatalog();
        var proposal = HistoricalProposal() with { ForbiddenAction = true };
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(proposal)), HistoricalIdentifier, catalog);
        Assert.False(WasRecovered(outcome.Logger));
        Assert.Equal(0, catalog.LiveLookups);
        Assert.DoesNotContain("HTMLEnCeldaAdvanced", outcome.Result.Response ?? string.Empty);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Reason") as string == "CapabilityNotAllowed");
    }

    [Fact]
    public async Task ASentenceIsNotRecoveredEvenWhenThePlannerNamesAnIdentifier()
    {
        var catalog = ApproximateCatalog();
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(HistoricalProposalJson), "Tengo algo con HTMLAdvanced", catalog);
        Assert.Equal(502, outcome.Http);
        Assert.False(WasRecovered(outcome.Logger));
        Assert.Equal(0, catalog.LiveLookups);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Reason") as string == "UntrustedPlannerConversation");
    }

    [Fact]
    public async Task RecoveredIdentifierWithoutEvidenceReportsThatTheSubjectWasNotConfirmed()
    {
        var catalog = new SubjectCatalogFixture();
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(HistoricalProposalJson), HistoricalIdentifier, catalog, seed: false);
        Assert.Equal(200, outcome.Http);
        Assert.Contains("No pude confirmar el sujeto", outcome.Result.Response);
        Assert.Equal(1, catalog.LiveLookups);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Reason") as string == "SubjectNotFound");
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("ContextDisposition") as string == "TurnOnly");
        await using var stored = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.Null(stored.GetCurrentFocus());
        Assert.Empty(stored.GetLastCandidateSet());
    }

    [Fact]
    public async Task RecoveredIdentifierWithAnExactCatalogueMatchAsksForTheOperationWithoutExecuting()
    {
        var catalog = new SubjectCatalogFixture { Handlers = [SubjectCatalogFixture.Handler(HistoricalIdentifier, 7)] };
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(HistoricalProposalJson), HistoricalIdentifier, catalog, seed: false);
        Assert.Equal(200, outcome.Http);
        Assert.Contains("configurar", outcome.Result.Response);
        Assert.False(outcome.Result.ToolInvoked);
        Assert.Equal(1, outcome.Chat.Calls);
        var finalized = Assert.Single(outcome.Logger.Entries, entry => entry.Fields.ContainsKey("ContextDisposition"));
        Assert.Equal("Committed", finalized.Fields["ContextDisposition"]);
        Assert.Equal("None", finalized.Fields["Executor"]?.ToString());
        Assert.Equal("RequestKindAmbiguous", finalized.Fields["Reason"]);
        await using var stored = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.Equal(new ActiveSubject("Handler", HistoricalIdentifier), stored.GetCurrentFocus()?.Subject);
        Assert.Null(stored.GetCurrentFocus()?.RequestKind);
        Assert.Empty(stored.GetGroundedArtifacts());
    }

    [Fact]
    public async Task RecoveredCandidatesDoNotDisplaceAnExistingTechnicalFocus()
    {
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(HistoricalProposalJson), HistoricalIdentifier, ApproximateCatalog());
        Assert.Equal(200, outcome.Http);
        Assert.Contains("HTMLEnCeldaAdvanced_ControlRoom", outcome.Result.Response);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("ContextDisposition") as string == "CandidatesOnly");
        await using var stored = await outcome.Store.AcquireAsync(outcome.Id, default);
        Assert.Equal("HandlerB", stored.GetCurrentFocus()?.Subject.Name);
        Assert.Equal("Examples", stored.GetCurrentFocus()?.RequestKind);
        Assert.Equal("HandlerA", stored.GetPreviousFocus()?.Subject.Name);
        Assert.Equal("{}", Assert.Single(stored.GetGroundedArtifacts()).Content);
        Assert.Equal(2, stored.GetLastCandidateSet().Count);
        Assert.DoesNotContain("Usuario anterior: " + HistoricalIdentifier, stored.BuildPrompt("next"));
    }
}

public sealed class PlannerClarificationRoutingTests
{
    [Fact]
    public async Task AConfirmedTechnicalSubjectWithoutAnOperationStillUsesTheTechnicalClarificationPath()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var proposal = new SemanticProposal("HandlerQuestion", null, false, null, "explanation", [], null, null, null,
            SubjectKind: "Handler", SubjectName: "HandlerA");
        var result = await fixture.RunAsync(proposal, "HandlerA", isPlannerOutput: true);
        Assert.NotNull(result.Validation.Plan);
        Assert.Equal("HandlerA", result.Decision?.Identity?.Subject.Name);
        Assert.Equal("RequestKindAmbiguous", result.Decision?.Reason);
        Assert.Equal("Committed", result.Outcome.ContextDisposition);
        Assert.Equal("HandlerA", (await fixture.StateAsync()).Current?.Subject.Name);
        Assert.Empty(fixture.ExternalExecutions);
    }

    [Theory]
    [InlineData("Parameters", "HandlerEvidence")]
    [InlineData("Explanation", "GroundedKnowledge")]
    public async Task ExplicitHandlerRequestsKeepTheirCanonicalExecutorAndCommit(string requestKind, string executor)
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var proposal = new SemanticProposal("HandlerQuestion", null, false, null, "explanation", ["HandlerA"], null, null, null,
            SubjectKind: "Handler", SubjectName: "HandlerA", RequestKind: requestKind);
        var result = await fixture.RunAsync(proposal, "Dame " + requestKind + " de HandlerA", (decision, _) =>
        {
            Assert.Equal(executor, decision.Executor.ToString());
            Assert.Equal("Live", decision.Identity?.Source);
            return Task.FromResult(new ChatResult("ok", "Respuesta de prueba", false, null));
        }, isPlannerOutput: true);
        Assert.NotNull(result.Validation.Plan);
        Assert.Equal("Committed", result.Outcome.ContextDisposition);
        Assert.Equal(requestKind, (await fixture.StateAsync()).Current?.RequestKind);
        Assert.Single(fixture.ExternalExecutions);
    }
}
