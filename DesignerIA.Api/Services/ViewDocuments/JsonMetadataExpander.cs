using System.Text.Json.Nodes;
using DesignerIA.Api.Services.ViewSpecs;
using DesignerIA.Contracts;
using static DesignerIA.Api.Services.ViewDocuments.JsonMetadataNode;

namespace DesignerIA.Api.Services.ViewDocuments;

/// <summary>
/// Completa las convenciones de la versión 1.0 sobre el propio documento: la
/// definición efectiva es el mismo contrato con las omisiones resueltas.
/// Sólo escribe propiedades ausentes que tienen una regla; nunca toca un valor
/// explícito, ni siquiera null. Es determinística e idempotente: expandir un
/// documento ya expandido no lo cambia. La única normalización es el orden de las
/// propiedades, que no altera el significado.
/// Las convenciones pertenecen a la versión del formato: cambiar una exige una
/// versión nueva, no una edición de estas constantes.
/// </summary>
public static class JsonMetadataExpander
{
    public const string SchemaVersion = "1.0";
    public const string DefaultViewType = "site";
    public const string DefaultAreaName = "Area01";
    public const string FixedSourceKind = "fixed";

    public static void Expand(JsonObject root, List<JsonMetadataAnnotation> annotations)
    {
        CollectReceived(root, "", annotations);
        CompleteDocument(root, annotations);
        OrderDocument(root);
    }

    private static void CompleteDocument(JsonObject root, List<JsonMetadataAnnotation> annotations)
    {
        ExpandView(AsObject(root["view"]), annotations);

        if (!root.ContainsKey("states"))
        {
            root["states"] = new JsonArray(new JsonObject
            {
                ["name"] = ViewSpecNormalizer.DefaultStateName,
                ["isDefault"] = true,
                ["rows"] = new JsonArray()
            });
            annotations.Add(new("states", JsonMetadataOrigin.Derivado,
                $"Vista vacía: un estado inicial «{ViewSpecNormalizer.DefaultStateName}» sin filas."));
            return;
        }
        if (AsList(root["states"]) is not { } states)
        {
            return;
        }

        var areas = new List<(JsonObject Area, string Path)>();
        for (var stateIndex = 0; stateIndex < states.Count; stateIndex++)
        {
            if (AsObject(states[stateIndex]) is not { } state)
            {
                continue;
            }
            var statePath = Item("states", stateIndex);
            if (states.Count == 1)
            {
                Complete(state, statePath, "name", ViewSpecNormalizer.DefaultStateName, "Nombre por convención del único estado.", annotations);
                Complete(state, statePath, "isDefault", true, "El único estado es el inicial.", annotations);
            }
            Complete(state, statePath, "rows", new JsonArray(), "Estado sin contenido.", annotations);
            CollectAreas(state, statePath, areas);
        }

        if (areas.Count == 1)
        {
            Complete(areas[0].Area, areas[0].Path, "name", DefaultAreaName, "Nombre por convención de la única área.", annotations);
        }
        foreach (var (area, areaPath) in areas)
        {
            ExpandData(AsObject(AsObject(area["object"])?["data"]), Child(Child(areaPath, "object"), "data"), annotations);
        }
    }

    private static void ExpandView(JsonObject? view, List<JsonMetadataAnnotation> annotations)
    {
        if (view is null)
        {
            return;
        }
        if (AsText(view["id"]) is { Length: > 0 } id)
        {
            Complete(view, "view", "description", id, "Igual al identificador de la vista.", annotations);
        }
        Complete(view, "view", "type", DefaultViewType, "Tipo por defecto de una vista nueva.", annotations);
        Complete(view, "view", "usesSecurity", false, "Sin seguridad.", annotations);
    }

