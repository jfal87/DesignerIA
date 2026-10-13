using System.Text.Json;
using System.Text.Json.Nodes;
using DesignerIA.Contracts;
using static DesignerIA.Api.Services.ViewDocuments.JsonMetadataNode;

namespace DesignerIA.Api.Services.ViewDocuments;

/// <summary>
/// Lectura estricta de un documento JSONMetadata 1.0. Comprueba la forma: JSON
/// válido sin propiedades repetidas, propiedades conocidas y tipos. No descarta
/// nada en silencio: una propiedad desconocida es un error, una propiedad del
/// contrato que esta versión no materializa es «no soportado», y un valor null
/// es un pendiente («no lo sé»), distinto de una propiedad ausente.
/// </summary>
public static class JsonMetadataReader
{
    public const int MaxContentLength = 256 * 1024;

    private enum Kind { Text, Flag, Object, List }

    private sealed record Shape(IReadOnlyDictionary<string, Kind> Supported, IReadOnlyList<string> Reserved);

    private static readonly Shape Root = new(
        new Dictionary<string, Kind> { ["schemaVersion"] = Kind.Text, ["view"] = Kind.Object, ["controls"] = Kind.List, ["states"] = Kind.List },
        ["leftMenu"]);

    private static readonly Shape View = new(
        new Dictionary<string, Kind> { ["id"] = Kind.Text, ["description"] = Kind.Text, ["type"] = Kind.Text, ["usesSecurity"] = Kind.Flag, ["js"] = Kind.Text },
        []);

    private static readonly Shape State = new(
        new Dictionary<string, Kind> { ["name"] = Kind.Text, ["isDefault"] = Kind.Flag, ["controls"] = Kind.List, ["rows"] = Kind.List },
        ["leftMenuItems", "popUp", "layout"]);

    private static readonly Shape Control = new(
        new Dictionary<string, Kind> { ["name"] = Kind.Text, ["type"] = Kind.Text, ["items"] = Kind.List, ["actions"] = Kind.List },
        ["title", "order", "wildcard", "data", "visibleIn", "autoHide", "enabled", "mode"]);

    private static readonly Shape ControlItem = new(
        new Dictionary<string, Kind> { ["text"] = Kind.Text, ["value"] = Kind.Text },
        []);

    private static readonly Shape ControlAction = new(
        new Dictionary<string, Kind> { ["name"] = Kind.Text, ["parameters"] = Kind.List },
        []);

    private static readonly Shape Row = new(
        new Dictionary<string, Kind> { ["areas"] = Kind.List },
        ["mode", "height"]);

    private static readonly Shape Area = new(
        new Dictionary<string, Kind> { ["name"] = Kind.Text, ["object"] = Kind.Object },
        ["width", "height", "marginLeft", "marginTop", "rowHeight", "buttons", "hashtag", "title", "help", "pivot"]);

    private static readonly Shape AreaObject = new(
        new Dictionary<string, Kind> { ["type"] = Kind.Text, ["data"] = Kind.Object },
        ["settings", "handlers", "gridButtons", "axes", "series"]);

    private static readonly Shape Data = new(
        new Dictionary<string, Kind> { ["connection"] = Kind.Text, ["storedProcedure"] = Kind.Text, ["parameters"] = Kind.List },
        []);

    private static readonly Shape Parameter = new(
        new Dictionary<string, Kind> { ["name"] = Kind.Text, ["source"] = Kind.Object, ["default"] = Kind.Text },
        ["myVision"]);

    private static readonly Shape Source = new(
        new Dictionary<string, Kind> { ["kind"] = Kind.Text, ["name"] = Kind.Text },
        []);

