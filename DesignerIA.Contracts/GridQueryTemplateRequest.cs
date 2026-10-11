namespace DesignerIA.Contracts;

public record GridQueryTemplateRequest(
    string? Name,
    string? Description,
    string? StoredProcedure,
    string? ConnectionAlias);