    private static void ExpandData(JsonObject? data, string dataPath, List<JsonMetadataAnnotation> annotations)
    {
        if (data is null)
        {
            return;
        }
        Complete(data, dataPath, "parameters", new JsonArray(), "No se declaran parámetros.", annotations);
        if (AsList(data["parameters"]) is not { } parameters)
        {
            return;
        }
        for (var index = 0; index < parameters.Count; index++)
        {
            if (AsObject(parameters[index]) is { } parameter)
            {
                Complete(parameter, Item(Child(dataPath, "parameters"), index), "source",
                    new JsonObject { ["kind"] = FixedSourceKind }, "Valor fijo, sin fuente.", annotations);
            }
        }
    }

    private static void CollectAreas(JsonObject state, string statePath, List<(JsonObject Area, string Path)> areas)
    {
        if (AsList(state["rows"]) is not { } rows)
        {
            return;
        }
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            if (AsList(AsObject(rows[rowIndex])?["areas"]) is not { } rowAreas)
            {
                continue;
            }
            var areasPath = Child(Item(Child(statePath, "rows"), rowIndex), "areas");
            for (var areaIndex = 0; areaIndex < rowAreas.Count; areaIndex++)
            {
                if (AsObject(rowAreas[areaIndex]) is { } area)
                {
                    areas.Add((area, Item(areasPath, areaIndex)));
                }
            }
        }
    }

    // Ausente significa «aplica la convención». Presente, aunque sea null, no se toca.
    private static void Complete(JsonObject owner, string ownerPath, string name, JsonNode value, string note,
        List<JsonMetadataAnnotation> annotations)
    {
        if (owner.ContainsKey(name))
        {
            return;
        }
        owner[name] = value;
        annotations.Add(new(Child(ownerPath, name), JsonMetadataOrigin.Default, note));
    }

    // El orden de las propiedades no cambia el significado del documento. Se normaliza para
    // que la definición efectiva se lea siempre igual, con lo completado en su sitio.
    private static void OrderDocument(JsonObject root)
    {
        Order(root, "schemaVersion", "view", "controls", "states");
        Order(AsObject(root["view"]), "id", "description", "type", "usesSecurity", "js");
        foreach (var control in Objects(root["controls"]))
        {
            Order(control, "name", "type", "items", "actions");
        }
        foreach (var state in Objects(root["states"]))
        {
            Order(state, "name", "isDefault", "controls", "rows");
            foreach (var area in Objects(state["rows"]).SelectMany(row => Objects(row["areas"])))
            {
                Order(area, "name", "object");
                var areaObject = AsObject(area["object"]);
                Order(areaObject, "type", "data");
                var data = AsObject(areaObject?["data"]);
                Order(data, "connection", "storedProcedure", "parameters");
                foreach (var parameter in Objects(data?["parameters"]))
                {
                    Order(parameter, "name", "source", "default");
                }
            }
        }
    }

    private static IEnumerable<JsonObject> Objects(JsonNode? list) => AsList(list)?.OfType<JsonObject>() ?? [];

    private static void Order(JsonObject? item, params string[] first)
    {
        if (item is null)
        {
            return;
        }
        var properties = item.ToList();
        var current = properties.Select(property => property.Key).ToList();
        var ordered = first.Where(current.Contains).Concat(current.Where(name => !first.Contains(name))).ToList();
        if (ordered.SequenceEqual(current))
        {
            return;
        }
        item.Clear();
        foreach (var name in ordered)
        {
            item[name] = properties.First(property => property.Key == name).Value;
        }
    }

    private static void CollectReceived(JsonNode? node, string path, List<JsonMetadataAnnotation> annotations)
    {
        switch (node)
        {
            case JsonObject item:
                foreach (var (name, value) in item)
                {
                    CollectReceived(value, Child(path, name), annotations);
                }
                break;
            case JsonArray list:
                for (var index = 0; index < list.Count; index++)
                {
                    CollectReceived(list[index], Item(path, index), annotations);
                }
                break;
            case JsonValue:
                annotations.Add(new(path, JsonMetadataOrigin.Recibido, "Escrito en el documento."));
                break;
        }
    }
}
