namespace DesignerIA.Contracts;

public record DatabaseHealthStatus(string Status, string Server, string Database, int ViewsCount);
