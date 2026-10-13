using System.Text.RegularExpressions;
using DesignerIA.Api.Services;
using DesignerIA.Api.Services.Knowledge;
using DesignerIA.Contracts;
using Xunit;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

/// <summary>
/// Retrieval without the model, over the existing corpus: the documented fragment must reach the evidence selected
/// for the answer. Questions come from the 100-message evaluation (artifacts\eval-100-20261009) plus rewordings.
/// </summary>
public sealed class KnowledgeEvidenceSelectionRegressionTests
{
    private static readonly KnowledgeSearchService Knowledge = new ReadOnlyKnowledgeFixture().Search;

    private static IReadOnlyList<KnowledgeSearchResultItem> SelectFor(
        string message, string topic, IReadOnlyList<string>? plannerQueries = null, bool documentary = false) =>
        SelectFor(message, topic, out _, plannerQueries, documentary);

    private static IReadOnlyList<KnowledgeSearchResultItem> SelectFor(
        string message, string topic, out string? conflict, IReadOnlyList<string>? plannerQueries = null, bool documentary = false,
        string? subjectName = null)
    {
        var subject = new ActiveSubject("Topic", subjectName ?? topic);
        var queries = CopilotChatService.BuildKnowledgeQueries(message, plannerQueries ?? [], topic, subject);
        var candidates = queries.SelectMany(query => Knowledge.SearchWide(query)).ToList();
        var hints = string.Join(" ", queries.Except(plannerQueries ?? [], StringComparer.OrdinalIgnoreCase));
        var planner = string.Join(" ", plannerQueries ?? []);
        return KnowledgeEvidencePolicy.Select(candidates, subject.Name, topic + " " + message, out conflict,
            requireSubjectMatch: documentary, allowSubjectMembers: true,
            coverageHints: hints, termWeights: Knowledge.TermWeights(topic + " " + message + " " + hints + " " + planner));
    }

    private static bool Supports(KnowledgeSearchResultItem item, string file, string marker) =>
        Regex.IsMatch(item.SourceFile, file, RegexOptions.IgnoreCase) && item.Snippet.Contains(marker, StringComparison.OrdinalIgnoreCase);

    // The topic is the name the planner gave in the evaluation run; only the user's wording and that name are searched.
    [Theory]
    [InlineData("como limito los comentarios a una sola columna de la grilla", "limitar comentarios a una columna de la grilla", "columna especificada", "desvio total")]
    [InlineData("se pueden poner comentarios en la columna de referencia de una grilla?", "comentarios en columna de referencia de una grilla", "Comentarios en Reference", "Reference:")]
    [InlineData("como cambio el fondo del tooltip de un grafico a negro", "tooltip de gráfico", "Color de Fondo Tooltip", "chartNewTooltip")]
    [InlineData("quiero que el tooltip del grafico tenga fondo negro", "tooltip con fondo negro", "Color de Fondo Tooltip", "chartNewTooltip")]
    [InlineData("como activo el cache en archivo para los SP de una vista", "cache en archivo para SP de una vista", "CacheEnArchivo", "VistasCache")]
    [InlineData("necesito cachear en archivos los stored procedures de una vista", "cache en archivos de stored procedures", "CacheEnArchivo", "VistasCache")]
    [InlineData("como distribuyo por mail la bajada a Excel de una vista", "distribución por mail de la bajada a Excel de una vista", "Descarga autom", "ExportacionExcelPorId")]
    [InlineData("como hago para que los comentarios de una vista no queden atados a un usuario", "comentarios no atados a un usuario", "no atado", "NoAtado")]
    [InlineData("cuales son los pasos en el IDE CONFIG para activar comentarios en celdas", "IDE CONFIG comentarios en celdas", "Documentacion|Wiki", "Aplicar Acciones Grilla")]
    [InlineData("como dejo un radio button seleccionado por default", "radio button seleccionado por default", "Radio", "Comod")]
    [InlineData("como agrego bolitas a las series de mi grafico", "marcadores de series en gráfico", "marca|Documentacion", "CIRCLE: 0")]
    [InlineData("cuales son los tipos de marca de una serie y sus valores", "tipos de marca de una serie", "marca", "DIAMOND: 1")]
    [InlineData("y en grillas?", "comentarios en grillas", "Comentarios en las grillas", "JsonColumnasYRenglones")]
    public void TheDocumentedFragmentReachesTheSelectedEvidenceFromTheUsersOwnWording(string message, string topic, string file, string marker)
    {
        var selected = SelectFor(message, topic);
        Assert.Contains(selected, item => Supports(item, file, marker));
    }

