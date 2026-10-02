using DesignerIA.Contracts;

namespace DesignerIA.Api.Services.ViewScripts;

/// <summary>Convierte un ViewSpec normalizado y válido al plan físico mínimo.</summary>
public static class ViewCreationPlanFactory
{
    private const string EmptyStateConfiguration =
        "{\"filters\":\"\",\"leftMenus\":\"\",\"isPopUp\":false,\"popUp\":{},\"idLayout\":\"00\"}";

    public static ViewCreationPlan Create(ViewSpec spec)
    {
        var state = spec.States!.Single(s => s.Name == spec.InitialState);
        return new ViewCreationPlan(
            new ViewCreationPlanView(spec.Name!, spec.Description!),
            new ViewCreationPlanState(state.Name!, IsDefault: true),
            new ViewCreationPlanStateConfiguration(EmptyStateConfiguration),
            new ViewCreationPlanRow(state.Name + "F0", Width: 0, Height: 0));
    }
}
