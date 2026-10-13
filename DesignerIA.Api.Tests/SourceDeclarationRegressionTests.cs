using System.Text.RegularExpressions;
using DesignerIA.Api.Services;
using DesignerIA.Contracts;
using Xunit;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

// Confirmed in artifacts\eval-100-20261010-closure: G06, G07 and G17 showed "fragmento 1" / "[2]" to the user, and an
// absent or unusable source declaration ended up citing every retrieved fragment.
public sealed partial class PlannerFailureRegressionTests
{
    private static readonly IReadOnlyList<IReadOnlyList<KnowledgeSearchResultItem>> ThreeFragments =
    [
        [new("Grafico HTML5 configuración de Escalas", "DocumentacionEngineNET_V2.6.html", "a", "AxisStart: inicio de escala", 3),
         new("Grafico HTML5 configuración de Escalas", "DocumentacionEngineNET_V2.8.html", "b", "AxisStart: inicio de escala", 3)],
        [new("personalizarJsonGrafico", "DocumentacionEngineNET.html", "c", "function personalizarJsonGrafico(jsonGrafico)", 2)],
        [new("Hoja1", "Handlers.xlsx", "d", "columnaTotalizadora | Agrega columna totalizando", 1)]
    ];

    [Theory]
    [InlineData("FUENTES: 1,3\nSegún la documentación...", "Según la documentación...", "Valid", new[] { 0, 2 })]
    [InlineData("fuentes: 2.\r\n\r\nTexto", "Texto", "Valid", new[] { 1 })]
    [InlineData("FUENTES: 2, 9\nTexto", "Texto", "Valid", new[] { 1 })]
    [InlineData("FUENTES: ninguna\nNo hay evidencia.", "No hay evidencia.", "None", new int[0])]
    [InlineData("Según la documentación\nFUENTES: 1", "Según la documentación\nFUENTES: 1", "Missing", new int[0])]
    [InlineData("Texto sin declaración", "Texto sin declaración", "Missing", new int[0])]
    [InlineData("FUENTES: 7, 9\nTexto", "Texto", "Invalid", new int[0])]
    [InlineData("FUENTES: 0\nTexto", "Texto", "Invalid", new int[0])]
    [InlineData("FUENTES: todas las anteriores\nTexto", "Texto", "Invalid", new int[0])]
    [InlineData("FUENTES:\nTexto", "Texto", "Invalid", new int[0])]
    [InlineData("FUENTES: 1 y 2\nTexto", "Texto", "Invalid", new int[0])]
    public void EachKindOfSourceDeclarationIsToldApart(string content, string body, string state, int[] used)
    {
        var declared = CopilotChatService.ParseDeclaredSources(content, fragments: 3);
        Assert.Equal(body, declared.Body);
        Assert.Equal(state, declared.State.ToString());
        Assert.Equal(used, declared.Used);
    }

    [Fact]
    public void FragmentNumbersInTheAnswerBecomeTitlesAndFiles()
    {
        // The three wordings seen in G07, G17 and G06.
        var resolved = CopilotChatService.ResolveFragmentReferences(
            "El fragmento 1 confirma la sintaxis. Según los fragmentos 1 y 2, aplica. La clave es Columnas en todos los fragmentos ([2], [3]).",
            ThreeFragments);
        const string first = "«Grafico HTML5 configuración de Escalas» (DocumentacionEngineNET_V2.6.html; DocumentacionEngineNET_V2.8.html)";
        const string second = "«personalizarJsonGrafico» (DocumentacionEngineNET.html)";
        Assert.Equal($"El fragmento {first} confirma la sintaxis. Según los fragmentos {first} y {second}, aplica. "
            + $"La clave es Columnas en todos los fragmentos ({second}, «Hoja1» (Handlers.xlsx)).", resolved.Text);
        Assert.Equal([0, 1, 2], resolved.Referenced);
    }

    [Theory]
    // Code, JSON, arrays, quantities and list numbering are not references.
    [InlineData("Ejemplo:\n```json\n{\"Serie\": [1], \"Puntos\": [2]}\n```\nfin")]
    [InlineData("Usa `serie.points[1]` y `[2]` como índice.")]
    [InlineData("{\"Columnas\":\"[1]\", \"Filas\":[2]}")]
    [InlineData("valores = [1, 2, 3] y matriz[1][2]")]
    [InlineData("1. Cargar el IDE CONFIG.\n2. Elegir el handler 4 (body on render).\n3. Usar 2 decimales.")]
    [InlineData("Consulta la nota [7] y el enlace [1](https://ejemplo)")]
    // Reported by the review: data and code without backticks.
    [InlineData("La configuración es {\"Filas\": [2], \"Columnas\": [1]}")]
    [InlineData("var puntos = [1];\nvar otros = [2];")]
    [InlineData("\"Nota\": \"ver el fragmento 1\",")]
    [InlineData("serie.points = [1]\nlista: [2]\nvalores, [3]")]
    public void TextThatIsNotAFragmentReferenceStaysIntact(string text)
    {
        var resolved = CopilotChatService.ResolveFragmentReferences(text, ThreeFragments);
        Assert.Equal(text, resolved.Text);
        Assert.Empty(resolved.Referenced);
    }