    [Fact]
    public void AGenericDocumentedSubjectDoesNotHideTheFragmentThatAnswersTheQuestion()
    {
        // Evaluation G01: the planner named the subject just "gráfico"; accents and plural must not exclude the source.
        var selected = SelectFor("Necesito saber como agregar o implementar comentario a mi grafico", "gráfico",
            ["agregar comentario gráfico", "anotación en serie gráfico", "comment annotation chart GestionEngine"], documentary: true);
        Assert.Contains(selected, item => Supports(item, "Comentarios en gr", "comentariosEnGrafico"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARewordedTopicNameDoesNotChangeWhichSourceAnswers(bool documentary)
    {
        // Second real run of G01: the planner named the topic "comentario en gráfico" and the answer drifted to grid comments.
        var selected = SelectFor("Necesito saber como agregar o implementar comentario a mi grafico", "comentario en gráfico",
            ["agregar comentario gráfico", "anotación en gráfico Serie", "configurar comentario chart GestionEngine"], documentary);
        Assert.Contains(selected, item => Supports(item, "Comentarios en gr", "comentariosEnGrafico"));
        Assert.DoesNotContain(selected, item => item.Title.Contains("celdas de la grilla", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MarkerTypesAreListedInsteadOfReportingAnotherEnumerationAsAConflict()
    {
        // Real run of G27 after widening the candidate pool: a different enumeration (series types) triggered a conflict notice.
        var selected = SelectFor("cuales son los tipos de marca de una serie y sus valores", "tipos de marca de una serie", out var conflict,
            ["TipoSerie valores marca", "tipos de marca serie grafico", "MarkerSize tipo marca punto linea"], documentary: true);
        Assert.Contains(selected, item => Supports(item, "marca", "DIAMOND: 1"));
        Assert.Null(conflict);
    }

    [Fact]
    public void TwoVersionsOfTheSameEnumerationThatDifferInOneValueAreStillAConflict()
    {
        var older = new KnowledgeSearchResultItem("Tipos de marca", "v1.html", "v1.html", "CIRCLE: 0, DIAMOND: 1, PIRAMID: 2, SQUARE: 5, LINE: 3", 5);
        var newer = new KnowledgeSearchResultItem("Tipos de marca", "v2.html", "v2.html", "CIRCLE: 0, DIAMOND: 1, PIRAMID: 2, SQUARE: 5, LINE: 9", 5);
        KnowledgeEvidencePolicy.Select([older, newer], null, "cuales son los valores del tipo de marca LINE", out var conflict);
        Assert.NotNull(conflict);
        Assert.Contains("LINE", conflict);
    }

    [Fact]
    public void TwoVersionsThatAlsoAddAndRemoveMembersAreStillAConflict()
    {
        var older = new KnowledgeSearchResultItem("Tipos de marca", "v1.html", "v1.html", "CIRCLE: 0, DIAMOND: 1, PIRAMID: 2, LINE: 3, SQUARE: 5, CROSS: 4", 5);
        var newer = new KnowledgeSearchResultItem("Tipos de marca", "v2.html", "v2.html", "CIRCLE: 0, DIAMOND: 1, PIRAMID: 2, LINE: 9, STAR: 6, HEXAGON: 7", 5);
        KnowledgeEvidencePolicy.Select([older, newer], null, "cuales son los valores del tipo de marca LINE", out var conflict);
        Assert.NotNull(conflict);
        Assert.Contains("LINE", conflict);
    }

    [Fact]
    public void AFragmentBelowTheMinimumShareIsNotCitedNextToTheOneThatAnswers()
    {
        var answer = new KnowledgeSearchResultItem("Tipografía", "tema.msg", "tema.msg",
            "Significa que la tipografía predeterminada de la vista se hereda del tema", 5);
        var noise = new KnowledgeSearchResultItem("Vista", "vista.msg", "vista.msg",
            "La vista se abre desde el menú de la vista; selecciona la vista", 9);
        var selected = KnowledgeEvidencePolicy.Select([noise, answer], null, "que significa tipografia predeterminada de una vista", out _);
        Assert.Equal([answer], selected);
    }

    [Fact]
    public void ALongTermMatchesItsCognateAtTheStartOfAWordOnly()
    {
        Assert.Equal(1, KnowledgeSearchService.CountTerm("la correlacion de relaciones entre vistas", "relaciones"));
        Assert.Equal(1, KnowledgeSearchService.CountTerm("nombrecolumnas", "columnas"));
        Assert.Equal(0, KnowledgeSearchService.CountTerm("nombrecolumna", "columnas"));
    }

    // Evaluation G17. The corpus says "Indica el inicio de escala en eje Y" / "Valor mínimo del eje" next to AxisStart.
    [Theory]
    [InlineData("como fijo el inicio y el fin del eje Y de un grafico", "eje Y de gráfico - límites inicio y fin")]
    [InlineData("como fijo el inicio y el fin del eje Y de un grafico", "eje Y de un gráfico (inicio y fin)")]
    [InlineData("quiero que el eje Y del grafico empiece en 100, como indico el inicio de la escala", "escala del eje Y")]
    [InlineData("cual es el valor minimo y maximo que puedo fijar en el eje de un grafico", "valor mínimo y máximo del eje")]
    public void AxisRangeQuestionsReachTheAxisStartAndAxisEndDocumentation(string message, string topic)
    {
        var selected = SelectFor(message, topic);
        Assert.Contains(selected, item => item.Snippet.Contains("AxisStart", StringComparison.Ordinal)
            && item.Snippet.Contains("AxisEnd", StringComparison.Ordinal)
            && Regex.IsMatch(item.Snippet, "inicio de escala|Valor mínimo del eje", RegexOptions.IgnoreCase));
    }

    // Evaluation G09, with the planner output of the last real run: it referenced the previous topic (comments on the
    // reference column) and its queries carried a rare catalogue name that must not drag the evidence elsewhere.
    [Theory]
    [InlineData("como limito los comentarios a una sola columna de la grilla")]
    [InlineData("quiero que los comentarios apliquen solo a una columna especifica")]
    public void ASingleColumnCommentQuestionReachesItsExampleEvenUnderThePreviousTopic(string message)
    {
        var selected = SelectFor(message, "comentarios en columna de referencia de una grilla",
            ["JsonColumnasYRenglones Columna específica comentarios", "ComentariosEnCeldas limitar columna", "configuración comentarios por columna grilla"]);
        Assert.Contains(selected, item => Supports(item, "columna especificada", "desvio total"));
    }

    [Fact]
    public void AShortTermOnlyMatchesAsAWholeWord()
    {
        Assert.Equal(0, KnowledgeSearchService.CountTerm("un ejemplo que define el final", "eje"));
        Assert.Equal(0, KnowledgeSearchService.CountTerm("un ejemplo que define el final", "fin"));
        Assert.Equal(2, KnowledgeSearchService.CountTerm("inicio de escala en eje y fin de escala eje", "eje"));
        Assert.Equal(1, KnowledgeSearchService.CountTerm("clickenceldagrilla", "celda"));
        Assert.Equal(1, KnowledgeSearchService.CountTerm("comentarios en reference msg", "referencia"));
        Assert.Equal(0, KnowledgeSearchService.CountTerm("comentarios en las grillas", "referencia"));
    }

    [Fact]
    public void PlannerQueriesCannotPushTheUsersQuestionOutOfTheSearchBudget()
    {
        const string message = "como activo el cache en archivo para los SP de una vista";
        var queries = CopilotChatService.BuildKnowledgeQueries(message,
            ["cache archivo stored procedure vista", "file cache SP vista configuración", "caché SP disco vista GestionEngine"],
            "cache en archivo para SP de una vista", new ActiveSubject("Topic", "cache en archivo para SP de una vista"));
        Assert.Equal(message, queries[0]);
        Assert.InRange(queries.Count, 1, 4);
    }

    [Fact]
    public void ADocumentedPropertyIsFoundWhenThePlannerAddsItsVocabulary()
    {
        // Evaluation G29: the definition lives in a Wiki entry titled for the grid, not for the property.
        var selected = SelectFor("para que sirve la opcion Ordenar Columnas de una grilla", "Ordenar Columnas",
            ["Ordenar Columnas grilla configuración", "ordenamiento de columnas en grilla Designer"], documentary: true);
        Assert.Contains(selected, item => item.Snippet.Contains("Habilita o deshabilita el ordenamiento", StringComparison.OrdinalIgnoreCase));
    }

    // Real run of evaluation G20 (2026-10-10, targeted): with this planner proposal the catalogue row was not even a
    // candidate. The question says "fondo"; the row says "Setea el Background-Color de las celdas de una columna".
    [Theory]
    [InlineData("como pinto el fondo de las celdas de una columna")]
    public void TheBackgroundQuestionReachesTheCatalogueRowWithItsParameters(string message)
    {
        var selected = SelectFor(message, "color de fondo de celdas por columna", out _,
            ["pintar fondo celda columna", "color de fondo celda grid", "background color columna WebControls"],
            subjectName: "configuración de color de celdas por columna");
        Assert.Contains(selected, item => item.Snippet.Contains("ColorCelda", StringComparison.Ordinal)
            && item.Snippet.Contains("Setea el Background-Color de las celdas de una columna", StringComparison.Ordinal)
            && item.Snippet.Contains("9:#D8D8D8", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSourceTermIsOnlyARewordingOfTheQuestion()
    {
        Assert.Equal(["como pinto el Background-Color de las celdas"], RetrievalQueryExpansion.Reword("como pinto el fondo de las celdas"));
        Assert.Empty(RetrievalQueryExpansion.Reword("como cambio el ancho de una columna"));
    }

    // A grid-comments mail whose title names neither grids nor cells (real: "RE: Edición comentarios vistas gestión").
    [Theory]
    [InlineData("como agrego comentarios a un grafico", "handler ComentariosEnCelda con JsonColumnasYRenglones y el parametro NoAtado para comentarios", false)]
    [InlineData("como agrego comentarios a un grafico", "se agrego ChartComments aparte de GridComments.js para los comentarios del grafico", true)]
    [InlineData("como agrego comentarios a una grilla", "la propiedad comentariosEnGrafico en true habilita los comentarios", false)]
    [InlineData("como agrego comentarios a una grilla", "los comentarios requieren permisos de edicion", true)]
    public void AFragmentBelongsToAFeatureAlsoByTheIdentifiersItNames(string question, string snippet, bool kept)
    {
        var item = new KnowledgeSearchResultItem("RE: Edición comentarios vistas gestión", "mail.msg", "mail.msg", snippet, 5);
        Assert.Equal(kept, KnowledgeEvidencePolicy.Select([item], null, question, out _).Count == 1);
    }

    [Theory]
    [InlineData("Necesito saber como agregar o implementar comentario a mi grafico", "comentarios en gráfico", "ComentariosEnCelda|JsonColumnasYRenglones", "comentariosEnGrafico|ChartComments|personalizarJsonGrafico")]
    [InlineData("como agrego comentarios a las celdas de una grilla", "comentarios en grilla", "comentariosEnGrafico|ChartComments", "ComentariosEnCelda|GridComments|JsonColumnasYRenglones")]
    public void OnTheCorpusNoFragmentOfTheOtherFeatureIsSelected(string message, string topic, string other, string own)
    {
        var selected = SelectFor(message, topic);
        Assert.NotEmpty(selected);
        // Whatever its title says, no selected fragment names only the other feature's identifiers.
        Assert.DoesNotContain(selected, item => Regex.IsMatch(item.Snippet, other) && !Regex.IsMatch(item.Snippet, own));
    }

    [Fact]
    public void ADocumentedEntryNamedLikeTheSubjectDoesNotHideTheSourceThatHasTheSyntax()
    {
        // Real run of evaluation G12 (2026-10-10): the active subject was named exactly like a Wiki entry and its
        // chunks, repeated in three versions, filled the five places; the mail with the documented syntax was left out.
        var selected = SelectFor("dame un JSON válido para configurar ComentariosEnCelda en todas las columnas", "Comentarios en celdas de la grilla",
            out _, ["JsonColumnasYRenglones todas las columnas ejemplo", "ComentariosEnCelda Columna todas JSON", "configuración JSON todas columnas comentarios grilla"],
            documentary: true, subjectName: "comentarios en celdas de la grilla");
        Assert.Contains(selected, item => item.Snippet.Contains("{\"JsonColumnasYRenglones\":\"[{Columna:\"Todas\",Renglon:\"Todos\"}]\"}", StringComparison.Ordinal));
        Assert.InRange(selected.Count(item => item.Title == "Comentarios en celdas de la grilla"), 1, 4);
    }

    [Fact]
    public void AnEntryPublishedInManyFilesTakesPlacesFromAtMostTwoOfThem()
    {
        KnowledgeSearchResultItem Chunk(string file, string part) => new("Comentarios en celdas", file, file, "comentarios en celdas de la grilla " + part, 9);
        var other = new KnowledgeSearchResultItem("Comentarios en las grillas", "mail.msg", "mail.msg", "comentarios en celdas de la grilla: sintaxis", 1);
        var selected = KnowledgeEvidencePolicy.Select(
            [Chunk("V2.6.html", "a"), Chunk("V2.8.html", "a"), Chunk("V2.6.html", "b"), Chunk("V2.8.html", "b"), Chunk("V4.html", "a"), Chunk("Wiki.html", "a"), other],
            null, "comentarios en celdas de la grilla", out _);
        Assert.InRange(selected.Where(item => item.Title == "Comentarios en celdas").Select(item => item.SourceFile).Distinct().Count(), 1, 2);
        Assert.Contains(other, selected);
    }

    [Fact]
    public void VersionsOfTheSameWikiEntryCannotFillTheEvidence()
    {
        KnowledgeSearchResultItem Version(string file, double score) => new("HBOR.ColorCelda", file, file,
            "ColorCelda : params: oNode, cell. Setea el color de fondo de la celda. " + file, score);
        var catalogue = new KnowledgeSearchResultItem("Hoja1", "Handlers.xlsx", "Handlers.xlsx",
            "ColorCelda | Setea el Background-Color de las celdas de una columna, ejemplo { \"Columnas\":\"9:#D8D8D8\"} | BodyOnRender", 1);
        var selected = KnowledgeEvidencePolicy.Select(
            [Version("V2.4.html", 9), Version("V2.6.html", 9), Version("V2.8.html", 9), Version("V4.html", 9), Version("Wiki.html", 9), catalogue],
            "ColorCelda", "ColorCelda como pinto el fondo de las celdas de una columna", out _, requireSubjectMatch: true);
        Assert.Equal(2, selected.Count(item => item.Title == "HBOR.ColorCelda"));
        Assert.Contains(catalogue, selected);
    }

    [Fact]
    public void IdenticalTextFromTwoFilesKeepsBothProvenances()
    {
        var one = new KnowledgeSearchResultItem("ACCIONES HANDLER", "a.xlsx", "a.xlsx", "HandlerA | {\"Columnas\":\"Todas\"}", 3);
        var two = one with { SourceFile = "b.xlsx", RelativePath = "b.xlsx" };
        Assert.Equal(2, KnowledgeEvidencePolicy.Select([one, two], "HandlerA", "HandlerA parametros", out _, requireSubjectMatch: true).Count);
    }

    [Theory]
    [InlineData("como agrego comentarios a un grafico", "Comentarios en gráficos", "Comentarios en las grillas")]
    [InlineData("como agrego comentarios a una grilla", "Comentarios en las grillas", "Comentarios en gráficos")]
    public void ChartAndGridCommentsStaySeparateWithoutDemandingTopicWordsInEveryTitle(string question, string kept, string excluded)
    {
        var charts = new KnowledgeSearchResultItem("Comentarios en gráficos", "charts.msg", "charts.msg", "agregar comentarios comentariosEnGrafico = true", 5);
        var grids = new KnowledgeSearchResultItem("Comentarios en las grillas", "grids.msg", "grids.msg", "agregar comentarios handler ComentariosEnCelda", 5);
        var neutral = new KnowledgeSearchResultItem("Permisos de comentarios", "permisos.msg", "permisos.msg", "para agregar comentarios hay que dar permisos de edición", 5);
        var selected = KnowledgeEvidencePolicy.Select([charts, grids, neutral], null, question, out _);
        Assert.Contains(selected, item => item.Title == kept);
        Assert.DoesNotContain(selected, item => item.Title == excluded);
    }

    [Theory]
    [InlineData("como agrego comentarios a un grafico", "Comentarios en las grillas", "grids.msg")]
    [InlineData("como agrego comentarios a una grilla", "Comentarios en gráficos", "charts.msg")]
    public void WhenOnlyTheOtherFeatureIsDocumentedNoEvidenceIsSelected(string question, string title, string file)
    {
        var other = new KnowledgeSearchResultItem(title, file, file, "para agregar comentarios se configura la funcionalidad", 5);
        Assert.Empty(KnowledgeEvidencePolicy.Select([other], null, question, out _));
    }

    // Nothing titled for the other feature is cited.
    [Theory]
    [InlineData("como agrego comentarios a un grafico", "comentarios en gráfico", "Comentarios en gr", "grilla|celda")]
    [InlineData("Necesito saber como agregar o implementar comentario a mi grafico", "comentarios en gráfico", "Comentarios en gr", "grilla|celda")]
    [InlineData("como agrego comentarios a las celdas de una grilla", "comentarios en grilla", "Comentarios en celdas|Comentarios en las grillas", "gr[aá]fico")]
    public void OnTheCorpusChartAndGridCommentQuestionsCiteTheirOwnFeature(string message, string topic, string own, string other)
    {
        var selected = SelectFor(message, topic);
        Assert.Contains(selected, item => Regex.IsMatch(item.Title + " " + item.SourceFile, own, RegexOptions.IgnoreCase));
        Assert.DoesNotContain(selected, item => Regex.IsMatch(item.Title + " " + item.SourceFile, other, RegexOptions.IgnoreCase)
            && !Regex.IsMatch(item.Title + " " + item.SourceFile, own, RegexOptions.IgnoreCase));
    }

    [Fact]
    public void AQuestionComparingChartsAndGridsKeepsBothFeatures()
    {
        // Evaluation G10.
        var selected = SelectFor("cual es la diferencia entre comentarios en graficos y comentarios en grillas", "comentarios en gráficos y en grillas");
        Assert.Contains(selected, item => item.SourceFile.StartsWith("Comentarios en gr", StringComparison.Ordinal));
        Assert.Contains(selected, item => item.SourceFile.StartsWith("Comentarios en las grillas", StringComparison.Ordinal));
    }

    // Nothing in the corpus documents this: the common words "vista" and "configuro" are not evidence of it.
    [Fact]
    public void AQuestionOutsideTheCorpusSelectsNoEvidence()
    {
        Assert.Empty(SelectFor("como configuro el teletransporte cuantico de una vista", "teletransporte cuántico de una vista", ["teletransporte cuantico"]));
    }

    [Fact]
    public void ATitleThatRepeatsOneCommonWordOfTheQuestionIsNotEvidenceOfIt()
    {
        var onlyTheCommonWord = new KnowledgeSearchResultItem("Vista", "vista.msg", "vista.msg", "la vista se abre desde el menu de la vista", 9);
        Assert.Empty(KnowledgeEvidencePolicy.Select([onlyTheCommonWord], null, "como configuro el teletransporte cuantico de una vista", out _));
    }

    [Fact]
    public void FragmentsThatBarelyTouchTheQuestionAreNotCitedNextToTheOneThatAnswersIt()
    {
        var answer = new KnowledgeSearchResultItem("Color de Fondo Tooltip", "tooltip.msg", "tooltip.msg",
            "pinta el fondo de negro declarando var chartNewTooltip = true en el JS del grafico", 4);
        var other = new KnowledgeSearchResultItem("Series superpuestas", "series.msg", "series.msg", "configuracion de series del grafico", 9);
        var selected = KnowledgeEvidencePolicy.Select([other, answer], null, "como cambio el fondo del tooltip de un grafico a negro", out _);
        Assert.Equal([answer], selected);
    }

    [Fact]
    public void ARareTermInTheBodyOutranksAGenericWordInManyTitles()
    {
        var results = Knowledge.SearchWide("ComentariosEnCelda columna referencia");
        Assert.Contains(results, item => item.SourceFile.StartsWith("Comentarios", StringComparison.Ordinal));
        Assert.InRange(Knowledge.Search("ComentariosEnCelda columna referencia").Count, 1, 5);
    }
}
