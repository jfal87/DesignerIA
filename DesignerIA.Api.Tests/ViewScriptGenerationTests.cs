using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesignerIA.Api.Services;
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
    public void GridSpecMatchesContractExampleJson()
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
                    "handlers": [],
                    "parameters": null
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

    [Fact]
    public void NullAndEmptyParametersKeepTheCurrentGridScript()
    {
        var current = ViewScriptGenerator.Generate(GridSpec(), CompleteGridPreflight(), AuditUser);
        var area = GridSpec().States![0].Rows![0].Areas![0];
        Assert.Null(area.Parameters);

        var empty = ViewScriptGenerator.Generate(WithArea(GridSpec(), area with { Parameters = [] }),
            CompleteGridPreflight() with { Parameters = [] }, AuditUser);

        Assert.True(current.CanGenerate);
        Assert.True(empty.CanGenerate);
        Assert.Equal(current.Script, empty.Script);
        Assert.DoesNotContain("IDX_SPParam", current.Script);
    }

    [Fact]
    public void TwoFixedParametersInsertTheirRowsInBothTablesInDeclarationOrder()
    {
        // Capturados en otro orden, con «@» y con otra capitalización: el script usa los nombres y el orden del procedimiento.
        var spec = ExampleSpec(new("@paramdos", "FIJO-DOS-61"), new("ParamUno", "FIJO-UNO-61"));
        var result = ViewScriptGenerator.Generate(spec, ExamplePreflight(spec), AuditUser);
        Assert.True(result.CanGenerate);
        Assert.Empty(result.Gaps);
        var script = result.Script!;

        Assert.Equal(new[] { "Vistas", "Estados", "Filas", "Areas", "ExtraccionesDatos", "StoredProcedure",
            "ParametrosSP", "ParametrosSP", "MapeoParametros", "MapeoParametros",
            "ObjetosDeArea", "ConfiguracionesObjetoDeArea", "LeftMenus", "ConfiguracionesEstados" },
            Regex.Matches(script, @"INSERT INTO dbo\.(\w+)").Select(match => match.Groups[1].Value).ToArray());

        var parameters = GetInsertRows(script, "ParametrosSP");
        Assert.All(parameters, row => Assert.Equal(
            "IdParametroSP,IdStoredProcedure,Nombre,UsaComillas,EsMyVision,User_Create,User_Update,F_Create,F_Update,TipoParametro".Split(','),
            row.Keys.ToArray()));
        Assert.Equal(new[] { "@IDX_SPParam", "@IDX_StoredProcedure", "N'ParamUno'", "1", "0", "N'TestAuditUser'", "NULL", "GETDATE()", "NULL", "'varchar'" },
            parameters[0].Values.ToArray());
        Assert.Equal(new[] { "@IDX_SPParam", "@IDX_StoredProcedure", "N'ParamDos'", "1", "0", "N'TestAuditUser'", "NULL", "GETDATE()", "NULL", "'varchar'" },
            parameters[1].Values.ToArray());

        var mappings = GetInsertRows(script, "MapeoParametros");
        Assert.All(mappings, row => Assert.Equal(
            "IdMapeoParametro,IdEstado,NombreArea,Parametro,Variable,ValorDefault,IdStoredProcedure,VariableRequest,User_Create,User_Update,F_Create,F_Update".Split(','),
            row.Keys.ToArray()));
        Assert.Equal(new[] { "@IDX_SPParamBinding", "@IDX_Estado", "N'Area01'", "N'ParamUno'", "N''", "N'FIJO-UNO-61'", "@IDX_StoredProcedure", "N''", "N'TestAuditUser'", "NULL", "GETDATE()", "NULL" },
            mappings[0].Values.ToArray());
        Assert.Equal(new[] { "@IDX_SPParamBinding", "@IDX_Estado", "N'Area01'", "N'ParamDos'", "N''", "N'FIJO-DOS-61'", "@IDX_StoredProcedure", "N''", "N'TestAuditUser'", "NULL", "GETDATE()", "NULL" },
            mappings[1].Values.ToArray());

        // Mismo patrón de IDs: MAX bloqueado, una vez por tabla, e incremento por fila entre registros.
        Assert.Equal(new[] { "Estados", "Filas", "Areas", "ExtraccionesDatos", "StoredProcedure", "ObjetosDeArea",
            "ConfiguracionesObjetoDeArea", "LeftMenus", "ParametrosSP", "MapeoParametros" },
            Regex.Matches(script, @"SELECT @IDX_\w+ = ISNULL\(MAX\(\w+\), 1\) \+ 1 FROM dbo\.(\w+) WITH \(UPDLOCK, HOLDLOCK\);")
                .Select(match => match.Groups[1].Value).ToArray());
        Assert.Contains("SELECT @IDX_SPParam = ISNULL(MAX(IdParametroSP), 1) + 1 FROM dbo.ParametrosSP WITH (UPDLOCK, HOLDLOCK);", script);
        Assert.Contains("SELECT @IDX_SPParamBinding = ISNULL(MAX(IdMapeoParametro), 1) + 1 FROM dbo.MapeoParametros WITH (UPDLOCK, HOLDLOCK);", script);
        Assert.Single(Regex.Matches(script, @"SET @IDX_SPParam = @IDX_SPParam \+ 1;"));
        Assert.Single(Regex.Matches(script, @"SET @IDX_SPParamBinding = @IDX_SPParamBinding \+ 1;"));
        Assert.True(script.LastIndexOf("dbo.MapeoParametros (", StringComparison.Ordinal) < script.IndexOf("INSERT INTO dbo.ObjetosDeArea", StringComparison.Ordinal));
        Assert.True(script.LastIndexOf("dbo.MapeoParametros (", StringComparison.Ordinal) < script.IndexOf("COMMIT TRANSACTION", StringComparison.Ordinal));
        Assert.Contains("FROM dbo.StringsConnection WHERE StringConnection = N'GestionEngine';", script);
        Assert.DoesNotMatch(@"IdStringConnection[^\r\n]*\b14\b", script);
        foreach (var forbidden in new[] { "DROP TABLE IF EXISTS", "THROW", "TRY_CONVERT", "IIF", "CONCAT", "OFFSET", "FETCH", "SEQUENCE",
            "DATEFROMPARTS", "EOMONTH", "FORMAT", "JSON_VALUE", "OPENJSON", "STRING_SPLIT", "STRING_AGG", "CREATE OR ALTER", "SET FMTONLY", "WebControls", "Handlers" })
        {
            Assert.False(Regex.IsMatch(script, @"\b" + Regex.Escape(forbidden) + @"\b", RegexOptions.IgnoreCase), forbidden);
        }
    }

    [Fact]
    public void IntegerAndBitParametersAreWrittenWithoutQuotesFlagAndTextIsPreservedExactly()
    {
        var declared = new ViewProcedureParameter[]
        {
            new("@Cantidad", false, "int", 4), new("@Activo", false, "bit", 1), new("@Titulo", false, "nvarchar", 40)
        };
        var spec = ExampleSpec(new("Cantidad", "-25"), new("Activo", "1"), new("Titulo", "  A+b & NULL  "));
        Assert.Empty(ViewCreationPreflightService.ResolveParameters(spec.States![0].Rows![0].Areas![0].Parameters!, declared, out var resolved));
        var result = ViewScriptGenerator.Generate(spec, ExamplePreflight(ExampleSpec()) with { Parameters = resolved }, AuditUser);
        Assert.True(result.CanGenerate);

        var parameters = GetInsertRows(result.Script!, "ParametrosSP");
        Assert.Equal(new[] { "N'Cantidad'|0|'int'", "N'Activo'|0|'bit'", "N'Titulo'|1|'nvarchar'" },
            parameters.Select(row => $"{row["Nombre"]}|{row["UsaComillas"]}|{row["TipoParametro"]}").ToArray());
        Assert.All(parameters, row => Assert.Equal("0", row["EsMyVision"]));
        // El texto «NULL» es texto, no un NULL de SQL; los espacios iniciales y finales se conservan.
        Assert.Contains("N'Titulo', N'', N'  A+b & NULL  ', @IDX_StoredProcedure, N'',", result.Script!);
        Assert.Contains("N'Cantidad', N'', N'-25', @IDX_StoredProcedure, N'',", result.Script!);
        Assert.Equal(2, Regex.Matches(result.Script!, @"SET @IDX_SPParam = @IDX_SPParam \+ 1;").Count);
    }

    [Theory]
    [InlineData("1abc")]
    [InlineData("con espacio")]
    [InlineData("Año")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("@@Doble")]
    [InlineData("ParamUno ")]
    [InlineData("a234567890b234567890c234567890d234567890e234567890f234567890g234567890h23456")]
    public void ParameterNameMustBeAnAsciiIdentifierOfUpTo75Characters(string? name)
    {
        var spec = ExampleSpec(new(name, "valor"), new("ParamDos", "valor"));
        var result = ViewScriptGenerator.Generate(spec, ExamplePreflight(ExampleSpec()), AuditUser);

        AssertRejected(result);
        Assert.Contains(result.Gaps, gap => gap.Topic == "Nombre de parámetro no admitido" && gap.Description.Contains("#1"));
    }

    [Fact]
    public void NullParameterElementIsRejectedByPositionWithoutExceptionOrScript()
    {
        // Es lo que produce un JSON con «null» dentro de la lista de parámetros.
        var parameters = JsonSerializer.Deserialize<ViewSpecArea>(
            """{"parameters":[{"name":"ParamUno","defaultValue":"a"},null]}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Parameters!;
        Assert.Null(parameters[1]);
        var spec = GridViewSpec(null, "dbo.PRC_GET_GRID_EXAMPLE_DESIGNERIA", parameters);

        var result = ViewScriptGenerator.Generate(spec, ExamplePreflight(ExampleSpec()), AuditUser);

        AssertRejected(result);
        var gap = Assert.Single(result.Gaps);
        Assert.Equal("Parámetro no informado", gap.Topic);
        Assert.Contains("#2", gap.Description);
        // El contraste con el procedimiento tampoco falla con ese elemento: lo informa como rechazo.
        Assert.NotEmpty(ViewCreationPreflightService.ResolveParameters(parameters, ExampleDeclared(), out var resolved));
        Assert.Null(resolved);
    }

    [Fact]
    public void ParameterNameAcceptsOptionalAtSignAndLimitAndRejectsDuplicatesIgnoringCase()
    {
        var limit = new string('p', 75);
        Assert.True(ViewScriptGenerationValidator.TryParseParameterName("@" + limit, out var parsed));
        Assert.Equal(limit, parsed);
        Assert.True(ViewScriptGenerationValidator.TryParseParameterName("_Param_1", out _));
        Assert.False(ViewScriptGenerationValidator.TryParseParameterName(limit + "p", out _));

        var duplicated = ExampleSpec(new("ParamUno", "a"), new("@PARAMUNO", "b"));
        var result = ViewScriptGenerator.Generate(duplicated, ExamplePreflight(ExampleSpec()), AuditUser);

        AssertRejected(result);
        Assert.Contains(result.Gaps, gap => gap.Topic == "Parámetro duplicado" && gap.Description.Contains("PARAMUNO"));
    }

    [Theory]
    [InlineData(null, "Valor de parámetro no informado")]
    [InlineData("", "Valor de parámetro no informado")]
    [InlineData("O'Brien", "Valor de parámetro no admitido")]
    [InlineData("dice \"hola\"", "Valor de parámetro no admitido")]
    [InlineData(@"ruta\archivo", "Valor de parámetro no admitido")]
    [InlineData("a<b", "Valor de parámetro no admitido")]
    [InlineData("a>b", "Valor de parámetro no admitido")]
    [InlineData("línea", "Valor de parámetro no admitido")]
    [InlineData("dos\nlíneas", "Valor de parámetro no admitido")]
    [InlineData("dos\r\nlineas", "Valor de parámetro no admitido")]
    [InlineData("tab\tulado", "Valor de parámetro no admitido")]
    [InlineData("control\u007f", "Valor de parámetro no admitido")]
    [InlineData("uno-mas-dos", "Valor de parámetro no admitido")]
    [InlineData("uno-andpersand-dos", "Valor de parámetro no admitido")]
    public void ParameterValueIsRejectedWithoutBeingAltered(string? value, string topic)
    {
        var spec = ExampleSpec(new("ParamUno", value), new("ParamDos", "valor"));
        var result = ViewScriptGenerator.Generate(spec, ExamplePreflight(ExampleSpec()), AuditUser);

        AssertRejected(result);
        var gap = Assert.Single(result.Gaps);
        Assert.Equal(topic, gap.Topic);
        Assert.Contains("ParamUno", gap.Description);
    }

    [Theory]
    [InlineData("FIJO-UNO-61")]
    [InlineData("a+b&c")]
    [InlineData("NULL")]
    [InlineData("  con espacios  ")]
    [InlineData(" ")]
    [InlineData("-MAS-")]
    [InlineData("mas-")]
    [InlineData("!#$%()*,./:;=?@[]^_`{|}~")]
    public void PrintableAsciiValuesAreAcceptedAsWritten(string value)
    {
        Assert.Null(ViewScriptGenerationValidator.DescribeParameterValueProblem(value));
        var spec = ExampleSpec(new("ParamUno", value), new("ParamDos", "valor"));
        var result = ViewScriptGenerator.Generate(spec, ExamplePreflight(spec), AuditUser);

        Assert.True(result.CanGenerate);
        Assert.Contains($"N'ParamUno', N'', N'{value}', @IDX_StoredProcedure", result.Script!);
    }

    [Fact]
    public void ParametersMustCoverExactlyTheInputParametersOfTheProcedure()
    {
        var declared = ExampleDeclared();
        Assert.Empty(ViewCreationPreflightService.ResolveParameters([new("paramdos", "b"), new("@PARAMUNO", "a")], declared, out var resolved));
        Assert.Equal(new ViewCreationPreflightParameter[] { new("ParamUno", "varchar", "a"), new("ParamDos", "varchar", "b") }, resolved);

        var missing = Assert.Single(ViewCreationPreflightService.ResolveParameters([new("ParamUno", "a")], declared, out var none));
        Assert.Null(none);
        Assert.Equal("Parámetros no coinciden con el procedimiento", missing.Topic);
        Assert.Contains("Falta el valor del parámetro «ParamDos»", missing.Description);
        Assert.Contains("@ParamUno (varchar), @ParamDos (varchar)", missing.Description);

        var extra = Assert.Single(ViewCreationPreflightService.ResolveParameters(
            [new("ParamUno", "a"), new("ParamDos", "b"), new("Otro", "c")], declared, out _));
        Assert.Equal("Parámetros no coinciden con el procedimiento", extra.Topic);
        Assert.Contains("no tiene el parámetro «Otro»", extra.Description);

        var withoutParameters = Assert.Single(ViewCreationPreflightService.ResolveParameters([new("ParamUno", "a")], [], out _));
        Assert.Contains("no declara parámetros", withoutParameters.Description);

        // Nombres del catálogo que Motor no distinguiría (sólo difieren en mayúsculas) o que no son identificadores ASCII.
        foreach (var second in new[] { "@PARAMUNO", "@Con Espacio" })
        {
            Assert.Contains(ViewCreationPreflightService.ResolveParameters([new("ParamUno", "a")],
                [new("@ParamUno", false, "varchar", 500), new(second, false, "varchar", 500)], out _),
                gap => gap.Topic == "Nombre de parámetro no admitido");
        }
    }

    [Theory]
    [InlineData("@Salida", true, "int", false, "Parámetro de salida no admitido")]
    [InlineData("@Tabla", false, "table type", true, "Parámetro de tipo tabla no admitido")]
    [InlineData("@Fecha", false, "datetime", false, "Tipo de parámetro no admitido")]
    [InlineData("@Importe", false, "decimal", false, "Tipo de parámetro no admitido")]
    [InlineData("@Texto", false, "text", false, "Tipo de parámetro no admitido")]
    [InlineData("@SinTipo", false, null, false, "Tipo de parámetro no admitido")]
    [InlineData("@logon_user", false, "varchar", false, "Parámetro resuelto por Motor")]
    [InlineData("@LOGON_USER", false, "varchar", false, "Parámetro resuelto por Motor")]
    [InlineData("@GruposVistaUsuario", false, "varchar", false, "Parámetro resuelto por Motor")]
    public void UnsupportedProcedureParametersAreRejectedByName(string name, bool isOutput, string? type, bool isTable, string topic)
    {
        var declared = new ViewProcedureParameter[] { new("@ParamUno", false, "varchar", 500), new(name, isOutput, type, 50, isTable) };
        var requested = new ViewSpecParameter[] { new("ParamUno", "a"), new(name, "1") };

        var gap = Assert.Single(ViewCreationPreflightService.ResolveParameters(requested, declared, out var resolved));

        Assert.Null(resolved);
        Assert.Equal(topic, gap.Topic);
        Assert.Contains(name[1..], gap.Description);
    }

    [Theory]
    [InlineData("tinyint", "0", true)]
    [InlineData("tinyint", "255", true)]
    [InlineData("tinyint", "256", false)]
    [InlineData("tinyint", "-1", false)]
    [InlineData("smallint", "-32768", true)]
    [InlineData("smallint", "32768", false)]
    [InlineData("int", "2147483647", true)]
    [InlineData("int", "-2147483648", true)]
    [InlineData("int", "2147483648", false)]
    [InlineData("bigint", "9223372036854775807", true)]
    [InlineData("bigint", "-9223372036854775808", true)]
    [InlineData("bigint", "9223372036854775808", false)]
    [InlineData("int", "1.0", false)]
    [InlineData("int", "1,000", false)]
    [InlineData("int", "1e3", false)]
    [InlineData("int", "+1", false)]
    [InlineData("int", "01", false)]
    [InlineData("int", "-0", false)]
    [InlineData("int", " 1", false)]
    [InlineData("int", "1 ", false)]
    [InlineData("int", "0x10", false)]
    [InlineData("int", "1;DROP", false)]
    [InlineData("int", "1 OR 1=1", false)]
    [InlineData("int", "NULL", false)]
    [InlineData("int", "", false)]
    [InlineData("bit", "0", true)]
    [InlineData("bit", "1", true)]
    [InlineData("bit", "2", false)]
    [InlineData("bit", "true", false)]
    [InlineData("bit", "NULL", false)]
    public void IntegerAndBitValuesUseStrictDecimalRepresentationWithinTheTypeRange(string type, string value, bool accepted)
    {
        var gaps = ViewCreationPreflightService.ResolveParameters([new("Valor", value)], [new("@Valor", false, type, 8)], out var resolved);

        Assert.Equal(accepted, gaps.Count == 0);
        if (accepted)
        {
            Assert.Equal(value, Assert.Single(resolved!).DefaultValue);
        }
        else
        {
            var gap = Assert.Single(gaps);
            Assert.Equal("Valor de parámetro no admitido", gap.Topic);
            Assert.Contains("«Valor»", gap.Description);
        }
    }

    [Theory]
    [InlineData("varchar", 5, 5, true)]
    [InlineData("varchar", 5, 6, false)]
    [InlineData("char", 3, 4, false)]
    [InlineData("nvarchar", 10, 5, true)]
    [InlineData("nvarchar", 10, 6, false)]
    [InlineData("nchar", 4, 3, false)]
    [InlineData("varchar", -1, 500, true)]
    [InlineData("varchar", -1, 501, false)]
    [InlineData("nvarchar", -1, 500, true)]
    [InlineData("nvarchar", -1, 501, false)]
    public void TextLengthFollowsTheParameterLengthAndTheDesignerIaCeilingForMaxTypes(string type, int maxLength, int length, bool accepted)
    {
        var value = new string('x', length);

        var problem = ViewCreationPreflightService.DescribeTypedValueProblem(type, maxLength, value);

        Assert.Equal(accepted, problem is null);
        if (!accepted)
        {
            Assert.Contains(maxLength < 0 ? "límite propio de DesignerIA" : "admite hasta", problem);
        }
        Assert.Equal(500, ViewCreationPreflightService.MaxTypeValueLength);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unexpected")]
    [InlineData("other-value")]
    [InlineData("other-name")]
    public void GeneratorRequiresPreflightParametersThatMatchTheViewSpec(string problem)
    {
        var spec = ExampleSpec(new("ParamUno", "FIJO-UNO-61"), new("ParamDos", "FIJO-DOS-61"));
        var preflight = ExamplePreflight(spec);
        (spec, preflight) = problem switch
        {
            "missing" => (spec, preflight with { Parameters = null }),
            "unexpected" => (ExampleSpec(), preflight),
            "other-value" => (spec, preflight with { Parameters = [preflight.Parameters![0] with { DefaultValue = "fijo-uno-61" }, preflight.Parameters[1]] }),
            _ => (spec, preflight with { Parameters = [preflight.Parameters![0] with { Name = "Otro" }, preflight.Parameters[1]] })
        };

        var result = ViewScriptGenerator.Generate(spec, preflight, AuditUser);

        AssertRejected(result);
        Assert.Equal("Preflight incompleto", Assert.Single(result.Gaps).Topic);
    }

    private static ViewSpec ExampleSpec(params ViewSpecParameter[] parameters) =>
        GridViewSpec("Consulta de prueba con grilla", "dbo.PRC_GET_GRID_EXAMPLE_DESIGNERIA", parameters);

    private static ViewProcedureParameter[] ExampleDeclared() =>
        [new("@ParamUno", false, "varchar", 500), new("@ParamDos", false, "varchar", 500)];

    private static ViewCreationPreflightResult ExamplePreflight(ViewSpec spec)
    {
        IReadOnlyList<ViewCreationPreflightParameter>? resolved = null;
        if (spec.States![0].Rows![0].Areas![0].Parameters is { } requested)
        {
            Assert.Empty(ViewCreationPreflightService.ResolveParameters(requested, ExampleDeclared(), out resolved));
        }
        return new(true, "vConsultaPrueba01", null, [], "[dbo].[PRC_GET_GRID_EXAMPLE_DESIGNERIA]", [1, 2, 3, 4, 5, 6], resolved);
    }

    private static List<Dictionary<string, string>> GetInsertRows(string script, string table) =>
        Regex.Matches(script, $@"INSERT INTO dbo\.{Regex.Escape(table)} \((?<columns>[^)]+)\)\s+VALUES \((?<values>.*?)\);", RegexOptions.Singleline)
            .Select(insert =>
            {
                var columns = insert.Groups["columns"].Value.Split(',').Select(column => column.Trim()).ToArray();
                var values = Regex.Matches(insert.Groups["values"].Value, @"N?'(?:''|[^'])*'|[^\s,]+").Select(match => match.Value).ToArray();
                Assert.Equal(columns.Length, values.Length);
                return columns.Select((column, index) => (column, value: values[index])).ToDictionary(pair => pair.column, pair => pair.value);
            }).ToList();

    private static ViewSpec GridSpec() => GridViewSpec("Consulta de prueba con grilla", "dbo.PRC_GET_GRID_TEST_SORT_COLUMN");

    // La forma con grilla que admite el generador: vista go con un estado, una fila y una grilla.
    private static ViewSpec GridViewSpec(string? description, string storedProcedure,
        IReadOnlyList<ViewSpecParameter>? parameters = null) => new(
        "vConsultaPrueba01", description, "Estado01",
        [new ViewSpecState("Estado01", [new ViewSpecRow(
            [new ViewSpecArea("Grilla", storedProcedure, "GestionEngine", [],
                "Area01", "Grilla01", GridConfigurationPresets.GridQuery,
                parameters is { Count: > 0 } ? parameters : null)], "Fila01")])],
        "go");

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
