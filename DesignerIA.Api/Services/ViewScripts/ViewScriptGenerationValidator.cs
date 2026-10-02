using DesignerIA.Contracts;

namespace DesignerIA.Api.Services.ViewScripts;

/// <summary>
/// Delimita el único plan que V1 puede generar: una vista, un estado inicial y
/// default, y cero filas. Las extensiones futuras se rechazan sin SQL parcial.
/// </summary>
public static class ViewScriptGenerationValidator
{
    public static IReadOnlyList<ViewScriptGenerationGap> Validate(
        ViewSpec spec,
        ViewSpecValidationResult viewSpecValidation)
    {
        var gaps = new List<ViewScriptGenerationGap>();

        if (!viewSpecValidation.IsValid)
        {
            return [new ViewScriptGenerationGap(
                "ViewSpec inválido",
                "El ViewSpec tiene errores estructurales; deben resolverse antes de generar el script.")];
        }

        if (viewSpecValidation.Pending.Count > 0)
        {
            return [new ViewScriptGenerationGap(
                "Datos del ViewSpec pendientes",
                "El ViewSpec todavía tiene datos pendientes: " + string.Join("; ", viewSpecValidation.Pending))];
        }

        var states = spec.States ?? [];
        if (states.Count != 1 || states[0].Name != spec.InitialState || (states[0].Rows ?? []).Count != 0)
        {
            gaps.Add(new ViewScriptGenerationGap(
                "Forma no soportada en V1",
                "V1 sólo genera una vista con un único estado inicial/default y cero filas."));
        }

        return gaps;
    }
}
