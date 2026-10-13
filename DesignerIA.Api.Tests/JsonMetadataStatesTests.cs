using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DesignerIA.Api.Services.ViewDocuments;
using DesignerIA.Api.Services.ViewScripts;
using DesignerIA.Contracts;
using Xunit;

namespace DesignerIA.Api.Tests;

/// <summary>
/// Varios estados con una grilla cada uno y web controls de navegación definidos
/// en la vista y activados por estado; y el límite de tamaño de la definición
/// efectiva. Sin SQL: plan, script, verificación y diagnósticos.
/// </summary>
public class JsonMetadataStatesTests
{
    private const string AuditUser = "DOMINIO\\usuario";

    // El estado inicial es el segundo: se identifica por su marca, no por su posición.
    private const string TwoStates = """
        {
          "schemaVersion": "1.0",
          "view": { "id": "vDemoEstados01", "type": "go" },
          "controls": [
            { "name": "wcIrDetalle", "type": "buttonList",
              "items": [ { "text": "Ir a detalle", "value": "detalle" } ],
              "actions": [ { "name": "cambiarEstado", "parameters": [ "Detalle" ] } ] },
            { "name": "wcVolver", "type": "buttonList",
              "items": [ { "text": "Volver", "value": "volver" } ],
              "actions": [ { "name": "cambiarEstado", "parameters": [ "Principal" ] } ] }
          ],
          "states": [
            { "name": "Detalle", "controls": [ "wcVolver" ],
              "rows": [ { "areas": [ { "name": "AreaDetalle", "object": { "type": "grid",
                "data": { "connection": "GestionEngine", "storedProcedure": "dbo.PRC_GET_GRID_TEST_SORT_COLUMN" } } } ] } ] },
            { "name": "Principal", "isDefault": true, "controls": [ "wcIrDetalle" ],
              "rows": [ { "areas": [ { "name": "AreaPrincipal", "object": { "type": "grid",
                "data": { "connection": "GestionEngine", "storedProcedure": "dbo.PRC_GET_GRID_TEST_SORT_COLUMN" } } } ] } ] }
          ]
        }
        """;

    [Fact]
    public void SeveralStatesExpandOnlyWhatHasARuleAndExpansionIsIdempotent()
    {
        var reading = JsonMetadataService.ReadDocument(TwoStates);

        Assert.Empty(reading.Diagnostics);
        // Con varios estados no se deriva nombre, estado inicial ni nombre de área.
        Assert.Equal(
            ["view.description", "view.usesSecurity", "states[0].rows[0].areas[0].object.data.parameters", "states[1].rows[0].areas[0].object.data.parameters"],
            reading.Annotations.Where(annotation => annotation.Origin == JsonMetadataOrigin.Default).Select(annotation => annotation.Path).ToArray());
        Assert.False(reading.Document!["states"]![0]!.AsObject().ContainsKey("isDefault"));
        Assert.Equal(["schemaVersion", "view", "controls", "states"], reading.Document.Select(property => property.Key).ToArray());

        var again = JsonMetadataService.ReadDocument(reading.EffectiveText);
        Assert.Empty(again.Diagnostics);
        Assert.Equal(reading.EffectiveText, again.EffectiveText);
    }

    [Fact]
    public void ControlsAreDefinedOnceAndEachStateListsItsActiveOnes()
    {
        var plan = TwoStatesPlan();

        Assert.Equal(
            [("Detalle", false, "DetalleF0", "AreaDetalle_Obj", (string?)null), ("Principal", true, "PrincipalF0", "AreaPrincipal_Obj", "LMFiltros")],
            plan.StateBlocks.Select(block => (block.State.Name, block.State.IsDefault, block.Row.Name, block.Grid!.ObjectName, block.Grid.LeftMenu)).ToArray());
        Assert.Equal(
            [
                """{"filters":"wcVolver","leftMenus":"","isPopUp":false,"popUp":{},"idLayout":"00"}""",
                """{"filters":"wcIrDetalle","leftMenus":"","isPopUp":false,"popUp":{},"idLayout":"00"}"""
            ],
            plan.StateBlocks.Select(block => block.StateConfiguration.Configuration).ToArray());

        Assert.Equal(["wcIrDetalle", "wcVolver"], plan.Controls!.Select(control => control.Name).ToArray());
        Assert.All(plan.Controls!, control => Assert.Equal((4, "ButtonList"), (control.TypeId, control.TypeName)));
        Assert.Equal([0, 1], plan.Controls!.Select(control => control.Order).ToArray());
        // El mismo texto que escribe Designer para una lista de botones con cambiarEstado.
        Assert.Equal("""{"actions":{"cambiarEstado":["Detalle"]}, "SufijoBoton":"false", "visibleEn":"A", "enabled":"true"}""", plan.Controls![0].Configuration);
        Assert.Equal(new ViewCreationPlanControlItem("detalle", "Ir a detalle", true), Assert.Single(plan.Controls[0].Items));
    }

