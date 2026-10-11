using System.Globalization;

namespace DesignerIA.Api.Services.ViewScripts;

/// <summary>
/// Compara lo leído de la base después de ejecutar el script con lo que el plan
/// físico debía crear. Es puro: la lectura la hace <see cref="ViewCreationService"/>
/// y aquí sólo se decide si los conteos, las relaciones y los valores coinciden.
/// </summary>
public static class ViewCreationVerifier
{
    /// <summary>
    /// Conteos independientes, tabla por tabla, de los registros de la vista. No
    /// dependen de un JOIN encadenado, para que un duplicado no quede oculto.
    /// </summary>
    public const string CountsSql = """
        WITH E AS (SELECT IdEstado FROM dbo.Estados WHERE IdVista = @viewName),
             F AS (SELECT IdFila FROM dbo.Filas WHERE IdEstado IN (SELECT IdEstado FROM E)),
             A AS (SELECT IdArea FROM dbo.Areas WHERE IdFila IN (SELECT IdFila FROM F)),
             O AS (SELECT IdObjetoDeArea, IdExtraccionDato FROM dbo.ObjetosDeArea WHERE IdArea IN (SELECT IdArea FROM A))
        SELECT (SELECT COUNT(*) FROM dbo.Vistas WHERE IdVista = @viewName) AS [Vistas],
               (SELECT COUNT(*) FROM dbo.Vistas WHERE IdVista = @viewName OR Nombre = @viewName) AS [Vistas por IdVista o Nombre],
               (SELECT COUNT(*) FROM E) AS [Estados],
               (SELECT COUNT(*) FROM F) AS [Filas],
               (SELECT COUNT(*) FROM dbo.Filas WHERE IdFila IN (SELECT IdFila FROM F)) AS [Filas con el mismo IdFila],
               (SELECT COUNT(*) FROM A) AS [Areas],
               (SELECT COUNT(*) FROM dbo.ExtraccionesDatos WHERE IdExtraccionDato IN (SELECT IdExtraccionDato FROM O)) AS [ExtraccionesDatos],
               (SELECT COUNT(*) FROM dbo.StoredProcedure WHERE IdExtraccionDato IN (SELECT IdExtraccionDato FROM O)) AS [StoredProcedure],
               (SELECT COUNT(*) FROM O) AS [ObjetosDeArea],
               (SELECT COUNT(*) FROM dbo.ConfiguracionesObjetoDeArea WHERE IdObjetoDeArea IN (SELECT IdObjetoDeArea FROM O)) AS [ConfiguracionesObjetoDeArea],
               (SELECT COUNT(*) FROM dbo.LeftMenus WHERE IdEstado IN (SELECT IdEstado FROM E)) AS [LeftMenus],
               (SELECT COUNT(*) FROM dbo.ConfiguracionesEstados WHERE IdEstado IN (SELECT IdEstado FROM E)) AS [ConfiguracionesEstados];
        """;