    /// <summary>
    /// Devuelve el documento como árbol JSON, o null si el contenido no se puede
    /// leer. Los problemas de forma se agregan a <paramref name="diagnostics"/>.
    /// </summary>
    public static JsonObject? Read(string? content, List<JsonMetadataDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            diagnostics.Add(Error("", "DocumentoVacio", "El archivo está vacío.", "Un documento JSONMetadata con schemaVersion y view.id."));
            return null;
        }
        if (content.Length > MaxContentLength)
        {
            diagnostics.Add(Error("", "DocumentoDemasiadoGrande",
                $"El documento tiene {content.Length} caracteres y el máximo admitido es {MaxContentLength}.", "Un documento más pequeño."));
            return null;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(content, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false });
        }
        catch (JsonException exception)
        {
            diagnostics.Add(Error("", "JsonInvalido",
                $"El contenido no es JSON válido o repite una propiedad (línea {exception.LineNumber + 1}, posición {exception.BytePositionInLine + 1}).",
                "Un documento JSON válido, sin comentarios, comas finales ni propiedades repetidas."));
            return null;
        }

        if (node is not JsonObject root)
        {
            diagnostics.Add(Error("", "RaizNoEsObjeto", "El documento debe ser un objeto JSON.", "Un objeto con schemaVersion y view."));
            return null;
        }

        CheckDocument(root, diagnostics);
        return root;
    }

    private static void CheckDocument(JsonObject root, List<JsonMetadataDiagnostic> diagnostics)
    {
        Check(root, "", Root, diagnostics);
        if (AsObject(root["view"]) is { } view)
        {
            Check(view, "view", View, diagnostics);
        }
        ForEachObject(root["controls"], "controls", diagnostics, (control, controlPath) =>
        {
            Check(control, controlPath, Control, diagnostics);
            ForEachObject(control["items"], Child(controlPath, "items"), diagnostics,
                (item, itemPath) => Check(item, itemPath, ControlItem, diagnostics));
            ForEachObject(control["actions"], Child(controlPath, "actions"), diagnostics, (action, actionPath) =>
            {
                Check(action, actionPath, ControlAction, diagnostics);
                CheckTexts(action["parameters"], Child(actionPath, "parameters"), diagnostics);
            });
        });
        ForEachObject(root["states"], "states", diagnostics, (state, statePath) =>
        {
            Check(state, statePath, State, diagnostics);
            CheckTexts(state["controls"], Child(statePath, "controls"), diagnostics);
            ForEachObject(state["rows"], Child(statePath, "rows"), diagnostics, (row, rowPath) =>
            {
                Check(row, rowPath, Row, diagnostics);
                ForEachObject(row["areas"], Child(rowPath, "areas"), diagnostics, (area, areaPath) =>
                {
                    Check(area, areaPath, Area, diagnostics);
                    CheckAreaObject(AsObject(area["object"]), Child(areaPath, "object"), diagnostics);
                });
            });
        });
    }

    private static void CheckAreaObject(JsonObject? areaObject, string path, List<JsonMetadataDiagnostic> diagnostics)
    {
        if (areaObject is null)
        {
            return;
        }
        Check(areaObject, path, AreaObject, diagnostics);
        if (AsObject(areaObject["data"]) is not { } data)
        {
            return;
        }
        var dataPath = Child(path, "data");
        Check(data, dataPath, Data, diagnostics);
        ForEachObject(data["parameters"], Child(dataPath, "parameters"), diagnostics, (parameter, parameterPath) =>
        {
            Check(parameter, parameterPath, Parameter, diagnostics);
            if (AsObject(parameter["source"]) is { } source)
            {
                Check(source, Child(parameterPath, "source"), Source, diagnostics);
            }
        });
    }

    private static void Check(JsonObject node, string path, Shape shape, List<JsonMetadataDiagnostic> diagnostics)
    {
        foreach (var (name, value) in node)
        {
            var propertyPath = Child(path, name);
            if (shape.Reserved.Contains(name))
            {
                AddUnsupported(value, propertyPath, name, diagnostics);
            }
            else if (!shape.Supported.TryGetValue(name, out var kind))
            {
                diagnostics.Add(Error(propertyPath, "PropiedadDesconocida",
                    $"«{name}» no existe en el contrato {JsonMetadataExpander.SchemaVersion}.", "Quita la propiedad o corrige su nombre."));
            }
            else if (value is null)
            {
                diagnostics.Add(Pending(propertyPath, "ValorDesconocido",
                    $"«{name}» es null, que significa «no lo sé». No se completa con una convención ni representa un NULL de SQL.",
                    "Indica el valor, u omite la propiedad si quieres la convención de esta versión."));
            }
            else if (!Matches(value, kind))
            {
                diagnostics.Add(Error(propertyPath, "TipoIncorrecto", $"«{name}» debe ser {Describe(kind)}.", $"Un valor que sea {Describe(kind)}."));
            }
        }
    }

    // Una capacidad declarada se rechaza con su ubicación; si es una lista, elemento por elemento.
    private static void AddUnsupported(JsonNode? value, string path, string name, List<JsonMetadataDiagnostic> diagnostics)
    {
        const string cause = "es parte del contrato, pero esta versión no lo materializa.";
        const string needed = "Quítalo del documento: no se crea una vista parcial.";
        if (value is JsonArray { Count: > 0 } list)
        {
            for (var index = 0; index < list.Count; index++)
            {
                diagnostics.Add(Unsupported(Item(path, index), $"«{name}» {cause}", needed));
            }
            return;
        }
        diagnostics.Add(Unsupported(path, $"«{name}» {cause}", needed));
    }

    private static void ForEachObject(JsonNode? node, string path, List<JsonMetadataDiagnostic> diagnostics, Action<JsonObject, string> visit)
    {
        if (AsList(node) is not { } list)
        {
            return;
        }
        for (var index = 0; index < list.Count; index++)
        {
            var itemPath = Item(path, index);
            if (list[index] is JsonObject item)
            {
                visit(item, itemPath);
            }
            else
            {
                diagnostics.Add(Error(itemPath, "TipoIncorrecto", "Cada elemento de la lista debe ser un objeto.", "Un objeto, o quita el elemento."));
            }
        }
    }

    private static void CheckTexts(JsonNode? node, string path, List<JsonMetadataDiagnostic> diagnostics)
    {
        if (AsList(node) is not { } list)
        {
            return;
        }
        for (var index = 0; index < list.Count; index++)
        {
            if (AsText(list[index]) is null)
            {
                diagnostics.Add(Error(Item(path, index), "TipoIncorrecto", "Cada elemento de la lista debe ser un texto.", "Un texto, o quita el elemento."));
            }
        }
    }

    private static bool Matches(JsonNode value, Kind kind) => kind switch
    {
        Kind.Text => value.GetValueKind() == JsonValueKind.String,
        Kind.Flag => value.GetValueKind() is JsonValueKind.True or JsonValueKind.False,
        Kind.Object => value is JsonObject,
        _ => value is JsonArray
    };

    private static string Describe(Kind kind) => kind switch
    {
        Kind.Text => "un texto",
        Kind.Flag => "true o false",
        Kind.Object => "un objeto",
        _ => "una lista"
    };
}
