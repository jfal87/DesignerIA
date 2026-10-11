namespace DesignerIA.Api.Services.ViewScripts;

/// <summary>
/// Operaciones físicas mínimas que Designer oficial materializa para una vista
/// vacía o con una grilla opcional. Se mantiene separado del ViewSpec conceptual.
/// </summary>
public record ViewCreationPlan(
    ViewCreationPlanView View,
    ViewCreationPlanState State,
    ViewCreationPlanStateConfiguration StateConfiguration,
    ViewCreationPlanRow Row,
    ViewCreationPlanGrid? Grid = null);

public record ViewCreationPlanView(string Name, string Description, string ViewType = "site");

public record ViewCreationPlanState(string Name, bool IsDefault);

public record ViewCreationPlanStateConfiguration(string Configuration);

public record ViewCreationPlanRow(string Name, int Width, int Height);

public record ViewCreationPlanGrid(
    string AreaName, string ObjectName, string CanonicalStoredProcedure,
    string ConnectionAlias, string Configuration, string LeftMenu);
