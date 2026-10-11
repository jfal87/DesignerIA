using System.Text;
using DesignerIA.Api.Services.ViewSpecs;
using DesignerIA.Contracts;

namespace DesignerIA.Api.Services.ViewScripts;

/// <summary>
/// Genera texto SQL determinístico para una vista vacía o con una grilla mediante
/// una única entrada que normaliza y valida el ViewSpec y exige su preflight.
/// El script se devuelve como artefacto; este servicio nunca lo ejecuta. Copilot
/// no genera SQL y todos los valores textuales pasan por <see cref="SqlLiteralEscaper"/>.
/// </summary>
public static class ViewScriptGenerator
{
    public static ViewScriptGenerationResult Generate(
        ViewSpec spec, ViewCreationPreflightResult? preflight, string? auditUser)
    {
        var normalizedSpec = ViewSpecNormalizer.Normalize(spec);
        var validation = ViewSpecValidator.Validate(normalizedSpec);
        var summary = BuildSummary(normalizedSpec);
        var gaps = ViewScriptGenerationValidator.Validate(normalizedSpec, validation).ToList();
        if (string.IsNullOrWhiteSpace(auditUser))
        {
            gaps.Add(new("AuditUser no disponible", "No se puede generar el script hasta recibir la identidad autenticada de la request (HttpContext.User.Identity.Name)."));
        }
        if (gaps.Count == 0)
        {
            ValidatePreflight(normalizedSpec, preflight, gaps);
        }
        return gaps.Count > 0
            ? new(false, null, gaps, summary, preflight)
            : new(true, BuildScript(ViewCreationPlanFactory.Create(normalizedSpec, preflight!), auditUser!), [], summary, preflight);
    }

