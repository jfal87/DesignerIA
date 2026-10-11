using System.Text.Json;
using DesignerIA.Api.Services;
using DesignerIA.Api.Services.ViewSpecs;
using DesignerIA.Contracts;
using Xunit;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

public sealed class ViewSpecPreparationFormatTests
{
    private static SemanticProposal CreateProposal(string mode) =>
        new("ViewCreation", null, false, null, mode, [], null, null, null, RequestKind: "Configuration");

    private static ViewSpecResult ValidPreparation()
    {
        var spec = new ViewSpec("PreparedView", "Prepared view", "Initial", [new("Initial", [])]);
        return new(spec, ViewSpecValidator.Validate(spec));
    }

    [Fact]
    public async Task JsonPreparationKeepsValidatedTypedPayloadWithoutDocumentaryArtifacts()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var payload = ValidPreparation();
        var result = await fixture.RunAsync(CreateProposal("json"), "preparar vista en JSON",
            (_, _) => Task.FromResult(new ChatResult("ok", "Vista preparada, no aplicada.", false, null,
                ViewSpecResult: payload)));

        Assert.Equal(ConversationExecutor.ViewSpecPreparation, result.Decision?.Executor);
        Assert.Equal("ok", result.Outcome.Result.Status);
        Assert.Same(payload, result.Outcome.Result.ViewSpecResult);
        using var json = JsonDocument.Parse(result.Outcome.Result.Response!);
        Assert.Equal(payload.Spec.Name, json.RootElement.GetProperty("spec").GetProperty("name").GetString());
        Assert.True(json.RootElement.GetProperty("validation").GetProperty("isValid").GetBoolean());
        Assert.Empty((await fixture.StateAsync()).Artifacts);
    }

    [Fact]
    public async Task NormalPreparationPreservesSummaryAndTypedPayloadThroughHttpSerialization()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var payload = ValidPreparation();
        const string summary = "Vista preparada, no aplicada.";
        var result = await fixture.RunAsync(CreateProposal("explanation"), "preparar vista",
            (_, _) => Task.FromResult(new ChatResult("ok", summary, false, null, ViewSpecResult: payload)));

        Assert.Equal("ok", result.Outcome.Result.Status);
        Assert.Equal(summary, result.Outcome.Result.Response);
        Assert.Same(payload, result.Outcome.Result.ViewSpecResult);
        Assert.Equal("Committed", result.Outcome.ContextDisposition);
        using var httpBody = JsonDocument.Parse(JsonSerializer.Serialize(result.Outcome.Result,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.Equal(payload.Spec.Name,
            httpBody.RootElement.GetProperty("viewSpecResult").GetProperty("spec").GetProperty("name").GetString());
        Assert.True(httpBody.RootElement.GetProperty("viewSpecResult").GetProperty("validation").GetProperty("isValid").GetBoolean());
    }

    [Fact]
    public async Task ExclusiveJsonSerializesOnlyPreparationNotModelTextOrOtherSubjectsArtifacts()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler("Original"));
        var before = await fixture.StateAsync();
        var payload = ValidPreparation();
        const string message = "preparar vista, dame solo el json";
        var mode = ChatResponseFormat.Resolve(message);
        Assert.Equal("json", mode);
        var result = await fixture.RunAsync(CreateProposal(mode), message, (_, staged) =>
        {
            staged.SetGroundedArtifacts([new(GroundedArtifactKind.Json, "{\"other\":true}", "Knowledge", "other.md",
                "Other", "Other", new("Handler", "Other"))]);
            return Task.FromResult(new ChatResult("ok", "{\"arbitraryModelText\":true}", false, null, ViewSpecResult: payload));
        });

        Assert.Equal("ok", result.Outcome.Result.Status);
        Assert.Same(payload, result.Outcome.Result.ViewSpecResult);
        var decoded = JsonSerializer.Deserialize<ViewSpecResult>(result.Outcome.Result.Response!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(payload.Spec.Name, decoded?.Spec.Name);
        Assert.True(decoded?.Validation.IsValid);
        Assert.DoesNotContain("arbitraryModelText", result.Outcome.Result.Response);
        Assert.DoesNotContain("\"other\"", result.Outcome.Result.Response);
        Assert.Equal(before.Current, (await fixture.StateAsync()).Current);
        Assert.Equal(before.Previous, (await fixture.StateAsync()).Previous);
    }

    [Theory]
    [InlineData("explanation")]
    [InlineData("json")]
    public async Task InvalidStructureRetainsValidationErrorsAndDiscardsStagedContext(string mode)
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler("Original"));
        var before = await fixture.StateAsync();
        var spec = new ViewSpec("Broken", "Invalid state reference", "Missing", [new("Initial", [])]);
        var payload = new ViewSpecResult(spec, ViewSpecValidator.Validate(spec));
        Assert.False(payload.Validation.IsValid);
        var result = await fixture.RunAsync(CreateProposal(mode), "preparar vista invalida", (_, staged) =>
        {
            staged.SetResolvedSubject(SubjectCatalogFixture.Handler("Provisional"));
            return Task.FromResult(new ChatResult("ok", "Invalid preparation", false, null, ViewSpecResult: payload));
        });

        Assert.Equal("error", result.Outcome.Result.Status);
        Assert.False(result.Outcome.Result.ViewSpecResult?.Validation.IsValid);
        Assert.Equal(payload.Validation.Errors, result.Outcome.Result.ViewSpecResult?.Validation.Errors);
        Assert.Contains("Missing", result.Outcome.Result.Error);
        Assert.Equal("InvalidViewSpecPreparation", result.Outcome.FallbackReason);
        await AssertDiscardedAsync(fixture, before, result);
    }

    [Fact]
    public async Task MissingTypedPreparationCannotAuthorizeModelJsonOrDocumentaryJson()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler("Original"));
        var before = await fixture.StateAsync();
        var result = await fixture.RunAsync(CreateProposal("json"), "preparar vista", (_, staged) =>
        {
            staged.SetGroundedArtifacts([new(GroundedArtifactKind.Json, "{\"fake\":true}", "Knowledge", "document.md",
                "Original", "Original", new("Handler", "Original"))]);
            return Task.FromResult(new ChatResult("ok", "{\"fake\":true}", true, null));
        });

        Assert.Equal("error", result.Outcome.Result.Status);
        Assert.Null(result.Outcome.Result.ViewSpecResult);
        Assert.Null(result.Outcome.Result.Response);
        Assert.Equal("InvalidViewSpecPreparation", result.Outcome.FallbackReason);
        await AssertDiscardedAsync(fixture, before, result);
    }

    [Fact]
    public async Task ExistingPreparationErrorIsNotTurnedIntoJsonSuccess()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        await fixture.SeedAsync(SubjectCatalogFixture.Handler("Original"));
        var before = await fixture.StateAsync();
        var error = new ChatResult("error", null, false, "Generation failed", ViewSpecResult: ValidPreparation());
        var result = await fixture.RunAsync(CreateProposal("json"), "preparar vista",
            (_, _) => Task.FromResult(error));
        Assert.Same(error, result.Outcome.Result);
        await AssertDiscardedAsync(fixture, before, result);
    }

    [Fact]
    public async Task ReportedValidationErrorIsPreservedEvenForStructurallyValidSpec()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var payload = ValidPreparation() with { Validation = new(false, ["Reported validation failure"], []) };
        var result = await fixture.RunAsync(CreateProposal("json"), "preparar vista",
            (_, _) => Task.FromResult(new ChatResult("ok", "Rejected", false, null, ViewSpecResult: payload)));
        Assert.Equal("error", result.Outcome.Result.Status);
        Assert.Same(payload, result.Outcome.Result.ViewSpecResult);
        Assert.Contains("Reported validation failure", result.Outcome.Result.Error);
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
    }

    [Fact]
    public async Task ClaimedValidPayloadMustStillPassDeterministicValidation()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var payload = new ViewSpecResult(new(null, null, null, []), new(true, [], []));
        var result = await fixture.RunAsync(CreateProposal("json"), "preparar vista",
            (_, _) => Task.FromResult(new ChatResult("ok", "Claimed valid", false, null, ViewSpecResult: payload)));
        Assert.Equal("error", result.Outcome.Result.Status);
        Assert.False(result.Outcome.Result.ViewSpecResult?.Validation.IsValid);
        Assert.NotEmpty(result.Outcome.Result.ViewSpecResult!.Validation.Errors);
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
    }

    [Fact]
    public async Task ValidJsonPreservesPendingAndKnowledgeGaps()
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var spec = new ViewSpec("PendingView", "Pending input", "Initial",
            [new("Initial", [new([new("Grid", null, null, [])])])]);
        var payload = new ViewSpecResult(spec, ViewSpecValidator.Validate(spec), [new("Topic", "Evidence unavailable")]);
        Assert.True(payload.Validation.IsValid);
        Assert.NotEmpty(payload.Validation.Pending);
        var result = await fixture.RunAsync(CreateProposal("json"), "preparar vista",
            (_, _) => Task.FromResult(new ChatResult("ok", "Pending input", false, null, ViewSpecResult: payload)));
        Assert.Equal("ok", result.Outcome.Result.Status);
        Assert.Same(payload, result.Outcome.Result.ViewSpecResult);
        using var json = JsonDocument.Parse(result.Outcome.Result.Response!);
        Assert.Equal(payload.Validation.Pending.Count, json.RootElement.GetProperty("validation").GetProperty("pending").GetArrayLength());
        Assert.Equal("Topic", json.RootElement.GetProperty("knowledgeGaps")[0].GetProperty("topic").GetString());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypedPayloadDoesNotAuthorizeUngroundedDocumentaryJson(bool attachTypedPayload)
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var proposal = new SemanticProposal("KnowledgeQuestion", null, false, null, "json", ["Handler JSON"],
            null, null, null, SubjectKind: "Handler", SubjectName: "HTMLEnCeldaAdvanced", RequestKind: "Use");
        var result = await fixture.RunAsync(proposal, "mostrar JSON",
            (_, _) => Task.FromResult(new ChatResult("ok", "{\"invented\":true}", true, null,
                ViewSpecResult: attachTypedPayload ? ValidPreparation() : null)));
        Assert.Equal(ConversationExecutor.GroundedKnowledge, result.Decision?.Executor);
        Assert.Equal("error", result.Outcome.Result.Status);
        Assert.Equal("RequestedFormatUnavailable", result.Outcome.FallbackReason);
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
    }

    [Fact]
    public async Task AuthenticCompatibleDocumentaryJsonRemainsAllowed()
    {
        const string handler = "HTMLEnCeldaAdvanced";
        const string json = "{\"documented\":true}";
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var evidence = new KnowledgeSearchResultItem(handler, "handler.md", "handler.md", handler + " " + json, 1);
        var proposal = new SemanticProposal("KnowledgeQuestion", null, false, null, "json", ["Handler JSON"],
            null, null, null, SubjectKind: "Handler", SubjectName: handler, RequestKind: "Use");
        var result = await fixture.RunAsync(proposal, "mostrar JSON",
            (decision, staged) => ConversationIntegrationFixture.Knowledge(decision, staged, json, evidence));
        Assert.Equal("ok", result.Outcome.Result.Status);
        Assert.Equal(json, result.Outcome.Result.Response);
        Assert.Equal("Committed", result.Outcome.ContextDisposition);
        var artifact = Assert.Single((await fixture.StateAsync()).Artifacts, a => a.Kind == GroundedArtifactKind.Json);
        Assert.Equal(new ActiveSubject("Handler", handler), artifact.OriginatingSubject);
        Assert.Equal("Knowledge", artifact.Origin);
    }

    [Theory]
    [InlineData("explanation")]
    [InlineData("json")]
    public async Task PreparationOnlyExecutesAuthorizedPreparationWithoutLiveQueries(string mode)
    {
        var fixture = new ConversationIntegrationFixture(SubjectCatalogFixture.ExistingHandlers());
        var result = await fixture.RunAsync(CreateProposal(mode), "preparar vista",
            (decision, _) =>
            {
                Assert.Equal(ConversationExecutor.ViewSpecPreparation, decision.Executor);
                Assert.Equal(ConversationCapabilityEffect.Preparation, decision.Effect);
                Assert.Null(decision.Resource);
                Assert.Null(decision.ResourceOperation);
                return Task.FromResult(new ChatResult("ok", "Prepared", false, null, ViewSpecResult: ValidPreparation()));
            });
        Assert.Equal("ok", result.Outcome.Result.Status);
        Assert.Equal(ConversationExecutor.ViewSpecPreparation, Assert.Single(fixture.ExternalExecutions).Executor);
        Assert.Equal(0, fixture.Catalog.LiveLookups);
        Assert.Equal(0, fixture.Catalog.DocumentLookups);
        Assert.False(result.Outcome.Result.ToolInvoked);
    }

    private static async Task AssertDiscardedAsync(ConversationIntegrationFixture fixture,
        ConversationIntegrationState before, ConversationIntegrationResult result)
    {
        var after = await fixture.StateAsync();
        Assert.Equal("Discarded", result.Outcome.ContextDisposition);
        Assert.Equal(before.Current, after.Current);
        Assert.Equal(before.Previous, after.Previous);
        Assert.Equal(before.Artifacts.ToArray(), after.Artifacts.ToArray());
        Assert.Equal(before.Prompt, after.Prompt);
    }
}
