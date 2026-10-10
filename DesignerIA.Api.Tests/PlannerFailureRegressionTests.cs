using System.Net;
using System.Security.Authentication;
using System.Text.Json;
using DesignerIA.Api.Controllers;
using DesignerIA.Api.Services;
using DesignerIA.Api.Services.Knowledge;
using DesignerIA.Contracts;
using GitHub.Copilot;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

public sealed partial class PlannerFailureRegressionTests
{
    private const string Message = "Explica los parámetros de un handler de GestionEngine";
    private const string Sensitive = "Authorization: Bearer secret-token; password=secret-password; prompt-private";

    private sealed class FakePlanner(
        ConversationContextStore store, KnowledgeSearchService knowledge, RecordingLogger logger,
        Func<CopilotChatService.ChatTimings, CancellationToken, Task<string?>> send, IConversationSubjectCatalog? catalog = null,
        Func<string, string?>? answer = null)
        : CopilotChatService(Health(), store, Handlers(), Metadata(), knowledge,
            catalog ?? new ConversationSubjectCatalog(Metadata(), knowledge), new CopilotClientProvider(), logger)
    {
        public int Calls { get; private set; }
        public int AnswerCalls { get; private set; }
        // Receives the prompt with the numbered evidence; by default it declares the first fragment.
        internal override Task<string?> SendGroundedAnswerAsync(string instructions, string prompt, ChatTimings timings, CancellationToken cancellationToken)
        {
            AnswerCalls++;
            timings.CopilotRequestCount++;
            return Task.FromResult(answer is null ? "FUENTES: 1\nRespuesta documental de prueba." : answer(prompt));
        }
        internal override Task<string?> SendPlannerAsync(string prompt, ChatTimings timings, CancellationToken cancellationToken)
        {
            Calls++;
            return send(timings, cancellationToken);
        }
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["ConnectionStrings:GestionEngineLocal"] = "Server=unused;Database=unused;Integrated Security=True" }).Build();
    private static GestionEngineHealthService Health() => new(Configuration(), NullLogger<GestionEngineHealthService>.Instance);
    private static HandlerExamplesService Handlers() => new(Configuration(), NullLogger<HandlerExamplesService>.Instance);
    private static GestionEngineMetadataService Metadata() => new(Configuration(), NullLogger<GestionEngineMetadataService>.Instance);

    private static string State(ConversationLease stored) => JsonSerializer.Serialize(new
    {
        Current = stored.GetCurrentFocus(), Previous = stored.GetPreviousFocus(), Recent = stored.GetRecentSubjects(),
        Artifacts = stored.GetGroundedArtifacts(), Candidates = stored.GetLastCandidateSet(), Selected = stored.GetSelectedEntity(),
        Metadata = stored.GetActiveMetadataResource(), Topic = stored.GetActiveKnowledgeTopic(), Queries = stored.GetActiveKnowledgeQueries(),
        Prompt = stored.BuildPrompt("next")
    });

    private sealed class RecordingLogger : ILogger<CopilotChatService>
    {
        public List<(string Text, IReadOnlyDictionary<string, object?> Fields, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((formatter(state, exception),
                ((IEnumerable<KeyValuePair<string, object?>>)(object)state!).ToDictionary(pair => pair.Key, pair => pair.Value), exception));
    }

    private static async Task<(ChatResult Result, int Http, RecordingLogger Logger, FakePlanner Chat,
        ConversationContextStore Store, string Id)> RunAsync(
        Func<CopilotChatService.ChatTimings, CancellationToken, Task<string?>> send, string message = Message,
        IConversationSubjectCatalog? catalog = null, bool seed = true, string? firstMessage = null,
        Action<ConversationLease>? arrange = null, Func<string, string?>? answer = null)
    {
        var store = new ConversationContextStore();
        var id = Guid.NewGuid().ToString();
        var logger = new RecordingLogger();
        var chat = new FakePlanner(store, new ReadOnlyKnowledgeFixture().Search, logger, send, catalog, answer);
        var controller = new CopilotController(null!, null!, chat);
        if (seed)
        {
            await using var stored = await store.AcquireAsync(id, default);
            stored.SetResolvedSubject(SubjectCatalogFixture.Handler("HandlerA"));
            stored.SetResolvedSubject(SubjectCatalogFixture.Handler("HandlerB"));
            stored.SetRequestKind("Examples");
            stored.SetLastCandidateSet([new("Handler", "HandlerA"), new("Handler", "HandlerB")]);
            stored.SelectCandidate(new("Handler", "HandlerB"));
            stored.AddTurn("previous question", "previous answer");
            stored.SetGroundedArtifacts([new(GroundedArtifactKind.Json, "{}", "Live", "Handlers: 1", "HandlerB", "HandlerB", new("Handler", "HandlerB"))]);
        }
        if (arrange is not null)
        {
            await using var stored = await store.AcquireAsync(id, default);
            arrange(stored);
        }
        if (firstMessage is not null)
        {
            // Server-authored turn; it never reaches the planner double.
            Assert.IsType<OkObjectResult>(await controller.Chat(new ChatRequest(firstMessage, id), default));
            logger.Entries.Clear();
        }
        string before;
        await using (var stored = await store.AcquireAsync(id, default)) { before = State(stored); }
        var response = Assert.IsAssignableFrom<ObjectResult>(await controller.Chat(new ChatRequest(message, id), default));
        var result = Assert.IsType<ChatResult>(response.Value);
        await using (var stored = await store.AcquireAsync(id, default))
        {
            if (result.Status != "ok") { Assert.Equal(before, State(stored)); }
        }
        return (result, response.StatusCode!.Value, logger, chat, store, id);
    }

    [Theory]
    [InlineData("unavailable", "RuntimeUnavailable")]
    [InlineData("authentication", "Authentication")]
    [InlineData("quota", "Quota")]
    [InlineData("timeout", "Timeout")]
    [InlineData("transport", "Transport")]
    [InlineData("unexpected", "Unclassified")]
    [InlineData("rate-limit", "RateLimit")]
    public async Task SdkFailuresAreErrorsWithVerifiableDiagnosticsAndNoContextCommit(string failure, string category)
    {
        var outcome = await RunAsync((timings, _) =>
        {
            Exception exception = failure switch
            {
                "unavailable" => new FileNotFoundException(Sensitive),
                "authentication" => new AuthenticationException(Sensitive),
                "quota" => new InvalidOperationException(Sensitive),
                "timeout" => new TimeoutException(Sensitive),
                "transport" => new IOException(Sensitive),
                "rate-limit" => new HttpRequestException(Sensitive, null, HttpStatusCode.TooManyRequests),
                _ => new InvalidOperationException("quota exhausted " + Sensitive)
            };
            if (failure == "quota")
            {
                timings.PlannerError = new SessionErrorData { ErrorType = "quota", ErrorCode = "quota_exceeded", Message = Sensitive, StatusCode = 429 };
            }
            return Task.FromException<string?>(exception);
        });
        Assert.Equal("error", outcome.Result.Status);
        Assert.Equal(503, outcome.Http);
        Assert.Null(outcome.Result.Response);
        Assert.DoesNotContain("más de detalle", outcome.Result.Error);
        Assert.Equal(1, outcome.Chat.Calls);
        var diagnostic = Assert.Single(outcome.Logger.Entries, entry => entry.Fields.ContainsKey("FailureCategory"));
        Assert.Equal(category, diagnostic.Fields["FailureCategory"]);
        Assert.Equal("PlannerFailure", diagnostic.Fields["Reason"]);
        Assert.NotNull(diagnostic.Fields["ExceptionType"]);
        var finalized = Assert.Single(outcome.Logger.Entries, entry => entry.Fields.ContainsKey("ContextDisposition"));
        Assert.Equal("Discarded", finalized.Fields["ContextDisposition"]);
        Assert.Equal(diagnostic.Fields["RequestId"], finalized.Fields["RequestId"]);
        Assert.All(outcome.Logger.Entries, entry =>
        {
            Assert.DoesNotContain("secret-token", entry.Text);
            Assert.DoesNotContain("secret-password", entry.Text);
            Assert.DoesNotContain("prompt-private", entry.Text);
            Assert.Null(entry.Exception);
        });
        if (failure == "quota")
        {
            Assert.Equal("quota", diagnostic.Fields["SdkErrorType"]);
            Assert.Equal("quota_exceeded", diagnostic.Fields["SdkErrorCode"]);
            Assert.Equal(429, diagnostic.Fields["UpstreamStatus"]);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n")]
    public async Task EmptyPlannerResponsesAreTechnicalErrors(string? response)
    {
        var outcome = await RunAsync((_, _) => Task.FromResult(response));
        Assert.Equal("error", outcome.Result.Status);
        Assert.Equal(503, outcome.Http);
        Assert.Null(outcome.Result.Response);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Reason") as string == "PlannerNoResponse");
    }

    [Theory]
    [InlineData("arbitrary model answer", "PlannerNoJson")]
    [InlineData("{invalid JSON}", "InvalidProposalJson")]
    [InlineData("{\"intent\":\"GeneralQuestion\"}", "InvalidProposalJson")]
    [InlineData("{\"intent\":\"GeneralQuestion\",\"referencesPreviousTopic\":false,\"responseMode\":\"explanation\",\"searchQueries\":[],\"executor\":\"ExecuteSql\"}", "InvalidProposalJson")]
    public async Task InvalidPlannerOutputsAreSemanticErrorsWithoutPublishingModelText(string content, string reason)
    {
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(content));
        Assert.Equal("error", outcome.Result.Status);
        Assert.Equal(502, outcome.Http);
        Assert.Null(outcome.Result.Response);
        Assert.DoesNotContain(content, outcome.Result.Error);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Reason") as string == reason);
    }

    [Theory]
    [InlineData("Conversation", "UntrustedPlannerConversation")]
    [InlineData("GeneralQuestion", "MetadataFieldsOutsideCapability")]
    public async Task WellFormedButRejectedProposalsAreSemanticErrors(string intent, string reason)
    {
        var json = JsonSerializer.Serialize(new SemanticProposal(intent, null, false, null, "explanation", [],
            intent == "GeneralQuestion" ? "HandlerActions" : null, null, null, GeneralAnswer: Sensitive,
            RequestKind: intent == "Conversation" ? "Parameters" : null),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(json));
        Assert.Equal("error", outcome.Result.Status);
        Assert.Equal(502, outcome.Http);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("Reason") as string == reason);
        Assert.Null(outcome.Result.Response);
    }

    [Fact]
    public async Task ARealMissingSubjectStillAsksForClarification()
    {
        var json = JsonSerializer.Serialize(new SemanticProposal("KnowledgeQuestion", null, false, null, "explanation", [],
            null, null, null, RequestKind: "Parameters"), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(json));
        Assert.Equal(200, outcome.Http);
        Assert.Equal("ok", outcome.Result.Status);
        Assert.NotNull(outcome.Result.Response);
        Assert.Null(outcome.Result.Error);
    }

    [Fact]
    public async Task ValidGeneralAnswerUsesThePlannerAnswerWithoutAnotherSdkRequest()
    {
        const string answer = "Un Dictionary permite buscar valores por clave.";
        var json = JsonSerializer.Serialize(new SemanticProposal("GeneralQuestion", null, false, null, "explanation", [],
            null, null, null, GeneralAnswer: answer), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(json), "Qué es un Dictionary en C#");
        Assert.Equal(200, outcome.Http);
        Assert.Equal("ok", outcome.Result.Status);
        Assert.Equal(answer, outcome.Result.Response);
        Assert.False(outcome.Result.ToolInvoked);
        Assert.Equal(1, outcome.Chat.Calls);
    }

    [Fact]
    public async Task InsufficientEvidenceIsNotReportedAsPlannerUnavailability()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var proposal = new SemanticProposal("HandlerQuestion", null, false, null, "explanation", ["HandlerA"],
            null, null, null, SubjectKind: "Handler", SubjectName: "HandlerA", RequestKind: "Explanation");
        const string response = "La consulta se entendió, pero no hay evidencia documental suficiente.";
        var outcome = await fixture.RunAsync(proposal, "explica HandlerA", (_, _) =>
            Task.FromResult(new ChatResult("ok", response, true, null)), isPlannerOutput: true);
        Assert.Equal("ok", outcome.Outcome.Result.Status);
        Assert.Equal(response, outcome.Outcome.Result.Response);
        Assert.Null(outcome.Outcome.FallbackReason);
        Assert.Empty((await fixture.StateAsync()).Artifacts);
    }

    [Fact]
    public async Task MultipleConfirmedCandidatesRemainALegitimateClarification()
    {
        var catalog = new SubjectCatalogFixture { Handlers = [SubjectCatalogFixture.Handler("HandlerA", 1), SubjectCatalogFixture.Handler("HandlerA", 2)] };
        var fixture = new ConversationIntegrationFixture(catalog);
        var proposal = new SemanticProposal("HandlerQuestion", null, false, null, "explanation", ["HandlerA"],
            null, null, null, SubjectKind: "Handler", SubjectName: "HandlerA", RequestKind: "Parameters");
        var outcome = await fixture.RunAsync(proposal, "HandlerA", isPlannerOutput: true);
        Assert.Equal("SubjectAmbiguous", outcome.Validation.Reason);
        var result = PlannerFailurePolicy.Rejected(outcome.Validation);
        Assert.Equal("ok", result.Status);
        Assert.Contains("varios candidatos", result.Response);
        Assert.Null(result.ErrorKind);
        Assert.Empty(fixture.ExternalExecutions);
    }

    [Fact]
    public async Task SdkErrorEventIsNotIgnoredEvenIfAnAssistantMessageWasReturned()
    {
        var outcome = await RunAsync((timings, _) =>
        {
            timings.PlannerError = new SessionErrorData { ErrorType = "authentication", Message = Sensitive, StatusCode = 401 };
            return Task.FromResult<string?>("{\"intent\":\"GeneralQuestion\",\"responseMode\":\"explanation\",\"referencesPreviousTopic\":false,\"searchQueries\":[],\"generalAnswer\":\"unsafe success\"}");
        });
        Assert.Equal(503, outcome.Http);
        Assert.Equal("technical", outcome.Result.ErrorKind);
        Assert.Null(outcome.Result.Response);
        Assert.Contains(outcome.Logger.Entries, entry => entry.Fields.GetValueOrDefault("FailureCategory") as string == "Authentication");
    }

    [Fact]
    public async Task UnrecognizedSdkMetadataIsFingerprintOnly()
    {
        var outcome = await RunAsync((timings, _) =>
        {
            timings.PlannerError = new SessionErrorData { ErrorType = Sensitive, ErrorCode = Sensitive, Message = Sensitive };
            return Task.FromException<string?>(new InvalidOperationException(Sensitive));
        });
        Assert.Equal(503, outcome.Http);
        var diagnostic = Assert.Single(outcome.Logger.Entries, entry => entry.Fields.ContainsKey("FailureCategory"));
        Assert.Equal("Unclassified", diagnostic.Fields["FailureCategory"]);
        Assert.Equal("Unrecognized", diagnostic.Fields["SdkErrorType"]);
        Assert.Equal("Unrecognized", diagnostic.Fields["SdkErrorCode"]);
        Assert.NotNull(diagnostic.Fields["SdkErrorCodeFingerprint"]);
        Assert.All(outcome.Logger.Entries, entry => Assert.DoesNotContain(Sensitive, entry.Text));
    }

    [Fact]
    public async Task ServerAuthoredSocialResponsesDoNotCallTheSdk()
    {
        var outcome = await RunAsync((_, _) => throw new InvalidOperationException("SDK must not be called"), "Hola");
        Assert.Equal(200, outcome.Http);
        Assert.Equal("ok", outcome.Result.Status);
        Assert.Equal(0, outcome.Chat.Calls);
    }

    [Fact]
    public async Task CallerCancellationIsPropagatedWithoutFallbackOrContextCommit()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new ConversationContextStore();
        var id = Guid.NewGuid().ToString();
        string before;
        await using (var stored = await store.AcquireAsync(id, default))
        {
            stored.SetResolvedSubject(SubjectCatalogFixture.Handler("HandlerA"));
            before = State(stored);
        }
        var logger = new RecordingLogger();
        var chat = new FakePlanner(store, new ReadOnlyKnowledgeFixture().Search, logger, (_, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            cancellation.Cancel();
            return Task.FromCanceled<string?>(token);
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => chat.RunAsync(Message, id, cancellation.Token));
        await using var after = await store.AcquireAsync(id, default);
        Assert.Equal(before, State(after));
        Assert.Equal(1, chat.Calls);
        Assert.Contains(logger.Entries, entry => entry.Fields.GetValueOrDefault("FallbackReason") as string == "RequestCancelled");
        Assert.DoesNotContain(logger.Entries, entry => entry.Fields.ContainsKey("FailureCategory"));
    }
}
