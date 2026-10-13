using System.Text.RegularExpressions;
using DesignerIA.Api.Services.ViewSpecs;
using DesignerIA.Contracts;

namespace DesignerIA.Api.Services.ViewScripts;

/// <summary>
/// Delimita las dos formas generables: una vista site con un estado inicial y
/// default sin filas lógicas, o una vista go con un estado inicial y default,
/// una fila y una grilla de consulta, con parámetros de valor fijo opcionales.
/// Otras formas se rechazan sin SQL parcial.
/// </summary>
public static class ViewScriptGenerationValidator
{
    private const string Part = @"[A-Za-z_][A-Za-z0-9_]{0,127}";

    public static bool TryParseStoredProcedure(string? value, out string schema, out string name)
    {
        schema = string.Empty;
        name = string.Empty;
        if (value is null)
        {
            return false;
        }
        var match = Regex.Match(value,
            @"\A(?:(?<schema>" + Part + @")\.(?<name>" + Part + @")|\[(?<schema>" + Part + @")\]\.\[(?<name>" + Part + @")\])\z");
        if (!match.Success)
        {
            return false;
        }
        schema = match.Groups["schema"].Value;
        name = match.Groups["name"].Value;
        return true;
    }

    public static IReadOnlyList<ViewScriptGenerationGap> Validate(
        ViewSpec spec, ViewSpecValidationResult viewSpecValidation)
    {
        if (!viewSpecValidation.IsValid)
        {
            return [new("ViewSpec inválido", "El ViewSpec tiene errores estructurales; deben resolverse antes de generar el script.")];
        }
        if (viewSpecValidation.Pending.Count > 0)
        {
            return [new("Datos del ViewSpec pendientes", "El ViewSpec todavía tiene datos pendientes: " + string.Join("; ", viewSpecValidation.Pending))];
        }

        var gaps = new List<ViewScriptGenerationGap>();
        CheckText(spec.Name, 250, "Nombre de vista", gaps);
        CheckText(spec.Description, 350, "Descripción", gaps);
        var states = spec.States ?? [];
        if (states.Count != 1 || !ViewSpecValidator.StateNameComparer.Equals(states[0].Name, spec.InitialState))
        {
            gaps.Add(UnsupportedShape());
            return gaps;
        }
        var state = states[0];
        CheckText(state.Name, 250, "Estado", gaps);
        var rows = state.Rows ?? [];
        if (rows.Count == 0 && (spec.ViewType is null || spec.ViewType == "site"))
        {
            CheckText(state.Name + "F0", 250, "Fila", gaps);
            return gaps;
        }
        if (spec.ViewType != "go" || rows.Count != 1 || (rows[0].Areas ?? []).Count != 1)
        {
            gaps.Add(UnsupportedShape());
            return gaps;
        }
        var row = rows[0];
        var area = row.Areas![0];
        if (area.Type != "Grilla")
        {
            gaps.Add(UnsupportedShape());
        }
        CheckText(row.Name, 250, "Fila", gaps);
        CheckText(area.Name, 75, "Área", gaps);
        CheckText(area.ObjectName, 250, "Objeto", gaps);
        if (!TryParseStoredProcedure(area.StoredProcedure, out var schema, out var name))
        {
            gaps.Add(new("Forma del SP no admitida", "El SP debe ser esquema.nombre o [esquema].[nombre], con partes ASCII de hasta 128 caracteres."));
        }
        else
        {
            CheckText($"[{schema}].[{name}]", 250, "SP canónico", gaps);
        }
        if (area.ConfigurationPreset != GridConfigurationPresets.GridQuery)
        {
            gaps.Add(new("Preset desconocido", "La plantilla requiere el preset grid-consulta-v1."));
        }
        if ((area.Handlers ?? []).Count != 0)
        {
            gaps.Add(new("Handlers no admitidos", "La plantilla no admite handlers."));
        }
        CheckParameters(area.Parameters ?? [], gaps);
        return gaps;
    }

