using System.Text.Json;
using System.Text.Json.Nodes;
using DesignerIA.Api.Services.ViewDocuments;
using DesignerIA.Api.Services.ViewScripts;
using DesignerIA.Contracts;
using Xunit;

namespace DesignerIA.Api.Tests;

/// <summary>
/// Entrada por documento JSONMetadata 1.0: lectura estricta, expansión,
/// validación y su enlace con el camino de creación compartido. Sin SQL.
/// Las expansiones exactas fijan el significado de la versión 1.0: si una de
/// estas pruebas cambia, ha cambiado el formato y corresponde otra versión.
/// </summary>
public class JsonMetadataTests
{
    private const string AuditUser = "DOMINIO\\usuario";
    private const string AreaPath = "states[0].rows[0].areas[0]";
    private const string DataPath = AreaPath + ".object.data";

    private const string EmptyCompact = """{ "schemaVersion": "1.0", "view": { "id": "vNueva" } }""";

    private const string EmptyEffective = """
        {
          "schemaVersion": "1.0",
          "view": { "id": "vNueva", "description": "vNueva", "type": "site", "usesSecurity": false },
          "states": [ { "name": "Pantalla", "isDefault": true, "rows": [] } ]
        }
        """;

    private const string GridCompact = """
        {
          "schemaVersion": "1.0",
          "view": { "id": "vDemoGrilla01", "type": "go" },
          "states": [ { "rows": [ { "areas": [ { "object": {
            "type": "grid",
            "data": { "connection": "GestionEngine", "storedProcedure": "dbo.PRC_GET_GRID_TEST_SORT_COLUMN" }
          } } ] } ] } ]
        }
        """;

    private const string GridEffective = """
        {
          "schemaVersion": "1.0",
          "view": { "id": "vDemoGrilla01", "description": "vDemoGrilla01", "type": "go", "usesSecurity": false },
          "states": [ {
            "name": "Pantalla",
            "isDefault": true,
            "rows": [ { "areas": [ {
              "name": "Area01",
              "object": {
                "type": "grid",
                "data": { "connection": "GestionEngine", "storedProcedure": "dbo.PRC_GET_GRID_TEST_SORT_COLUMN", "parameters": [] }
              }
            } ] } ]
          } ]
        }
        """;

    private const string IncompleteGrid = """
        {
          "schemaVersion": "1.0",
          "view": { "id": "vDemoGrilla02", "type": "go" },
          "states": [ { "rows": [ { "areas": [ {
            "titulo": "Ventas",
            "object": {
              "type": "grid",
              "settings": { "Renglones": 100 },
              "data": {
                "storedProcedure": "PRC_VENTAS",
                "parameters": [ { "name": "Anio" }, { "name": "Planta", "default": null } ]
              }
            }
          } ] } ] } ]
        }
        """;

    [Theory]
    [InlineData(EmptyCompact, EmptyEffective)]
    [InlineData(GridCompact, GridEffective)]
    public void CompactDocumentExpandsExactlyAndExpansionIsIdempotent(string compact, string effective)
    {
        var reading = JsonMetadataService.ReadDocument(compact);

        Assert.Empty(reading.Diagnostics);
        AssertSameJson(effective, reading.Document);
        // También el orden de las propiedades es el canónico, con lo completado en su sitio.
        Assert.Equal(JsonNode.Parse(effective)!.ToJsonString(), reading.Document!.ToJsonString());

        Assert.True(reading.EffectiveText!.Length <= JsonMetadataReader.MaxContentLength);
        var again = JsonMetadataService.ReadDocument(reading.EffectiveText);
        Assert.Empty(again.Diagnostics);
        AssertSameJson(effective, again.Document);
        Assert.DoesNotContain(again.Annotations, annotation => annotation.Origin == JsonMetadataOrigin.Default);
    }

    [Fact]
    public void AnnotationsSeparateReceivedFromCompletedValues()
    {
        var reading = JsonMetadataService.ReadDocument(GridCompact);

        AssertOrigin(reading, "view.id", JsonMetadataOrigin.Recibido);
        AssertOrigin(reading, DataPath + ".connection", JsonMetadataOrigin.Recibido);
        AssertOrigin(reading, "view.description", JsonMetadataOrigin.Default);
        AssertOrigin(reading, "states[0].name", JsonMetadataOrigin.Default);
        AssertOrigin(reading, "states[0].isDefault", JsonMetadataOrigin.Default);
        AssertOrigin(reading, AreaPath + ".name", JsonMetadataOrigin.Default);
        AssertOrigin(reading, DataPath + ".parameters", JsonMetadataOrigin.Default);
        AssertOrigin(JsonMetadataService.ReadDocument(EmptyCompact), "states", JsonMetadataOrigin.Derivado);
    }

