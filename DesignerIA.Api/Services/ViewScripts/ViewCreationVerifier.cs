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
             O AS (SELECT IdObjetoDeArea, IdExtraccionDato FROM dbo.ObjetosDeArea WHERE IdArea IN (SELECT IdArea FROM A)),
             S AS (SELECT IdStoredProcedure FROM dbo.StoredProcedure WHERE IdExtraccionDato IN (SELECT IdExtraccionDato FROM O))
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
               (SELECT COUNT(*) FROM dbo.ConfiguracionesEstados WHERE IdEstado IN (SELECT IdEstado FROM E)) AS [ConfiguracionesEstados],
               (SELECT COUNT(*) FROM dbo.ParametrosSP WHERE IdStoredProcedure IN (SELECT IdStoredProcedure FROM S)) AS [ParametrosSP],
               (SELECT COUNT(*) FROM dbo.MapeoParametros WHERE IdEstado IN (SELECT IdEstado FROM E)
                    OR IdStoredProcedure IN (SELECT IdStoredProcedure FROM S)) AS [MapeoParametros],
               (SELECT COUNT(*) FROM dbo.WebControls WHERE IdEstado IN (SELECT IdEstado FROM E)) AS [WebControls],
               (SELECT COUNT(*) FROM dbo.ItemsWebControl WHERE IdWebControl IN
                    (SELECT IdWebControl FROM dbo.WebControls WHERE IdEstado IN (SELECT IdEstado FROM E))) AS [ItemsWebControl],
               (SELECT COUNT(*) FROM dbo.ConfiguracionesFiltros WHERE IdWebControl IN
                    (SELECT IdWebControl FROM dbo.WebControls WHERE IdEstado IN (SELECT IdEstado FROM E))) AS [ConfiguracionesFiltros];
        """;

    /// <summary>
    /// Web controls de la vista con su configuración, y sus opciones. Indica si el
    /// estado al que pertenece cada control es el inicial: Designer los escribe ahí.
    /// La comparación con el plan se hace en C#, de forma exacta.
    /// </summary>
    public const string ControlsSql = """
        SELECT w.Nombre AS [Nombre], w.IdTipoWebControl AS [IdTipoWebControl], CAST(s.[Default] AS int) AS [Estado inicial],
               w.IdExtraccionDato AS [IdExtraccionDato], w.Titulo AS [Titulo], w.ValoresComodin AS [ValoresComodin], w.Orden AS [Orden],
               CAST(c.Configuracion AS nvarchar(max)) AS [Configuracion], c.User_Create AS [Configuracion.User_Create],
               CASE WHEN c.F_Update IS NULL THEN 0 ELSE 1 END AS [Configuracion.F_Update informada],
               w.User_Create AS [User_Create], w.User_Update AS [User_Update],
               CASE WHEN w.F_Create IS NULL THEN 0 ELSE 1 END AS [F_Create informada],
               CASE WHEN w.F_Update IS NULL THEN 0 ELSE 1 END AS [F_Update informada]
        FROM dbo.WebControls w
        INNER JOIN dbo.Estados s ON s.IdEstado = w.IdEstado
        LEFT JOIN dbo.ConfiguracionesFiltros c ON c.IdWebControl = w.IdWebControl
        WHERE s.IdVista = @viewName
        ORDER BY w.IdWebControl;

        SELECT w.Nombre + N'|' + CAST(i.ItemId AS nvarchar(4000)) AS [Clave],
               i.ItemDescripcion AS [ItemDescripcion], CAST(i.ItemDefault AS int) AS [ItemDefault],
               i.User_Create AS [User_Create], i.User_Update AS [User_Update],
               CASE WHEN i.F_Create IS NULL THEN 0 ELSE 1 END AS [F_Create informada],
               CASE WHEN i.F_Update IS NULL THEN 0 ELSE 1 END AS [F_Update informada]
        FROM dbo.ItemsWebControl i
        INNER JOIN dbo.WebControls w ON w.IdWebControl = i.IdWebControl
        INNER JOIN dbo.Estados s ON s.IdEstado = w.IdEstado
        WHERE s.IdVista = @viewName
        ORDER BY i.IdItemWebControl;
        """;

    /// <summary>
    /// Tercera lectura, con dos conjuntos completos: todos los ParametrosSP de los SP
    /// de los objetos de la vista, y todos los MapeoParametros cuyo estado sea de la
    /// vista o cuyo SP sea de sus objetos. No filtra por nombre, área ni valor: la
    /// comparación con el plan se hace en C#, de forma exacta, para no depender de
    /// la igualdad de texto de la base. Los mapeos indican si su estado y su SP
    /// pertenecen a la vista, porque Motor los une sin IdStoredProcedure.
    /// </summary>
    public const string ParametersSql = """
        WITH E AS (SELECT IdEstado, Descripcion FROM dbo.Estados WHERE IdVista = @viewName),
             F AS (SELECT IdFila, IdEstado FROM dbo.Filas WHERE IdEstado IN (SELECT IdEstado FROM E)),
             A AS (SELECT IdArea, IdFila, Nombre FROM dbo.Areas WHERE IdFila IN (SELECT IdFila FROM F)),
             O AS (SELECT IdExtraccionDato, IdArea FROM dbo.ObjetosDeArea WHERE IdArea IN (SELECT IdArea FROM A)),
             S AS (SELECT sp.IdStoredProcedure, e.Descripcion AS Estado, a.Nombre AS Area
                   FROM dbo.StoredProcedure sp
                   INNER JOIN O o ON o.IdExtraccionDato = sp.IdExtraccionDato
                   INNER JOIN A a ON a.IdArea = o.IdArea
                   INNER JOIN F f ON f.IdFila = a.IdFila
                   INNER JOIN E e ON e.IdEstado = f.IdEstado)
        SELECT (SELECT Estado FROM S WHERE IdStoredProcedure = p.IdStoredProcedure) AS [SP.Estado],
               (SELECT Area FROM S WHERE IdStoredProcedure = p.IdStoredProcedure) AS [SP.Area],
               p.Nombre AS [Nombre], CAST(p.UsaComillas AS int) AS [UsaComillas], CAST(p.EsMyVision AS int) AS [EsMyVision],
               p.TipoParametro AS [TipoParametro], p.User_Create AS [User_Create], p.User_Update AS [User_Update],
               CASE WHEN p.F_Create IS NULL THEN 0 ELSE 1 END AS [F_Create informada],
               CASE WHEN p.F_Update IS NULL THEN 0 ELSE 1 END AS [F_Update informada]
        FROM dbo.ParametrosSP p
        WHERE p.IdStoredProcedure IN (SELECT IdStoredProcedure FROM S)
        ORDER BY p.IdParametroSP;

        WITH E AS (SELECT IdEstado, Descripcion FROM dbo.Estados WHERE IdVista = @viewName),
             F AS (SELECT IdFila, IdEstado FROM dbo.Filas WHERE IdEstado IN (SELECT IdEstado FROM E)),
             A AS (SELECT IdArea, IdFila, Nombre FROM dbo.Areas WHERE IdFila IN (SELECT IdFila FROM F)),
             O AS (SELECT IdExtraccionDato, IdArea FROM dbo.ObjetosDeArea WHERE IdArea IN (SELECT IdArea FROM A)),
             S AS (SELECT sp.IdStoredProcedure, e.Descripcion AS Estado, a.Nombre AS Area
                   FROM dbo.StoredProcedure sp
                   INNER JOIN O o ON o.IdExtraccionDato = sp.IdExtraccionDato
                   INNER JOIN A a ON a.IdArea = o.IdArea
                   INNER JOIN F f ON f.IdFila = a.IdFila
                   INNER JOIN E e ON e.IdEstado = f.IdEstado)
        SELECT (SELECT Descripcion FROM dbo.Estados WHERE IdEstado = m.IdEstado) AS [Estado],
               (SELECT Estado FROM S WHERE IdStoredProcedure = m.IdStoredProcedure) AS [SP.Estado],
               (SELECT Area FROM S WHERE IdStoredProcedure = m.IdStoredProcedure) AS [SP.Area],
               CASE WHEN m.IdEstado IN (SELECT IdEstado FROM E) THEN 1 ELSE 0 END AS [Estado de la vista],
               CASE WHEN m.IdStoredProcedure IN (SELECT IdStoredProcedure FROM S) THEN 1 ELSE 0 END AS [SP de la vista],
               m.NombreArea AS [NombreArea], m.Parametro AS [Parametro], m.Variable AS [Variable],
               m.ValorDefault AS [ValorDefault], m.VariableRequest AS [VariableRequest],
               m.User_Create AS [User_Create], m.User_Update AS [User_Update],
               CASE WHEN m.F_Create IS NULL THEN 0 ELSE 1 END AS [F_Create informada],
               CASE WHEN m.F_Update IS NULL THEN 0 ELSE 1 END AS [F_Update informada]
        FROM dbo.MapeoParametros m
        WHERE m.IdEstado IN (SELECT IdEstado FROM E)
           OR m.IdStoredProcedure IN (SELECT IdStoredProcedure FROM S)
        ORDER BY m.IdMapeoParametro;
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
        ["Areas", "ExtraccionesDatos", "StoredProcedure", "ObjetosDeArea", "ConfiguracionesObjetoDeArea"];

    private static readonly string[] ControlCounts = ["WebControls", "ItemsWebControl", "ConfiguracionesFiltros"];

    /// <summary>Indica si no existe ningún registro de la vista, por IdVista ni por Nombre.</summary>
    public static bool IsAbsent(IReadOnlyDictionary<string, int> counts) => counts.Values.All(count => count == 0);

    public static IReadOnlyDictionary<string, int> ExpectedCounts(ViewCreationPlan plan)
    {
        var blocks = plan.StateBlocks;
        var grids = blocks.Count(block => block.Grid is not null);
        var parameters = blocks.Sum(block => block.Grid?.Parameters?.Count ?? 0);
        var counts = new Dictionary<string, int>
        {
            ["Vistas"] = 1,
            ["Vistas por IdVista o Nombre"] = 1,
            ["Estados"] = blocks.Count,
            ["Filas"] = blocks.Count,
            ["Filas con el mismo IdFila"] = blocks.Count,
            ["Areas"] = grids,
            ["ExtraccionesDatos"] = grids,
            ["StoredProcedure"] = grids,
            ["ObjetosDeArea"] = grids,
            ["ConfiguracionesObjetoDeArea"] = grids,
            ["LeftMenus"] = blocks.Count(block => block.Grid?.LeftMenu is not null),
            ["ConfiguracionesEstados"] = blocks.Count,
            ["ParametrosSP"] = parameters,
            ["MapeoParametros"] = parameters
        };
        // Sólo forman parte de lo esperado cuando el plan tiene web controls; sin ellos, Compare exige que no haya ninguno.
        if (plan.Controls is { Count: > 0 } controls)
        {
            counts["WebControls"] = controls.Count;
            counts["ItemsWebControl"] = controls.Sum(control => control.Items.Count);
            counts["ConfiguracionesFiltros"] = controls.Count;
        }
        return counts;
    }

    /// <summary>
    /// Filas de WebControls con su ConfiguracionesFiltros que el script escribe para
    /// el plan, con los nombres de columna del primer conjunto de <see cref="ControlsSql"/>.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string?>> ExpectedControlRows(ViewCreationPlan plan, string auditUser) =>
        (plan.Controls ?? []).Select(control => WithRowAudit(new()
        {
            ["Nombre"] = control.Name,
            ["IdTipoWebControl"] = control.TypeId.ToString(CultureInfo.InvariantCulture),
            ["Estado inicial"] = "1",
            ["IdExtraccionDato"] = "-1",
            ["Titulo"] = "",
            ["ValoresComodin"] = "",
            ["Orden"] = control.Order.ToString(CultureInfo.InvariantCulture),
            ["Configuracion"] = control.Configuration,
            ["Configuracion.User_Create"] = auditUser,
            ["Configuracion.F_Update informada"] = "1"
        }, auditUser)).ToList();

    /// <summary>Opciones de los web controls del plan, con las columnas del segundo conjunto de <see cref="ControlsSql"/>.</summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string?>> ExpectedControlItemRows(ViewCreationPlan plan, string auditUser) =>
        (plan.Controls ?? []).SelectMany(control => control.Items.Select(item => WithRowAudit(new()
        {
            ["Clave"] = control.Name + "|" + item.Value,
            ["ItemDescripcion"] = item.Text,
            ["ItemDefault"] = item.IsDefault ? "1" : "0"
        }, auditUser))).ToList();

    /// <summary>
    /// Filas de ParametrosSP que el script escribe para el plan, en su orden, con
    /// los nombres de columna de <see cref="ParametersSql"/>.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string?>> ExpectedParameterRows(ViewCreationPlan plan, string auditUser) =>
        plan.StateBlocks.SelectMany(block => (block.Grid?.Parameters ?? []).Select(parameter =>
        {
            var row = new Dictionary<string, string?>
            {
                ["Nombre"] = parameter.Name,
                ["UsaComillas"] = parameter.UsesQuotes ? "1" : "0",
                ["EsMyVision"] = "0",
                ["TipoParametro"] = parameter.Type
            };
            // Con un estado los conteos ya dejan un único SP. Con varios hay que identificar su grilla.
            if (plan.StateBlocks.Count > 1)
            {
                row["SP.Estado"] = block.State.Name;
                row["SP.Area"] = block.Grid!.AreaName;
            }
            return WithRowAudit(row, auditUser);
        })).ToList();

    /// <summary>
    /// Filas de MapeoParametros que el script escribe para el plan: estado y SP de
    /// la vista, área de la grilla, sin fuente (cadenas vacías, no NULL) y con el
    /// valor fijo en ValorDefault. Con varios estados comprueba además el estado
    /// exacto del mapeo y la grilla propietaria de su SP.
    /// </summary>
    public static IReadOnlyList<IReadOnlyDictionary<string, string?>> ExpectedMappingRows(ViewCreationPlan plan, string auditUser) =>
        plan.StateBlocks.Where(block => block.Grid is not null).SelectMany(block => (block.Grid!.Parameters ?? []).Select(parameter =>
        {
            var row = new Dictionary<string, string?>
            {
                ["Estado de la vista"] = "1",
                ["SP de la vista"] = "1",
                ["NombreArea"] = block.Grid.AreaName,
                ["Parametro"] = parameter.Name,
                ["Variable"] = "",
                ["ValorDefault"] = parameter.DefaultValue,
                ["VariableRequest"] = ""
            };
            if (plan.StateBlocks.Count > 1)
            {
                row["Estado"] = block.State.Name;
                row["SP.Estado"] = block.State.Name;
                row["SP.Area"] = block.Grid.AreaName;
            }
            return WithRowAudit(row, auditUser);
        })).ToList();

    /// <summary>
    /// Valores que el script generado escribe para el plan: los del plan y las
    /// constantes del generador. Null significa que la columna debe quedar en NULL.
    /// </summary>
    public static IReadOnlyDictionary<string, string?> ExpectedValues(ViewCreationPlan plan, string auditUser) =>
        ExpectedStateValues(plan, plan.StateBlocks[0], auditUser);

    /// <summary>Valores de la vista y de uno de sus estados, con las columnas de <see cref="ValuesSql"/>.</summary>
    public static IReadOnlyDictionary<string, string?> ExpectedStateValues(ViewCreationPlan plan, ViewCreationPlanStateBlock block, string auditUser)
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
            ["Vistas.JsCode"] = plan.View.JsCode,
            ["Vistas.Frente"] = "",
            ["Vistas.IdStringsConnectionMyVision"] = null,
            ["Vistas.FechaCreacion informada"] = "1",
            ["Estados.IdVista"] = plan.View.Name,
            ["Estados.Default"] = block.State.IsDefault ? "1" : "0",
            ["Estados.Descripcion"] = block.State.Name,
            ["Estados.ArchivoJs"] = "",
            ["Estados.LeftMenuFijo"] = "0",
            ["Filas.Nombre"] = block.Row.Name,
            ["Filas.Ancho"] = block.Row.Width.ToString(CultureInfo.InvariantCulture),
            ["Filas.Altura"] = block.Row.Height.ToString(CultureInfo.InvariantCulture),
            ["ConfiguracionesEstados.Configuracion"] = block.StateConfiguration.Configuration,
            ["ConfiguracionesEstados.User_Create"] = auditUser,
            ["ConfiguracionesEstados.F_Update informada"] = "1"
        };
        AddStandardAudit(expected, "Vistas", auditUser);
        AddStandardAudit(expected, "Estados", auditUser);
        AddStandardAudit(expected, "Filas", auditUser);
        if (block.Grid is { } grid)
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
            if (grid.LeftMenu is not null)
            {
                AddStandardAudit(expected, "LeftMenus", auditUser);
            }
            else
            {
                // El estado no lleva LeftMenus: la lectura no encuentra fila y todo llega sin informar.
                expected["LeftMenus.User_Create"] = null;
                expected["LeftMenus.User_Update"] = null;
                expected["LeftMenus.F_Create informada"] = "0";
                expected["LeftMenus.F_Update informada"] = "0";
            }
        }
        return expected;
    }

    /// <summary>
    /// Devuelve las diferencias entre lo leído y lo esperado; vacío significa que
    /// la vista quedó exactamente como el plan. <paramref name="valueRows"/> es el
    /// número de filas que devolvió <see cref="ValuesSql"/>.
    /// <paramref name="parameterRows"/> y <paramref name="mappingRows"/> son los dos
    /// conjuntos de <see cref="ParametersSql"/>; null equivale a ningún registro leído.
    /// </summary>
    /// <remarks>
    /// Con varios estados, <paramref name="stateValues"/> trae la fila de valores de cada estado por su
    /// nombre. <paramref name="controlRows"/> y <paramref name="controlItemRows"/> son los dos conjuntos
    /// de <see cref="ControlsSql"/>.
    /// </remarks>
    public static IReadOnlyList<string> Compare(ViewCreationPlan plan, string auditUser,
        IReadOnlyDictionary<string, int> counts, int valueRows, IReadOnlyDictionary<string, string?>? values,
        IReadOnlyList<IReadOnlyDictionary<string, string?>>? parameterRows = null,
        IReadOnlyList<IReadOnlyDictionary<string, string?>>? mappingRows = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>>? stateValues = null,
        IReadOnlyList<IReadOnlyDictionary<string, string?>>? controlRows = null,
        IReadOnlyList<IReadOnlyDictionary<string, string?>>? controlItemRows = null)
    {
        var mismatches = new List<string>();
        var expectedCounts = ExpectedCounts(plan);
        // Un plan sin web controls no los espera: si la lectura los encuentra, es una diferencia.
        foreach (var name in ControlCounts.Where(name => !expectedCounts.ContainsKey(name)))
        {
            if (counts.TryGetValue(name, out var unexpected) && unexpected != 0)
            {
                mismatches.Add($"Conteo «{name}»: esperado 0, encontrado {unexpected}.");
            }
        }
        foreach (var (name, expectedCount) in expectedCounts)
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
        mappingRows ??= [];
        // Motor une los mapeos por estado, área y parámetro, sin IdStoredProcedure: un segundo registro
        // con esa clave lo afecta aunque pertenezca a otro SP. Se agrupa sin distinguir mayúsculas ni
        // espacios finales, que es la igualdad más amplia que puede aplicar la base.
        foreach (var duplicate in mappingRows
            .Where(row => row.GetValueOrDefault("Estado de la vista") == "1")
            .GroupBy(row => (Loose(row.GetValueOrDefault("NombreArea")), Loose(row.GetValueOrDefault("Parametro"))))
            .Where(group => group.Count() > 1))
        {
            mismatches.Add($"MapeoParametros: {duplicate.Count()} registros para el área {Describe(duplicate.First().GetValueOrDefault("NombreArea"))} " +
                $"y el parámetro {Describe(duplicate.First().GetValueOrDefault("Parametro"))} en el estado de la vista.");
        }
        CompareRows("ParametrosSP", "Nombre", ExpectedParameterRows(plan, auditUser), parameterRows ?? [], mismatches);
        CompareRows("MapeoParametros", "Parametro", ExpectedMappingRows(plan, auditUser), mappingRows, mismatches);
        const string definitionOrder = "los registros no están en el orden de definición de la vista.";
        CompareRows("WebControls", "Nombre", ExpectedControlRows(plan, auditUser), controlRows ?? [], mismatches, definitionOrder);
        CompareRows("ItemsWebControl", "Clave", ExpectedControlItemRows(plan, auditUser), controlItemRows ?? [], mismatches, definitionOrder);

        var blocks = plan.StateBlocks;
        if (blocks.Count == 1)
        {
            if (valueRows != 1 || values is null)
            {
                mismatches.Add($"Lectura de valores: se esperaba 1 fila y se obtuvieron {valueRows}.");
                return mismatches;
            }
            CompareValues("Valor", ExpectedValues(plan, auditUser), values, mismatches);
            return mismatches;
        }
        // Varios estados: una fila de valores por estado, identificada por su nombre.
        if (valueRows != blocks.Count || stateValues is null)
        {
            mismatches.Add($"Lectura de valores: se esperaban {blocks.Count} filas, una por estado, y se obtuvieron {valueRows}.");
            return mismatches;
        }
        foreach (var block in blocks)
        {
            if (!stateValues.TryGetValue(block.State.Name, out var stateRow))
            {
                mismatches.Add($"Estado {Describe(block.State.Name)}: no se leyeron sus valores.");
                continue;
            }
            CompareValues($"Estado {Describe(block.State.Name)}, valor", ExpectedStateValues(plan, block, auditUser), stateRow, mismatches);
        }
        return mismatches;
    }

    private static void CompareValues(string label, IReadOnlyDictionary<string, string?> expected,
        IReadOnlyDictionary<string, string?> values, List<string> mismatches)
    {
        foreach (var (name, expectedValue) in expected)
        {
            if (!values.TryGetValue(name, out var found))
            {
                mismatches.Add($"{label} «{name}»: no se pudo leer.");
            }
            else if (!string.Equals(found, expectedValue, StringComparison.Ordinal))
            {
                mismatches.Add($"{label} «{name}»: esperado {Describe(expectedValue)}, encontrado {Describe(found)}.");
            }
        }
    }

    /// <summary>
    /// Compara un conjunto leído con el esperado, registro a registro y de forma
    /// exacta (mayúsculas, espacios finales, cadena vacía y NULL son distintos).
    /// Informa ausentes, duplicados, sobrantes, valores distintos y orden distinto.
    /// </summary>
    private static void CompareRows(string table, string keyColumn,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> expected,
        IReadOnlyList<IReadOnlyDictionary<string, string?>> found, List<string> mismatches,
        string orderRule = "los registros no están en el orden de declaración del procedimiento.")
    {
        var before = mismatches.Count;
        if (found.Count != expected.Count)
        {
            mismatches.Add($"{table}: se esperaban {expected.Count} registros y se leyeron {found.Count}.");
        }
        var compared = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var expectedRow in expected)
        {
            var key = expectedRow[keyColumn];
            var occurrence = compared.GetValueOrDefault(key ?? string.Empty);
            compared[key ?? string.Empty] = occurrence + 1;
            var expectedWithKey = expected.Count(row => string.Equals(row[keyColumn], key, StringComparison.Ordinal));
            var matches = found.Where(row => string.Equals(row.GetValueOrDefault(keyColumn), key, StringComparison.Ordinal)).ToList();
            if (matches.Count != expectedWithKey)
            {
                if (occurrence == 0)
                {
                    mismatches.Add(matches.Count == 0 ? $"{table} {Describe(key)}: ausente."
                        : expectedWithKey == 1 ? $"{table} {Describe(key)}: duplicado, {matches.Count} registros."
                        : $"{table} {Describe(key)}: se esperaban {expectedWithKey} registros y se leyeron {matches.Count}.");
                }
                continue;
            }
            var match = matches[occurrence];
            foreach (var (name, expectedValue) in expectedRow)
            {
                if (!match.TryGetValue(name, out var value))
                {
                    mismatches.Add($"{table} {Describe(key)}, «{name}»: no se pudo leer.");
                }
                else if (!string.Equals(value, expectedValue, StringComparison.Ordinal))
                {
                    mismatches.Add($"{table} {Describe(key)}, «{name}»: esperado {Describe(expectedValue)}, encontrado {Describe(value)}.");
                }
            }
        }
        foreach (var row in found.Where(candidate => !expected.Any(expectedRow =>
            string.Equals(expectedRow[keyColumn], candidate.GetValueOrDefault(keyColumn), StringComparison.Ordinal))))
        {
            var relations = string.Join(", ", new[] { "Estado de la vista", "SP de la vista", "NombreArea" }
                .Where(row.ContainsKey).Select(name => $"{name}: {Describe(row[name])}"));
            mismatches.Add($"{table} {Describe(row.GetValueOrDefault(keyColumn))}: sobrante{(relations.Length == 0 ? "" : $" ({relations})")}.");
        }
        if (mismatches.Count == before && !found.Select(row => row.GetValueOrDefault(keyColumn))
            .SequenceEqual(expected.Select(row => row[keyColumn]), StringComparer.Ordinal))
        {
            mismatches.Add($"{table}: {orderRule}");
        }
    }

    private static string Loose(string? value) => (value ?? string.Empty).TrimEnd(' ').ToUpperInvariant();

    private static IReadOnlyDictionary<string, string?> WithRowAudit(Dictionary<string, string?> row, string auditUser)
    {
        row["User_Create"] = auditUser;
        row["User_Update"] = null;
        row["F_Create informada"] = "1";
        row["F_Update informada"] = "0";
        return row;
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
