using System.Text.Json;
using System.Text.Json.Nodes;
using DesignerIA.Api.Services.ViewDocuments;
using DesignerIA.Api.Services.ViewScripts;
using DesignerIA.Contracts;
using Xunit;

namespace DesignerIA.Api.Tests;

public class JsonMetadataJavaScriptTests
{
    private const string AuditUser = "TEST\\DesignerIA";
    private const string GridDocument = """
        {"schemaVersion":"1.0","view":{"id":"vJsDemo","type":"go"},"states":[
          {"name":"Principal","isDefault":true,"rows":[{"areas":[{"name":"AreaPrincipal","object":{"type":"grid","data":{"connection":"GestionEngine","storedProcedure":"dbo.PRC_GET_GRID_TEST_SORT_COLUMN"}}}]}]},
          {"name":"Detalle","rows":[{"areas":[{"name":"AreaDetalle","object":{"type":"grid","data":{"connection":"GestionEngine","storedProcedure":"dbo.PRC_GET_GRID_TEST_SORT_COLUMN"}}}]}]}
        ]}
        """;

    [Theory]
    [InlineData("")]
    [InlineData("function Inicializar() { return 'demo'; }")]
    [InlineData("var contador = 1;\r\nfunction clickEnWC() { contador += 1; }")]
    public void JavaScriptIsPreservedInTheEffectiveDocumentAndTypedContract(string code)
    {
        var reading = Read(code);
        Assert.Empty(reading.Diagnostics);
        Assert.Equal(code, reading.Document!["view"]!["js"]!.GetValue<string>());
        Assert.Equal(code, reading.Document.Deserialize<JsonMetadataDocument>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!.View!.Js);
        var expanded = JsonMetadataService.ReadDocument(reading.EffectiveText);
        Assert.Empty(expanded.Diagnostics);
        Assert.Equal(reading.EffectiveText, expanded.EffectiveText);
    }

    [Theory]
    [InlineData("var value = \"demo\"; function clickEnWC() { return 'clicked'; }")]
    [InlineData("var n = null; // nullable value\nvar text = 'niño';")]
    [InlineData("var x = \"'); DROP TABLE dbo.Vistas; --\";")]
    public void ScriptStoresDesignerEncodingAndMotorRestoresTheOriginalCode(string code)
    {
        var plan = Plan(Read(code));
        var encoded = plan.View.JsCode;
        Assert.DoesNotContain("'", encoded);
        Assert.DoesNotContain("\"", encoded);
        Assert.Equal(code, MotorDecode(encoded));
        var script = ViewScriptGenerator.BuildScript(plan, AuditUser);
        Assert.Contains($"NULL, NULL, {SqlLiteralEscaper.Literal(encoded)}, N'', N'1.0');", script);
        Assert.Equal(2, plan.StateBlocks.Count);
        Assert.Equal(encoded, ViewCreationVerifier.ExpectedValues(plan, AuditUser)["Vistas.JsCode"]);
    }

    [Fact]
    public void EncodingMatchesTheMarkersWrittenByDesigner()
    {
        Assert.Equal(@"var s = \u0022demo\u0022; var n = \u0000; f(\u0027x\u0027);",
            ViewJavaScriptMetadata.Encode("var s = \"demo\"; var n = null; f('x');"));
    }

    [Fact]
    public void OmittedAndEmptyJavaScriptKeepThePreviousSql()
    {
        var omitted = Plan(JsonMetadataService.ReadDocument(GridDocument));
        var empty = Plan(Read(string.Empty));
        Assert.Empty(omitted.View.JsCode);
        Assert.Equal(ViewScriptGenerator.BuildScript(omitted, AuditUser), ViewScriptGenerator.BuildScript(empty, AuditUser));
    }

    [Theory]
    [InlineData("null", JsonMetadataSeverity.Pendiente)]
    [InlineData("{}", JsonMetadataSeverity.Error)]
    [InlineData("[]", JsonMetadataSeverity.Error)]
    [InlineData("42", JsonMetadataSeverity.Error)]
    [InlineData("true", JsonMetadataSeverity.Error)]
    public void UnknownOrNonTextJavaScriptCannotReachThePlan(string value, JsonMetadataSeverity severity)
    {
        var document = JsonNode.Parse(GridDocument)!;
        document["view"]!["js"] = JsonNode.Parse(value);
        var diagnostic = Assert.Single(JsonMetadataService.ReadDocument(document.ToJsonString()).Diagnostics);
        Assert.Equal("view.js", diagnostic.Path);
        Assert.Equal(severity, diagnostic.Severity);
    }

    [Theory]
    [InlineData(@"var text = '\u0027';")]
    [InlineData(@"var text = '\u0022';")]
    [InlineData(@"var text = '\u0000';")]
    [InlineData("var text = '</ScRiPt>'; ")]
    [InlineData("var text = '\0';")]
    public void CodeThatMotorWouldCorruptOrCutOffIsRejected(string code)
    {
        var diagnostic = Assert.Single(Read(code).Diagnostics);
        Assert.Equal("view.js", diagnostic.Path);
        Assert.Equal(JsonMetadataSeverity.Error, diagnostic.Severity);
        Assert.Equal("JavaScriptNoRepresentable", diagnostic.Code);
    }

    [Fact]
    public void CreationVerificationRequiresTheJavaScriptInEveryStateSnapshot()
    {
        var plan = Plan(Read("var contador = 1; function clickEnWC() { contador += 1; }"));
        var snapshots = plan.StateBlocks.ToDictionary(block => block.State.Name,
            block => ViewCreationVerifier.ExpectedStateValues(plan, block, AuditUser));
        var snapshot = new ViewCreationSnapshot(ViewCreationVerifier.ExpectedCounts(plan), 2,
            ViewCreationVerifier.ExpectedValues(plan, AuditUser), [], [], snapshots, [], []);
        var completed = new ScriptExecution(ScriptExecutionKind.Completed, [], TranCountAfter: 0);
        Assert.Equal(ViewCreationStatus.Created, ViewCreationService.Decide(plan, AuditUser, completed, snapshot).Status);

        var wrong = new Dictionary<string, string?>(snapshots["Detalle"]) { ["Vistas.JsCode"] = "" };
        snapshots["Detalle"] = wrong;
        var result = ViewCreationService.Decide(plan, AuditUser, completed, snapshot);
        Assert.Equal(ViewCreationStatus.Indeterminate, result.Status);
        Assert.Contains(result.Diagnostics, detail => detail.Contains("Vistas.JsCode"));
    }

    private static JsonMetadataReading Read(string code)
    {
        var document = JsonNode.Parse(GridDocument)!;
        document["view"]!["js"] = code;
        return JsonMetadataService.ReadDocument(document.ToJsonString());
    }

    private static ViewCreationPlan Plan(JsonMetadataReading reading)
    {
        Assert.Empty(reading.Diagnostics);
        var document = reading.Document!.Deserialize<JsonMetadataDocument>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var catalog = document.States!.Select(_ => (ViewCreationPreflightResult?)new ViewCreationPreflightResult(
            true, "vJsDemo", null, [], "[dbo].[PRC_GET_GRID_TEST_SORT_COLUMN]", [1, 2, 3, 4, 5, 6])).ToArray();
        return JsonMetadataPlanFactory.Create(document, catalog, reading.Annotations);
    }

    private static string MotorDecode(string encoded) => encoded.Replace(@"\u0022", "\"")
        .Replace(@"\u0027", "'").Replace(@"\u0000", "null");
}