    /// <summary>
    /// Valores de los registros de la vista siguiendo sus relaciones. Las fechas se
    /// leen como «informada» (1) o no (0); el resto, tal como está guardado.
    /// El alias de conexión no se compara como texto: la base resuelve @alias con su
    /// propia igualdad, igual que el script, y se comprueba que resuelve una sola
    /// conexión y que su IdStringConnection es el guardado.
    /// </summary>
    public const string ValuesSql = """
        SELECT v.IdVista AS [Vistas.IdVista], v.Nombre AS [Vistas.Nombre], v.Descripcion AS [Vistas.Descripcion],
               v.IdOwner AS [Vistas.IdOwner], v.IdHistoricoCambio AS [Vistas.IdHistoricoCambio],
               v.VersionActual AS [Vistas.VersionActual], v.VistaVersionado AS [Vistas.VistaVersionado], v.TipoVista AS [Vistas.TipoVista],
               CAST(v.UsaSeguridad AS int) AS [Vistas.UsaSeguridad], v.ArchivoJs AS [Vistas.ArchivoJs], v.JsCode AS [Vistas.JsCode],
               v.Frente AS [Vistas.Frente], v.IdStringsConnectionMyVision AS [Vistas.IdStringsConnectionMyVision],
               CASE WHEN v.FechaCreacion IS NULL THEN 0 ELSE 1 END AS [Vistas.FechaCreacion informada],
               v.User_Create AS [Vistas.User_Create], v.User_Update AS [Vistas.User_Update],
               CASE WHEN v.F_Create IS NULL THEN 0 ELSE 1 END AS [Vistas.F_Create informada],
               CASE WHEN v.F_Update IS NULL THEN 0 ELSE 1 END AS [Vistas.F_Update informada],
               e.IdVista AS [Estados.IdVista], CAST(e.[Default] AS int) AS [Estados.Default], e.Descripcion AS [Estados.Descripcion],
               e.ArchivoJs AS [Estados.ArchivoJs], CAST(e.LeftMenuFijo AS int) AS [Estados.LeftMenuFijo],
               e.User_Create AS [Estados.User_Create], e.User_Update AS [Estados.User_Update],
               CASE WHEN e.F_Create IS NULL THEN 0 ELSE 1 END AS [Estados.F_Create informada],
               CASE WHEN e.F_Update IS NULL THEN 0 ELSE 1 END AS [Estados.F_Update informada],
               f.Nombre AS [Filas.Nombre], f.Ancho AS [Filas.Ancho], f.Altura AS [Filas.Altura],
               f.User_Create AS [Filas.User_Create], f.User_Update AS [Filas.User_Update],
               CASE WHEN f.F_Create IS NULL THEN 0 ELSE 1 END AS [Filas.F_Create informada],
               CASE WHEN f.F_Update IS NULL THEN 0 ELSE 1 END AS [Filas.F_Update informada],
               ce.Configuracion AS [ConfiguracionesEstados.Configuracion], ce.User_Create AS [ConfiguracionesEstados.User_Create],
               CASE WHEN ce.F_Update IS NULL THEN 0 ELSE 1 END AS [ConfiguracionesEstados.F_Update informada],
               a.Nombre AS [Areas.Nombre], a.Ancho AS [Areas.Ancho], a.Altura AS [Areas.Altura],
               a.User_Create AS [Areas.User_Create], a.User_Update AS [Areas.User_Update],
               CASE WHEN a.F_Create IS NULL THEN 0 ELSE 1 END AS [Areas.F_Create informada],
               CASE WHEN a.F_Update IS NULL THEN 0 ELSE 1 END AS [Areas.F_Update informada],
               x.Descripcion AS [ExtraccionesDatos.Descripcion],
               x.User_Create AS [ExtraccionesDatos.User_Create], x.User_Update AS [ExtraccionesDatos.User_Update],
               CASE WHEN x.F_Create IS NULL THEN 0 ELSE 1 END AS [ExtraccionesDatos.F_Create informada],
               CASE WHEN x.F_Update IS NULL THEN 0 ELSE 1 END AS [ExtraccionesDatos.F_Update informada],
               CAST(sp.Nombre AS nvarchar(750)) AS [StoredProcedure.Nombre], sp.Descripcion AS [StoredProcedure.Descripcion],
               sp.OrdenEjecucion AS [StoredProcedure.OrdenEjecucion], sp.IdTipoEjecucionSP AS [StoredProcedure.IdTipoEjecucionSP],
               (SELECT COUNT(*) FROM dbo.StringsConnection WHERE StringConnection = @alias) AS [StoredProcedure.Conexiones que resuelve el alias],
               CASE WHEN sp.IdStringConnection = (SELECT MIN(IdStringConnection) FROM dbo.StringsConnection WHERE StringConnection = @alias)
                    THEN 1 ELSE 0 END AS [StoredProcedure.IdStringConnection es el del alias],
               sp.User_Create AS [StoredProcedure.User_Create], sp.User_Update AS [StoredProcedure.User_Update],
               CASE WHEN sp.F_Create IS NULL THEN 0 ELSE 1 END AS [StoredProcedure.F_Create informada],
               CASE WHEN sp.F_Update IS NULL THEN 0 ELSE 1 END AS [StoredProcedure.F_Update informada],
               o.Nombre AS [ObjetosDeArea.Nombre], o.IdTipoObjetoDeArea AS [ObjetosDeArea.IdTipoObjetoDeArea],
               CAST(o.UsaPaginador AS int) AS [ObjetosDeArea.UsaPaginador],
               o.User_Create AS [ObjetosDeArea.User_Create], o.User_Update AS [ObjetosDeArea.User_Update],
               CASE WHEN o.F_Create IS NULL THEN 0 ELSE 1 END AS [ObjetosDeArea.F_Create informada],
               CASE WHEN o.F_Update IS NULL THEN 0 ELSE 1 END AS [ObjetosDeArea.F_Update informada],
               CAST(c.Configuracion AS nvarchar(max)) AS [ConfiguracionesObjetoDeArea.Configuracion],
               c.User_Create AS [ConfiguracionesObjetoDeArea.User_Create], c.User_Update AS [ConfiguracionesObjetoDeArea.User_Update],
               CASE WHEN c.F_Create IS NULL THEN 0 ELSE 1 END AS [ConfiguracionesObjetoDeArea.F_Create informada],
               CASE WHEN c.F_Update IS NULL THEN 0 ELSE 1 END AS [ConfiguracionesObjetoDeArea.F_Update informada],
               lm.Nombre AS [LeftMenus.Nombre], lm.User_Create AS [LeftMenus.User_Create], lm.User_Update AS [LeftMenus.User_Update],
               CASE WHEN lm.F_Create IS NULL THEN 0 ELSE 1 END AS [LeftMenus.F_Create informada],
               CASE WHEN lm.F_Update IS NULL THEN 0 ELSE 1 END AS [LeftMenus.F_Update informada]
        FROM dbo.Vistas v
        LEFT JOIN dbo.Estados e ON e.IdVista = v.IdVista
        LEFT JOIN dbo.Filas f ON f.IdEstado = e.IdEstado
        LEFT JOIN dbo.ConfiguracionesEstados ce ON ce.IdEstado = e.IdEstado
        LEFT JOIN dbo.Areas a ON a.IdFila = f.IdFila
        LEFT JOIN dbo.ObjetosDeArea o ON o.IdArea = a.IdArea
        LEFT JOIN dbo.ExtraccionesDatos x ON x.IdExtraccionDato = o.IdExtraccionDato
        LEFT JOIN dbo.StoredProcedure sp ON sp.IdExtraccionDato = x.IdExtraccionDato
        LEFT JOIN dbo.ConfiguracionesObjetoDeArea c ON c.IdObjetoDeArea = o.IdObjetoDeArea
        LEFT JOIN dbo.LeftMenus lm ON lm.IdEstado = e.IdEstado
        WHERE v.IdVista = @viewName;
        """;

