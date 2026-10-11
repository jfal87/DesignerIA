using DesignerIA.Api.Services;
using DesignerIA.Contracts;
using Xunit;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

public sealed class KnowledgeArtifactRegressionTests
{
    private const string Handler = "botonUltimoNivel";
    private const string Json = "{\"EstiloNivel\":\"\",\"ToolTipoNivel\":\"\",\"EstiloUltimoNivel\":\"G_GRAFICO|G_DET_FACT\",\"ToolTipoUltimoNivel\":\"Modificar Frecuencia|asas\"}";
    private static SemanticProposal Proposal(string? reference = null, bool reuse = false) =>
        new("HandlerQuestion", null, false, null, reuse ? "json" : "explanation", reuse ? [] : [Handler],
            null, null, null, SubjectKind: "Handler", SubjectName: reference is null ? Handler : null,
            SubjectReference: reference, RequestKind: "Explanation", ReuseGroundedArtifact: reuse);

    [Theory]
    [InlineData("Imports\\DataGestionEngineDesarrollo.xlsx::chunk-31")]
    [InlineData("Imports\\LoadGestionEngineLab.sql::chunk-31")]
    [InlineData("WikiData\\Documento Handlers.docx::chunk-15")]
    [InlineData("WikiData\\Handlers.xlsx::chunk-16")]
    public async Task IndexedJsonWithGenericHeadingKeepsItsDocumentedOwnerAndCanBeReused(string id)
    {
        var document = ReadOnlyKnowledgeFixture.Document(id);
        Assert.Contains(Json, document.Snippet);
        var fixture = new ConversationIntegrationFixture(new SubjectCatalogFixture
        {
            Handlers = [SubjectCatalogFixture.Handler(Handler)], Documents = [document]
        });
        var first = await fixture.RunAsync(Proposal(), Handler, (decision, staged) =>
            ConversationIntegrationFixture.Knowledge(decision, staged, "Ejemplo documentado: " + Json, document));
        Assert.Equal(ConversationExecutor.GroundedKnowledge, first.Decision?.Executor);
        var before = await fixture.StateAsync();
        var artifact = Assert.Single(before.Artifacts, artifact => artifact.Kind == GroundedArtifactKind.Json);
        Assert.Equal(new ActiveSubject("Handler", Handler), artifact.OriginatingSubject);
        Assert.Equal("Knowledge", artifact.Origin);
        Assert.Contains(document.SourceFile, artifact.Source);
        Assert.Equal(Json, artifact.Content);

        var reused = await fixture.RunAsync(Proposal("current", true), "dame solo el JSON del ejemplo");
        Assert.Equal(ConversationExecutor.ArtifactReuse, reused.Decision?.Executor);
        Assert.Equal(Json, reused.Outcome.Result.Response);
        var after = await fixture.StateAsync();
        Assert.Equal(before.Current, after.Current);
        Assert.Equal(before.Previous, after.Previous);
        Assert.Equal(before.Artifacts, after.Artifacts);
        Assert.Single(fixture.ExternalExecutions);
    }

    [Fact]
    public void ASeparateRecordCannotAcquireAnOwnerFromAnotherMentionInTheDocument()
    {
        var document = ReadOnlyKnowledgeFixture.Document("Imports\\DataGestionEngineDesarrollo.xlsx::chunk-31");
        var extracted = GroundedArtifactExtractor.FromKnowledge("{\"Columnas\":\"0:4\"}", [document], Handler, new("Handler", Handler));
        Assert.All(extracted.Artifacts.Where(artifact => artifact.Kind == GroundedArtifactKind.Json),
            artifact => Assert.Null(artifact.OriginatingSubject));
    }

    [Fact]
    public void IndividuallyNamedRowsKeepDifferentOwnersInsideOneGenericDocument()
    {
        var document = ReadOnlyKnowledgeFixture.Document("Imports\\DataGestionEngineDesarrollo.xlsx::chunk-31");
        var other = new ActiveSubject("Handler", "textoAlColapsarColumna");
        var extracted = GroundedArtifactExtractor.FromKnowledge(Json + "\n{\"Columnas\":\"0:4\"}", [document], Handler,
            new("Handler", Handler), [other]);
        Assert.Equal(new ActiveSubject("Handler", Handler), extracted.Artifacts.Single(a => a.Content == Json).OriginatingSubject);
        Assert.Equal(other, extracted.Artifacts.Single(a => a.Content == "{\"Columnas\":\"0:4\"}").OriginatingSubject);
    }