    private static void ValidatePreflight(ViewSpec spec, ViewCreationPreflightResult? preflight,
        List<ViewScriptGenerationGap> gaps)
    {
        if (preflight is null || preflight.ViewName != spec.Name)
        {
            gaps.Add(new("Preflight incompleto", "Se requiere el preflight de esta vista antes de generar el script."));
            return;
        }
        if (preflight.Gaps is { Count: > 0 })
        {
            gaps.AddRange(preflight.Gaps);
            return;
        }
        if (!preflight.IsAvailable || preflight.ExistingViewId is not null)
        {
            gaps.Add(new("Nombre de vista existente", "Ya existe una vista con el nombre indicado; no se generó un script que pueda sobrescribirla."));
            return;
        }
        if (spec.ViewType != "go")
        {
            return;
        }
        var area = spec.States![0].Rows![0].Areas![0];
        var canonical = preflight.CanonicalStoredProcedure;
        var validCanonical = canonical is not null && canonical.Length <= 250 &&
            ViewScriptGenerationValidator.TryParseStoredProcedure(canonical, out var schema, out var name) &&
            canonical == $"[{schema}].[{name}]" &&
            ViewScriptGenerationValidator.TryParseStoredProcedure(area.StoredProcedure, out var requestedSchema, out var requestedName) &&
            string.Equals(schema, requestedSchema, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(name, requestedName, StringComparison.OrdinalIgnoreCase);
        if (preflight.Gaps is null || !Enumerable.Range(1, 6).All(check => preflight.CompletedChecks?.Contains(check) == true) || !validCanonical)
        {
            gaps.Add(new("Preflight incompleto", "La grilla requiere las seis comprobaciones superadas y el SP canónico resuelto."));
        }
    }

    private static string BuildScript(ViewCreationPlan plan, string auditUser)
    {
        var viewName = SqlLiteralEscaper.Literal(plan.View.Name);
        var description = SqlLiteralEscaper.Literal(plan.View.Description);
        var stateName = SqlLiteralEscaper.Literal(plan.State.Name);
        var user = SqlLiteralEscaper.Literal(auditUser);
        var configuration = SqlLiteralEscaper.Literal(plan.StateConfiguration.Configuration);
        var rowName = SqlLiteralEscaper.Literal(plan.Row.Name);
        var viewType = SqlLiteralEscaper.Literal(plan.View.ViewType);
        var script = new StringBuilder();
        script.AppendLine("IF @@TRANCOUNT <> 0");
        script.AppendLine("BEGIN");
        script.AppendLine("    RAISERROR(N'El script requiere una sesión sin transacción abierta.', 16, 1);");
        script.AppendLine("    RETURN;");
        script.AppendLine("END");
        script.AppendLine();
        script.AppendLine("SET NOCOUNT ON;");
        script.AppendLine("SET XACT_ABORT ON;");
        script.AppendLine("SET LOCK_TIMEOUT 5000;");
        script.AppendLine();
        script.AppendLine("BEGIN TRY");
        script.AppendLine("    BEGIN TRANSACTION;");
        script.AppendLine();
        script.AppendLine("    IF EXISTS (SELECT 1 FROM dbo.Vistas WITH (UPDLOCK, HOLDLOCK)");
        script.AppendLine($"               WHERE IdVista = {viewName} OR Nombre = {viewName})");
        script.AppendLine("        RAISERROR(N'La vista ya existe; el script no realiza cambios.', 16, 1);");
        script.AppendLine();
        if (plan.Grid is { } grid)
        {
            var alias = SqlLiteralEscaper.Literal(grid.ConnectionAlias);
            script.AppendLine("    DECLARE @IdStringConnection int, @AliasCount int;");
            script.AppendLine("    SELECT @AliasCount = COUNT(*), @IdStringConnection = MIN(IdStringConnection)");
            script.AppendLine($"    FROM dbo.StringsConnection WHERE StringConnection = {alias};");
            script.AppendLine("    IF @AliasCount <> 1");
            script.AppendLine("        RAISERROR(N'El alias de conexión no existe o es ambiguo en esta base.', 16, 2);");
            script.AppendLine();
            script.AppendLine("    DECLARE @IDX_Estado int, @IDX_Fila int, @IDX_Area int, @IDX_ExtraccionDato int,");
            script.AppendLine("            @IDX_StoredProcedure int, @IDX_ObjetoArea int, @IDX_ObjetoAreaConfig int, @IDX_LeftMenu int;");
        }
        else
        {
            script.AppendLine("    DECLARE @IDX_Estado int;");
            script.AppendLine("    SELECT @IDX_Estado = ISNULL(MAX(IdEstado), 1) FROM dbo.Estados WITH (UPDLOCK, HOLDLOCK);");
            script.AppendLine("    DECLARE @IDX_Fila int;");
            script.AppendLine("    SELECT @IDX_Fila = ISNULL(MAX(IdFila), 1) FROM dbo.Filas WITH (UPDLOCK, HOLDLOCK);");
        }
        if (plan.Grid is not null)
        {
            AppendId(script, "Estado", "IdEstado", "Estados");
            AppendId(script, "Fila", "IdFila", "Filas");
            AppendId(script, "Area", "IdArea", "Areas");
            AppendId(script, "ExtraccionDato", "IdExtraccionDato", "ExtraccionesDatos");
            AppendId(script, "StoredProcedure", "IdStoredProcedure", "StoredProcedure");
            AppendId(script, "ObjetoArea", "IdObjetoDeArea", "ObjetosDeArea");
            AppendId(script, "ObjetoAreaConfig", "Id", "ConfiguracionesObjetoDeArea");
            AppendId(script, "LeftMenu", "IdLeftMenu", "LeftMenus");
        }
        script.AppendLine();
        script.AppendLine("    INSERT INTO dbo.Vistas (IdOwner, IdHistoricoCambio, Nombre, Descripcion, FechaCreacion, VersionActual, ArchivoJs, IdVista, TipoVista, UsaSeguridad, User_Create, User_Update, F_Create, F_Update, IdStringsConnectionMyVision, JsCode, Frente, VistaVersionado)");
        script.AppendLine($"    VALUES (1, 1, {viewName}, {description}, GETDATE(), N'1.0', N'', {viewName}, {viewType}, 0, {user}, NULL, GETDATE(), NULL, NULL, N'', N'', N'1.0');");
        script.AppendLine();
        if (plan.Grid is null)
        {
            script.AppendLine("    SET @IDX_Estado = @IDX_Estado + 1;");
        }
        script.AppendLine("    INSERT INTO dbo.Estados (IdEstado, [Default], Descripcion, IdVista, ArchivoJs, LeftMenuFijo, User_Create, User_Update, F_Create, F_Update)");
        script.AppendLine($"    VALUES (@IDX_Estado, 1, {stateName}, {viewName}, N'', 0, {user}, NULL, GETDATE(), NULL);");
        script.AppendLine();
        if (plan.Grid is null)
        {
            script.AppendLine("    SET @IDX_Fila = @IDX_Fila + 1;");
        }
        script.AppendLine("    INSERT INTO dbo.Filas (IdFila, IdEstado, Nombre, Ancho, Altura, User_Create, User_Update, F_Create, F_Update)");
        script.AppendLine($"    VALUES (@IDX_Fila, @IDX_Estado, {rowName}, {plan.Row.Width}, {plan.Row.Height}, {user}, NULL, GETDATE(), NULL);");
        script.AppendLine();
        if (plan.Grid is { } gridPlan)
        {
            AppendGridInserts(script, gridPlan, user);
        }
        script.AppendLine("    INSERT INTO dbo.ConfiguracionesEstados (IdEstado, Configuracion, User_Create, F_Update)");
        script.AppendLine($"    VALUES (@IDX_Estado, {configuration}, {user}, GETDATE());");
        script.AppendLine();
        script.AppendLine("    IF (SELECT COUNT(*) FROM dbo.Filas WHERE IdFila = @IDX_Fila) <> 1");
        script.AppendLine("        RAISERROR(N'IdFila duplicado; no se realizan cambios.', 16, 3);");
        script.AppendLine($"    IF (SELECT COUNT(*) FROM dbo.Vistas WHERE IdVista = {viewName} OR Nombre = {viewName}) <> 1");
        script.AppendLine("        RAISERROR(N'Nombre o IdVista duplicado; no se realizan cambios.', 16, 4);");
        script.AppendLine();
        script.AppendLine("    COMMIT TRANSACTION;");
        script.AppendLine("END TRY");
        script.AppendLine("BEGIN CATCH");
        script.AppendLine("    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;");
        script.AppendLine("    DECLARE @ErrorNumber int, @ErrorState int, @ErrorMessage nvarchar(2048);");
        script.AppendLine("    SELECT @ErrorNumber = ERROR_NUMBER(), @ErrorState = ERROR_STATE(), @ErrorMessage = ERROR_MESSAGE();");
        script.AppendLine("    RAISERROR(N'[%d/%d] %s', 16, 1, @ErrorNumber, @ErrorState, @ErrorMessage);");
        script.AppendLine("END CATCH");
        script.AppendLine();
        script.AppendLine("SET LOCK_TIMEOUT -1;");
        script.AppendLine("SET XACT_ABORT OFF;");
        return script.ToString();
    }

    private static void AppendId(StringBuilder script, string variable, string column, string table) =>
        script.AppendLine($"    SELECT @IDX_{variable} = ISNULL(MAX({column}), 1) + 1 FROM dbo.{table} WITH (UPDLOCK, HOLDLOCK);");

    private static void AppendGridInserts(StringBuilder script, ViewCreationPlanGrid grid, string user)
    {
        var area = SqlLiteralEscaper.Literal(grid.AreaName);
        var procedure = SqlLiteralEscaper.Literal(grid.CanonicalStoredProcedure);
        var ansiProcedure = SqlLiteralEscaper.Literal(grid.CanonicalStoredProcedure, unicode: false);
        var objectName = SqlLiteralEscaper.Literal(grid.ObjectName);
        var configuration = SqlLiteralEscaper.Literal(grid.Configuration);
        var leftMenu = SqlLiteralEscaper.Literal(grid.LeftMenu);
        script.AppendLine("    INSERT INTO dbo.Areas (IdArea, IdFila, Nombre, Ancho, Altura, User_Create, User_Update, F_Create, F_Update)");
        script.AppendLine($"    VALUES (@IDX_Area, @IDX_Fila, {area}, 0, 0, {user}, NULL, GETDATE(), NULL);");
        script.AppendLine();
        script.AppendLine("    INSERT INTO dbo.ExtraccionesDatos (IdExtraccionDato, Descripcion, User_Create, User_Update, F_Create, F_Update)");
        script.AppendLine($"    VALUES (@IDX_ExtraccionDato, {procedure}, {user}, NULL, GETDATE(), NULL);");
        script.AppendLine();
        script.AppendLine("    INSERT INTO dbo.StoredProcedure (IdStoredProcedure, Nombre, Descripcion, OrdenEjecucion, IdStringConnection, IdExtraccionDato, IdTipoEjecucionSP, User_Create, User_Update, F_Create, F_Update)");
        script.AppendLine($"    VALUES (@IDX_StoredProcedure, {ansiProcedure}, N'', 0, @IdStringConnection, @IDX_ExtraccionDato, 1, {user}, NULL, GETDATE(), NULL);");
        script.AppendLine();
        script.AppendLine("    INSERT INTO dbo.ObjetosDeArea (IdObjetoDeArea, IdArea, IdExtraccionDato, IdTipoObjetoDeArea, Nombre, UsaPaginador, User_Create, User_Update, F_Create, F_Update)");
        script.AppendLine($"    VALUES (@IDX_ObjetoArea, @IDX_Area, @IDX_ExtraccionDato, 2, {objectName}, 0, {user}, NULL, GETDATE(), NULL);");
        script.AppendLine();
        script.AppendLine("    INSERT INTO dbo.ConfiguracionesObjetoDeArea (Id, IdObjetoDeArea, Configuracion, User_Create, User_Update, F_Create, F_Update)");
        script.AppendLine($"    VALUES (@IDX_ObjetoAreaConfig, @IDX_ObjetoArea, {configuration}, {user}, NULL, GETDATE(), NULL);");
        script.AppendLine();
        script.AppendLine("    INSERT INTO dbo.LeftMenus (IdLeftMenu, IdEstado, Nombre, User_Create, User_Update, F_Create, F_Update)");
        script.AppendLine($"    VALUES (@IDX_LeftMenu, @IDX_Estado, {leftMenu}, {user}, NULL, GETDATE(), NULL);");
        script.AppendLine();
    }

    private static ViewScriptGenerationSummary BuildSummary(ViewSpec spec)
    {
        var states = spec.States ?? [];
        var rows = states.SelectMany(state => state.Rows ?? []).ToList();
        var areas = rows.SelectMany(row => row.Areas ?? []).ToList();
        var handlers = areas.SelectMany(area => area.Handlers ?? []).ToList();
        return new(1, states.Count, rows.Count, areas.Count, handlers.Count);
    }
}
