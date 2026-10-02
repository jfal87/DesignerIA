using System.Text;
using DesignerIA.Api.Services.ViewSpecs;
using DesignerIA.Contracts;

namespace DesignerIA.Api.Services.ViewScripts;

/// <summary>
/// Genera texto SQL determinístico para el plan físico mínimo de V1. El script
/// se devuelve como artefacto; este servicio nunca lo ejecuta. Copilot no genera
/// SQL y todos los valores textuales pasan por <see cref="SqlLiteralEscaper"/>.
/// </summary>
public static class ViewScriptGenerator
{
    public static ViewScriptGenerationResult Generate(
        ViewSpec spec,
        ViewSpecValidationResult viewSpecValidation,
        string? auditUser)
    {
        _ = viewSpecValidation;
        var normalizedSpec = ViewSpecNormalizer.Normalize(spec);
        var validation = ViewSpecValidator.Validate(normalizedSpec);
        var summary = BuildSummary(normalizedSpec);
        var gaps = ViewScriptGenerationValidator.Validate(normalizedSpec, validation).ToList();

        if (string.IsNullOrWhiteSpace(auditUser))
        {
            gaps.Add(new ViewScriptGenerationGap(
                "AuditUser no disponible",
                "No se puede generar el script hasta recibir la identidad autenticada de la request (HttpContext.User.Identity.Name)."));
        }

        return gaps.Count > 0
            ? new ViewScriptGenerationResult(false, null, gaps, summary)
            : Generate(ViewCreationPlanFactory.Create(normalizedSpec), summary, auditUser!);
    }

    public static ViewScriptGenerationResult Generate(
        ViewCreationPlan plan,
        ViewScriptGenerationSummary summary,
        string? auditUser)
    {
        if (string.IsNullOrWhiteSpace(auditUser))
        {
            return new ViewScriptGenerationResult(
                false,
                null,
                [new ViewScriptGenerationGap(
                    "AuditUser no disponible",
                    "No se puede generar el script hasta recibir la identidad autenticada de la request (HttpContext.User.Identity.Name).")],
                summary);
        }

        return new ViewScriptGenerationResult(true, BuildScript(plan, auditUser), [], summary);
    }

    private static string BuildScript(ViewCreationPlan plan, string auditUser)
    {
        var viewName = SqlLiteralEscaper.EscapeLiteral(plan.View.Name);
        var description = SqlLiteralEscaper.EscapeLiteral(plan.View.Description);
        var stateName = SqlLiteralEscaper.EscapeLiteral(plan.State.Name);
        var escapedAuditUser = SqlLiteralEscaper.EscapeLiteral(auditUser);
        var configuration = SqlLiteralEscaper.EscapeLiteral(plan.StateConfiguration.Configuration);
        var rowName = SqlLiteralEscaper.EscapeLiteral(plan.Row.Name);
        var script = new StringBuilder();

        script.AppendLine("SET NOCOUNT ON;");
        script.AppendLine();
        script.AppendLine("IF EXISTS (SELECT 1 FROM dbo.Vistas WHERE IdVista = '" + viewName + "' OR Nombre = '" + viewName + "')");
        script.AppendLine("BEGIN");
        script.AppendLine("    RAISERROR('La vista ya existe; el script no realiza cambios.', 16, 1);");
        script.AppendLine("    RETURN;");
        script.AppendLine("END");
        script.AppendLine();
        script.AppendLine("BEGIN TRY");
        script.AppendLine("    BEGIN TRANSACTION;");
        script.AppendLine("    DECLARE @IDX_Estado int;");
        script.AppendLine("    SELECT @IDX_Estado = ISNULL(MAX(IdEstado), 1) FROM dbo.Estados;");
        script.AppendLine("    DECLARE @IDX_Fila int;");
        script.AppendLine("    SELECT @IDX_Fila = ISNULL(MAX(IdFila), 1) FROM dbo.Filas;");
        script.AppendLine();
        script.AppendLine("    INSERT INTO dbo.Vistas (IdOwner, IdHistoricoCambio, Nombre, Descripcion, FechaCreacion, VersionActual, ArchivoJs, IdVista, TipoVista, UsaSeguridad, User_Create, User_Update, F_Create, F_Update, IdStringsConnectionMyVision, JsCode, Frente, VistaVersionado)");
        script.AppendLine("    VALUES (1, 1, '" + viewName + "', '" + description + "', GETDATE(), '1.0', '', '" + viewName + "', 'site', 0, '" + escapedAuditUser + "', NULL, GETDATE(), NULL, NULL, '', '', '1.0');");
        script.AppendLine();
        script.AppendLine("    SET @IDX_Estado = @IDX_Estado + 1;");
        script.AppendLine("    INSERT INTO dbo.Estados (IdEstado, [Default], Descripcion, IdVista, ArchivoJs, LeftMenuFijo, User_Create, User_Update, F_Create, F_Update)");
        script.AppendLine("    VALUES (@IDX_Estado, 1, '" + stateName + "', '" + viewName + "', '', 0, '" + escapedAuditUser + "', NULL, GETDATE(), NULL);");
        script.AppendLine();
        script.AppendLine("    SET @IDX_Fila = @IDX_Fila + 1;");
        script.AppendLine("    INSERT INTO dbo.Filas (IdFila, IdEstado, Nombre, Ancho, Altura, User_Create, User_Update, F_Create, F_Update)");
        script.AppendLine("    VALUES (@IDX_Fila, @IDX_Estado, '" + rowName + "', " + plan.Row.Width + ", " + plan.Row.Height + ", '" + escapedAuditUser + "', NULL, GETDATE(), NULL);");
        script.AppendLine();
        script.AppendLine("    INSERT INTO dbo.ConfiguracionesEstados (IdEstado, Configuracion, User_Create, F_Update)");
        script.AppendLine("    VALUES (@IDX_Estado, '" + configuration + "', '" + escapedAuditUser + "', GETDATE());");
        script.AppendLine("    COMMIT TRANSACTION;");
        script.AppendLine("END TRY");
        script.AppendLine("BEGIN CATCH");
        script.AppendLine("    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;");
        script.AppendLine("    DECLARE @ErrorMessage nvarchar(4000);");
        script.AppendLine("    SET @ErrorMessage = ERROR_MESSAGE();");
        script.AppendLine("    RAISERROR('%s', 16, 1, @ErrorMessage);");
        script.AppendLine("END CATCH");
        return script.ToString();
    }

    private static ViewScriptGenerationSummary BuildSummary(ViewSpec spec)
    {
        var states = spec.States ?? [];
        var rows = states.SelectMany(state => state.Rows ?? []).ToList();
        var areas = rows.SelectMany(row => row.Areas ?? []).ToList();
        var handlers = areas.SelectMany(area => area.Handlers ?? []).ToList();
        return new ViewScriptGenerationSummary(1, states.Count, rows.Count, areas.Count, handlers.Count);
    }
}
