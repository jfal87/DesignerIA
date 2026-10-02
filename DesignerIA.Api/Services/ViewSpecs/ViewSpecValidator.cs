using DesignerIA.Contracts;

namespace DesignerIA.Api.Services.ViewSpecs;

/// <summary>
/// Validación determinística de un <see cref="ViewSpec"/> generado por Copilot.
/// Se ejecuta siempre en C#, nunca se le pide al modelo que valide su propia
/// salida (AGENTS.md). Distingue entre:
/// - Errors: problemas estructurales que invalidan la vista (por ejemplo un
///   handler que apunta a un estado inexistente).
/// - Pending: datos conocidos como faltantes que no invalidan la estructura
///   (por ejemplo un Stored Procedure sin indicar todavía).
/// </summary>
public static class ViewSpecValidator
{
    public static ViewSpecValidationResult Validate(ViewSpec spec)
    {
        var errors = new List<string>();
        var pending = new List<string>();

        if (string.IsNullOrWhiteSpace(spec.Name))
        {
            errors.Add("La vista no tiene nombre.");
        }

        var states = spec.States ?? [];
        if (states.Count == 0)
        {
            errors.Add("La vista debe tener al menos un estado.");
        }

        var stateNames = states
            .Select(s => s.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(spec.InitialState))
        {
            errors.Add("No se determinó el estado inicial de la vista.");
        }
        else if (!stateNames.Contains(spec.InitialState))
        {
            errors.Add($"El estado inicial '{spec.InitialState}' no corresponde a ningún estado definido.");
        }

        for (var stateIndex = 0; stateIndex < states.Count; stateIndex++)
        {
            var state = states[stateIndex];
            var stateLabel = string.IsNullOrWhiteSpace(state.Name) ? $"Estado #{stateIndex + 1}" : state.Name;

            if (string.IsNullOrWhiteSpace(state.Name))
            {
                errors.Add($"El estado #{stateIndex + 1} no tiene nombre.");
            }

            var rows = state.Rows ?? [];
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var areas = rows[rowIndex].Areas ?? [];
                if (areas.Count == 0)
                {
                    errors.Add($"La fila #{rowIndex + 1} del estado '{stateLabel}' no tiene áreas.");
                    continue;
                }

                foreach (var area in areas)
                {
                    ValidateArea(area, stateLabel, stateNames, errors, pending);
                }
            }
        }

        return new ViewSpecValidationResult(errors.Count == 0, errors, pending);
    }

    private static void ValidateArea(
        ViewSpecArea area,
        string stateLabel,
        HashSet<string> stateNames,
        List<string> errors,
        List<string> pending)
    {
        var areaKind = string.IsNullOrWhiteSpace(area.Type) ? "área" : area.Type.ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(area.Type))
        {
            pending.Add($"Tipo del área en el estado '{stateLabel}'.");
        }

        if (string.IsNullOrWhiteSpace(area.StoredProcedure))
        {
            pending.Add($"Stored Procedure de la {areaKind} de '{stateLabel}'.");
        }
        else if (string.IsNullOrWhiteSpace(area.ConnectionAlias))
        {
            pending.Add($"Alias de conexión (ConnectionAlias) del Stored Procedure '{area.StoredProcedure}' de la {areaKind} de '{stateLabel}'.");
        }

        var handlers = area.Handlers ?? [];
        foreach (var handler in handlers)
        {
            var handlerLabel = string.IsNullOrWhiteSpace(handler.Type) ? "handler" : handler.Type;

            if (string.IsNullOrWhiteSpace(handler.Type))
            {
                pending.Add($"Tipo de handler en la {areaKind} de '{stateLabel}'.");
            }

            if (string.IsNullOrWhiteSpace(handler.TargetState))
            {
                pending.Add($"Estado destino del handler '{handlerLabel}' en '{stateLabel}'.");
            }
            else if (!stateNames.Contains(handler.TargetState))
            {
                errors.Add($"El handler '{handlerLabel}' en '{stateLabel}' apunta al estado '{handler.TargetState}', que no existe.");
            }
        }
    }
}