    private static readonly string[] GridTables =
        ["Areas", "ExtraccionesDatos", "StoredProcedure", "ObjetosDeArea", "ConfiguracionesObjetoDeArea", "LeftMenus"];

    /// <summary>Indica si no existe ningún registro de la vista, por IdVista ni por Nombre.</summary>
    public static bool IsAbsent(IReadOnlyDictionary<string, int> counts) => counts.Values.All(count => count == 0);

    public static IReadOnlyDictionary<string, int> ExpectedCounts(ViewCreationPlan plan)
    {
        var grid = plan.Grid is null ? 0 : 1;
        return new Dictionary<string, int>
        {
            ["Vistas"] = 1,
            ["Vistas por IdVista o Nombre"] = 1,
            ["Estados"] = 1,
            ["Filas"] = 1,
            ["Filas con el mismo IdFila"] = 1,
            ["Areas"] = grid,
            ["ExtraccionesDatos"] = grid,
            ["StoredProcedure"] = grid,
            ["ObjetosDeArea"] = grid,
            ["ConfiguracionesObjetoDeArea"] = grid,
            ["LeftMenus"] = grid,
            ["ConfiguracionesEstados"] = 1
        };
    }

    /// <summary>
    /// Valores que el script generado escribe para el plan: los del plan y las
    /// constantes del generador. Null significa que la columna debe quedar en NULL.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> ExpectedValues(ViewCreationPlan plan, string auditUser)
    {
        var expected = new Dictionary<string, string?>
        {
            ["Vistas.IdVista"] = plan.View.Name,
            ["Vistas.Nombre"] = plan.View.Name,
            ["Vistas.Descripcion"] = plan.View.Description,
            ["Vistas.IdOwner"] = "1",
            ["Vistas.IdHistoricoCambio"] = "1",
            ["Vistas.VersionActual"] = "1.0",
            ["Vistas.VistaVersionado"] = "1.0",
            ["Vistas.TipoVista"] = plan.View.ViewType,
            ["Vistas.UsaSeguridad"] = "0",
            ["Vistas.ArchivoJs"] = "",
            ["Vistas.JsCode"] = "",
            ["Vistas.Frente"] = "",
            ["Vistas.IdStringsConnectionMyVision"] = null,
            ["Vistas.FechaCreacion informada"] = "1",
            ["Estados.IdVista"] = plan.View.Name,
            ["Estados.Default"] = plan.State.IsDefault ? "1" : "0",
            ["Estados.Descripcion"] = plan.State.Name,
            ["Estados.ArchivoJs"] = "",
            ["Estados.LeftMenuFijo"] = "0",
            ["Filas.Nombre"] = plan.Row.Name,
            ["Filas.Ancho"] = plan.Row.Width.ToString(CultureInfo.InvariantCulture),
            ["Filas.Altura"] = plan.Row.Height.ToString(CultureInfo.InvariantCulture),
            ["ConfiguracionesEstados.Configuracion"] = plan.StateConfiguration.Configuration,
            ["ConfiguracionesEstados.User_Create"] = auditUser,
            ["ConfiguracionesEstados.F_Update informada"] = "1"
        };
        AddStandardAudit(expected, "Vistas", auditUser);
        AddStandardAudit(expected, "Estados", auditUser);
        AddStandardAudit(expected, "Filas", auditUser);
        if (plan.Grid is { } grid)
        {
            expected["Areas.Nombre"] = grid.AreaName;
            expected["Areas.Ancho"] = "0";
            expected["Areas.Altura"] = "0";
            expected["ExtraccionesDatos.Descripcion"] = grid.CanonicalStoredProcedure;
            expected["StoredProcedure.Nombre"] = grid.CanonicalStoredProcedure;
            expected["StoredProcedure.Descripcion"] = "";
            expected["StoredProcedure.OrdenEjecucion"] = "0";
            expected["StoredProcedure.IdTipoEjecucionSP"] = "1";
            expected["StoredProcedure.Conexiones que resuelve el alias"] = "1";
            expected["StoredProcedure.IdStringConnection es el del alias"] = "1";
            expected["ObjetosDeArea.Nombre"] = grid.ObjectName;
            expected["ObjetosDeArea.IdTipoObjetoDeArea"] = "2";
            expected["ObjetosDeArea.UsaPaginador"] = "0";
            expected["ConfiguracionesObjetoDeArea.Configuracion"] = grid.Configuration;
            expected["LeftMenus.Nombre"] = grid.LeftMenu;
            foreach (var table in GridTables)
            {
                AddStandardAudit(expected, table, auditUser);
            }
        }
        return expected;
    }

