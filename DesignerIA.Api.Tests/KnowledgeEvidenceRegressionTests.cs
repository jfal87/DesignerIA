using DesignerIA.Api.Services;
using Xunit;

namespace DesignerIA.Api.Tests;

public sealed class KnowledgeEvidenceRegressionTests
{
    [Fact]
    public void ConfirmedCellSubjectIsSelectedBeforeRelatedChartEvidence()
    {
        var cells = ReadOnlyKnowledgeFixture.Document("Wiki\\DocumentacionEngineNET_V2.6.html::chunk-1082");
        var charts = ReadOnlyKnowledgeFixture.Document("WikiData\\Comentarios en gráficos y Error motor ordenar columnas.msg::chunk-0") with { Score = 20 };
        var selected = KnowledgeEvidencePolicy.Select([cells, charts], "ComentariosEnCelda",
            "Necesito configurar comentarios en celdas, no comentarios de gráficos.", out _, requireSubjectMatch: true);
        Assert.Equal(cells, Assert.Single(selected));
    }

    [Fact]
    public void NegatedChartTopicCannotSuppressCellEvidenceWithoutACanonicalName()
    {
        var cells = ReadOnlyKnowledgeFixture.Document("Wiki\\DocumentacionEngineNET_V2.6.html::chunk-1082");
        var charts = ReadOnlyKnowledgeFixture.Document("WikiData\\Comentarios en gráficos y Error motor ordenar columnas.msg::chunk-0");
        var selected = KnowledgeEvidencePolicy.Select([cells, charts], null,
            "Necesito configurar comentarios en celdas, no comentarios de gráficos.", out _);
        Assert.Equal(cells, Assert.Single(selected));
    }

    [Fact]
    public void ActualChartRequestsKeepChartEvidence()
    {
        var cells = ReadOnlyKnowledgeFixture.Document("Wiki\\DocumentacionEngineNET_V2.6.html::chunk-1082");
        var charts = ReadOnlyKnowledgeFixture.Document("WikiData\\Comentarios en gráficos y Error motor ordenar columnas.msg::chunk-0");
        var selected = KnowledgeEvidencePolicy.Select([cells, charts], null,
            "Necesito comentarios de gráficos, no comentarios en celdas.", out _);
        Assert.Equal(charts, Assert.Single(selected));
    }

    [Fact]
    public void MissingCanonicalHandlerEvidenceCannotBecomeAGenericStoredProcedureContract()
    {
        var generic = ReadOnlyKnowledgeFixture.Document("Wiki\\DocumentacionEngineNET.html::chunk-25");
        var selected = KnowledgeEvidencePolicy.Select([generic], "ComentariosEnCelda",
            "Qué parámetros recibe el handler", out _, requireSubjectMatch: true);
        Assert.Empty(selected);
    }

    [Fact]
    public void ScopeMatchingIncludesDocumentedCamelCaseMembersOfTheResolvedTopic()
    {
        var refresh = ReadOnlyKnowledgeFixture.Document("WikiData\\Funciones para refrescar LM.msg::chunk-0");
        var setup = new DesignerIA.Contracts.KnowledgeSearchResultItem("LeftMenu búsqueda", "setup.msg", "setup.msg",
            "LeftMenu permite filtros por URL", 1);
        var selected = KnowledgeEvidencePolicy.Select([refresh, setup], "LeftMenu", "recargar LeftMenu", out _, requireSubjectMatch: true, allowSubjectMembers: true);
        Assert.Contains(refresh, selected);
    }

    [Fact]
    public void RequestedOperationIsSelectedBeforeHigherScoringSetupEvidence()
    {
        var refresh = ReadOnlyKnowledgeFixture.Document("WikiData\\Funciones para refrescar LM.msg::chunk-0");
        var setup = new DesignerIA.Contracts.KnowledgeSearchResultItem("LeftMenu búsqueda", "setup.msg", "setup.msg",
            "LeftMenu permite filtros por URL", 100);
        var selected = KnowledgeEvidencePolicy.Select([setup, refresh], "LeftMenu", "recargar LeftMenu", out _,
            requireSubjectMatch: true, allowSubjectMembers: true);
        Assert.Equal(refresh, Assert.Single(selected));
    }

    [Fact]
    public void MissingRequestedOperationKeepsScopedEvidenceWithoutInventingAnApi()
    {
        var setup = new DesignerIA.Contracts.KnowledgeSearchResultItem("LeftMenu búsqueda", "setup.msg", "setup.msg",
            "LeftMenu permite filtros por URL", 100);
        var selected = KnowledgeEvidencePolicy.Select([setup], "LeftMenu", "recargar LeftMenu", out _,
            requireSubjectMatch: true, allowSubjectMembers: true);
        Assert.Equal(setup, Assert.Single(selected));
    }
}