    [Fact]
    public void ExplicitValuesAreNeverReplacedByConventions()
    {
        var reading = JsonMetadataService.ReadDocument("""
            {
              "schemaVersion": "1.0",
              "view": { "id": "vDemo", "description": "Mi descripción", "type": "go", "usesSecurity": false },
              "states": [ { "name": "Principal", "isDefault": true, "rows": [ { "areas": [ { "name": "AreaVentas", "object": {
                "type": "grid",
                "data": { "connection": "DW", "storedProcedure": "ventas.PRC_X",
                  "parameters": [ { "name": "@Anio", "source": { "kind": "fixed" }, "default": "2026" } ] }
              } } ] } ] } ]
            }
            """);

        Assert.Empty(reading.Diagnostics);
        Assert.DoesNotContain(reading.Annotations, annotation => annotation.Origin == JsonMetadataOrigin.Default);
        var plan = Plan(reading, GridPreflight("vDemo") with { CanonicalStoredProcedure = "[ventas].[PRC_X]", Parameters = [new("Anio", "int", "2026")] });
        Assert.Equal("Mi descripción", plan.View.Description);
        Assert.Equal(("Principal", true, "PrincipalF0"), (plan.State.Name, plan.State.IsDefault, plan.Row.Name));
        Assert.Equal(("AreaVentas", "AreaVentas_Obj", "DW", "[ventas].[PRC_X]"),
            (plan.Grid!.AreaName, plan.Grid.ObjectName, plan.Grid.ConnectionAlias, plan.Grid.CanonicalStoredProcedure));
        Assert.Equal(new ViewCreationPlanParameter("Anio", "int", false, "2026"), Assert.Single(plan.Grid.Parameters!));
    }

