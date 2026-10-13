using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DesignerIA.Api.Services.ViewScripts;
using DesignerIA.Contracts;
using static DesignerIA.Api.Services.ViewDocuments.JsonMetadataNode;

namespace DesignerIA.Api.Services.ViewDocuments;

/// <summary>
/// Estructura, referencias y soporte de un documento JSONMetadata ya expandido.
/// Distingue tres resultados: error (el documento está mal), pendiente (falta un
/// dato que alguien debe aportar) y no soportado (válido, pero esta versión no lo
/// materializa). Nunca completa un dato: un alias, un SP o un valor que faltan se
/// señalan con su ubicación.
/// Esta versión materializa una vista vacía site, o una vista go con uno o varios
/// estados, cada uno con una fila, un área y una grilla con parámetros de valor
/// fijo, y web controls de navegación (lista de botones con opciones fijas y la
/// acción cambiarEstado) definidos en la vista y activados por estado.
/// </summary>
public static partial class JsonMetadataValidator
{
    private static readonly string[] ReservedAreaNames = ["divWebControls", "leftMenu"];
    private static readonly string[] SourceKindsWithoutSupport = ["control", "leftMenu", "pivot", "variable"];
    private static readonly string[] ControlTypesWithoutSupport =
        ["combo", "comboExpandable", "comboCheck", "calendar", "calendarMonth", "buttonClean", "checkBoxList", "radioButtonList", "textBox", "pivot"];
    private static readonly string[] ActionsWithoutSupport = ["RefrescarAreas", "RefrescarWC", "RefrescarLM"];
    public const string ButtonListType = "buttonList";
    public const string ChangeStateAction = "cambiarEstado";

    public static void Validate(JsonObject root, List<JsonMetadataDiagnostic> diagnostics)
    {
        ValidateVersion(root, diagnostics);
        var view = AsObject(root["view"]);
        if (view is not null)
        {
            ValidateView(view, diagnostics);
        }
        else if (!root.ContainsKey("view"))
        {
            diagnostics.Add(Pending("view.id", "DatoRequerido", "El documento no identifica la vista.", "El identificador de la vista nueva, en view.id."));
        }
        var controlNames = ValidateControls(root, diagnostics);
        var rowCounts = ValidateStates(root, controlNames, diagnostics);
        ValidateTypeAgainstContent(view, root, rowCounts, diagnostics);
    }