    /// <summary>
    /// Devuelve el nombre del parámetro sin su «@» inicial opcional cuando es un
    /// identificador ASCII de hasta 75 caracteres (dbo.MapeoParametros.Parametro).
    /// No transforma ningún otro carácter.
    /// </summary>
    public static bool TryParseParameterName(string? value, out string name)
    {
        name = string.Empty;
        if (value is null)
        {
            return false;
        }
        var match = Regex.Match(value, @"\A@?(?<name>[A-Za-z_][A-Za-z0-9_]{0,74})\z");
        if (!match.Success)
        {
            return false;
        }
        name = match.Groups["name"].Value;
        return true;
    }

    /// <summary>
    /// Motivo por el que el texto de un valor fijo no se admite, o null si se admite.
    /// Se rechaza lo que Motor quitaría o alteraría al armar la llamada y lo que no
    /// tiene evidencia de conservarse; nunca se recorta ni se sustituye el texto.
    /// </summary>
    public static string? DescribeParameterValueProblem(string? value)
    {
        if (value is null)
        {
            return "no está informado. Indica un valor: esta entrega no representa un NULL de SQL.";
        }
        if (value.Length == 0)
        {
            return "está vacío. Indica un texto no vacío.";
        }
        if (value.Any(character => character < ' ' || character == '\u007f'))
        {
            return "contiene saltos de línea o caracteres de control, que no se admiten.";
        }
        if (value.Any(character => character > '~'))
        {
            return "contiene caracteres fuera de ASCII imprimible, cuya conservación en Motor no está comprobada.";
        }
        var forbidden = value.FirstOrDefault(character => character is '\'' or '"' or '\\' or '<' or '>');
        if (forbidden != default)
        {
            return $"contiene el carácter {forbidden}, que no se admite: no se admiten apóstrofe, comilla doble, barra invertida, < ni >.";
        }
        foreach (var sequence in new[] { "-mas-", "-andpersand-" })
        {
            if (value.Contains(sequence, StringComparison.Ordinal))
            {
                return $"contiene la secuencia {sequence}, que Motor reemplaza por otro carácter.";
            }
        }
        return null;
    }

    private static void CheckParameters(IReadOnlyList<ViewSpecParameter> parameters, List<ViewScriptGenerationGap> gaps)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < parameters.Count; index++)
        {
            var parameter = parameters[index];
            // Un JSON con «null» dentro de la lista llega aquí como elemento nulo.
            if (parameter is null)
            {
                gaps.Add(new("Parámetro no informado",
                    $"El parámetro #{index + 1} de la lista está vacío; indica su nombre y su valor, o quítalo."));
                continue;
            }
            if (!TryParseParameterName(parameter.Name, out var name))
            {
                gaps.Add(new("Nombre de parámetro no admitido",
                    $"El nombre del parámetro #{index + 1} debe ser un identificador ASCII de hasta 75 caracteres, con «@» inicial opcional."));
                continue;
            }
            if (!names.Add(name))
            {
                gaps.Add(new("Parámetro duplicado", $"El parámetro «{name}» está repetido; cada parámetro se indica una sola vez."));
            }
            var problem = DescribeParameterValueProblem(parameter.DefaultValue);
            if (problem is not null)
            {
                gaps.Add(new(string.IsNullOrEmpty(parameter.DefaultValue) ? "Valor de parámetro no informado" : "Valor de parámetro no admitido",
                    $"El valor del parámetro «{name}» {problem}"));
            }
        }
    }

    private static ViewScriptGenerationGap UnsupportedShape() => new(
        "Forma no soportada en V1", "Sólo se genera una vista vacía site o una vista go con un estado inicial, una fila y una grilla.");

    private static void CheckText(string? value, int maximum, string label, List<ViewScriptGenerationGap> gaps)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum)
        {
            gaps.Add(new(label, $"{label} es obligatorio y admite hasta {maximum} caracteres."));
        }
    }
}
