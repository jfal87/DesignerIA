using DesignerIA.Api.Controllers;
using DesignerIA.Api.Services.ViewScripts;
using DesignerIA.Contracts;
using Xunit;

namespace DesignerIA.Api.Tests;

/// <summary>
/// Pruebas de las decisiones puras de la creación de vistas. Ninguna abre una
/// conexión SQL: la ejecución real se comprueba aparte, con autorización.
/// </summary>
public sealed class ViewCreationTests
{
    private const string AuditUser = @"EQUIPO\usuario";
    private const string AllowedUser = @"EQUIPO\usuario";

    [Theory]
    [InlineData(false, true, 60, AllowedUser, true, AllowedUser, "desarrollo")]
    [InlineData(true, false, 60, AllowedUser, true, AllowedUser, "deshabilitada")]
    [InlineData(true, true, 0, AllowedUser, true, AllowedUser, "tiempo máximo")]
    [InlineData(true, true, -1, AllowedUser, true, AllowedUser, "tiempo máximo")]
    [InlineData(true, true, 60, "", true, AllowedUser, "usuario autorizado configurado")]
    [InlineData(true, true, 60, null, true, AllowedUser, "usuario autorizado configurado")]
    [InlineData(true, true, 60, AllowedUser, false, AllowedUser, "no está autorizado")]
    [InlineData(true, true, 60, AllowedUser, true, @"EQUIPO\otro", "no está autorizado")]
    [InlineData(true, true, 60, AllowedUser, true, null, "no está autorizado")]
    public void CreationIsBlockedUnlessEveryConditionHolds(bool isDevelopment, bool enabled, int commandTimeoutSeconds,
        string? allowedUser, bool isAuthenticated, string? userName, string expectedFragment)
    {
        var reason = ViewCreationService.GetBlockReason(isDevelopment, enabled, commandTimeoutSeconds, allowedUser, isAuthenticated, userName);

        Assert.NotNull(reason);
        Assert.Contains(expectedFragment, reason);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(60)]
    public void CreationIsAllowedForTheConfiguredWindowsUserIgnoringCase(int commandTimeoutSeconds)
    {
        Assert.Null(ViewCreationService.GetBlockReason(true, true, commandTimeoutSeconds, AllowedUser, true, AllowedUser));
        Assert.Null(ViewCreationService.GetBlockReason(true, true, commandTimeoutSeconds, AllowedUser, true, @"equipo\USUARIO"));
    }