    [Fact]
    public void AFragmentThatDoesNotExistIsSaidToBeUnidentifiedInsteadOfGettingAWrongTitle()
    {
        var resolved = CopilotChatService.ResolveFragmentReferences("Según el fragmento 7, se habilita; ver los fragmentos 1 y 8.", ThreeFragments);
        Assert.Equal("Según el fragmento no identificado, se habilita; ver los fragmentos no identificados.", resolved.Text);
        Assert.Empty(resolved.Referenced);
    }

    [Fact]
    public void WithoutADeclarationOnlyFragmentsTheAnswerLetsTheReaderCheckAreVerifiable()
    {
        var artifact = new GroundedArtifact(GroundedArtifactKind.Snippet, "columnaTotalizadora | Agrega columna totalizando", "Knowledge",
            "Handlers.xlsx: Hoja1", "columnaTotalizadora", "Hoja1");
        Assert.Empty(CopilotChatService.VerifiableFragments("AxisStart fija el inicio.", [], ThreeFragments, []));
        Assert.Equal([0], CopilotChatService.VerifiableFragments("Según DocumentacionEngineNET_V2.8.html, AxisStart fija el inicio.", [], ThreeFragments, []));
        Assert.Equal([1], CopilotChatService.VerifiableFragments("Ver la entrada personalizarJsonGrafico.", [], ThreeFragments, []));
        // A short generic title ("Hoja1") is not a reference by itself; reproducing its literal text is.
        Assert.Empty(CopilotChatService.VerifiableFragments("Está en la Hoja1.", [], ThreeFragments, []));
        Assert.Equal([2], CopilotChatService.VerifiableFragments("Agrega una columna.", [artifact], ThreeFragments, []));
        Assert.Equal([1], CopilotChatService.VerifiableFragments("Texto.", [], ThreeFragments, [1]));
        // A longer identifier that merely starts with the title does not name it.
        Assert.Empty(CopilotChatService.VerifiableFragments("Usa personalizarJsonGraficoExtendido.", [], ThreeFragments, []));
        Assert.Empty(CopilotChatService.VerifiableFragments("Según Copia_DocumentacionEngineNET_V2.8.html.", [], ThreeFragments, []));
        // A file that several fragments share does not say which of them is meant; the entry title does.
        IReadOnlyList<IReadOnlyList<KnowledgeSearchResultItem>> sameFile =
            [.. ThreeFragments, [new("iniciarFuncionesPropiasDeLaVista", "DocumentacionEngineNET.html", "e", "se ejecuta al finalizar la carga", 1)]];
        Assert.Empty(CopilotChatService.VerifiableFragments("Según DocumentacionEngineNET.html, se ejecuta al cargar.", [], sameFile, []));
        Assert.Equal([3], CopilotChatService.VerifiableFragments("Según iniciarFuncionesPropiasDeLaVista (DocumentacionEngineNET.html).", [], sameFile, []));
    }

    // ---- Chat level, with the planner and the answer replaced; evidence comes from the real corpus.
    private static readonly SemanticProposal AxisProposal = new("KnowledgeQuestion", "eje Y de un gráfico (inicio y fin)", false, null, "explanation",
        ["AxisY Minimum Maximum gráfico", "configurar inicio fin eje Y chart"], null, null, null,
        SubjectKind: "Topic", SubjectName: "eje Y de un gráfico (inicio y fin)", RequestKind: "Configuration");