    [Fact]
    public void StateActivationUsesViewDefinitionOrderAndDoesNotDuplicateControls()
    {
        var reading = JsonMetadataService.ReadDocument(Change(document =>
        {
            // Los dos controles activos en ambos estados, referenciados en orden inverso al de su definición.
            document["states"]![0]!["controls"] = new JsonArray("wcVolver", "wcIrDetalle");
            document["states"]![1]!["controls"] = new JsonArray("wcVolver", "wcIrDetalle");
        }));

        Assert.Empty(reading.Diagnostics);
        var plan = Plan(reading);
        Assert.Equal(2, plan.Controls!.Count);
        Assert.All(plan.StateBlocks, block => Assert.Contains("\"filters\":\"wcIrDetalle,wcVolver\"", block.StateConfiguration.Configuration));
        Assert.Equal(2, Count(ViewScriptGenerator.BuildScript(plan, AuditUser), "INSERT INTO dbo.WebControls "));
    }

    [Fact]
    public void ScriptWritesEveryStateAndTheControlsInsideTheInitialState()
    {
        var script = ViewScriptGenerator.BuildScript(TwoStatesPlan(), AuditUser);

        Assert.Equal(1, Count(script, "INSERT INTO dbo.Vistas "));
        foreach (var table in new[] { "Estados", "Filas", "Areas", "ExtraccionesDatos", "StoredProcedure", "ObjetosDeArea",
            "ConfiguracionesObjetoDeArea", "ConfiguracionesEstados", "WebControls", "ItemsWebControl", "ConfiguracionesFiltros" })
        {
            Assert.Equal(2, Count(script, $"INSERT INTO dbo.{table} "));
        }
        // LMFiltros sólo en el estado inicial, como Designer.
        Assert.Equal(1, Count(script, "INSERT INTO dbo.LeftMenus "));
        Assert.Contains("VALUES (@IDX_Estado, 0, N'Detalle', N'vDemoEstados01'", script);
        Assert.Contains("VALUES (@IDX_Estado, 1, N'Principal', N'vDemoEstados01'", script);

        // Los controles quedan entre el alta del estado inicial y su configuración: usan su @IDX_Estado.
        var initialState = script.IndexOf("N'Principal', N'vDemoEstados01'", StringComparison.Ordinal);
        var firstControl = script.IndexOf("INSERT INTO dbo.WebControls ", StringComparison.Ordinal);
        var initialConfiguration = script.IndexOf("""N'{"filters":"wcIrDetalle",""", StringComparison.Ordinal);
        Assert.True(initialState < firstControl && firstControl < initialConfiguration);
        Assert.Contains("VALUES (@IDX_WebControl, 4, @IDX_Estado, -1, N'wcIrDetalle', N'', N'', 0,", script);
        Assert.Contains("VALUES (@IDX_WebControl, 4, @IDX_Estado, -1, N'wcVolver', N'', N'', 1,", script);
        Assert.Contains("VALUES (@IDX_WebControlItem, @IDX_WebControl, N'detalle', N'Ir a detalle', 1,", script);
        Assert.Contains("""N'{"actions":{"cambiarEstado":["Principal"]}, "SufijoBoton":"false", "visibleEn":"A", "enabled":"true"}'""", script);

        // El Id del tipo se comprueba contra el catálogo de la base, y cada Id avanza entre estados.
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM dbo.TiposWebControl WHERE IdTipoWebControl = 4 AND Tipo = N'ButtonList')", script);
        foreach (var variable in new[] { "Estado", "Fila", "Area", "ExtraccionDato", "StoredProcedure", "ObjetoArea", "ObjetoAreaConfig", "WebControl", "WebControlItem" })
        {
            Assert.Equal(1, Count(script, $"SET @IDX_{variable} = @IDX_{variable} + 1;"));
        }
        Assert.DoesNotContain("SET @IDX_LeftMenu", script);
        // El alias se resuelve de nuevo para la segunda grilla.
        Assert.Equal(2, Count(script, "FROM dbo.StringsConnection WHERE StringConnection = N'GestionEngine';"));
    }

    [Theory]
    [InlineData("THROW")]
    [InlineData("TRY_CONVERT")]
    [InlineData("IIF(")]
    [InlineData("CONCAT(")]
    [InlineData("STRING_AGG")]
    [InlineData("CREATE OR ALTER")]
    [InlineData("DROP TABLE IF EXISTS")]
    public void ScriptForSeveralStatesStaysWithinSqlServer2008R2(string construct)
    {
        Assert.DoesNotContain(construct, ViewScriptGenerator.BuildScript(TwoStatesPlan(), AuditUser), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SameParameterInTwoStatesIsWrittenAndVerifiedForEachGrid()
    {
        var reading = JsonMetadataService.ReadDocument(Change(document =>
        {
            foreach (var state in document["states"]!.AsArray())
            {
                state!["rows"]![0]!["areas"]![0]!["object"]!["data"]!["parameters"] =
                    new JsonArray(new JsonObject { ["name"] = "QueryString", ["default"] = "a=1" });
            }
        }));
        Assert.Empty(reading.Diagnostics);
        var grid = GridCatalog() with { Parameters = [new("QueryString", "varchar", "a=1")] };
        var plan = Plan(reading, grid, grid);

        var script = ViewScriptGenerator.BuildScript(plan, AuditUser);
        Assert.Equal(2, Count(script, "INSERT INTO dbo.ParametrosSP "));
        Assert.Equal(1, Count(script, "SET @IDX_SPParam = @IDX_SPParam + 1;"));
        Assert.Contains("N'AreaDetalle', N'QueryString', N'', N'a=1'", script);
        Assert.Contains("N'AreaPrincipal', N'QueryString', N'', N'a=1'", script);
        Assert.Empty(Compare(plan, MatchingSnapshot(plan)));
        Assert.Equal(ViewCreationStatus.Created, ViewCreationService.Decide(plan, AuditUser, Completed(), MatchingSnapshot(plan)).Status);
    }

    [Theory]
    [InlineData("parameter-state", "ParametrosSP", "SP.Estado")]
    [InlineData("parameter-area", "ParametrosSP", "SP.Area")]
    [InlineData("mapping-state", "MapeoParametros", "Estado")]
    [InlineData("mapping-sp-state", "MapeoParametros", "SP.Estado")]
    [InlineData("mapping-sp-area", "MapeoParametros", "SP.Area")]
    [InlineData("missing-owner", "ParametrosSP", "SP.Estado")]
    public void ParametersAndMappingsMustBelongToTheirOwnGrid(string defect, string table, string column)
    {
        // Mismo SP y mismo parámetro en ambos estados: sólo la relación identifica la grilla.
        var reading = JsonMetadataService.ReadDocument(Change(document =>
        {
            foreach (var state in document["states"]!.AsArray())
            {
                state!["rows"]![0]!["areas"]![0]!["object"]!["data"]!["parameters"] =
                    new JsonArray(new JsonObject { ["name"] = "QueryString", ["default"] = "a=1" });
            }
        }));
        var grid = GridCatalog() with { Parameters = [new("QueryString", "varchar", "a=1")] };
        var plan = Plan(reading, grid, grid);
        var snapshot = MatchingSnapshot(plan);
        var parameters = snapshot.ParameterRows!.Select(row => new Dictionary<string, string?>(row)).ToList();
        var mappings = snapshot.MappingRows!.Select(row => new Dictionary<string, string?>(row)).ToList();
        for (var index = 0; index < 2; index++)
        {
            var state = index == 0 ? "Detalle" : "Principal";
            var area = index == 0 ? "AreaDetalle" : "AreaPrincipal";
            parameters[index]["SP.Estado"] = state;
            parameters[index]["SP.Area"] = area;
            mappings[index]["Estado"] = state;
            mappings[index]["SP.Estado"] = state;
            mappings[index]["SP.Area"] = area;
        }
        Assert.Empty(Compare(plan, snapshot with { ParameterRows = parameters, MappingRows = mappings }));

        var rows = table == "ParametrosSP" ? parameters : mappings;
        if (defect == "missing-owner")
        {
            rows[0].Remove(column);
        }
        else if (defect is "parameter-state" or "mapping-sp-state")
        {
            // IdStoredProcedure cambiado a la grilla del otro estado, sin cambiar sus valores.
            rows[0]["SP.Estado"] = "Principal";
            rows[0]["SP.Area"] = "AreaPrincipal";
        }
        else
        {
            rows[0][column] = column.EndsWith("Area", StringComparison.Ordinal) ? "AreaPrincipal" : "Principal";
        }
        var misplaced = snapshot with { ParameterRows = parameters, MappingRows = mappings };

        Assert.Contains(Compare(plan, misplaced), mismatch => mismatch.Contains(table) && mismatch.Contains(column));
        Assert.Equal(ViewCreationStatus.Indeterminate, ViewCreationService.Decide(plan, AuditUser, Completed(), misplaced).Status);
    }

    [Fact]
    public void VerificationCoversEveryStateAndTheControls()
    {
        var plan = TwoStatesPlan();
        var counts = ViewCreationVerifier.ExpectedCounts(plan);

        Assert.Equal((2, 2, 2, 1, 2, 2, 2, 2),
            (counts["Estados"], counts["Filas"], counts["Areas"], counts["LeftMenus"], counts["ConfiguracionesEstados"],
             counts["WebControls"], counts["ItemsWebControl"], counts["ConfiguracionesFiltros"]));
        foreach (var name in counts.Keys)
        {
            Assert.Contains($"AS [{name}]", ViewCreationVerifier.CountsSql);
        }
        foreach (var name in ViewCreationVerifier.ExpectedControlRows(plan, AuditUser)[0].Keys
            .Concat(ViewCreationVerifier.ExpectedControlItemRows(plan, AuditUser)[0].Keys))
        {
            Assert.Contains($"AS [{name}]", ViewCreationVerifier.ControlsSql);
        }
        Assert.DoesNotMatch(@"\b(INSERT|UPDATE|DELETE|MERGE|EXEC|INTO)\b", ViewCreationVerifier.ControlsSql);

        Assert.Empty(Compare(plan, MatchingSnapshot(plan)));
        var created = ViewCreationService.Decide(plan, AuditUser, Completed(), MatchingSnapshot(plan));
        Assert.Equal(ViewCreationStatus.Created, created.Status);
        Assert.Contains(created.Diagnostics, line => line.Contains("Verificados 24 registros"));
    }

    [Fact]
    public void VerificationDetectsMissingOrMisplacedPieces()
    {
        var plan = TwoStatesPlan();
        var matching = MatchingSnapshot(plan);

        // Un control que no quedó en el estado inicial.
        var misplaced = matching.ControlRows!.Select((row, index) =>
            index == 0 ? new Dictionary<string, string?>(row) { ["Estado inicial"] = "0" } : row).ToList();
        Assert.Contains(Compare(plan, matching with { ControlRows = misplaced }), mismatch => mismatch.Contains("wcIrDetalle") && mismatch.Contains("Estado inicial"));

        // Un control ausente.
        Assert.Contains(Compare(plan, matching with { ControlRows = matching.ControlRows!.Skip(1).ToList() }), mismatch => mismatch.Contains("WebControls") && mismatch.Contains("ausente"));

        // La configuración de un estado con otros controles activos.
        var otherState = new Dictionary<string, IReadOnlyDictionary<string, string?>>(matching.StateValues!)
        {
            ["Detalle"] = new Dictionary<string, string?>(matching.StateValues!["Detalle"]) { ["ConfiguracionesEstados.Configuracion"] = "{}" }
        };
        Assert.Contains(Compare(plan, matching with { StateValues = otherState }), mismatch => mismatch.Contains("«Detalle»") && mismatch.Contains("ConfiguracionesEstados.Configuracion"));

        // Los valores de un estado que no se leyeron.
        var missingState = matching.StateValues!.Where(pair => pair.Key != "Detalle").ToDictionary(pair => pair.Key, pair => pair.Value);
        Assert.Contains(Compare(plan, matching with { StateValues = missingState }), mismatch => mismatch.Contains("«Detalle»"));
        Assert.Equal(ViewCreationStatus.Indeterminate,
            ViewCreationService.Decide(plan, AuditUser, Completed(), matching with { StateValues = missingState }).Status);
    }

    [Fact]
    public void PlanWithoutControlsRejectsControlsFoundInTheDatabase()
    {
        var reading = JsonMetadataService.ReadDocument("""
            { "schemaVersion": "1.0", "view": { "id": "vDemoGrilla01", "type": "go" },
              "states": [ { "rows": [ { "areas": [ { "object": { "type": "grid",
                "data": { "connection": "GestionEngine", "storedProcedure": "dbo.PRC_GET_GRID_TEST_SORT_COLUMN" } } } ] } ] } ] }
            """);
        var plan = Plan(reading, GridCatalog());
        var counts = new Dictionary<string, int>(ViewCreationVerifier.ExpectedCounts(plan)) { ["WebControls"] = 1 };

        Assert.DoesNotContain("WebControls", ViewCreationVerifier.ExpectedCounts(plan).Keys);
        var mismatch = Assert.Single(ViewCreationVerifier.Compare(plan, AuditUser, counts, 1, ViewCreationVerifier.ExpectedValues(plan, AuditUser)));
        Assert.Contains("WebControls", mismatch);
    }

    public static TheoryData<string, string, JsonMetadataSeverity, string> RejectedCases => new()
    {
        // Referencias.
        { Change(document => document["states"]![0]!["controls"] = new JsonArray("wcNoDefinido")), "states[0].controls[0]", JsonMetadataSeverity.Error, "ReferenciaRota" },
        { Change(document => Action(document, 0)["parameters"] = new JsonArray("NoExiste")), "controls[0].actions[0].parameters[0]", JsonMetadataSeverity.Error, "ReferenciaRota" },
        { Change(document => Action(document, 0)["parameters"] = new JsonArray("detalle")), "controls[0].actions[0].parameters[0]", JsonMetadataSeverity.Error, "ReferenciaRota" },
        { Change(document => Action(document, 0)["parameters"] = new JsonArray("Detalle", "Principal")), "controls[0].actions[0].parameters", JsonMetadataSeverity.Error, "ValorNoAdmitido" },
        { Change(document => document["states"]![0]!["controls"] = new JsonArray("wcVolver", "wcVolver")), "states[0].controls[1]", JsonMetadataSeverity.Error, "NombreDuplicado" },
        { Change(document => { Control(document, 1)["name"] = "wcIrDetalle"; document["states"]![0]!["controls"] = new JsonArray("wcIrDetalle"); }), "controls[1].name", JsonMetadataSeverity.Error, "NombreDuplicado" },
        { Change(document => document["states"]![0]!["rows"]![0]!["areas"]![0]!["name"] = "wcVolver"), "states[0].rows[0].areas[0].name", JsonMetadataSeverity.Error, "NombreDuplicado" },
        { Change(document => { document["states"]![0]!["name"] = "Principal"; Action(document, 0)["parameters"] = new JsonArray("Principal"); }), "states[1].name", JsonMetadataSeverity.Error, "NombreDuplicado" },
        // Datos que faltan.
        { Change(document => Control(document, 0).Remove("items")), "controls[0].items", JsonMetadataSeverity.Pendiente, "DatoRequerido" },
        { Change(document => Control(document, 0).Remove("actions")), "controls[0].actions", JsonMetadataSeverity.Pendiente, "DatoRequerido" },
        { Change(document => Action(document, 0).Remove("parameters")), "controls[0].actions[0].parameters", JsonMetadataSeverity.Pendiente, "DatoRequerido" },
        { Change(document => Control(document, 0)["items"] = new JsonArray()), "controls[0].items", JsonMetadataSeverity.Error, "ColeccionVacia" },
        { Change(document => { document["states"]![0]!.AsObject().Remove("name"); Action(document, 0)["parameters"] = new JsonArray("Principal"); }), "states[0].name", JsonMetadataSeverity.Pendiente, "DatoRequerido" },
        { Change(document => document["states"]![0]!["rows"]![0]!["areas"]![0]!.AsObject().Remove("name")), "states[0].rows[0].areas[0].name", JsonMetadataSeverity.Pendiente, "DatoRequerido" },
        // Estado inicial por marca.
        { Change(document => document["states"]![1]!.AsObject().Remove("isDefault")), "states", JsonMetadataSeverity.Error, "EstadoInicial" },
        { Change(document => document["states"]![0]!["isDefault"] = true), "states", JsonMetadataSeverity.Error, "EstadoInicial" },
        // Capacidades que siguen pendientes.
        { Change(document => Control(document, 0)["type"] = "combo"), "controls[0].type", JsonMetadataSeverity.NoSoportado, "CapacidadNoSoportada" },
        { Change(document => Control(document, 0)["type"] = "pivot"), "controls[0].type", JsonMetadataSeverity.NoSoportado, "CapacidadNoSoportada" },
        { Change(document => Action(document, 0)["name"] = "RefrescarAreas"), "controls[0].actions[0].name", JsonMetadataSeverity.NoSoportado, "CapacidadNoSoportada" },
        { Change(document => Control(document, 0)["data"] = new JsonObject { ["connection"] = "c" }), "controls[0].data", JsonMetadataSeverity.NoSoportado, "CapacidadNoSoportada" },
        { Change(document => Control(document, 0)["title"] = "Ir"), "controls[0].title", JsonMetadataSeverity.NoSoportado, "CapacidadNoSoportada" },
        { Change(document => document["states"]![0]!["popUp"] = new JsonObject { ["width"] = "1200" }), "states[0].popUp", JsonMetadataSeverity.NoSoportado, "CapacidadNoSoportada" },
        { Change(document => document["states"]![0]!["leftMenuItems"] = new JsonArray("lm1")), "states[0].leftMenuItems[0]", JsonMetadataSeverity.NoSoportado, "CapacidadNoSoportada" },
        { Change(document => document["leftMenu"] = new JsonObject { ["tabs"] = new JsonArray() }), "leftMenu", JsonMetadataSeverity.NoSoportado, "CapacidadNoSoportada" },
        { Change(document => document["states"]![0]!["rows"] = new JsonArray()), "states[0].rows", JsonMetadataSeverity.NoSoportado, "CapacidadNoSoportada" },
        // Forma.
        { Change(document => { Control(document, 0)["name"] = "wc con espacios"; document["states"]![1]!.AsObject().Remove("controls"); }), "controls[0].name", JsonMetadataSeverity.Error, "NombreNoAdmitido" },
        { Change(document => Control(document, 0)["type"] = "slider"), "controls[0].type", JsonMetadataSeverity.Error, "ValorNoAdmitido" },
        { Change(document => Action(document, 0)["name"] = "borrarTodo"), "controls[0].actions[0].name", JsonMetadataSeverity.Error, "ValorNoAdmitido" },
        { Change(document => Control(document, 0)["items"]![0]!["text"] = "Ir a <b>detalle</b>"), "controls[0].items[0].text", JsonMetadataSeverity.Error, "ValorNoAdmitido" },
        { Change(document => document["states"]![0]!["controls"] = new JsonArray(7)), "states[0].controls[0]", JsonMetadataSeverity.Error, "TipoIncorrecto" },
        { Change(document => Control(document, 0)["icono"] = "flecha"), "controls[0].icono", JsonMetadataSeverity.Error, "PropiedadDesconocida" }
    };

    [Theory]
    [MemberData(nameof(RejectedCases))]
    public void IncompleteOrUnsupportedCompositionsAreRejectedAtTheirLocation(string document, string path, JsonMetadataSeverity severity, string code)
    {
        var diagnostic = Assert.Single(JsonMetadataService.ReadDocument(document).Diagnostics);

        Assert.Equal((path, severity, code), (diagnostic.Path, diagnostic.Severity, diagnostic.Code));
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Needed));
    }

    [Fact]
    public void SiteViewWithSeveralStatesOrControlsIsNotSupported()
    {
        var reading = JsonMetadataService.ReadDocument(
            """{ "schemaVersion": "1.0", "view": { "id": "vDemo" }, "states": [ { "name": "A", "isDefault": true }, { "name": "B" } ] }""");

        var diagnostic = Assert.Single(reading.Diagnostics);
        Assert.Equal(("view.type", JsonMetadataSeverity.NoSoportado), (diagnostic.Path, diagnostic.Severity));
    }

    [Fact]
    public void EffectiveDocumentOverTheLimitCannotBeCreatedEvenIfTheCompactOneFits()
    {
        // Compacto dentro del límite; al completar la fuente de cada parámetro y sangrar, la definición efectiva lo supera.
        var parameters = new StringBuilder();
        for (var index = 0; index < 6000; index++)
        {
            parameters.Append(index == 0 ? "" : ",").Append($$"""{"name":"P{{index:D5}}","default":"1"}""");
        }
        var compact = """{"schemaVersion":"1.0","view":{"id":"vGrande","type":"go"},"states":[{"rows":[{"areas":[{"object":{"type":"grid","data":{"connection":"c","storedProcedure":"dbo.P","parameters":[""" +
            parameters + "]}}}]}]}]}";
        Assert.True(compact.Length <= JsonMetadataReader.MaxContentLength);

        var reading = JsonMetadataService.ReadDocument(compact);

        var diagnostic = Assert.Single(reading.Diagnostics);
        Assert.Equal(("", JsonMetadataSeverity.Error, "DocumentoDemasiadoGrande"), (diagnostic.Path, diagnostic.Severity, diagnostic.Code));
        Assert.True(reading.EffectiveText!.Length > JsonMetadataReader.MaxContentLength);
        // Y lo que no pasa de ese límite siempre puede volver a recibirse.
        var small = JsonMetadataService.ReadDocument(TwoStates);
        Assert.True(small.EffectiveText!.Length <= JsonMetadataReader.MaxContentLength);
        Assert.DoesNotContain(JsonMetadataService.ReadDocument(small.EffectiveText).Diagnostics, item => item.Code == "DocumentoDemasiadoGrande");
    }

    private static ViewCreationPlan TwoStatesPlan() => Plan(JsonMetadataService.ReadDocument(TwoStates));

    private static ViewCreationPlan Plan(JsonMetadataReading reading, params ViewCreationPreflightResult?[] grids)
    {
        Assert.Empty(reading.Diagnostics);
        var document = reading.Document!.Deserialize<JsonMetadataDocument>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return JsonMetadataPlanFactory.Create(document,
            grids.Length > 0 ? grids : document.States!.Select(_ => (ViewCreationPreflightResult?)GridCatalog()).ToArray(), reading.Annotations);
    }

    private static ViewCreationPreflightResult GridCatalog() => new(
        true, "vDemoEstados01", null, [], "[dbo].[PRC_GET_GRID_TEST_SORT_COLUMN]", [2, 3, 4, 5, 6]);

    private static IReadOnlyList<string> Compare(ViewCreationPlan plan, ViewCreationSnapshot snapshot) => ViewCreationVerifier.Compare(
        plan, AuditUser, snapshot.Counts, snapshot.ValueRows, snapshot.Values, snapshot.ParameterRows, snapshot.MappingRows,
        snapshot.StateValues, snapshot.ControlRows, snapshot.ControlItemRows);

    private static ViewCreationSnapshot MatchingSnapshot(ViewCreationPlan plan) => new(
        ViewCreationVerifier.ExpectedCounts(plan), plan.StateBlocks.Count, ViewCreationVerifier.ExpectedValues(plan, AuditUser),
        ViewCreationVerifier.ExpectedParameterRows(plan, AuditUser), ViewCreationVerifier.ExpectedMappingRows(plan, AuditUser),
        plan.StateBlocks.ToDictionary(block => block.State.Name, block => ViewCreationVerifier.ExpectedStateValues(plan, block, AuditUser)),
        ViewCreationVerifier.ExpectedControlRows(plan, AuditUser), ViewCreationVerifier.ExpectedControlItemRows(plan, AuditUser));

    private static ScriptExecution Completed() => new(ScriptExecutionKind.Completed, [], TranCountAfter: 0);

    private static string Change(Action<JsonObject> change)
    {
        var document = JsonNode.Parse(TwoStates)!.AsObject();
        change(document);
        return document.ToJsonString();
    }

    private static JsonObject Control(JsonObject document, int index) => document["controls"]![index]!.AsObject();

    private static JsonObject Action(JsonObject document, int index) => Control(document, index)["actions"]![0]!.AsObject();

    private static int Count(string text, string fragment) => Regex.Matches(text, Regex.Escape(fragment)).Count;
}