    [Theory]
    [InlineData("OtherHandler | {\"other\":1}")]
    [InlineData("botonUltimoNivel y OtherHandler comparten un ejemplo: {\"other\":1}")]
    [InlineData("Sin identidad documental: {\"other\":1}")]
    public void UnboundOrAmbiguousFragmentsNeverInheritCurrentFocus(string content)
    {
        var document = new KnowledgeSearchResultItem("Catálogo", "catalog.md", "catalog.md", content, 1);
        var extracted = GroundedArtifactExtractor.FromKnowledge("{\"other\":1}", [document], Handler,
            new("Handler", Handler), [new("Handler", "OtherHandler")]);
        Assert.All(extracted.Artifacts.Where(a => a.Kind == GroundedArtifactKind.Json), a =>
        {
            if (content.StartsWith("OtherHandler |", StringComparison.Ordinal))
            {
                Assert.NotEqual(new ActiveSubject("Handler", Handler), a.OriginatingSubject);
            }
            else { Assert.Null(a.OriginatingSubject); }
        });
    }

    [Theory]
    [InlineData("{\"invented\":2}")]
    [InlineData("{invalid json}")]
    public void ModelInventedOrInvalidJsonIsNotRegistered(string response)
    {
        var document = ReadOnlyKnowledgeFixture.Document("Imports\\DataGestionEngineDesarrollo.xlsx::chunk-31");
        var extracted = GroundedArtifactExtractor.FromKnowledge(response, [document], Handler, new("Handler", Handler));
        Assert.DoesNotContain(extracted.Artifacts, a => a.Kind == GroundedArtifactKind.Json);
    }

    [Fact]
    public void IdenticalContentFromTwoSourcesKeepsBothDocumentaryProvenances()
    {
        var one = ReadOnlyKnowledgeFixture.Document("Imports\\DataGestionEngineDesarrollo.xlsx::chunk-31");
        var two = one with { SourceFile = "second.xlsx", RelativePath = "second.xlsx", Score = 2 };
        var selected = KnowledgeEvidencePolicy.Select([one, two], Handler, Handler, out _);
        Assert.Equal(2, selected.Count);
        var extracted = GroundedArtifactExtractor.FromKnowledge(Json, selected, Handler, new("Handler", Handler));
        var artifacts = extracted.Artifacts.Where(a => a.Kind == GroundedArtifactKind.Json).ToArray();
        Assert.Equal(2, artifacts.Length);
        Assert.Equal(2, artifacts.Select(a => a.Source).Distinct().Count());
        Assert.All(artifacts, a => Assert.Equal(new ActiveSubject("Handler", Handler), a.OriginatingSubject));
    }

    [Fact]
    public void TheSameJsonInDifferentRowsCannotUseTheTitleToChooseOneOwner()
    {
        var document = new KnowledgeSearchResultItem(Handler, "catalog.md", "catalog.md",
            Handler + " | " + Json + "\nOtherHandler | " + Json, 1);
        var extracted = GroundedArtifactExtractor.FromKnowledge(Json, [document], Handler,
            new("Handler", Handler), [new("Handler", "OtherHandler")]);
        Assert.All(extracted.Artifacts.Where(a => a.Kind == GroundedArtifactKind.Json), a => Assert.Null(a.OriginatingSubject));
    }

    [Fact]
    public void LiveEvidenceCannotBeRelabelledAsKnowledgeByTheDocumentaryExtractor()
    {
        var document = new KnowledgeSearchResultItem(Handler, "AccionesHandler: 18", "live", Handler + " | " + Json, 1, "Live");
        var extracted = GroundedArtifactExtractor.FromKnowledge(Json, [document], Handler, new("Handler", Handler));
        Assert.Empty(extracted.Artifacts);
        Assert.Empty(KnowledgeEvidencePolicy.Select([document], Handler, Handler, out _));
    }

    [Theory]
    [InlineData("", "catalog.md")]
    [InlineData("catalog.md", "")]
    public void MissingSourceMetadataCannotProduceReusableArtifacts(string sourceFile, string relativePath)
    {
        var document = new KnowledgeSearchResultItem(Handler, sourceFile, relativePath, Handler + " | " + Json, 1);
        Assert.Empty(GroundedArtifactExtractor.FromKnowledge(Json, [document], Handler, new("Handler", Handler)).Artifacts);
    }
}
