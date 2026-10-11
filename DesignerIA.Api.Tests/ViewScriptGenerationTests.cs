using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesignerIA.Api.Services;
using DesignerIA.Api.Services.ViewTemplates;
using DesignerIA.Api.Services.ViewScripts;
using DesignerIA.Api.Services.ViewSpecs;
using DesignerIA.Contracts;
using Xunit;

namespace DesignerIA.Api.Tests;

public sealed class ViewScriptGenerationTests
{
    private const string AuditUser = "TestAuditUser";

    [Fact]
    public void EmptyViewCanGenerateWithoutGapsAndSummarizesLogicalStructure()
    {
        var result = Generate(new ViewSpec("VistaMinima", null, null, []));

        Assert.True(result.CanGenerate);
        Assert.NotNull(result.Script);
        Assert.Empty(result.Gaps);
        Assert.Equal(1, result.Summary.Views);
        Assert.Equal(1, result.Summary.States);
        Assert.Equal(0, result.Summary.Rows);
        Assert.Equal(0, result.Summary.Areas);
        Assert.Equal(0, result.Summary.Handlers);
    }

    [Fact]
    public void EmptyViewInsertsExactlyFourTablesInCurrentOrder()
    {
        var script = GenerateEmptyViewScript();
        var inserts = Regex.Matches(script, @"\bINSERT\b[^\r\n]*")
            .Select(match => match.Value)
            .ToArray();

        Assert.Collection(inserts,
            insert => Assert.StartsWith("INSERT INTO dbo.Vistas (", insert),
            insert => Assert.StartsWith("INSERT INTO dbo.Estados (", insert),
            insert => Assert.StartsWith("INSERT INTO dbo.Filas (", insert),
            insert => Assert.StartsWith("INSERT INTO dbo.ConfiguracionesEstados (", insert));
    }

    [Fact]
    public void EmptyViewUsesCurrentDefaultValuesAndStateConfiguration()
    {
        var script = GenerateEmptyViewScript();
        var view = GetInsertValues(script, "Vistas");
        var state = GetInsertValues(script, "Estados");
        var row = GetInsertValues(script, "Filas");
        var configuration = GetInsertValues(script, "ConfiguracionesEstados");

        Assert.Equal("N'VistaMinima'", view["Nombre"]);
        Assert.Equal("N'VistaMinima'", view["IdVista"]);
        Assert.Equal("N'VistaMinima'", view["Descripcion"]);
        Assert.Equal("N'site'", view["TipoVista"]);
        Assert.Equal("N'1.0'", view["VersionActual"]);
        Assert.Equal("N'1.0'", view["VistaVersionado"]);
        Assert.Equal("1", view["IdOwner"]);
        Assert.Equal("1", view["IdHistoricoCambio"]);
        Assert.Equal("0", view["UsaSeguridad"]);
        Assert.Equal("GETDATE()", view["FechaCreacion"]);
        Assert.Equal("@IDX_Estado", state["IdEstado"]);
        Assert.Equal("1", state["[Default]"]);
        Assert.Equal("N'Pantalla'", state["Descripcion"]);
        Assert.Equal("N'VistaMinima'", state["IdVista"]);
        Assert.Equal("@IDX_Fila", row["IdFila"]);
        Assert.Equal("@IDX_Estado", row["IdEstado"]);
        Assert.Equal("N'PantallaF0'", row["Nombre"]);
        Assert.Equal("0", row["Ancho"]);
        Assert.Equal("0", row["Altura"]);
        Assert.Equal("@IDX_Estado", configuration["IdEstado"]);
        Assert.Equal("N'{\"filters\":\"\",\"leftMenus\":\"\",\"isPopUp\":false,\"popUp\":{},\"idLayout\":\"00\"}'",
            configuration["Configuracion"]);
    }

    [Theory]
    [InlineData("Vistas")]
    [InlineData("Estados")]
    [InlineData("Filas")]
    public void EmptyViewUsesStandardCreationAuditValues(string table)
    {
        var values = GetInsertValues(GenerateEmptyViewScript(), table);

        Assert.Equal("N'TestAuditUser'", values["User_Create"]);
        Assert.Equal("NULL", values["User_Update"]);
        Assert.Equal("GETDATE()", values["F_Create"]);
        Assert.Equal("NULL", values["F_Update"]);
    }

    [Fact]
    public void StateConfigurationUsesOnlyItsCurrentAuditColumns()
    {
        var values = GetInsertValues(GenerateEmptyViewScript(), "ConfiguracionesEstados");

        Assert.Equal(4, values.Count);
        Assert.Equal("N'TestAuditUser'", values["User_Create"]);
        Assert.Equal("GETDATE()", values["F_Update"]);
        Assert.False(values.ContainsKey("User_Update"));
        Assert.False(values.ContainsKey("F_Create"));
    }