    private static async Task<(ChatResult Result, RecordingLogger Logger, FakePlanner Chat, string Prompt)> AskAxisAsync(string modelAnswer)
    {
        var prompt = string.Empty;
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(AxisProposal)),
            "como fijo el inicio y el fin del eje Y de un grafico", seed: false,
            answer: received => { prompt = received; return modelAnswer; });
        Assert.Equal(200, outcome.Http);
        Assert.Equal(1, outcome.Chat.AnswerCalls);
        return (outcome.Result, outcome.Logger, outcome.Chat, prompt);
    }

    private static string FirstFragmentHeader(string prompt) =>
        Regex.Match(prompt, @"^\[1\] (?<header>.+?\)): ", RegexOptions.Multiline).Groups["header"].Value;

    private static string? Field(RecordingLogger logger, string name) =>
        logger.Entries.Single(entry => entry.Fields.ContainsKey("SourceDeclaration") && entry.Fields.ContainsKey("TotalMs")).Fields[name]?.ToString();

    [Fact]
    public async Task AValidDeclarationCitesOnlyItsFragmentsAndTheAnswerShowsTitlesInsteadOfNumbers()
    {
        var asked = await AskAxisAsync("FUENTES: 1\nSegún el fragmento 1, `AxisStart` fija el inicio [1]. Ejemplo:\n```json\n{\"AxisStart\": \"800\", \"Serie\": [1]}\n```");
        var header = FirstFragmentHeader(asked.Prompt);
        Assert.NotEmpty(header);
        Assert.Contains("[2] ", asked.Prompt);
        Assert.Equal("Valid", Field(asked.Logger, "SourceDeclaration"));
        Assert.DoesNotContain("FUENTES", asked.Result.Response);
        Assert.DoesNotContain("fragmento 1", asked.Result.Response);
        Assert.DoesNotContain("inicio [1]", asked.Result.Response);
        Assert.Contains("«", asked.Result.Response);
        Assert.Contains("{\"AxisStart\": \"800\", \"Serie\": [1]}", asked.Result.Response);
        Assert.NotEmpty(asked.Result.Sources!);
        Assert.All(asked.Result.Sources!, source => Assert.Contains($"{source.Title} ({source.SourceFile})", header));
    }

    [Fact]
    public async Task WhenTheModelDeclaresNoSourceTheChatSaysSoAndCitesNothing()
    {
        var asked = await AskAxisAsync("FUENTES: ninguna\nSegún la documentación, AxisStart fija el inicio.");
        Assert.Equal("None", Field(asked.Logger, "SourceDeclaration"));
        Assert.Equal("NoSupportingEvidence", Field(asked.Logger, "FallbackReason"));
        Assert.StartsWith("No encontré en la documentación indexada evidencia", asked.Result.Response);
        Assert.DoesNotContain("AxisStart", asked.Result.Response);
        Assert.True(asked.Result.Sources is null or { Count: 0 });
    }

    [Theory]
    [InlineData("AxisStart fija el inicio de la escala y AxisEnd el fin.", "Missing")]
    [InlineData("FUENTES: 9, 12\nAxisStart fija el inicio de la escala y AxisEnd el fin.", "Invalid")]
    [InlineData("FUENTES: todas\nAxisStart fija el inicio de la escala y AxisEnd el fin.", "Invalid")]
    public async Task WithoutAUsableDeclarationNorAVerifiableReferenceNothingIsCitedAndTheAnswerSaysIt(string modelAnswer, string state)
    {
        var asked = await AskAxisAsync(modelAnswer);
        Assert.Equal(state, Field(asked.Logger, "SourceDeclaration"));
        Assert.Equal("SupportNotVerified", Field(asked.Logger, "FallbackReason"));
        Assert.StartsWith("> No pude verificar qué documentos respaldan esta respuesta", asked.Result.Response);
        Assert.Contains("AxisStart fija el inicio", asked.Result.Response);
        Assert.DoesNotContain("FUENTES", asked.Result.Response);
        Assert.True(asked.Result.Sources is null or { Count: 0 });
    }

    [Fact]
    public async Task WithoutADeclarationADocumentTheAnswerNamesIsStillCitedAndTheOthersAreNot()
    {
        string? named = null;
        var prompt = string.Empty;
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(AxisProposal)),
            "como fijo el inicio y el fin del eje Y de un grafico", seed: false,
            answer: received =>
            {
                prompt = received;
                named = Regex.Match(FirstFragmentHeader(received), @"^(?<title>.+?) \(").Groups["title"].Value;
                return $"Según {named}, AxisStart fija el inicio de la escala.";
            });
        Assert.NotNull(named);
        Assert.DoesNotContain("No pude verificar", outcome.Result.Response);
        Assert.NotEmpty(outcome.Result.Sources!);
        Assert.True(named!.Length >= 12, "the corpus entry used here has a distinctive title");
        Assert.All(outcome.Result.Sources!, source => Assert.Equal(named, source.Title));
        Assert.Contains("[2] ", prompt);
        Assert.Equal("Missing", Field(outcome.Logger, "SourceDeclaration"));
        Assert.Equal("None", Field(outcome.Logger, "FallbackReason"));
    }

    [Fact]
    public async Task WhenTheModelReturnsNothingNoRetrievedDocumentIsCited()
    {
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(AxisProposal)),
            "como fijo el inicio y el fin del eje Y de un grafico", seed: false, answer: _ => null);
        Assert.Equal(1, outcome.Chat.AnswerCalls);
        Assert.Equal("error", outcome.Result.Status);
        Assert.True(outcome.Result.Sources is null or { Count: 0 });
    }

    [Fact]
    public async Task AnExclusiveFormatIsNeverRewrittenNorGivenANotice()
    {
        var code = AxisProposal with { ResponseMode = "code" };
        var outcome = await RunAsync((_, _) => Task.FromResult<string?>(ProposalJson(code)),
            "dame solo el codigo para fijar el inicio y el fin del eje Y de un grafico", seed: false,
            answer: _ => "var puntos = [1];\nvar otros = [2];");
        var text = outcome.Result.Response + outcome.Result.Error;
        Assert.Equal(1, outcome.Chat.AnswerCalls);
        Assert.NotEmpty(text);
        Assert.DoesNotContain("«", text);
        Assert.DoesNotContain("No pude verificar", text);
        Assert.True(outcome.Result.Sources is null or { Count: 0 });
    }
}