    /// <summary>
    /// Valida las definiciones de web controls de la vista y devuelve sus nombres.
    /// Un control se define una vez; los estados sólo lo referencian.
    /// </summary>
    private static HashSet<string> ValidateControls(JsonObject root, List<JsonMetadataDiagnostic> diagnostics)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (AsList(root["controls"]) is not { } controls)
        {
            return names;
        }
        var stateNames = (AsList(root["states"]) ?? []).Select(state => AsText(AsObject(state)?["name"])).OfType<string>().ToList();
        for (var index = 0; index < controls.Count; index++)
        {
            if (AsObject(controls[index]) is not { } control)
            {
                continue;
            }
            var path = Item("controls", index);
            var name = RequiredText(control, path, "name", "El nombre del web control.", diagnostics);
            if (name is not null && CheckName(name, 75, Child(path, "name"), "El nombre del web control", diagnostics) && !names.Add(name))
            {
                diagnostics.Add(Error(Child(path, "name"), "NombreDuplicado", $"El web control «{name}» está repetido.", "Un nombre distinto para cada web control: se define una sola vez."));
            }
            var type = RequiredText(control, path, "type", "El tipo de web control. Esta versión admite \"buttonList\".", diagnostics);
            if (type is not null && type != ButtonListType)
            {
                diagnostics.Add(ControlTypesWithoutSupport.Contains(type)
                    ? Unsupported(Child(path, "type"), $"Esta versión sólo crea listas de botones; «{type}» queda pendiente.", "\"buttonList\".")
                    : Error(Child(path, "type"), "ValorNoAdmitido", $"«{type}» no es un tipo de web control.", "\"buttonList\"."));
            }
            ValidateControlItems(control, path, diagnostics);
            ValidateControlActions(control, path, stateNames, diagnostics);
        }
        return names;
    }

    private static void ValidateControlItems(JsonObject control, string controlPath, List<JsonMetadataDiagnostic> diagnostics)
    {
        var path = Child(controlPath, "items");
        if (!control.ContainsKey("items"))
        {
            diagnostics.Add(Pending(path, "DatoRequerido", "El web control no declara sus opciones.", "Al menos una opción con text y value."));
            return;
        }
        if (AsList(control["items"]) is not { } items)
        {
            return;
        }
        if (items.Count == 0)
        {
            diagnostics.Add(Error(path, "ColeccionVacia", "El web control no tiene opciones.", "Al menos una opción con text y value."));
        }
        var values = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < items.Count; index++)
        {
            if (AsObject(items[index]) is not { } item)
            {
                continue;
            }
            var itemPath = Item(path, index);
            var text = RequiredText(item, itemPath, "text", "El texto que se muestra en la opción.", diagnostics);
            if (text is not null && (text.Length > 250 || text.Any(character => character < ' ' || character is '\'' or '"' or '\\' or '<' or '>')))
            {
                diagnostics.Add(Error(Child(itemPath, "text"), "ValorNoAdmitido", "El texto de la opción no es admitido.",
                    "Hasta 250 caracteres, sin saltos de línea, apóstrofe, comilla doble, barra invertida, < ni >."));
            }
            var value = RequiredText(item, itemPath, "value", "El valor de la opción.", diagnostics);
            if (value is not null && CheckName(value, 75, Child(itemPath, "value"), "El valor de la opción", diagnostics) && !values.Add(value))
            {
                diagnostics.Add(Error(Child(itemPath, "value"), "NombreDuplicado", $"El valor «{value}» está repetido.", "Un valor distinto para cada opción."));
            }
        }
    }

    // Navegación entre estados: una acción cambiarEstado cuyo único parámetro es el nombre de un estado de la vista.
    private static void ValidateControlActions(JsonObject control, string controlPath, List<string> stateNames, List<JsonMetadataDiagnostic> diagnostics)
    {
        var path = Child(controlPath, "actions");
        if (!control.ContainsKey("actions"))
        {
            diagnostics.Add(Pending(path, "DatoRequerido", "El web control no declara su acción.", "Una acción cambiarEstado con el estado destino."));
            return;
        }
        if (AsList(control["actions"]) is not { } actions)
        {
            return;
        }
        if (actions.Count == 0)
        {
            diagnostics.Add(Error(path, "ColeccionVacia", "El web control no tiene acciones.", "Una acción cambiarEstado con el estado destino."));
        }
        for (var index = 0; index < actions.Count; index++)
        {
            if (AsObject(actions[index]) is not { } action)
            {
                continue;
            }
            var actionPath = Item(path, index);
            if (index > 0)
            {
                diagnostics.Add(Unsupported(actionPath, "Esta versión crea web controls con una sola acción.", "Deja una única acción."));
            }
            var name = RequiredText(action, actionPath, "name", "\"cambiarEstado\".", diagnostics);
            if (name is null)
            {
                continue;
            }
            if (name != ChangeStateAction)
            {
                diagnostics.Add(ActionsWithoutSupport.Contains(name)
                    ? Unsupported(Child(actionPath, "name"), $"Esta versión sólo crea la acción cambiarEstado; «{name}» queda pendiente.", "\"cambiarEstado\".")
                    : Error(Child(actionPath, "name"), "ValorNoAdmitido", $"«{name}» no es una acción de web control.", "\"cambiarEstado\"."));
                continue;
            }
            var parametersPath = Child(actionPath, "parameters");
            var known = stateNames.Count == 0 ? "" : " Estados de la vista: " + string.Join(", ", stateNames) + ".";
            if (!action.ContainsKey("parameters"))
            {
                diagnostics.Add(Pending(parametersPath, "DatoRequerido", "La acción cambiarEstado no indica el estado destino.", "El nombre de un estado de la vista." + known));
            }
            else if (AsList(action["parameters"]) is { } parameters)
            {
                if (parameters.Count != 1)
                {
                    diagnostics.Add(Error(parametersPath, "ValorNoAdmitido", "cambiarEstado recibe exactamente un parámetro.", "El nombre de un estado de la vista." + known));
                }
                else if (AsText(parameters[0]) is { } target && !stateNames.Contains(target, StringComparer.Ordinal))
                {
                    diagnostics.Add(Error(Item(parametersPath, 0), "ReferenciaRota", $"El estado «{target}» no existe en la vista.", "El nombre de un estado de la vista." + known));
                }
            }
        }
    }

    private static void ValidateVersion(JsonObject root, List<JsonMetadataDiagnostic> diagnostics)
    {
        const string needed = "\"schemaVersion\": \"" + JsonMetadataExpander.SchemaVersion + "\"";
        if (!root.ContainsKey("schemaVersion"))
        {
            diagnostics.Add(Error("schemaVersion", "VersionRequerida", "El documento no indica la versión del formato.", needed));
        }
        else if (AsText(root["schemaVersion"]) is { } version && version != JsonMetadataExpander.SchemaVersion)
        {
            diagnostics.Add(Error("schemaVersion", "VersionNoAdmitida", $"La versión «{version}» no está admitida.", needed));
        }
    }

    private static void ValidateView(JsonObject view, List<JsonMetadataDiagnostic> diagnostics)
    {
        var id = RequiredText(view, "view", "id", "El identificador de la vista nueva.", diagnostics);
        if (id is not null)
        {
            CheckName(id, 250, "view.id", "El identificador de la vista", diagnostics);
        }
        if (AsText(view["description"]) is { } description)
        {
            if (string.IsNullOrWhiteSpace(description))
            {
                diagnostics.Add(Error("view.description", "ValorVacio", "La descripción está vacía.", "Un texto, u omite la propiedad para usar el identificador."));
            }
            else if (description.Length > 350)
            {
                diagnostics.Add(Error("view.description", "ValorNoAdmitido", $"La descripción tiene {description.Length} caracteres.", "Hasta 350 caracteres."));
            }
        }
        if (AsText(view["type"]) is { } type && type is not ("go" or "site"))
        {
            diagnostics.Add(Error("view.type", "ValorNoAdmitido", $"«{type}» no es un tipo de vista.", "\"go\" o \"site\"."));
        }
        if (AsText(view["js"]) is { } js)
        {
            // Motor inserts JsCode into an inline script and decodes these three legacy markers.
            // Reject inputs that its unconditional replacements would corrupt; never rewrite them silently.
            if (js.Contains('\0') || js.Contains("</script", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(Error("view.js", "JavaScriptNoRepresentable",
                    "El código contiene un carácter nulo o un cierre de script que Motor no puede cargar dentro de su script de vista.",
                    "Código sin caracteres nulos ni cierres literales </script>."));
            }
            if (new[] { @"\u0022", @"\u0027", @"\u0000" }.Any(marker => js.Contains(marker, StringComparison.Ordinal)))
            {
                diagnostics.Add(Error("view.js", "JavaScriptNoRepresentable",
                    "El código contiene una secuencia reservada que Motor sustituye al cargar JsCode.",
                    "Usa las comillas y null de JavaScript directamente; si necesitas ese texto literal, constrúyelo sin una secuencia reservada contigua."));
            }
        }
        if (AsFlag(view["usesSecurity"]) == true)
        {
            diagnostics.Add(Unsupported("view.usesSecurity", "Esta versión no crea vistas con seguridad.", "Omite la propiedad o usa false."));
        }
    }

    /// <summary>Valida los estados y devuelve cuántas filas declara cada uno, en su orden.</summary>
    private static IReadOnlyList<int> ValidateStates(JsonObject root, HashSet<string> controlNames, List<JsonMetadataDiagnostic> diagnostics)
    {
        if (AsList(root["states"]) is not { } states)
        {
            return [];
        }
        if (states.Count == 0)
        {
            diagnostics.Add(Error("states", "ColeccionVacia", "«states» está vacío.", "Omite la propiedad para una vista vacía, o declara un estado."));
            return [];
        }

        var stateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var areaNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defaults = 0;
        var defaultIsKnown = true;
        var rowCounts = new List<int>();
        for (var index = 0; index < states.Count; index++)
        {
            if (AsObject(states[index]) is not { } state)
            {
                // La lectura ya reportó el elemento; sin él no se puede juzgar el estado inicial.
                defaultIsKnown = false;
                rowCounts.Add(-1);
                continue;
            }
            var path = Item("states", index);
            var name = RequiredText(state, path, "name", "El nombre del estado; con varios estados no se deriva.", diagnostics);
            if (name is not null && CheckName(name, 248, Child(path, "name"), "El nombre del estado", diagnostics) && !stateNames.Add(name))
            {
                diagnostics.Add(Error(Child(path, "name"), "NombreDuplicado", $"El estado «{name}» está repetido.", "Un nombre distinto para cada estado."));
            }
            if (AsFlag(state["isDefault"]) == true)
            {
                defaults++;
            }
            else if (state.ContainsKey("isDefault") && AsFlag(state["isDefault"]) is null)
            {
                defaultIsKnown = false;
            }
            ValidateActiveControls(state, path, controlNames, diagnostics);
            rowCounts.Add(ValidateRows(state, path, areaNames, controlNames, diagnostics));
        }
        // El estado inicial se identifica por su marca; nunca se elige por posición.
        if (defaultIsKnown && defaults != 1)
        {
            diagnostics.Add(Error("states", "EstadoInicial",
                defaults == 0 ? "Ningún estado está marcado como inicial." : "Más de un estado está marcado como inicial.",
                "Exactamente un estado con \"isDefault\": true."));
        }
        return rowCounts;
    }

    // El estado no define controles: referencia por nombre los de la vista que tiene activos.
    private static void ValidateActiveControls(JsonObject state, string statePath, HashSet<string> controlNames, List<JsonMetadataDiagnostic> diagnostics)
    {
        if (AsList(state["controls"]) is not { } active)
        {
            return;
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < active.Count; index++)
        {
            if (AsText(active[index]) is not { } name)
            {
                continue;
            }
            var path = Item(Child(statePath, "controls"), index);
            if (!controlNames.Contains(name))
            {
                diagnostics.Add(Error(path, "ReferenciaRota", $"El web control «{name}» no está definido en la vista.",
                    "El nombre de un web control de «controls»" + (controlNames.Count == 0 ? "." : ": " + string.Join(", ", controlNames) + ".")));
            }
            else if (!seen.Add(name))
            {
                diagnostics.Add(Error(path, "NombreDuplicado", $"El web control «{name}» está referenciado dos veces en el estado.", "Una sola referencia."));
            }
        }
    }

    private static int ValidateRows(JsonObject state, string statePath, HashSet<string> areaNames, HashSet<string> controlNames, List<JsonMetadataDiagnostic> diagnostics)
    {
        if (AsList(state["rows"]) is not { } rows)
        {
            return 0;
        }
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            if (AsObject(rows[rowIndex]) is not { } row)
            {
                continue;
            }
            var rowPath = Item(Child(statePath, "rows"), rowIndex);
            var areasPath = Child(rowPath, "areas");
            if (rowIndex > 0)
            {
                diagnostics.Add(Unsupported(rowPath, "Esta versión crea estados de una sola fila.", "Deja una única fila."));
            }
            if (!row.ContainsKey("areas"))
            {
                diagnostics.Add(Pending(areasPath, "DatoRequerido", "La fila no declara áreas.", "Un área con su objeto."));
                continue;
            }
            if (AsList(row["areas"]) is not { } areas)
            {
                continue;
            }
            if (areas.Count == 0)
            {
                diagnostics.Add(Error(areasPath, "ColeccionVacia", "La fila no tiene áreas.", "Un área con su objeto, o quita la fila."));
            }
            for (var areaIndex = 0; areaIndex < areas.Count; areaIndex++)
            {
                if (AsObject(areas[areaIndex]) is not { } area)
                {
                    continue;
                }
                var areaPath = Item(areasPath, areaIndex);
                if (areaIndex > 0)
                {
                    diagnostics.Add(Unsupported(areaPath, "Esta versión crea filas de una sola área.", "Deja una única área."));
                }
                ValidateArea(area, areaPath, areaNames, controlNames, diagnostics);
            }
        }
        return rows.Count;
    }

    private static void ValidateArea(JsonObject area, string path, HashSet<string> areaNames, HashSet<string> controlNames, List<JsonMetadataDiagnostic> diagnostics)
    {
        var name = RequiredText(area, path, "name", "El nombre del área; con varias áreas no se deriva.", diagnostics);
        var namePath = Child(path, "name");
        if (name is not null && CheckName(name, 75, namePath, "El nombre del área", diagnostics))
        {
            if (ReservedAreaNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                diagnostics.Add(Error(namePath, "NombreReservado", $"«{name}» está reservado para los parámetros de controles y de left menu.", "Otro nombre de área."));
            }
            else if (!areaNames.Add(name))
            {
                diagnostics.Add(Error(namePath, "NombreDuplicado", $"El área «{name}» está repetida.", "Un nombre distinto para cada área."));
            }
            else if (controlNames.Contains(name))
            {
                diagnostics.Add(Error(namePath, "NombreDuplicado", $"El área «{name}» tiene el mismo nombre que un web control.",
                    "Nombres distintos: áreas y web controls comparten el mismo espacio de nombres en la página."));
            }
        }

        var objectPath = Child(path, "object");
        if (!area.ContainsKey("object"))
        {
            diagnostics.Add(Pending(objectPath, "DatoRequerido", "El área no declara su objeto.", "Un objeto con type y data."));
            return;
        }
        if (AsObject(area["object"]) is not { } areaObject)
        {
            return;
        }
        switch (RequiredText(areaObject, objectPath, "type", "El tipo de objeto. Esta versión admite \"grid\".", diagnostics))
        {
            case null:
                break;
            case "grid":
                ValidateGridData(areaObject, objectPath, diagnostics);
                break;
            case "chart" or "jsControl":
                diagnostics.Add(Unsupported(Child(objectPath, "type"), "Esta versión sólo crea grillas.", "\"grid\"."));
                break;
            case var type:
                diagnostics.Add(Error(Child(objectPath, "type"), "ValorNoAdmitido", $"«{type}» no es un tipo de objeto.", "\"grid\"."));
                break;
        }
    }

    // Una grilla sin alias o sin SP produce pendientes concretos: nunca se convierte en una vista vacía.
    private static void ValidateGridData(JsonObject areaObject, string objectPath, List<JsonMetadataDiagnostic> diagnostics)
    {
        const string connectionNeeded = "Un alias de conexión registrado en StringsConnection.";
        const string procedureNeeded = "Un Stored Procedure existente, como esquema.nombre.";
        var dataPath = Child(objectPath, "data");
        if (!areaObject.ContainsKey("data"))
        {
            diagnostics.Add(Pending(Child(dataPath, "connection"), "DatoRequerido", "La grilla no indica alias de conexión.", connectionNeeded));
            diagnostics.Add(Pending(Child(dataPath, "storedProcedure"), "DatoRequerido", "La grilla no indica Stored Procedure.", procedureNeeded));
            return;
        }
        if (AsObject(areaObject["data"]) is not { } data)
        {
            return;
        }
        RequiredText(data, dataPath, "connection", connectionNeeded, diagnostics);
        var procedure = RequiredText(data, dataPath, "storedProcedure", procedureNeeded, diagnostics);
        if (procedure is not null && !ViewScriptGenerationValidator.TryParseStoredProcedure(procedure, out _, out _))
        {
            diagnostics.Add(Error(Child(dataPath, "storedProcedure"), "FormaNoAdmitida",
                $"«{procedure}» no tiene la forma esquema.nombre.", "esquema.nombre o [esquema].[nombre], con partes ASCII de hasta 128 caracteres."));
        }
        ValidateParameters(data, dataPath, diagnostics);
    }

    private static void ValidateParameters(JsonObject data, string dataPath, List<JsonMetadataDiagnostic> diagnostics)
    {
        if (AsList(data["parameters"]) is not { } parameters)
        {
            return;
        }
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < parameters.Count; index++)
        {
            if (AsObject(parameters[index]) is not { } parameter)
            {
                continue;
            }
            var path = Item(Child(dataPath, "parameters"), index);
            var name = RequiredText(parameter, path, "name", "El nombre del parámetro, tal como lo declara el procedimiento.", diagnostics);
            if (name is not null)
            {
                if (!ViewScriptGenerationValidator.TryParseParameterName(name, out var parsedName))
                {
                    diagnostics.Add(Error(Child(path, "name"), "NombreNoAdmitido", $"«{name}» no es un nombre de parámetro admitido.",
                        "Un identificador ASCII de hasta 75 caracteres, con «@» inicial opcional."));
                }
                else if (!names.Add(parsedName))
                {
                    diagnostics.Add(Error(Child(path, "name"), "NombreDuplicado", $"El parámetro «{parsedName}» está repetido.", "Cada parámetro una sola vez."));
                }
            }
            ValidateSource(AsObject(parameter["source"]), Child(path, "source"), diagnostics);
            ValidateFixedValue(parameter, path, name ?? $"#{index + 1}", diagnostics);
        }
    }

    private static void ValidateSource(JsonObject? source, string path, List<JsonMetadataDiagnostic> diagnostics)
    {
        if (source is null)
        {
            return;
        }
        var kind = RequiredText(source, path, "kind", "\"fixed\".", diagnostics);
        if (kind is null)
        {
            return;
        }
        if (SourceKindsWithoutSupport.Contains(kind))
        {
            diagnostics.Add(Unsupported(Child(path, "kind"), "Esta versión sólo crea parámetros con valor fijo.", "\"fixed\", u omite source."));
        }
        else if (kind != JsonMetadataExpander.FixedSourceKind)
        {
            diagnostics.Add(Error(Child(path, "kind"), "ValorNoAdmitido", $"«{kind}» no es un origen de valor.", "\"fixed\"."));
        }
        else if (source.ContainsKey("name"))
        {
            diagnostics.Add(Error(Child(path, "name"), "ValorNoAdmitido", "Un valor fijo no tiene fuente.", "Quita name."));
        }
    }

    // No declarar el parámetro, declararlo vacío y pretender un NULL de SQL son tres cosas distintas.
    private static void ValidateFixedValue(JsonObject parameter, string path, string label, List<JsonMetadataDiagnostic> diagnostics)
    {
        var valuePath = Child(path, "default");
        if (!parameter.ContainsKey("default"))
        {
            diagnostics.Add(Pending(valuePath, "DatoRequerido", $"El parámetro «{label}» no tiene valor.", "Un valor de texto. Para no enviar el parámetro, no lo declares."));
        }
        else if (AsText(parameter["default"]) is { } value && ViewScriptGenerationValidator.DescribeParameterValueProblem(value) is { } problem)
        {
            diagnostics.Add(Error(valuePath, value.Length == 0 ? "ValorVacio" : "ValorNoAdmitido",
                $"El valor del parámetro «{label}» {problem}", "Un texto no vacío en ASCII imprimible, sin apóstrofe, comilla doble, barra invertida, < ni >."));
        }
    }

    // site sólo tiene comprobada la vista vacía de un estado; go, una grilla en cada estado.
    private static void ValidateTypeAgainstContent(JsonObject? view, JsonObject root, IReadOnlyList<int> rowCounts, List<JsonMetadataDiagnostic> diagnostics)
    {
        var type = AsText(view?["type"]);
        var hasContent = rowCounts.Any(count => count > 0);
        if (type == "site")
        {
            if (hasContent)
            {
                diagnostics.Add(Unsupported("view.type", "Una vista site con contenido no tiene un caso comprobado.", "\"type\": \"go\" para una vista con grilla."));
            }
            else if (rowCounts.Count > 1 || AsList(root["controls"]) is { Count: > 0 })
            {
                diagnostics.Add(Unsupported("view.type", "Una vista site con varios estados o con web controls no tiene un caso comprobado.",
                    "\"type\": \"go\", con una grilla en cada estado."));
            }
        }
        else if (type == "go")
        {
            if (!hasContent)
            {
                diagnostics.Add(Unsupported("view.type", "Una vista go sin contenido no tiene un caso comprobado.", "Omite type para una vista vacía, o declara una grilla."));
                return;
            }
            for (var index = 0; index < rowCounts.Count; index++)
            {
                if (rowCounts[index] == 0)
                {
                    diagnostics.Add(Unsupported(Child(Item("states", index), "rows"), "En una vista go, un estado sin contenido no tiene un caso comprobado.",
                        "Una fila con un área y una grilla en cada estado."));
                }
            }
        }
    }

    /// <summary>
    /// Devuelve el texto de un dato obligatorio, o null si falta o no es válido.
    /// Ausente es un pendiente; vacío es un error. Null y el tipo incorrecto ya los
    /// reportó la lectura.
    /// </summary>
    private static string? RequiredText(JsonObject owner, string ownerPath, string name, string needed, List<JsonMetadataDiagnostic> diagnostics)
    {
        var path = Child(ownerPath, name);
        if (!owner.ContainsKey(name))
        {
            diagnostics.Add(Pending(path, "DatoRequerido", $"Falta «{name}».", needed));
            return null;
        }
        if (AsText(owner[name]) is not { } text)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(text))
        {
            diagnostics.Add(Error(path, "ValorVacio", $"«{name}» está vacío.", needed));
            return null;
        }
        return text;
    }

    // Restricción de DesignerIA: Designer y Motor concatenan estos nombres en SQL y en el DOM sin escaparlos.
    private static bool CheckName(string value, int maximum, string path, string label, List<JsonMetadataDiagnostic> diagnostics)
    {
        if (value.Length <= maximum && IdentifierPattern().IsMatch(value))
        {
            return true;
        }
        diagnostics.Add(Error(path, "NombreNoAdmitido", $"{label} «{value}» no es admitido.",
            $"Letras ASCII, dígitos y guion bajo, hasta {maximum} caracteres."));
        return false;
    }

    [GeneratedRegex(@"\A[A-Za-z0-9_]+\z")]
    private static partial Regex IdentifierPattern();
}
