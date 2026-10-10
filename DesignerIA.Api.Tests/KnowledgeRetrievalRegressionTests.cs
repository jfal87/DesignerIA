using DesignerIA.Api.Services;
using Xunit;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

public sealed class KnowledgeRetrievalRegressionTests
{
    [Fact]
    public void ExistingLexicalSearcherCanFindRefreshDocumentationWithoutRankingChanges()
    {
        var corpus = new ReadOnlyKnowledgeFixture();
        var candidates = corpus.Search.Search("refrescar LM");
        Assert.Contains(candidates, item => item.SourceFile == "Funciones para refrescar LM.msg"
            && item.Snippet.Contains("refrescarItemLeftMenu", StringComparison.Ordinal));
        Assert.InRange(candidates.Count, 1, 5);
        Assert.All(candidates.GroupBy(item => item.SourceFile), group => Assert.InRange(group.Count(), 1, 3));
    }

    [Theory]
    [InlineData("Necesito refrescar el menú lateral de una vista. ¿Qué está documentado?")]
    [InlineData("¿Y cómo lo recargo después de cambiar datos?")]
    [InlineData("Volviendo al menú lateral, dame evidencia de la recarga.")]
    public void CurrentRefreshOperationAndResolvedTopicSurviveQueryBudgetAndEvidenceSelection(string message)
    {
        var corpus = new ReadOnlyKnowledgeFixture();
        var queries = CopilotChatService.BuildKnowledgeQueries(message,
            ["LeftMenu", "LeftMenu datos", "menú lateral"], "LeftMenu", new ActiveSubject("Topic", "LeftMenu"));
        Assert.InRange(queries.Count, 1, 4);
        var candidates = queries.SelectMany(corpus.Search.Search).ToArray();
        Assert.Contains(candidates, item => item.SourceFile == "Funciones para refrescar LM.msg"
            && item.Snippet.Contains("refrescarItemLeftMenu", StringComparison.Ordinal));
        var selected = KnowledgeEvidencePolicy.Select(candidates, "LeftMenu", message,
            out _, requireSubjectMatch: true, allowSubjectMembers: true);
        Assert.Contains(selected, item => item.SourceFile == "Funciones para refrescar LM.msg"
            && item.Snippet.Contains("refrescarItemLeftMenu", StringComparison.Ordinal));
        Assert.InRange(selected.Count, 1, 5);
        Assert.All(selected.GroupBy(item => item.RelativePath), group => Assert.InRange(group.Count(), 1, 3));
    }
}
