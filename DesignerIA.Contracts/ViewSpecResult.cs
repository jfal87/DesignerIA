namespace DesignerIA.Contracts;

/// <summary>
/// Empaqueta el <see cref="ViewSpec"/> generado por Copilot junto con el resultado
/// de su validación determinística en C#, para transportarlos juntos dentro de
/// <see cref="ChatResult"/> cuando el chat detecta una intención de creación de vista.
/// <paramref name="KnowledgeGaps"/> está deliberadamente separado de
/// <see cref="ViewSpecValidationResult.Pending"/>: representa conceptos del dominio
/// para los que la documentación indexada no fue suficiente (habilita "Enseñar a
/// DesignerIA" en la UI), mientras que Pending son datos que el requerimiento del
/// usuario simplemente no proporcionó.
/// </summary>
public record ViewSpecResult(
    ViewSpec Spec,
    ViewSpecValidationResult Validation,
    IReadOnlyList<ViewSpecKnowledgeGap>? KnowledgeGaps = null);
