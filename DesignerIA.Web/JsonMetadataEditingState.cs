using System.Text.Json;
using System.Text.Json.Nodes;
using DesignerIA.Contracts;

namespace DesignerIA.Web;

/// <summary>
/// Editing state only. Keeps the whole JSON tree, including unsupported properties.
/// Domain validation and defaults remain in the API. A preparation belongs to one revision.
/// </summary>
public sealed class JsonMetadataEditingState
{
    private static readonly JsonSerializerOptions TextOptions = new() { WriteIndented = true };

    public string? Content { get; private set; }
    public JsonObject? Document { get; private set; }
    public string? ReadingError { get; private set; }
    public JsonMetadataDraft? Draft { get; private set; }
    public long Revision { get; private set; }
    private long? PreparedRevision { get; set; }

    public bool CanCreate => PreparedRevision == Revision && Document is not null
        && Draft is { CanCreate: true, EffectiveDocument: not null };

    // Presentation of view.js only. Its support and meaning are still checked by the API.
    public bool CanEditJavaScript => Document?["view"] is JsonObject view
        && (view["js"] is null || view["js"] is JsonValue value && value.TryGetValue<string>(out _));

    public string JavaScript => Document?["view"] is JsonObject view && view["js"] is JsonValue value
        && value.TryGetValue<string>(out var text) ? text : string.Empty;

    public void EditJavaScript(string code)
    {
        if (!CanEditJavaScript || Document?["view"] is not JsonObject view) { return; }
        if (code.Length == 0) { view.Remove("js"); }
        else { view["js"] = code; }
        CommitFields();
    }

    public void EditText(string? content)
    {
        Content = content;
        ReadTree();
        Invalidate();
    }

    public void CommitFields()
    {
        if (Document is null) { return; }
        Content = Document.ToJsonString(TextOptions);
        Invalidate();
    }

    public void DiscardPreparation()
    {
        PreparedRevision = null;
        Draft = null;
    }

    public bool ApplyPreparation(long revision, JsonMetadataDraft draft)
    {
        // Also protects against an earlier request returning after an edit or a new file.
        if (revision != Revision) { return false; }
        Draft = draft;
        if (draft.EffectiveDocument is { } effective)
        {
            Content = effective;
            ReadTree();
        }
        PreparedRevision = revision;
        return true;
    }

    private void Invalidate()
    {
        Revision++;
        DiscardPreparation();
    }

    private void ReadTree()
    {
        Document = null;
        ReadingError = null;
        if (Content is null) { return; }
        try
        {
            Document = JsonNode.Parse(Content, documentOptions: new JsonDocumentOptions
            {
                AllowDuplicateProperties = false
            }) as JsonObject;
            if (Document is null)
            {
                ReadingError = "La definición debe ser un objeto JSON. Puedes corregirla en la pestaña JSON.";
            }
        }
        catch (JsonException)
        {
            ReadingError = "El JSON no se puede leer o tiene propiedades repetidas. Corrígelo en la pestaña JSON.";
        }
    }
}