    /// <summary>
    /// Devuelve las diferencias entre lo leído y lo esperado; vacío significa que
    /// la vista quedó exactamente como el plan. <paramref name="valueRows"/> es el
    /// número de filas que devolvió <see cref="ValuesSql"/>.
    /// </summary>
    public static IReadOnlyList<string> Compare(ViewCreationPlan plan, string auditUser,
        IReadOnlyDictionary<string, int> counts, int valueRows, IReadOnlyDictionary<string, string?>? values)
    {
        var mismatches = new List<string>();
        foreach (var (name, expectedCount) in ExpectedCounts(plan))
        {
            if (!counts.TryGetValue(name, out var found))
            {
                mismatches.Add($"Conteo «{name}»: no se pudo leer.");
            }
            else if (found != expectedCount)
            {
                mismatches.Add($"Conteo «{name}»: esperado {expectedCount}, encontrado {found}.");
            }
        }
        if (valueRows != 1 || values is null)
        {
            mismatches.Add($"Lectura de valores: se esperaba 1 fila y se obtuvieron {valueRows}.");
            return mismatches;
        }
        foreach (var (name, expectedValue) in ExpectedValues(plan, auditUser))
        {
            if (!values.TryGetValue(name, out var found))
            {
                mismatches.Add($"Valor «{name}»: no se pudo leer.");
            }
            else if (!string.Equals(found, expectedValue, StringComparison.Ordinal))
            {
                mismatches.Add($"Valor «{name}»: esperado {Describe(expectedValue)}, encontrado {Describe(found)}.");
            }
        }
        return mismatches;
    }

    private static void AddStandardAudit(Dictionary<string, string?> expected, string table, string auditUser)
    {
        expected[$"{table}.User_Create"] = auditUser;
        expected[$"{table}.User_Update"] = null;
        expected[$"{table}.F_Create informada"] = "1";
        expected[$"{table}.F_Update informada"] = "0";
    }

    private static string Describe(string? value) =>
        value is null ? "NULL" : value.Length <= 60 ? $"«{value}»" : $"«{value[..60]}…» ({value.Length} caracteres)";
}