    [Theory]
    [InlineData("Estado", "IdEstado", "Estados")]
    [InlineData("Fila", "IdFila", "Filas")]
    public void IdAllocationReadsLockedMaximumThenIncrementsBeforeInsert(
        string variableSuffix, string idColumn, string table)
    {
        var script = GenerateEmptyViewScript();
        var read = $"SELECT @IDX_{variableSuffix} = ISNULL(MAX({idColumn}), 1) FROM dbo.{table} WITH (UPDLOCK, HOLDLOCK);";
        var increment = $"SET @IDX_{variableSuffix} = @IDX_{variableSuffix} + 1;";
        var insert = $"INSERT INTO dbo.{table} (";

        Assert.Contains(read, script);
        Assert.Contains(increment, script);
        Assert.Contains(insert, script);
        var readIndex = script.IndexOf(read, StringComparison.Ordinal);
        var incrementIndex = script.IndexOf(increment, StringComparison.Ordinal);
        var insertIndex = script.IndexOf(insert, StringComparison.Ordinal);
        Assert.True(readIndex < incrementIndex, "The maximum ID must be read before incrementing it.");
        Assert.True(incrementIndex < insertIndex, "The ID must be incremented before its INSERT.");
    }

    [Fact]
    public void EmptyViewScriptMatchesCurrentFullTextIgnoringLineEndings()
    {
        const string expected = """
            IF @@TRANCOUNT <> 0
            BEGIN
                RAISERROR(N'El script requiere una sesión sin transacción abierta.', 16, 1);
                RETURN;
            END

            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            SET LOCK_TIMEOUT 5000;

            BEGIN TRY
                BEGIN TRANSACTION;

                IF EXISTS (SELECT 1 FROM dbo.Vistas WITH (UPDLOCK, HOLDLOCK)
                           WHERE IdVista = N'VistaMinima' OR Nombre = N'VistaMinima')
                    RAISERROR(N'La vista ya existe; el script no realiza cambios.', 16, 1);

                DECLARE @IDX_Estado int;
                SELECT @IDX_Estado = ISNULL(MAX(IdEstado), 1) FROM dbo.Estados WITH (UPDLOCK, HOLDLOCK);
                DECLARE @IDX_Fila int;
                SELECT @IDX_Fila = ISNULL(MAX(IdFila), 1) FROM dbo.Filas WITH (UPDLOCK, HOLDLOCK);

                INSERT INTO dbo.Vistas (IdOwner, IdHistoricoCambio, Nombre, Descripcion, FechaCreacion, VersionActual, ArchivoJs, IdVista, TipoVista, UsaSeguridad, User_Create, User_Update, F_Create, F_Update, IdStringsConnectionMyVision, JsCode, Frente, VistaVersionado)
                VALUES (1, 1, N'VistaMinima', N'VistaMinima', GETDATE(), N'1.0', N'', N'VistaMinima', N'site', 0, N'TestAuditUser', NULL, GETDATE(), NULL, NULL, N'', N'', N'1.0');

                SET @IDX_Estado = @IDX_Estado + 1;
                INSERT INTO dbo.Estados (IdEstado, [Default], Descripcion, IdVista, ArchivoJs, LeftMenuFijo, User_Create, User_Update, F_Create, F_Update)
                VALUES (@IDX_Estado, 1, N'Pantalla', N'VistaMinima', N'', 0, N'TestAuditUser', NULL, GETDATE(), NULL);

                SET @IDX_Fila = @IDX_Fila + 1;
                INSERT INTO dbo.Filas (IdFila, IdEstado, Nombre, Ancho, Altura, User_Create, User_Update, F_Create, F_Update)
                VALUES (@IDX_Fila, @IDX_Estado, N'PantallaF0', 0, 0, N'TestAuditUser', NULL, GETDATE(), NULL);

                INSERT INTO dbo.ConfiguracionesEstados (IdEstado, Configuracion, User_Create, F_Update)
                VALUES (@IDX_Estado, N'{"filters":"","leftMenus":"","isPopUp":false,"popUp":{},"idLayout":"00"}', N'TestAuditUser', GETDATE());

                IF (SELECT COUNT(*) FROM dbo.Filas WHERE IdFila = @IDX_Fila) <> 1
                    RAISERROR(N'IdFila duplicado; no se realizan cambios.', 16, 3);
                IF (SELECT COUNT(*) FROM dbo.Vistas WHERE IdVista = N'VistaMinima' OR Nombre = N'VistaMinima') <> 1
                    RAISERROR(N'Nombre o IdVista duplicado; no se realizan cambios.', 16, 4);

                COMMIT TRANSACTION;
            END TRY
            BEGIN CATCH
                IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
                DECLARE @ErrorNumber int, @ErrorState int, @ErrorMessage nvarchar(2048);
                SELECT @ErrorNumber = ERROR_NUMBER(), @ErrorState = ERROR_STATE(), @ErrorMessage = ERROR_MESSAGE();
                RAISERROR(N'[%d/%d] %s', 16, 1, @ErrorNumber, @ErrorState, @ErrorMessage);
            END CATCH

            SET LOCK_TIMEOUT -1;
            SET XACT_ABORT OFF;
            """;

        Assert.Equal(NormalizeLineEndings(expected) + "\n", NormalizeLineEndings(GenerateEmptyViewScript()));
    }

