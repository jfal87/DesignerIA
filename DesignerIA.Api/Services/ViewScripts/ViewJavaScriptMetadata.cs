namespace DesignerIA.Api.Services.ViewScripts;

/// <summary>
/// Storage format used by Designer for Vistas.JsCode. Motor reverses these
/// three literal markers before registering the inline script. This is not
/// JSON encoding or SQL escaping; the generator still uses SqlLiteralEscaper.
/// </summary>
public static class ViewJavaScriptMetadata
{
    public static string Encode(string? code) => (code ?? string.Empty)
        .Replace("\"", @"\u0022", StringComparison.Ordinal)
        .Replace("'", @"\u0027", StringComparison.Ordinal)
        .Replace("null", @"\u0000", StringComparison.Ordinal);
}
