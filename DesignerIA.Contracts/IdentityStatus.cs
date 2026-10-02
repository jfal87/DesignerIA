namespace DesignerIA.Contracts;

public record IdentityStatus(
    bool IsAuthenticated,
    string? Name,
    string? AuthenticationType);