    [Fact]
    public void IncompleteGridReportsConcretePendingDataWithoutInventingValues()
    {
        var reading = JsonMetadataService.ReadDocument(IncompleteGrid);

        Assert.Equal(
            [
                (AreaPath + ".titulo", JsonMetadataSeverity.Error, "PropiedadDesconocida"),
                (AreaPath + ".object.settings", JsonMetadataSeverity.NoSoportado, "CapacidadNoSoportada"),
                (DataPath + ".parameters[1].default", JsonMetadataSeverity.Pendiente, "ValorDesconocido"),
                (DataPath + ".connection", JsonMetadataSeverity.Pendiente, "DatoRequerido"),
                (DataPath + ".storedProcedure", JsonMetadataSeverity.Error, "FormaNoAdmitida"),
                (DataPath + ".parameters[0].default", JsonMetadataSeverity.Pendiente, "DatoRequerido")
            ],
            reading.Diagnostics.Select(diagnostic => (diagnostic.Path, diagnostic.Severity, diagnostic.Code)).ToArray());
        Assert.All(reading.Diagnostics, diagnostic => Assert.False(string.IsNullOrWhiteSpace(diagnostic.Needed)));

        // Sigue siendo una grilla incompleta: no pasa a vista vacía ni recibe alias, SP o valores.
        var data = reading.Document!["states"]![0]!["rows"]![0]!["areas"]![0]!["object"]!["data"]!.AsObject();
        Assert.False(data.ContainsKey("connection"));
        Assert.Equal("PRC_VENTAS", data["storedProcedure"]!.GetValue<string>());
        Assert.False(data["parameters"]![0]!.AsObject().ContainsKey("default"));
        Assert.Null(data["parameters"]![1]!["default"]);
        Assert.Equal("go", reading.Document["view"]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void GridWithoutDataAsksForAliasAndProcedure()
    {
        var reading = JsonMetadataService.ReadDocument(Grid(document => Area(document)["object"]!.AsObject().Remove("data")));

        Assert.Equal(
            [(DataPath + ".connection", JsonMetadataSeverity.Pendiente), (DataPath + ".storedProcedure", JsonMetadataSeverity.Pendiente)],
            reading.Diagnostics.Select(diagnostic => (diagnostic.Path, diagnostic.Severity)).ToArray());
    }

    public static TheoryData<string, string[]> UnsupportedCases => new()
    {
        { Grid(document => document["leftMenu"] = new JsonObject { ["tabs"] = new JsonArray() }), ["leftMenu"] },
        { Grid(document => document["view"]!["usesSecurity"] = true), ["view.usesSecurity"] },
        { Grid(document => document["view"]!["type"] = "site"), ["view.type"] },
        { Grid(document => State(document)["leftMenuItems"] = new JsonArray("lm1")), ["states[0].leftMenuItems[0]"] },
        { Grid(document => State(document)["popUp"] = new JsonObject { ["width"] = "1200" }), ["states[0].popUp"] },
        { Grid(document => Area(document)["width"] = "50%"), [AreaPath + ".width"] },
        { Grid(document => Area(document)["object"]!["settings"] = new JsonObject { ["Renglones"] = "100" }), [AreaPath + ".object.settings"] },
        { Grid(document => Area(document)["object"]!["handlers"] = new JsonArray(new JsonObject { ["action"] = "x" })), [AreaPath + ".object.handlers[0]"] },
        { Grid(document => Area(document)["object"]!["type"] = "chart"), [AreaPath + ".object.type"] },
        { Grid(document => Data(document)["parameters"] = new JsonArray(new JsonObject
            { ["name"] = "Anio", ["source"] = new JsonObject { ["kind"] = "control", ["name"] = "wcAnio" }, ["default"] = "2026" })),
          [DataPath + ".parameters[0].source.kind"] },
        { """{ "schemaVersion": "1.0", "view": { "id": "vNueva", "type": "go" } }""", ["view.type"] }
    };

    [Theory]
    [MemberData(nameof(UnsupportedCases))]
    public void DeclaredCapabilitiesWithoutSupportAreRejectedWithTheirLocation(string document, string[] expectedPaths)
    {
        var reading = JsonMetadataService.ReadDocument(document);

        Assert.Equal(expectedPaths, reading.Diagnostics.Select(diagnostic => diagnostic.Path).ToArray());
        Assert.All(reading.Diagnostics, diagnostic => Assert.Equal(JsonMetadataSeverity.NoSoportado, diagnostic.Severity));
    }

    [Fact]
    public void ExtraRowsAndAreasAndStatesWithoutContentAreRejectedOneByOne()
    {
        var reading = JsonMetadataService.ReadDocument("""
            {
              "schemaVersion": "1.0",
              "view": { "id": "vDemo", "type": "go" },
              "states": [
                { "name": "A", "isDefault": true, "rows": [
                  { "areas": [ { "name": "A1", "object": { "type": "grid", "data": { "connection": "c", "storedProcedure": "dbo.P" } } },
                               { "name": "A2", "object": { "type": "grid", "data": { "connection": "c", "storedProcedure": "dbo.P" } } } ] },
                  { "areas": [ { "name": "A3", "object": { "type": "grid", "data": { "connection": "c", "storedProcedure": "dbo.P" } } } ] } ] },
                { "name": "B", "isDefault": false }
              ]
            }
            """);

        Assert.Equal(
            ["states[0].rows[0].areas[1]", "states[0].rows[1]", "states[1].rows"],
            reading.Diagnostics.Select(diagnostic => diagnostic.Path).ToArray());
        Assert.All(reading.Diagnostics, diagnostic => Assert.Equal(JsonMetadataSeverity.NoSoportado, diagnostic.Severity));
    }

    [Fact]
    public void SeveralStatesNeedNamesAndExactlyOneDefaultInsteadOfPositionalGuesses()
    {
        var reading = JsonMetadataService.ReadDocument(
            """{ "schemaVersion": "1.0", "view": { "id": "vDemo" }, "states": [ { }, { } ] }""");

        Assert.Contains(reading.Diagnostics, diagnostic => diagnostic is { Path: "states[0].name", Severity: JsonMetadataSeverity.Pendiente });
        Assert.Contains(reading.Diagnostics, diagnostic => diagnostic is { Path: "states[1].name", Severity: JsonMetadataSeverity.Pendiente });
        Assert.Contains(reading.Diagnostics, diagnostic => diagnostic is { Path: "states", Code: "EstadoInicial" });
        Assert.False(reading.Document!["states"]![0]!.AsObject().ContainsKey("name"));
        Assert.False(reading.Document["states"]![0]!.AsObject().ContainsKey("isDefault"));
    }

    [Theory]
    [InlineData("""{ "schemaVersion": "1.0", "view": { "id": "vNueva", "descripcion": "x" } }""", "view.descripcion", "PropiedadDesconocida")]
    [InlineData("""{ "schemaVersion": "1.0", "schema": "designeria", "view": { "id": "vNueva" } }""", "schema", "PropiedadDesconocida")]
    [InlineData("""{ "schemaVersion": "1.0", "view": { "id": 7 } }""", "view.id", "TipoIncorrecto")]
    [InlineData("""{ "schemaVersion": "1.0", "view": { "id": "vNueva" }, "states": [ "Pantalla" ] }""", "states[0]", "TipoIncorrecto")]
    [InlineData("""{ "schemaVersion": "2.0", "view": { "id": "vNueva" } }""", "schemaVersion", "VersionNoAdmitida")]
    [InlineData("""{ "view": { "id": "vNueva" } }""", "schemaVersion", "VersionRequerida")]
    [InlineData("""{ "schemaVersion": "1.0", "view": { "id": "vista con espacios" } }""", "view.id", "NombreNoAdmitido")]
    [InlineData("""{ "schemaVersion": "1.0", "view": { "id": "vNueva", "type": "web" } }""", "view.type", "ValorNoAdmitido")]
    public void MalformedDocumentsAreErrorsAtTheirLocation(string document, string path, string code)
    {
        var diagnostic = Assert.Single(JsonMetadataService.ReadDocument(document).Diagnostics);

        Assert.Equal((path, JsonMetadataSeverity.Error, code), (diagnostic.Path, diagnostic.Severity, diagnostic.Code));
    }

    [Theory]
    [InlineData(null, "DocumentoVacio")]
    [InlineData("   ", "DocumentoVacio")]
    [InlineData("{ no es json", "JsonInvalido")]
    [InlineData("""{ "schemaVersion": "1.0", "view": { "id": "a" }, "view": { "id": "b" } }""", "JsonInvalido")]
    [InlineData("""{ "schemaVersion": "1.0", "view": { "id": "a" }, }""", "JsonInvalido")]
    [InlineData("""[ { "schemaVersion": "1.0" } ]""", "RaizNoEsObjeto")]
    public void UnreadableContentHasNoEffectiveDocument(string? content, string code)
    {
        var reading = JsonMetadataService.ReadDocument(content);

        Assert.Null(reading.Document);
        Assert.Equal(code, Assert.Single(reading.Diagnostics).Code);
    }

    [Fact]
    public void OversizedContentIsRejectedBeforeParsing()
    {
        var reading = JsonMetadataService.ReadDocument(new string(' ', JsonMetadataReader.MaxContentLength) + EmptyCompact);

        Assert.Null(reading.Document);
        Assert.Equal("DocumentoDemasiadoGrande", Assert.Single(reading.Diagnostics).Code);
    }

    [Fact]
    public void AbsentNullEmptyTextAndEmptyListAreDifferentThings()
    {
        // Ausente: se aplica la convención.
        Assert.Equal("vNueva", JsonMetadataService.ReadDocument(EmptyCompact).Document!["view"]!["description"]!.GetValue<string>());

        // Null: «no lo sé». Es un pendiente y no se completa, aunque exista convención.
        var withNull = JsonMetadataService.ReadDocument("""{ "schemaVersion": "1.0", "view": { "id": "vNueva", "description": null } }""");
        Assert.Equal(("view.description", JsonMetadataSeverity.Pendiente, "ValorDesconocido"), Key(Assert.Single(withNull.Diagnostics)));
        Assert.Null(withNull.Document!["view"]!["description"]);

        // Cadena vacía: un valor explícito que no se admite.
        var withEmptyText = JsonMetadataService.ReadDocument("""{ "schemaVersion": "1.0", "view": { "id": "vNueva", "description": "" } }""");
        Assert.Equal(("view.description", JsonMetadataSeverity.Error, "ValorVacio"), Key(Assert.Single(withEmptyText.Diagnostics)));

        // Lista vacía: «declaro que no hay». En states es un error; omitirla es la vista vacía.
        var withEmptyStates = JsonMetadataService.ReadDocument("""{ "schemaVersion": "1.0", "view": { "id": "vNueva" }, "states": [] }""");
        Assert.Equal(("states", JsonMetadataSeverity.Error, "ColeccionVacia"), Key(Assert.Single(withEmptyStates.Diagnostics)));

        // En rows y parameters, lista vacía y ausencia significan lo mismo.
        var withEmptyRows = JsonMetadataService.ReadDocument("""{ "schemaVersion": "1.0", "view": { "id": "vNueva" }, "states": [ { "rows": [] } ] }""");
        Assert.Empty(withEmptyRows.Diagnostics);
        AssertSameJson(EmptyEffective, withEmptyRows.Document);
    }

    [Theory]
    [InlineData("""{ "name": "Anio" }""", JsonMetadataSeverity.Pendiente, "DatoRequerido")]
    [InlineData("""{ "name": "Anio", "default": null }""", JsonMetadataSeverity.Pendiente, "ValorDesconocido")]
    [InlineData("""{ "name": "Anio", "default": "" }""", JsonMetadataSeverity.Error, "ValorVacio")]
    [InlineData("""{ "name": "Anio", "default": "O'Brien" }""", JsonMetadataSeverity.Error, "ValorNoAdmitido")]
    public void ParameterWithoutValueEmptyValueAndNullAreDifferentThings(string parameter, JsonMetadataSeverity severity, string code)
    {
        var reading = JsonMetadataService.ReadDocument(Grid(document => Data(document)["parameters"] = new JsonArray(JsonNode.Parse(parameter))));

        Assert.Equal((DataPath + ".parameters[0].default", severity, code), Key(Assert.Single(reading.Diagnostics)));
    }

    [Fact]
    public void EmptyDocumentProducesTheInheritedEmptyViewScript()
    {
        var reading = JsonMetadataService.ReadDocument(EmptyCompact);
        var preflight = ViewCreationPreflightService.FromExistingViewId("vNueva", null);

        var fromDocument = ViewScriptGenerator.BuildScript(Plan(reading, (ViewCreationPreflightResult?)null), AuditUser);
        var inherited = ViewScriptGenerator.Generate(new ViewSpec("vNueva", null, null, []), preflight, AuditUser);

        Assert.True(inherited.CanGenerate);
        Assert.Equal(inherited.Script, fromDocument);
        Assert.Contains("N'PantallaF0'", fromDocument);
        Assert.DoesNotContain("INSERT INTO dbo.LeftMenus", fromDocument);
    }

    [Fact]
    public void GridDocumentUsesDerivedNamesOnTheSharedGenerationPath()
    {
        var reading = JsonMetadataService.ReadDocument(GridCompact);
        var preflight = GridPreflight("vDemoGrilla01");

        var fromDocument = ViewScriptGenerator.BuildScript(Plan(reading, preflight), AuditUser);
        var equivalentSpec = ViewScriptGenerator.Generate(LegacyGridSpec("vDemoGrilla01", "Pantalla", "PantallaF0", "Area01_Obj"), preflight, AuditUser);

        Assert.True(equivalentSpec.CanGenerate);
        Assert.Equal(equivalentSpec.Script, fromDocument);
        Assert.Contains("N'PantallaF0'", fromDocument);
        Assert.Contains("N'Area01_Obj'", fromDocument);
        Assert.Contains("N'LMFiltros'", fromDocument);
        AssertOrigin(reading, "states[0].rows[0]", JsonMetadataOrigin.Derivado);
        AssertOrigin(reading, AreaPath + ".object", JsonMetadataOrigin.Derivado);
    }

    [Fact]
    public void InheritedViewSpecRoutesKeepTheirExplicitRowAndObjectNames()
    {
        var result = ViewScriptGenerator.Generate(LegacyGridSpec("vHeredada", "Estado01", "Fila01", "Grilla01"), GridPreflight("vHeredada"), AuditUser);

        Assert.True(result.CanGenerate);
        Assert.Contains("N'Fila01'", result.Script);
        Assert.Contains("N'Grilla01'", result.Script);
        Assert.DoesNotContain("Area01_Obj", result.Script);
        Assert.DoesNotContain("Estado01F0", result.Script);
    }

    [Fact]
    public void CatalogRejectionsAreLocatedOnTheDocumentPropertyThatCausesThem()
    {
        var rejectedGrid = GridPreflight("v") with
        {
            Gaps =
            [
                new("Alias inexistente", "No se encontró el alias."),
                new("SP inexistente o no visible para DesignerIA", "No se encontró el procedimiento."),
                new("Parámetros no coinciden con el procedimiento", "Falta el valor del parámetro «B».")
            ],
            CompletedChecks = []
        };
        var catalog = new JsonMetadataCatalog(ViewCreationPreflightService.FromExistingViewId("v", "v"), [null, rejectedGrid]);
        var diagnostics = new List<JsonMetadataDiagnostic>();

        JsonMetadataService.AddCatalogResults(catalog, [], diagnostics);

        const string secondState = "states[1].rows[0].areas[0].object.data";
        Assert.Equal(
            ["view.id", secondState + ".connection", secondState + ".storedProcedure", secondState + ".parameters"],
            diagnostics.Select(diagnostic => diagnostic.Path).ToArray());
        Assert.All(diagnostics, diagnostic => Assert.Equal(JsonMetadataSeverity.Error, diagnostic.Severity));
    }

    [Fact]
    public void OmittedParametersBecomeAPendingItemThatListsTheProcedureSignature()
    {
        var preflight = GridPreflight("vDemo") with
        {
            Gaps = [new("SP con parámetros no admitidos por la plantilla", "…")],
            CompletedChecks = [1, 2, 3, 4, 6],
            DeclaredParameters = [new("@Anio", "int", false), new("@Planta", "varchar", false)]
        };
        var diagnostics = new List<JsonMetadataDiagnostic>();

        JsonMetadataService.AddCatalogResults(new(ViewCreationPreflightService.FromExistingViewId("vDemo", null), [preflight]), [], diagnostics);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal((DataPath + ".parameters", JsonMetadataSeverity.Pendiente, "ParametrosRequeridos"), Key(diagnostic));
        Assert.Contains("@Anio (int), @Planta (varchar)", diagnostic.Needed);
    }

    [Fact]
    public void CatalogAnnotationsDescribeWhatTheEnvironmentResolvedWithoutPhysicalIds()
    {
        var preflight = GridPreflight("vDemoGrilla01") with { DeclaredParameters = [new("@QueryString", "varchar", false)] };
        var annotations = new List<JsonMetadataAnnotation>();
        var diagnostics = new List<JsonMetadataDiagnostic>();

        JsonMetadataService.AddCatalogResults(new(ViewCreationPreflightService.FromExistingViewId("vDemoGrilla01", null), [preflight]), annotations, diagnostics);

        Assert.Empty(diagnostics);
        Assert.Equal(
            ["view.id", DataPath + ".connection", DataPath + ".storedProcedure", DataPath + ".parameters"],
            annotations.Select(annotation => annotation.Path).ToArray());
        Assert.All(annotations, annotation => Assert.Equal(JsonMetadataOrigin.Catalogo, annotation.Origin));
        Assert.Contains("[dbo].[PRC_GET_GRID_TEST_SORT_COLUMN]", annotations[2].Note);
        Assert.Contains("@QueryString (varchar)", annotations[3].Note);
        Assert.Contains("excepción heredada", annotations[3].Note);
    }

    private static ViewCreationPlan Plan(JsonMetadataReading reading, params ViewCreationPreflightResult?[] grids) => JsonMetadataPlanFactory.Create(
        reading.Document!.Deserialize<JsonMetadataDocument>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!, grids, reading.Annotations);

    private static ViewSpec LegacyGridSpec(string name, string state, string row, string objectName) => new(
        name, null, state,
        [new ViewSpecState(state, [new ViewSpecRow(
            [new ViewSpecArea("Grilla", "dbo.PRC_GET_GRID_TEST_SORT_COLUMN", "GestionEngine", [],
                "Area01", objectName, GridConfigurationPresets.GridQuery)], row)])],
        "go");

    private static ViewCreationPreflightResult GridPreflight(string viewName) => new(
        true, viewName, null, [], "[dbo].[PRC_GET_GRID_TEST_SORT_COLUMN]", [1, 2, 3, 4, 5, 6]);

    private static string Grid(Action<JsonObject> change)
    {
        var document = JsonNode.Parse(GridCompact)!.AsObject();
        change(document);
        return document.ToJsonString();
    }

    private static JsonObject State(JsonObject document) => document["states"]![0]!.AsObject();

    private static JsonObject Area(JsonObject document) => State(document)["rows"]![0]!["areas"]![0]!.AsObject();

    private static JsonObject Data(JsonObject document) => Area(document)["object"]!["data"]!.AsObject();

    private static (string, JsonMetadataSeverity, string) Key(JsonMetadataDiagnostic diagnostic) =>
        (diagnostic.Path, diagnostic.Severity, diagnostic.Code);

    private static void AssertSameJson(string expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), $"Documento efectivo distinto del esperado:\n{actual}");

    private static void AssertOrigin(JsonMetadataReading reading, string path, JsonMetadataOrigin origin) =>
        Assert.Contains(reading.Annotations, annotation => annotation.Path == path && annotation.Origin == origin);
}
