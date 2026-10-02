namespace DesignerIA.Api.Services.ViewScripts;

/// <summary>
/// Operaciones físicas mínimas que Designer oficial materializa para una vista
/// vacía. Se mantiene separado del ViewSpec conceptual.
/// </summary>
public record ViewCreationPlan(
    ViewCreationPlanView View,
    ViewCreationPlanState State,
    ViewCreationPlanStateConfiguration StateConfiguration,
    ViewCreationPlanRow Row);

public record ViewCreationPlanView(string Name, string Description);

public record ViewCreationPlanState(string Name, bool IsDefault);

public record ViewCreationPlanStateConfiguration(string Configuration);

public record ViewCreationPlanRow(string Name, int Width, int Height);
