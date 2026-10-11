using DesignerIA.Api.Services.ViewSpecs;
using DesignerIA.Contracts;

namespace DesignerIA.Api.Services.ViewScripts;

/// <summary>
/// Convierte un ViewSpec normalizado y válido, con preflight aprobado, al plan
/// físico mínimo de una vista vacía o con una grilla.
/// </summary>
public static class ViewCreationPlanFactory
{
    private const string EmptyStateConfiguration =
        "{\"filters\":\"\",\"leftMenus\":\"\",\"isPopUp\":false,\"popUp\":{},\"idLayout\":\"00\"}";

    internal static ViewCreationPlan Create(ViewSpec spec, ViewCreationPreflightResult preflight)
    {
        var state = spec.States!.First(s => ViewSpecValidator.StateNameComparer.Equals(s.Name, spec.InitialState));
        var row = state.Rows?.FirstOrDefault();
        var area = row?.Areas?.FirstOrDefault();
        var grid = area is null ? null : new ViewCreationPlanGrid(
            area.Name!, area.ObjectName!, preflight.CanonicalStoredProcedure!, area.ConnectionAlias!,
            GridConfigurationPresets.GridQueryConfiguration, "LMFiltros");
        return new ViewCreationPlan(
            new ViewCreationPlanView(spec.Name!, spec.Description!, spec.ViewType ?? "site"),
            new ViewCreationPlanState(state.Name!, IsDefault: true),
            new ViewCreationPlanStateConfiguration(EmptyStateConfiguration),
            new ViewCreationPlanRow(row?.Name ?? state.Name + "F0", Width: 0, Height: 0),
            grid);
    }
}
