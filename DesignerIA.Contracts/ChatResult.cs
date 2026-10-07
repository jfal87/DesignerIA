namespace DesignerIA.Contracts;

/// <summary>
/// <paramref name="Sources"/> contiene los documentos de KnowledgeSource realmente
/// utilizados por la tool "search_knowledge" durante esta respuesta. Es null cuando
/// esa tool no fue invocada o no encontró evidencia.
/// <paramref name="ViewSpecResult"/> sólo está presente cuando el mensaje del usuario
/// fue interpretado como una petición de creación/diseño de vista (ver
/// <c>CopilotChatService</c>); en cualquier otro caso permanece null y el chat se
/// selecciona conocimiento general, documentación o metadata según la capacidad requerida.
/// </summary>
public record ChatResult(
    string Status,
    string? Response,
    bool ToolInvoked,
    string? Error,
    IReadOnlyList<KnowledgeSourceReference>? Sources = null,
    ViewSpecResult? ViewSpecResult = null);