    [Fact]
    public void ConnectionAliasIsResolvedByTheDatabaseAndNotComparedAsText()
    {
        var expected = ViewCreationVerifier.ExpectedValues(GridPlan(), AuditUser);

        // La igualdad del alias la decide la base, como en el script: dos lecturas con @alias y ninguna comparación de texto en C#.
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(ViewCreationVerifier.ValuesSql, @"WHERE StringConnection = @alias\)").Count);
        Assert.DoesNotContain("JOIN dbo.StringsConnection", ViewCreationVerifier.ValuesSql);
        Assert.DoesNotContain(expected, pair => pair.Value == "GestionEngine");
        Assert.Equal("1", expected["StoredProcedure.Conexiones que resuelve el alias"]);
        Assert.Equal("1", expected["StoredProcedure.IdStringConnection es el del alias"]);
    }

    [Theory]
    [InlineData("ConfiguracionesObjetoDeArea.Configuracion")]
    [InlineData("ConfiguracionesEstados.Configuracion")]
    [InlineData("Vistas.Nombre")]
    [InlineData("StoredProcedure.Nombre")]
    public void TextValuesAreComparedExactlyIncludingCase(string name)
    {
        var plan = GridPlan();
        var matching = MatchingSnapshot(plan).Values!;
        var values = new Dictionary<string, string?>(matching) { [name] = matching[name]!.ToUpperInvariant() };

        var mismatch = Assert.Single(ViewCreationVerifier.Compare(plan, AuditUser, ViewCreationVerifier.ExpectedCounts(plan), 1, values));

        Assert.Contains(name, mismatch);
    }

    [Fact]
    public void CreateEndpointReceivesOnlyTheViewSpec()
    {
        var parameters = typeof(ViewScriptController).GetMethod(nameof(ViewScriptController.Create))!.GetParameters();

        var parameter = Assert.Single(parameters);
        Assert.Equal(typeof(ViewSpec), parameter.ParameterType);
    }

    [Fact]
    public void BatchWithoutErrorsIsCompleted()
    {
        Assert.Equal(ScriptExecutionKind.Completed, ViewCreationService.Classify([]).Kind);
    }

    [Theory]
    [InlineData("[50000/1] La vista ya existe; el script no realiza cambios.", 50000, 1)]
    [InlineData("[1222/56] Lock request time out period exceeded.", 1222, 56)]
    [InlineData("[2627/1] Violation of PRIMARY KEY constraint.", 2627, 1)]
    public void ErrorForwardedByTheScriptKeepsItsOriginalNumberAndState(string message, int number, int state)
    {
        var execution = ViewCreationService.Classify([(50000, message)]);

        Assert.Equal(ScriptExecutionKind.RolledBackByScript, execution.Kind);
        Assert.Equal(number, execution.OriginalNumber);
        Assert.Equal(state, execution.OriginalState);
    }

    [Fact]
    public void ClientTimeoutIsNotTreatedAsARollback()
    {
        var execution = ViewCreationService.Classify([(-2, "Timeout expired."), (0, "Operation cancelled by user.")]);

        Assert.Equal(ScriptExecutionKind.Timeout, execution.Kind);
    }

    [Theory]
    [InlineData(0, "Operation cancelled by user.")]
    [InlineData(50000, "El script requiere una sesión sin transacción abierta.")]
    [InlineData(50000, "texto [1222/56] con el prefijo en medio")]
    [InlineData(547, "The INSERT statement conflicted with the FOREIGN KEY constraint.")]
    public void AnyOtherEndingIsUnknown(int number, string message)
    {
        Assert.Equal(ScriptExecutionKind.Unknown, ViewCreationService.Classify([(number, message)]).Kind);
    }

    [Fact]
    public void GridPlanExpectsTenRecordsAndEmptyViewExpectsFour()
    {
        var grid = ViewCreationVerifier.ExpectedCounts(GridPlan());
        var empty = ViewCreationVerifier.ExpectedCounts(EmptyPlan());

        Assert.Equal(10, CreatedRecords(grid));
        Assert.Equal(4, CreatedRecords(empty));
        Assert.Equal(0, empty["Areas"]);
        Assert.Equal(0, empty["LeftMenus"]);
        Assert.Equal(1, empty["ConfiguracionesEstados"]);
        Assert.All(grid.Values, count => Assert.Equal(1, count));
    }

    [Fact]
    public void ExpectedValuesComeFromThePlanAndTheGeneratorConstants()
    {
        var grid = ViewCreationVerifier.ExpectedValues(GridPlan(), AuditUser);
        var empty = ViewCreationVerifier.ExpectedValues(EmptyPlan(), AuditUser);

        Assert.Equal("vPrueba", grid["Vistas.IdVista"]);
        Assert.Equal("vPrueba", grid["Vistas.Nombre"]);
        Assert.Equal("go", grid["Vistas.TipoVista"]);
        Assert.Equal("0", grid["Vistas.UsaSeguridad"]);
        Assert.Null(grid["Vistas.IdStringsConnectionMyVision"]);
        Assert.Equal("[dbo].[PRC_PRUEBA]", grid["ExtraccionesDatos.Descripcion"]);
        Assert.Equal("[dbo].[PRC_PRUEBA]", grid["StoredProcedure.Nombre"]);
        Assert.Equal("1", grid["StoredProcedure.IdStringConnection es el del alias"]);
        Assert.Equal("2", grid["ObjetosDeArea.IdTipoObjetoDeArea"]);
        Assert.Equal(GridConfigurationPresets.GridQueryConfiguration, grid["ConfiguracionesObjetoDeArea.Configuracion"]);
        Assert.Equal("LMFiltros", grid["LeftMenus.Nombre"]);
        Assert.Equal(AuditUser, grid["LeftMenus.User_Create"]);
        Assert.Null(grid["Areas.User_Update"]);
        Assert.Equal("0", grid["Areas.F_Update informada"]);
        Assert.Equal("1", grid["ConfiguracionesEstados.F_Update informada"]);

        Assert.Equal("site", empty["Vistas.TipoVista"]);
        Assert.Equal("PantallaF0", empty["Filas.Nombre"]);
        Assert.DoesNotContain(empty.Keys, key => key.StartsWith("Areas.", StringComparison.Ordinal));
        Assert.DoesNotContain(empty.Keys, key => key.StartsWith("LeftMenus.", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryExpectedCountAndValueIsReadByTheVerificationQueries()
    {
        foreach (var name in ViewCreationVerifier.ExpectedCounts(GridPlan()).Keys)
        {
            Assert.Contains($"AS [{name}]", ViewCreationVerifier.CountsSql);
        }
        foreach (var name in ViewCreationVerifier.ExpectedValues(GridPlan(), AuditUser).Keys)
        {
            Assert.Contains($"AS [{name}]", ViewCreationVerifier.ValuesSql);
        }
    }

    [Theory]
    [InlineData("INSERT")]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    [InlineData("MERGE")]
    [InlineData("EXEC")]
    [InlineData("INTO")]
    public void VerificationQueriesOnlyRead(string keyword)
    {
        var pattern = $@"\b{keyword}\b";

        Assert.DoesNotMatch(pattern, ViewCreationVerifier.CountsSql);
        Assert.DoesNotMatch(pattern, ViewCreationVerifier.ValuesSql);
    }

    [Fact]
    public void MatchingSnapshotHasNoMismatchesForBothStructures()
    {
        foreach (var plan in new[] { GridPlan(), EmptyPlan() })
        {
            var snapshot = MatchingSnapshot(plan);

            Assert.Empty(ViewCreationVerifier.Compare(plan, AuditUser, snapshot.Counts, snapshot.ValueRows, snapshot.Values));
        }
    }

    [Fact]
    public void CountMismatchIsReported()
    {
        var plan = GridPlan();
        var counts = new Dictionary<string, int>(ViewCreationVerifier.ExpectedCounts(plan)) { ["Filas con el mismo IdFila"] = 2 };

        var mismatch = Assert.Single(ViewCreationVerifier.Compare(plan, AuditUser, counts, 1, MatchingSnapshot(plan).Values));

        Assert.Contains("Filas con el mismo IdFila", mismatch);
    }

    [Fact]
    public void EmptyViewWithGridRecordsIsReported()
    {
        var plan = EmptyPlan();
        var counts = new Dictionary<string, int>(ViewCreationVerifier.ExpectedCounts(plan)) { ["LeftMenus"] = 1 };

        var mismatch = Assert.Single(ViewCreationVerifier.Compare(plan, AuditUser, counts, 1, MatchingSnapshot(plan).Values));

        Assert.Contains("LeftMenus", mismatch);
    }

    [Theory]
    [InlineData("Vistas.TipoVista", "GO")]
    [InlineData("Vistas.User_Create", @"EQUIPO\otro")]
    [InlineData("Vistas.User_Update", "")]
    [InlineData("Vistas.JsCode", null)]
    [InlineData("StoredProcedure.IdStringConnection es el del alias", "0")]
    [InlineData("StoredProcedure.Conexiones que resuelve el alias", "2")]
    [InlineData("StoredProcedure.Conexiones que resuelve el alias", "0")]
    [InlineData("ConfiguracionesObjetoDeArea.Configuracion", "{}")]
    [InlineData("Estados.IdVista", "otraVista")]
    public void ValueMismatchIsReportedExactly(string name, string? found)
    {
        var plan = GridPlan();
        var values = new Dictionary<string, string?>(MatchingSnapshot(plan).Values!) { [name] = found };

        var mismatch = Assert.Single(ViewCreationVerifier.Compare(plan, AuditUser, ViewCreationVerifier.ExpectedCounts(plan), 1, values));

        Assert.Contains(name, mismatch);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void ValuesMustComeFromExactlyOneRow(int valueRows)
    {
        var plan = GridPlan();

        var mismatches = ViewCreationVerifier.Compare(plan, AuditUser, ViewCreationVerifier.ExpectedCounts(plan), valueRows, MatchingSnapshot(plan).Values);

        Assert.Contains(mismatches, mismatch => mismatch.Contains("Lectura de valores"));
    }

    [Fact]
    public void CompletedBatchWithMatchingVerificationIsCreated()
    {
        foreach (var plan in new[] { GridPlan(), EmptyPlan() })
        {
            var result = ViewCreationService.Decide(plan, AuditUser, Completed(), MatchingSnapshot(plan));

            Assert.Equal(ViewCreationStatus.Created, result.Status);
            Assert.Equal(plan.View.Name, result.ViewId);
        }
    }

    [Fact]
    public void CompletedBatchIsIndeterminateWhenVerificationDoesNotMatch()
    {
        var plan = GridPlan();
        var snapshot = MatchingSnapshot(plan);
        var wrong = snapshot with { Values = new Dictionary<string, string?>(snapshot.Values!) { ["Filas.Nombre"] = "OtraFila" } };

        var result = ViewCreationService.Decide(plan, AuditUser, Completed(), wrong);

        Assert.Equal(ViewCreationStatus.Indeterminate, result.Status);
        Assert.Null(result.ViewId);
        Assert.Contains(result.Diagnostics, line => line.Contains("Filas.Nombre"));
        Assert.Contains(result.Diagnostics, line => line.Contains("no se atribuye"));
    }

    [Fact]
    public void CompletedBatchIsIndeterminateWhenVerificationCannotBeRead()
    {
        var result = ViewCreationService.Decide(GridPlan(), AuditUser, Completed(), null);

        Assert.Equal(ViewCreationStatus.Indeterminate, result.Status);
        Assert.Null(result.ViewId);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(1, true)]
    public void CompletedBatchIsIndeterminateWhenTheSessionIsNotConfirmed(int? tranCountAfter, bool rolledBackAfter)
    {
        var plan = GridPlan();
        var execution = Completed() with { TranCountAfter = tranCountAfter, RolledBackAfter = rolledBackAfter };

        var result = ViewCreationService.Decide(plan, AuditUser, execution, MatchingSnapshot(plan));

        Assert.Equal(ViewCreationStatus.Indeterminate, result.Status);
    }

    [Fact]
    public void ExistingNameReportedByTheScriptIsNotCreatedAndIsNotAttributed()
    {
        var plan = GridPlan();
        var execution = new ScriptExecution(ScriptExecutionKind.RolledBackByScript, [50000], 50000, 1, TranCountAfter: 0);

        var result = ViewCreationService.Decide(plan, AuditUser, execution, MatchingSnapshot(plan));

        Assert.Equal(ViewCreationStatus.NotCreated, result.Status);
        Assert.Null(result.ViewId);
        Assert.Contains("Ya existe una vista con ese nombre", result.Message);
    }

    [Fact]
    public void RollbackWithNothingLeftIsNotCreated()
    {
        var plan = GridPlan();
        var execution = new ScriptExecution(ScriptExecutionKind.RolledBackByScript, [50000], 1222, 56, TranCountAfter: 0);

        var result = ViewCreationService.Decide(plan, AuditUser, execution, AbsentSnapshot(plan));

        Assert.Equal(ViewCreationStatus.NotCreated, result.Status);
        Assert.Contains("ocupada", result.Message);
        Assert.Contains(result.Diagnostics, line => line.Contains("1222"));
    }

    [Fact]
    public void RollbackThatLeavesAViewWithThatNameIsIndeterminate()
    {
        var plan = GridPlan();
        var execution = new ScriptExecution(ScriptExecutionKind.RolledBackByScript, [50000], 1222, 56, TranCountAfter: 0);

        var result = ViewCreationService.Decide(plan, AuditUser, execution, MatchingSnapshot(plan));

        Assert.Equal(ViewCreationStatus.Indeterminate, result.Status);
        Assert.Null(result.ViewId);
        Assert.Contains(result.Diagnostics, line => line.Contains("no se atribuye"));
    }

    [Theory]
    [InlineData(ScriptExecutionKind.Timeout)]
    [InlineData(ScriptExecutionKind.Unknown)]
    public void TimeoutOrUnknownEndingIsIndeterminateEvenIfNothingIsFound(ScriptExecutionKind kind)
    {
        var plan = GridPlan();
        var execution = new ScriptExecution(kind, [-2], TranCountAfter: 0);

        var absent = ViewCreationService.Decide(plan, AuditUser, execution, AbsentSnapshot(plan));
        var present = ViewCreationService.Decide(plan, AuditUser, execution, MatchingSnapshot(plan));

        Assert.Equal(ViewCreationStatus.Indeterminate, absent.Status);
        Assert.Contains(absent.Diagnostics, line => line.Contains("no hay registros"));
        Assert.Equal(ViewCreationStatus.Indeterminate, present.Status);
        Assert.Null(present.ViewId);
    }

    [Fact]
    public void BatchThatWasNotSentIsNotCreated()
    {
        var plan = GridPlan();
        var execution = new ScriptExecution(ScriptExecutionKind.NotExecuted, [], TranCountAfter: 1);

        var result = ViewCreationService.Decide(plan, AuditUser, execution, AbsentSnapshot(plan));

        Assert.Equal(ViewCreationStatus.NotCreated, result.Status);
        Assert.Null(result.ViewId);
    }

    [Theory]
    [InlineData("https://desagestion.ternium.net", "https://desagestion.ternium.net/GestionEngine/DefaultV2.aspx?Limpiarcache=vPrueba&StyleVersion=V2")]
    [InlineData("https://desagestionmx.ternium.net/", "https://desagestionmx.ternium.net/GestionEngine/DefaultV2.aspx?Limpiarcache=vPrueba&StyleVersion=V2")]
    [InlineData("  https://desagestion.ternium.net  ", "https://desagestion.ternium.net/GestionEngine/DefaultV2.aspx?Limpiarcache=vPrueba&StyleVersion=V2")]
    public void ViewUrlFollowsTheMotorPatternForEachConfiguredBase(string configured, string expected)
    {
        Assert.True(ViewCreationService.TryNormalizeMotorBaseUrl(configured, out var baseUrl));

        Assert.Equal(expected, ViewCreationService.BuildViewUrl(baseUrl!, "vPrueba"));
    }

    [Theory]
    [InlineData("Vista con espacios", "Vista%20con%20espacios")]
    [InlineData("a&b=c", "a%26b%3Dc")]
    [InlineData("vista#1?x", "vista%231%3Fx")]
    [InlineData("Año/2026", "A%C3%B1o%2F2026")]
    [InlineData("100%+listo", "100%25%2Blisto")]
    public void ViewIdIsEncodedAsAQueryValue(string viewId, string encoded)
    {
        var url = ViewCreationService.BuildViewUrl("https://desagestion.ternium.net", viewId);

        Assert.Equal($"https://desagestion.ternium.net/GestionEngine/DefaultV2.aspx?Limpiarcache={encoded}&StyleVersion=V2", url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingMotorBaseUrlIsValidAndDoesNotBlockCreation(string? configured)
    {
        Assert.True(ViewCreationService.TryNormalizeMotorBaseUrl(configured, out var baseUrl));
        Assert.Null(baseUrl);
        Assert.Null(ViewCreationService.GetBlockReason(true, true, 60, AllowedUser, true, AllowedUser, configured));
    }

    [Theory]
    [InlineData("desagestion.ternium.net")]
    [InlineData("/GestionEngine")]
    [InlineData("ftp://desagestion.ternium.net")]
    [InlineData("file:///C:/motor")]
    [InlineData("https://")]
    [InlineData("https://desagestion.ternium.net?x=1")]
    [InlineData("https://desagestion.ternium.net/#inicio")]
    [InlineData("https://usuario:clave@desagestion.ternium.net")]
    public void InvalidMotorBaseUrlBlocksCreationBeforeExecution(string configured)
    {
        Assert.False(ViewCreationService.TryNormalizeMotorBaseUrl(configured, out var baseUrl));
        Assert.Null(baseUrl);

        var reason = ViewCreationService.GetBlockReason(true, true, 60, AllowedUser, true, AllowedUser, configured);

        Assert.NotNull(reason);
        Assert.Contains("URL base de Motor", reason);
    }

    [Fact]
    public void ValidMotorBaseUrlDoesNotBlockCreation()
    {
        Assert.Null(ViewCreationService.GetBlockReason(true, true, 60, AllowedUser, true, AllowedUser, "https://desagestion.ternium.net"));
    }

    [Fact]
    public void CreatedResultCarriesTheViewUrlWhenTheMotorBaseIsConfigured()
    {
        var plan = GridPlan();

        var result = ViewCreationService.Decide(plan, AuditUser, Completed(), MatchingSnapshot(plan), "https://desagestion.ternium.net");

        Assert.Equal(ViewCreationStatus.Created, result.Status);
        Assert.Equal("vPrueba", result.ViewId);
        Assert.Equal("https://desagestion.ternium.net/GestionEngine/DefaultV2.aspx?Limpiarcache=vPrueba&StyleVersion=V2", result.ViewUrl);
    }

    [Fact]
    public void CreatedResultKeepsTheViewIdWithoutUrlWhenTheMotorBaseIsMissing()
    {
        var plan = GridPlan();

        var result = ViewCreationService.Decide(plan, AuditUser, Completed(), MatchingSnapshot(plan));

        Assert.Equal(ViewCreationStatus.Created, result.Status);
        Assert.Equal("vPrueba", result.ViewId);
        Assert.Null(result.ViewUrl);
        Assert.Contains(result.Diagnostics, line => line.Contains("sin URL"));
    }

    [Fact]
    public void OnlyCreatedResultsCarryAViewUrl()
    {
        const string motorBaseUrl = "https://desagestion.ternium.net";
        var plan = GridPlan();
        var matching = MatchingSnapshot(plan);
        var wrong = matching with { Values = new Dictionary<string, string?>(matching.Values!) { ["Filas.Nombre"] = "OtraFila" } };
        var rolledBack = new ScriptExecution(ScriptExecutionKind.RolledBackByScript, [50000], 1222, 56, TranCountAfter: 0);
        var results = new[]
        {
            ViewCreationService.Decide(plan, AuditUser, Completed(), wrong, motorBaseUrl),
            ViewCreationService.Decide(plan, AuditUser, Completed(), null, motorBaseUrl),
            ViewCreationService.Decide(plan, AuditUser, Completed() with { TranCountAfter = null }, matching, motorBaseUrl),
            ViewCreationService.Decide(plan, AuditUser, Completed() with { TranCountAfter = 1, RolledBackAfter = true }, matching, motorBaseUrl),
            ViewCreationService.Decide(plan, AuditUser, new ScriptExecution(ScriptExecutionKind.RolledBackByScript, [50000], 50000, 1, TranCountAfter: 0), matching, motorBaseUrl),
            ViewCreationService.Decide(plan, AuditUser, rolledBack, AbsentSnapshot(plan), motorBaseUrl),
            ViewCreationService.Decide(plan, AuditUser, rolledBack, matching, motorBaseUrl),
            ViewCreationService.Decide(plan, AuditUser, new ScriptExecution(ScriptExecutionKind.Timeout, [-2], TranCountAfter: 0), matching, motorBaseUrl),
            ViewCreationService.Decide(plan, AuditUser, new ScriptExecution(ScriptExecutionKind.Unknown, [547], TranCountAfter: 0), matching, motorBaseUrl),
            ViewCreationService.Decide(plan, AuditUser, new ScriptExecution(ScriptExecutionKind.NotExecuted, [], TranCountAfter: 1), AbsentSnapshot(plan), motorBaseUrl)
        };

        Assert.All(results, result =>
        {
            Assert.NotEqual(ViewCreationStatus.Created, result.Status);
            Assert.Null(result.ViewId);
            Assert.Null(result.ViewUrl);
        });
        Assert.Contains(results, result => result.Status == ViewCreationStatus.NotCreated);
        Assert.Contains(results, result => result.Status == ViewCreationStatus.Indeterminate);
    }

    private static ViewCreationPlan GridPlan() => new(
        new ViewCreationPlanView("vPrueba", "Descripción de prueba", "go"),
        new ViewCreationPlanState("Estado01", IsDefault: true),
        new ViewCreationPlanStateConfiguration("{\"filters\":\"\"}"),
        new ViewCreationPlanRow("Fila01", Width: 0, Height: 0),
        new ViewCreationPlanGrid("Area01", "Grilla01", "[dbo].[PRC_PRUEBA]", "GestionEngine",
            GridConfigurationPresets.GridQueryConfiguration, "LMFiltros"));

    private static ViewCreationPlan EmptyPlan() => new(
        new ViewCreationPlanView("vVacia", "vVacia"),
        new ViewCreationPlanState("Pantalla", IsDefault: true),
        new ViewCreationPlanStateConfiguration("{\"filters\":\"\"}"),
        new ViewCreationPlanRow("PantallaF0", Width: 0, Height: 0));

    private static ScriptExecution Completed() => new(ScriptExecutionKind.Completed, [], TranCountAfter: 0);

    private static ViewCreationSnapshot MatchingSnapshot(ViewCreationPlan plan) => new(
        ViewCreationVerifier.ExpectedCounts(plan), 1, ViewCreationVerifier.ExpectedValues(plan, AuditUser));

    private static ViewCreationSnapshot AbsentSnapshot(ViewCreationPlan plan) => new(
        ViewCreationVerifier.ExpectedCounts(plan).ToDictionary(count => count.Key, _ => 0), 0, null);

    private static int CreatedRecords(IReadOnlyDictionary<string, int> counts) =>
        counts.Where(count => count.Key is not ("Vistas por IdVista o Nombre" or "Filas con el mismo IdFila")).Sum(count => count.Value);
}
