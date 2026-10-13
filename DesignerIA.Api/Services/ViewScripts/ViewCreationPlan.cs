namespace DesignerIA.Api.Services.ViewScripts;

/// <summary>
/// Operaciones físicas que Designer oficial materializa para una vista. Se
/// mantiene separado de la definición lógica (ViewSpec o documento JSONMetadata).
/// Los cuatro primeros miembros y <paramref name="Grid"/> describen el primer
/// estado; <paramref name="AdditionalStates"/>, los demás, en su orden.
/// <paramref name="Controls"/> son los web controls de la vista: se escriben una
/// sola vez, en el estado inicial, y cada estado indica cuáles están activos en
/// su configuración.
/// </summary>
public record ViewCreationPlan(
    ViewCreationPlanView View,
    ViewCreationPlanState State,
    ViewCreationPlanStateConfiguration StateConfiguration,
    ViewCreationPlanRow Row,
    ViewCreationPlanGrid? Grid = null,
    IReadOnlyList<ViewCreationPlanStateBlock>? AdditionalStates = null,
    IReadOnlyList<ViewCreationPlanControl>? Controls = null)
{
    /// <summary>Todos los estados de la vista, en su orden.</summary>
    public IReadOnlyList<ViewCreationPlanStateBlock> StateBlocks =>
        [new(State, StateConfiguration, Row, Grid), .. AdditionalStates ?? []];
}

public record ViewCreationPlanView(string Name, string Description, string ViewType = "site", string JsCode = "");

public record ViewCreationPlanState(string Name, bool IsDefault);

public record ViewCreationPlanStateConfiguration(string Configuration);

public record ViewCreationPlanRow(string Name, int Width, int Height);

/// <summary>Un estado con su configuración, su fila y, si tiene contenido, su grilla.</summary>
public record ViewCreationPlanStateBlock(
    ViewCreationPlanState State,
    ViewCreationPlanStateConfiguration StateConfiguration,
    ViewCreationPlanRow Row,
    ViewCreationPlanGrid? Grid = null);

/// <param name="LeftMenu">Nombre de la fila de LeftMenus del estado, o null si el estado no la lleva:
/// Designer sólo la escribe en el estado inicial.</param>
public record ViewCreationPlanGrid(
    string AreaName, string ObjectName, string CanonicalStoredProcedure,
    string ConnectionAlias, string Configuration, string? LeftMenu,
    IReadOnlyList<ViewCreationPlanParameter>? Parameters = null);

/// <summary>
/// Parámetro con valor fijo del SP de la grilla, en el orden de declaración del
/// procedimiento: una fila en ParametrosSP y otra en MapeoParametros, sin fuente.
/// </summary>
public record ViewCreationPlanParameter(string Name, string Type, bool UsesQuotes, string DefaultValue);

/// <summary>
/// Web control de la vista con opciones fijas: una fila en WebControls, sus
/// ItemsWebControl y su ConfiguracionesFiltros.
/// </summary>
/// <param name="TypeId">dbo.TiposWebControl.IdTipoWebControl.</param>
/// <param name="TypeName">dbo.TiposWebControl.Tipo esperado para ese Id; el script lo comprueba en la base.</param>
/// <param name="Order">dbo.WebControls.Orden.</param>
/// <param name="Configuration">JSON de dbo.ConfiguracionesFiltros.Configuracion.</param>
public record ViewCreationPlanControl(
    string Name, int TypeId, string TypeName, int Order,
    IReadOnlyList<ViewCreationPlanControlItem> Items, string Configuration);

public record ViewCreationPlanControlItem(string Value, string Text, bool IsDefault);
