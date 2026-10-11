namespace DesignerIA.Contracts;

public record HandlerExamplesResult(
    string RequestedHandler,
    bool ActionFound,
    string? Action,
    string? Description,
    string? ParameterTemplate,
    int? IdActionHandler,
    int? IdTypeHandler,
    int TotalUsesCount,
    int ExcludedDesignerExamplesCount,
    IReadOnlyList<HandlerExample> Examples);

public record HandlerExample(
    int IdHandler,
    int IdActionHandler,
    string Action,
    string? Description,
    string? ParameterTemplate,
    int IdTypeHandler,
    string? Parameters,
    string? IdsApplyAction,
    int? Order,
    bool UsesMultiHandler,
    bool UsesConditional,
    int IdAreaObject,
    string? AreaObject,
    int IdArea,
    string? Area,
    int IdRow,
    string? Row,
    int IdState,
    string? State,
    string IdView,
    string? ViewName);
