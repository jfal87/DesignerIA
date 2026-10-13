using System.Text.Json;
using System.Text.Json.Nodes;
using DesignerIA.Contracts;

namespace DesignerIA.Api.Services.ViewDocuments;

/// <summary>
/// Acceso tolerante al documento JSONMetadata y construcción de rutas y
/// diagnósticos. Un nodo del tipo equivocado se lee como ausente: el tipo
/// incorrecto ya lo reporta <see cref="JsonMetadataReader"/>.
/// </summary>
internal static class JsonMetadataNode
{
    public static string Child(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

    public static string Item(string path, int index) => $"{path}[{index}]";

    public static JsonObject? AsObject(JsonNode? node) => node as JsonObject;

    public static JsonArray? AsList(JsonNode? node) => node as JsonArray;

    public static string? AsText(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    public static bool? AsFlag(JsonNode? node) => node is JsonValue value
        ? value.GetValueKind() switch { JsonValueKind.True => true, JsonValueKind.False => false, _ => null }
        : null;

    public static JsonMetadataDiagnostic Error(string path, string code, string cause, string needed) =>
        new(path, JsonMetadataSeverity.Error, code, cause, needed);

    public static JsonMetadataDiagnostic Pending(string path, string code, string cause, string needed) =>
        new(path, JsonMetadataSeverity.Pendiente, code, cause, needed);

    public static JsonMetadataDiagnostic Unsupported(string path, string cause, string needed) =>
        new(path, JsonMetadataSeverity.NoSoportado, "CapacidadNoSoportada", cause, needed);
}
