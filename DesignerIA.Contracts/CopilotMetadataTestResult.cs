namespace DesignerIA.Contracts;

public record CopilotMetadataTestResult(string Status, string? Response, bool ToolInvoked, int? ViewsCount, string? Error);
