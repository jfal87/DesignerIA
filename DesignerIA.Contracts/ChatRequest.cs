namespace DesignerIA.Contracts;

public record ChatRequest(string Message, string? ConversationId = null);
