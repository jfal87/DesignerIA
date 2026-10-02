namespace DesignerIA.Contracts;

/// <summary>Resultado read-only de comprobar la disponibilidad de un nombre.</summary>
public record ViewCreationPreflightResult(bool IsAvailable, string ViewName, string? ExistingViewId);