    [Fact]
    public void SingleQuoteInViewNameIsDoubledInSqlLiterals()
    {
        var result = Generate(new ViewSpec("Vista'Minima", null, null, []));

        Assert.True(result.CanGenerate);
        Assert.Empty(result.Gaps);
        var script = Assert.IsType<string>(result.Script);
        var view = GetInsertValues(script, "Vistas");
        var state = GetInsertValues(script, "Estados");
        Assert.Equal("N'Vista''Minima'", view["Nombre"]);
        Assert.Equal("N'Vista''Minima'", view["IdVista"]);
        Assert.Equal("N'Vista''Minima'", view["Descripcion"]);
        Assert.Equal("N'Vista''Minima'", state["IdVista"]);
        Assert.Contains("WHERE IdVista = N'Vista''Minima' OR Nombre = N'Vista''Minima'", script);
        Assert.DoesNotContain("'Vista'Minima'", script);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void MissingAuditUserRejectsGenerationWithoutScript(string? auditUser)
    {
        var result = Generate(new ViewSpec("VistaMinima", null, null, []), auditUser);

        Assert.False(result.CanGenerate);
        Assert.Null(result.Script);
        Assert.Equal("AuditUser no disponible", Assert.Single(result.Gaps).Topic);
    }

    [Fact]
    public void ViewWithOneRowAndAreaRejectsUnsupportedShapeWithoutScript()
    {
        var spec = new ViewSpec("VistaMinima", null, "Pantalla",
            [new("Pantalla", [new([new("Grilla", "dbo.TestProcedure", "TestConnection", [])])])]);
        var validation = ViewSpecValidator.Validate(ViewSpecNormalizer.Normalize(spec));
        Assert.True(validation.IsValid);
        Assert.Empty(validation.Errors);
        Assert.Empty(validation.Pending);

        var result = ViewScriptGenerator.Generate(spec, ViewCreationPreflightService.FromExistingViewId(spec.Name!, null), AuditUser);

        Assert.False(result.CanGenerate);
        Assert.Null(result.Script);
        Assert.Equal("Forma no soportada en V1", Assert.Single(result.Gaps).Topic);
    }

    [Fact]
    public void GridInsertsTenTablesWithContractValuesRelationsAndAudit()
    {
        var result = ViewScriptGenerator.Generate(GridSpec(), CompleteGridPreflight(), AuditUser);
        Assert.True(result.CanGenerate);
        Assert.Empty(result.Gaps);
        Assert.Equal(new ViewScriptGenerationSummary(1, 1, 1, 1, 0), result.Summary);
        var script = Assert.IsType<string>(result.Script);
        var tables = Regex.Matches(script, @"INSERT INTO dbo\.(\w+)")
            .Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(new[] { "Vistas", "Estados", "Filas", "Areas", "ExtraccionesDatos", "StoredProcedure",
            "ObjetosDeArea", "ConfiguracionesObjetoDeArea", "LeftMenus", "ConfiguracionesEstados" }, tables);
        var expected = new Dictionary<string, string[]>
        {
            ["Vistas"] = ["1", "1", "N'vConsultaPrueba01'", "N'Consulta de prueba con grilla'", "GETDATE()", "N'1.0'", "N''",
                "N'vConsultaPrueba01'", "N'go'", "0", "N'TestAuditUser'", "NULL", "GETDATE()", "NULL", "NULL", "N''", "N''", "N'1.0'"],
            ["Estados"] = ["@IDX_Estado", "1", "N'Estado01'", "N'vConsultaPrueba01'", "N''", "0", "N'TestAuditUser'", "NULL", "GETDATE()", "NULL"],
            ["Filas"] = ["@IDX_Fila", "@IDX_Estado", "N'Fila01'", "0", "0", "N'TestAuditUser'", "NULL", "GETDATE()", "NULL"],
            ["Areas"] = ["@IDX_Area", "@IDX_Fila", "N'Area01'", "0", "0", "N'TestAuditUser'", "NULL", "GETDATE()", "NULL"],
            ["ExtraccionesDatos"] = ["@IDX_ExtraccionDato", "N'[dbo].[PRC_GET_GRID_TEST_SORT_COLUMN]'", "N'TestAuditUser'", "NULL", "GETDATE()", "NULL"],
            ["StoredProcedure"] = ["@IDX_StoredProcedure", "'[dbo].[PRC_GET_GRID_TEST_SORT_COLUMN]'", "N''", "0", "@IdStringConnection", "@IDX_ExtraccionDato", "1", "N'TestAuditUser'", "NULL", "GETDATE()", "NULL"],
            ["ObjetosDeArea"] = ["@IDX_ObjetoArea", "@IDX_Area", "@IDX_ExtraccionDato", "2", "N'Grilla01'", "0", "N'TestAuditUser'", "NULL", "GETDATE()", "NULL"],
            ["ConfiguracionesObjetoDeArea"] = ["@IDX_ObjetoAreaConfig", "@IDX_ObjetoArea", "N'" + GridConfigurationPresets.GridQueryConfiguration + "'", "N'TestAuditUser'", "NULL", "GETDATE()", "NULL"],
            ["LeftMenus"] = ["@IDX_LeftMenu", "@IDX_Estado", "N'LMFiltros'", "N'TestAuditUser'", "NULL", "GETDATE()", "NULL"],
            ["ConfiguracionesEstados"] = ["@IDX_Estado", "N'{\"filters\":\"\",\"leftMenus\":\"\",\"isPopUp\":false,\"popUp\":{},\"idLayout\":\"00\"}'", "N'TestAuditUser'", "GETDATE()"]
        };
        var columns = new Dictionary<string, string>
        {
            ["Vistas"] = "IdOwner,IdHistoricoCambio,Nombre,Descripcion,FechaCreacion,VersionActual,ArchivoJs,IdVista,TipoVista,UsaSeguridad,User_Create,User_Update,F_Create,F_Update,IdStringsConnectionMyVision,JsCode,Frente,VistaVersionado",
            ["Estados"] = "IdEstado,[Default],Descripcion,IdVista,ArchivoJs,LeftMenuFijo,User_Create,User_Update,F_Create,F_Update",
            ["Filas"] = "IdFila,IdEstado,Nombre,Ancho,Altura,User_Create,User_Update,F_Create,F_Update",
            ["Areas"] = "IdArea,IdFila,Nombre,Ancho,Altura,User_Create,User_Update,F_Create,F_Update",
            ["ExtraccionesDatos"] = "IdExtraccionDato,Descripcion,User_Create,User_Update,F_Create,F_Update",
            ["StoredProcedure"] = "IdStoredProcedure,Nombre,Descripcion,OrdenEjecucion,IdStringConnection,IdExtraccionDato,IdTipoEjecucionSP,User_Create,User_Update,F_Create,F_Update",
            ["ObjetosDeArea"] = "IdObjetoDeArea,IdArea,IdExtraccionDato,IdTipoObjetoDeArea,Nombre,UsaPaginador,User_Create,User_Update,F_Create,F_Update",
            ["ConfiguracionesObjetoDeArea"] = "Id,IdObjetoDeArea,Configuracion,User_Create,User_Update,F_Create,F_Update",
            ["LeftMenus"] = "IdLeftMenu,IdEstado,Nombre,User_Create,User_Update,F_Create,F_Update",
            ["ConfiguracionesEstados"] = "IdEstado,Configuracion,User_Create,F_Update"
        };
        foreach (var table in tables)
        {
            var values = GetInsertValues(script, table);
            Assert.Equal(columns[table].Split(','), values.Keys.ToArray());
            Assert.Equal(expected[table], values.Values.ToArray());
        }
        Assert.Equal("@IDX_Fila", GetInsertValues(script, "Areas")["IdFila"]);
        Assert.Equal("@IdStringConnection", GetInsertValues(script, "StoredProcedure")["IdStringConnection"]);
        foreach (var forbidden in new[] { "ParametrosSP", "MapeoParametros", "WebControls", "Handlers" })
        {
            Assert.DoesNotContain(forbidden, script);
        }
        Assert.Contains("FROM dbo.StringsConnection WHERE StringConnection = N'GestionEngine';", script);
        Assert.Contains("IF @AliasCount <> 1", script);
        var allocations = Regex.Matches(script, @"SELECT @IDX_\w+ = ISNULL\(MAX\(\w+\), 1\) \+ 1 FROM dbo\.(\w+) WITH \(UPDLOCK, HOLDLOCK\);")
            .Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(tables.Skip(1).Take(8).ToArray(), allocations);
        Assert.True(script.IndexOf("WITH (UPDLOCK, HOLDLOCK)", StringComparison.Ordinal) <
            script.IndexOf("SELECT @IDX_Estado", StringComparison.Ordinal));
        Assert.True(script.IndexOf("IdFila duplicado", StringComparison.Ordinal) <
            script.IndexOf("COMMIT TRANSACTION", StringComparison.Ordinal));
        Assert.True(script.IndexOf("Nombre o IdVista duplicado", StringComparison.Ordinal) <
            script.IndexOf("COMMIT TRANSACTION", StringComparison.Ordinal));
    }

    [Fact]
    public void PresetIsExactAsciiTextAndAppearsLiterallyInScript()
    {
        var preset = GridConfigurationPresets.GridQueryConfiguration;
        Assert.Equal(900, preset.Length);
        Assert.All(preset, character => Assert.InRange((int)character, 0, 127));
        Assert.DoesNotContain("'", preset);
        Assert.Equal("f94db8d2aafd90c09374eac0f3e2a5a6adc64dda97fc2465529698a6e7b425d1",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(preset))).ToLowerInvariant());
        var result = ViewScriptGenerator.Generate(GridSpec(), CompleteGridPreflight(), AuditUser);
        Assert.True(result.CanGenerate);
        Assert.Contains("N'" + preset + "'", result.Script!);
    }

    [Fact]
    public void UnicodeSpacesCommasAndEscapedQuotesRemainInSingleLiterals()
    {
        var spec = GridSpec() with { Name = "Vista á, 'consulta'", Description = "Descripción, con 'comillas' y espacios" };
        var preflight = CompleteGridPreflight() with { ViewName = spec.Name! };
        var result = ViewScriptGenerator.Generate(spec, preflight, "Dominio\\O'Brien");
        Assert.True(result.CanGenerate);
        var values = GetInsertValues(result.Script!, "Vistas");
        Assert.Equal("N'Vista á, ''consulta'''", values["Nombre"]);
        Assert.Equal("N'Vista á, ''consulta'''", values["IdVista"]);
        Assert.Equal("N'Descripción, con ''comillas'' y espacios'", values["Descripcion"]);
        Assert.Equal("N'Dominio\\O''Brien'", values["User_Create"]);
        Assert.Equal("N''", values["ArchivoJs"]);
        Assert.Equal("N''", values["JsCode"]);
        Assert.Equal("N''", values["Frente"]);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("missing-alias")]
    [InlineData("wrong-area-type")]
    [InlineData("shape")]
    [InlineData("preset")]
    [InlineData("handlers")]
    [InlineData("rows")]
    [InlineData("areas")]
    [InlineData("missing-row-name")]
    [InlineData("missing-area-name")]
    [InlineData("missing-object-name")]
    public void UnsupportedOrIncompleteGridRejectsWithoutScript(string problem)
    {
        var spec = GridSpec();
        var row = spec.States![0].Rows![0];
        var area = row.Areas![0];
        spec = problem switch
        {
            "pending" => WithArea(spec, area with { StoredProcedure = null }),
            "missing-alias" => WithArea(spec, area with { ConnectionAlias = null }),
            "wrong-area-type" => WithArea(spec, area with { Type = "Formulario" }),
            "shape" => spec with { ViewType = "site" },
            "preset" => WithArea(spec, area with { ConfigurationPreset = "unknown" }),
            "handlers" => WithArea(spec, area with { Handlers = [new("click", "Estado01")] }),
            "rows" => spec with { States = [spec.States[0] with { Rows = [row, row] }] },
            "areas" => spec with { States = [spec.States[0] with { Rows = [row with { Areas = [area, area] }] }] },
            "missing-row-name" => spec with { States = [spec.States[0] with { Rows = [row with { Name = null }] }] },
            "missing-area-name" => WithArea(spec, area with { Name = null }),
            _ => WithArea(spec, area with { ObjectName = null })
        };
        AssertRejected(ViewScriptGenerator.Generate(spec, CompleteGridPreflight(), AuditUser));
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("name-only")]
    [InlineData("missing-checks")]
    [InlineData("missing-canonical")]
    [InlineData("partial-checks")]
    [InlineData("gap")]
    [InlineData("wrong-name")]
    [InlineData("wrong-procedure")]
    public void GridRequiresCompletePreflightAndCanonicalProcedure(string problem)
    {
        var full = CompleteGridPreflight();
        var preflight = problem switch
        {
            "absent" => null,
            "name-only" => new ViewCreationPreflightResult(true, "vConsultaPrueba01", null),
            "missing-checks" => full with { CompletedChecks = null },
            "missing-canonical" => full with { CanonicalStoredProcedure = null },
            "partial-checks" => full with { CompletedChecks = [1, 2, 3, 4, 5] },
            "gap" => full with { Gaps = [new("Resultado incompatible", "Resultado incompatible")] },
            "wrong-name" => full with { ViewName = "OtraVista" },
            _ => full with { CanonicalStoredProcedure = "[dbo].[OtroSP]" }
        };
        var result = ViewScriptGenerator.Generate(GridSpec(), preflight, AuditUser);
        AssertRejected(result);
        Assert.Equal(problem == "gap" ? "Resultado incompatible" : "Preflight incompleto", Assert.Single(result.Gaps).Topic);
    }

    [Fact]
    public void ConcreteAliasRejectionDoesNotAddIncompletePreflightGap()
    {
        var gap = ViewCreationPreflightService.FromAliasRows(0, null)!;
        var preflight = CompleteGridPreflight() with
        {
            Gaps = [gap],
            CanonicalStoredProcedure = null,
            CompletedChecks = [1]
        };

        var result = ViewScriptGenerator.Generate(GridSpec(), preflight, AuditUser);

        AssertRejected(result);
        Assert.Equal(gap, Assert.Single(result.Gaps));
    }

    [Fact]
    public void PreflightForAnotherViewDoesNotPreserveConcreteAliasRejection()
    {
        var gap = ViewCreationPreflightService.FromAliasRows(0, null)!;
        var preflight = CompleteGridPreflight() with
        {
            ViewName = "OtraVista",
            Gaps = [gap]
        };

        var result = ViewScriptGenerator.Generate(GridSpec(), preflight, AuditUser);

        AssertRejected(result);
        Assert.Equal("Preflight incompleto", Assert.Single(result.Gaps).Topic);
    }

    [Fact]
    public void EmptyViewRequiresPreflightAndRejectsNameCollision()
    {
        var spec = new ViewSpec("VistaMinima", null, null, []);
        AssertRejected(ViewScriptGenerator.Generate(spec, null, AuditUser));
        var preflight = ViewCreationPreflightService.FromExistingViewId("VistaMinima", "VistaExistente");
        var result = ViewScriptGenerator.Generate(spec, preflight, AuditUser);
        AssertRejected(result);
        var collisionGap = Assert.Single(result.Gaps);
        Assert.Equal("Nombre de vista existente", collisionGap.Topic);
        Assert.Equal("Ya existe una vista con el nombre indicado; no se generó un script que pueda sobrescribirla.", collisionGap.Description);
        var fallback = ViewScriptGenerator.Generate(spec, preflight with { Gaps = [] }, AuditUser);
        AssertRejected(fallback);
        Assert.Equal(collisionGap, Assert.Single(fallback.Gaps));
        Assert.False(preflight.IsAvailable);
        Assert.Equal("VistaExistente", preflight.ExistingViewId);
        Assert.Empty(preflight.CompletedChecks!);
        var available = ViewCreationPreflightService.FromExistingViewId("VistaMinima", null);
        Assert.True(available.IsAvailable);
        Assert.Empty(available.Gaps!);
        Assert.Equal(new[] { 1 }, available.CompletedChecks);
    }

    [Theory]
    [InlineData("dbo.Test", true)]
    [InlineData("[dbo].[Test]", true)]
    [InlineData("_schema.Test_123", true)]
    [InlineData("Test", false)]
    [InlineData("server.dbo.Test", false)]
    [InlineData("db.server.dbo.Test", false)]
    [InlineData("dbo.Prueba á", false)]
    [InlineData("dbo.Test-1", false)]
    [InlineData("dbo.1Test", false)]
    [InlineData("[dbo].Test", false)]
    [InlineData(" dbo.Test", false)]
    [InlineData("dbo.Test\n", false)]
    public void ProcedureShapeIsAcceptedOrRejectedWithoutTransformingInput(string procedure, bool accepted)
    {
        Assert.Equal(accepted, ViewScriptGenerationValidator.TryParseStoredProcedure(procedure, out var schema, out var name));
        var spec = WithArea(GridSpec(), GridSpec().States![0].Rows![0].Areas![0] with { StoredProcedure = procedure });
        var preflight = CompleteGridPreflight() with { CanonicalStoredProcedure = $"[{schema}].[{name}]" };
        var result = ViewScriptGenerator.Generate(spec, preflight, AuditUser);
        Assert.Equal(accepted, result.CanGenerate);
        if (!accepted)
        {
            AssertRejected(result);
        }
        else
        {
            Assert.Equal("'[" + schema + "].[" + name + "]'", GetInsertValues(result.Script!, "StoredProcedure")["Nombre"]);
        }
        Assert.Equal(procedure, ViewSpecNormalizer.Normalize(spec).States![0].Rows![0].Areas![0].StoredProcedure);
    }

    [Theory]
    [InlineData("name", 250)]
    [InlineData("description", 350)]
    [InlineData("state", 250)]
    [InlineData("row", 250)]
    [InlineData("area", 75)]
    [InlineData("object", 250)]
    public void ContractTextLengthsAcceptLimitAndRejectOverflow(string field, int limit)
    {
        foreach (var length in new[] { limit, limit + 1 })
        {
            var spec = GridSpec();
            var state = spec.States![0];
            var row = state.Rows![0];
            var area = row.Areas![0];
            var text = new string('a', length);
            spec = field switch
            {
                "name" => spec with { Name = text },
                "description" => spec with { Description = text },
                "state" => spec with { InitialState = text, States = [state with { Name = text }] },
                "row" => spec with { States = [state with { Rows = [row with { Name = text }] }] },
                "area" => WithArea(spec, area with { Name = text }),
                _ => WithArea(spec, area with { ObjectName = text })
            };
            var result = ViewScriptGenerator.Generate(spec, CompleteGridPreflight() with { ViewName = spec.Name! }, AuditUser);
            Assert.Equal(length == limit, result.CanGenerate);
            if (length > limit)
            {
                AssertRejected(result);
            }
        }
    }

    [Fact]
    public void CanonicalProcedureAndPartLengthsAreEnforced()
    {
        Assert.True(ViewScriptGenerationValidator.TryParseStoredProcedure("dbo." + new string('a', 128), out _, out _));
        Assert.False(ViewScriptGenerationValidator.TryParseStoredProcedure("dbo." + new string('a', 129), out _, out _));
        Assert.False(ViewScriptGenerationValidator.TryParseStoredProcedure(new string('a', 129) + ".Test", out _, out _));
        foreach (var nameLength in new[] { 125, 126 })
        {
            var schema = new string('s', 120);
            var name = new string('p', nameLength);
            var spec = GridSpec();
            spec = WithArea(spec, spec.States![0].Rows![0].Areas![0] with { StoredProcedure = schema + "." + name });
            var canonical = $"[{schema}].[{name}]";
            var result = ViewScriptGenerator.Generate(spec, CompleteGridPreflight() with { CanonicalStoredProcedure = canonical }, AuditUser);
            Assert.Equal(canonical.Length == 250, result.CanGenerate);
            if (canonical.Length > 250)
            {
                AssertRejected(result);
            }
        }
    }

    [Fact]
    public void InitialStateUsesSameCaseInsensitiveRuleAndDuplicateStatesAreErrors()
    {
        var spec = GridSpec() with { InitialState = "estado01" };
        Assert.True(ViewSpecValidator.Validate(spec).IsValid);
        var result = ViewScriptGenerator.Generate(spec, CompleteGridPreflight(), AuditUser);
        Assert.True(result.CanGenerate);
        Assert.Equal("N'Estado01'", GetInsertValues(result.Script!, "Estados")["Descripcion"]);
        var empty = new ViewSpec("VistaMinima", null, "pantalla", [new("Pantalla", [])]);
        Assert.True(Generate(empty).CanGenerate);
        var duplicate = spec with { States = [spec.States![0], spec.States[0] with { Name = "ESTADO01" }] };
        Assert.False(ViewSpecValidator.Validate(duplicate).IsValid);
        Assert.Contains(ViewSpecValidator.Validate(duplicate).Errors, error => error.Contains("duplicados", StringComparison.Ordinal));
        AssertRejected(ViewScriptGenerator.Generate(duplicate, CompleteGridPreflight(), AuditUser));
    }

    [Fact]
    public void NormalizeAndWebJsonPreserveViewTypeAndNeverInventComponentNames()
    {
        var spec = GridSpec();
        var normalized = ViewSpecNormalizer.Normalize(spec);
        Assert.Equal("go", normalized.ViewType);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(normalized, options);
        Assert.Equal("go", JsonDocument.Parse(json).RootElement.GetProperty("viewType").GetString());
        Assert.Equal("go", JsonSerializer.Deserialize<ViewSpec>(json, options)!.ViewType);
        var unnamed = WithArea(spec, spec.States![0].Rows![0].Areas![0] with { Name = null, ObjectName = null });
        unnamed = unnamed with { States = [unnamed.States![0] with { Rows = [unnamed.States[0].Rows![0] with { Name = null }] }] };
        var row = ViewSpecNormalizer.Normalize(unnamed).States![0].Rows![0];
        Assert.Null(row.Name);
        Assert.Null(row.Areas![0].Name);
        Assert.Null(row.Areas[0].ObjectName);
        Assert.Equal(GridConfigurationPresets.GridQuery, row.Areas[0].ConfigurationPreset);
    }

    [Fact]
    public void TemplateProducesContractExampleJson()
    {
        const string expected = """
            {
              "name": "vConsultaPrueba01",
              "description": "Consulta de prueba con grilla",
              "viewType": "go",
              "initialState": "Estado01",
              "states": [{
                "name": "Estado01",
                "rows": [{
                  "name": "Fila01",
                  "areas": [{
                    "name": "Area01",
                    "objectName": "Grilla01",
                    "type": "Grilla",
                    "storedProcedure": "dbo.PRC_GET_GRID_TEST_SORT_COLUMN",
                    "connectionAlias": "GestionEngine",
                    "configurationPreset": "grid-consulta-v1",
                    "handlers": []
                  }]
                }]
              }]
            }
            """;
        var actual = JsonSerializer.SerializeToNode(GridSpec(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(expected), actual));
    }

    [Theory]
    [InlineData(0, null, "Alias inexistente")]
    [InlineData(2, 0, "Alias ambiguo")]
    [InlineData(1, 1, "Alias no es SQL Server")]
    [InlineData(1, 2, "Alias no es SQL Server")]
    [InlineData(1, -1, "Alias no es SQL Server")]
    [InlineData(1, null, null)]
    [InlineData(1, 0, null)]
    public void AliasMetadataMapsToContractRejection(int rows, int? oracle, string? topic)
    {
        Assert.Equal(topic, ViewCreationPreflightService.FromAliasRows(rows, oracle)?.Topic);
        Assert.Equal(oracle is null or 0, StringConnectionCatalogService.IsSqlServer(oracle));
    }

    [Fact]
    public void ConfiguredConnectionAndProcedureMetadataMapToContractRejections()
    {
        Assert.Null(ViewCreationPreflightService.FromConfiguredConnection(true));
        Assert.Equal("Alias sin conexión configurada", ViewCreationPreflightService.FromConfiguredConnection(false)!.Topic);
        Assert.Equal("SP inexistente o no visible para DesignerIA", ViewCreationPreflightService.FromProcedure(null, null, out var missing)!.Topic);
        Assert.Null(missing);
        Assert.Null(ViewCreationPreflightService.FromProcedure("dbo", "Catalog_CASE", out var canonical));
        Assert.Equal("[dbo].[Catalog_CASE]", canonical);
        Assert.Equal("SP inexistente o no visible para DesignerIA", ViewCreationPreflightService.FromProcedure("dbo", "Con acento á", out _)!.Topic);
        Assert.NotNull(ViewCreationPreflightService.FromProcedure(new string('s', 128), new string('p', 128), out _));
    }

    [Fact]
    public void OnlyAgreedProcedureAcceptsItsSingleInputQueryStringParameter()
    {
        const string agreed = "[dbo].[PRC_GET_GRID_TEST_SORT_COLUMN]";
        Assert.Null(ViewCreationPreflightService.FromParameters("[dbo].[SinParametros]", []));
        Assert.Null(ViewCreationPreflightService.FromParameters(agreed, [new("@QueryString", false)]));
        foreach (var parameters in new IReadOnlyList<ViewProcedureParameter>[]
        {
            [new("@QueryString", true)], [new("@Otro", false)],
            [new("@QueryString", false), new("@Otro", false)]
        })
        {
            Assert.Equal("SP con parámetros no admitidos por la plantilla", ViewCreationPreflightService.FromParameters(agreed, parameters)!.Topic);
        }
        Assert.Equal("SP con parámetros no admitidos por la plantilla",
            ViewCreationPreflightService.FromParameters("[dbo].[OtroSP]", [new("@QueryString", false)])!.Topic);
        Assert.Equal("SP con parámetros no admitidos por la plantilla",
            ViewCreationPreflightService.FromParameters("[dbo].[prc_get_grid_test_sort_column]", [new("@QueryString", false)])!.Topic);
    }

    [Theory]
    [InlineData(false, 0, "Resultado no determinable")]
    [InlineData(false, 6, "Resultado no determinable")]
    [InlineData(true, 0, "Resultado incompatible")]
    [InlineData(true, 1, "Resultado incompatible")]
    [InlineData(true, 2, null)]
    [InlineData(true, 6, null)]
    public void FirstResultMetadataMapsToContractRejection(bool determinable, int visibleColumns, string? topic) =>
        Assert.Equal(topic, ViewCreationPreflightService.FromFirstResult(determinable, visibleColumns)?.Topic);

    [Fact]
    public void GeneratedScriptsExcludeSqlServer2008R2ForbiddenConstructionsAndEmptyViewGridBlocks()
    {
        var empty = GenerateEmptyViewScript();
        var grid = ViewScriptGenerator.Generate(GridSpec(), CompleteGridPreflight(), AuditUser).Script!;
        foreach (var script in new[] { empty, grid })
        {
            foreach (var forbidden in new[] { "DROP TABLE IF EXISTS", "THROW", "TRY_CONVERT", "IIF", "CONCAT", "OFFSET", "FETCH", "SEQUENCE",
                "DATEFROMPARTS", "EOMONTH", "FORMAT", "JSON_VALUE", "OPENJSON", "STRING_SPLIT", "STRING_AGG", "CREATE OR ALTER", "SET FMTONLY" })
            {
                Assert.False(Regex.IsMatch(script, @"\b" + Regex.Escape(forbidden) + @"\b", RegexOptions.IgnoreCase), forbidden);
            }
        }
        Assert.DoesNotContain("@IdStringConnection", empty);
        Assert.DoesNotContain("@IDX_Area", empty);
        Assert.DoesNotContain("LMFiltros", empty);
        Assert.DoesNotContain("dbo.LeftMenus", empty);
    }

    [Fact]
    public void EmptyViewAcceptsSiteAndRejectsUnknownTypeAndOversizedPhysicalRow()
    {
        var spec = new ViewSpec("VistaMinima", null, null, [], "site");
        Assert.True(Generate(spec).CanGenerate);
        AssertRejected(Generate(spec with { ViewType = "go" }));
        AssertRejected(Generate(spec with { ViewType = "unknown" }));
        var stateName = new string('s', 248);
        Assert.True(Generate(spec with { InitialState = stateName, States = [new(stateName, [])] }).CanGenerate);
        stateName += "s";
        AssertRejected(Generate(spec with { InitialState = stateName, States = [new(stateName, [])] }));
    }

    private static ViewSpec GridSpec() => GridQueryTemplate.Create(new(
        "vConsultaPrueba01", "Consulta de prueba con grilla", "dbo.PRC_GET_GRID_TEST_SORT_COLUMN", "GestionEngine"));

    private static ViewCreationPreflightResult CompleteGridPreflight() => new(
        true, "vConsultaPrueba01", null, [], "[dbo].[PRC_GET_GRID_TEST_SORT_COLUMN]", [1, 2, 3, 4, 5, 6]);

    private static ViewSpec WithArea(ViewSpec spec, ViewSpecArea area) => spec with
    {
        States = [spec.States![0] with { Rows = [spec.States[0].Rows![0] with { Areas = [area] }] }]
    };

    private static void AssertRejected(ViewScriptGenerationResult result)
    {
        Assert.False(result.CanGenerate);
        Assert.Null(result.Script);
        Assert.NotEmpty(result.Gaps);
    }

    private static ViewScriptGenerationResult Generate(ViewSpec spec, string? auditUser = AuditUser)
    {
        var normalized = ViewSpecNormalizer.Normalize(spec);
        return ViewScriptGenerator.Generate(spec, ViewCreationPreflightService.FromExistingViewId(normalized.Name!, null), auditUser);
    }

    private static string GenerateEmptyViewScript()
    {
        var result = Generate(new ViewSpec("VistaMinima", null, null, []));
        Assert.True(result.CanGenerate);
        Assert.Empty(result.Gaps);
        return Assert.IsType<string>(result.Script);
    }

    private static Dictionary<string, string> GetInsertValues(string script, string table)
    {
        var insert = Assert.Single(Regex.Matches(script,
            $@"INSERT INTO dbo\.{Regex.Escape(table)} \((?<columns>[^)]+)\)\s+VALUES \((?<values>.*?)\);",
            RegexOptions.Singleline));
        var columns = insert.Groups["columns"].Value.Split(',').Select(column => column.Trim()).ToArray();
        // Keep quoted literals together, including JSON commas and doubled single quotes.
        var values = Regex.Matches(insert.Groups["values"].Value, @"N?'(?:''|[^'])*'|[^\s,]+")
            .Select(match => match.Value)
            .ToArray();
        Assert.Equal(columns.Length, values.Length);
        return columns.Select((column, index) => new KeyValuePair<string, string>(column, values[index]))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    private static string NormalizeLineEndings(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
