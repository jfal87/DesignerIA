using DesignerIA.Contracts;

namespace DesignerIA.Api.Services.ViewSpecs;

/// <summary>
/// Completa únicamente las convenciones comprobadas para la vista mínima.
/// Designer.js crea el estado inicial "Pantalla" con Filas vacías.
/// </summary>
public static class ViewSpecNormalizer
{
    public const string DefaultStateName = "Pantalla";

    public static ViewSpec Normalize(ViewSpec spec)
    {
        var name = spec.Name?.Trim();
        var description = string.IsNullOrWhiteSpace(spec.Description) ? name : spec.Description.Trim();
        var states = spec.States ?? [];

        if (states.Count == 0 && !string.IsNullOrWhiteSpace(name))
        {
            states = [new ViewSpecState(DefaultStateName, [])];
        }

        var initialState = string.IsNullOrWhiteSpace(spec.InitialState) && states.Count == 1
            ? states[0].Name
            : spec.InitialState?.Trim();

        return new ViewSpec(name, description, initialState, states);
    }
}
