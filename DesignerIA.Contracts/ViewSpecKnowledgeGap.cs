namespace DesignerIA.Contracts;

/// <summary>
/// Concepto del dominio para el que Copilot no encontró evidencia documental
/// suficiente al generar un <see cref="ViewSpec"/> (ver <see cref="ViewSpecResult"/>).
/// Estructuralmente separado de <see cref="ViewSpecValidationResult.Pending"/>:
/// Pending es un dato que el requerimiento del usuario simplemente no proporcionó
/// (por ejemplo un Stored Procedure); KnowledgeGap es una relación o configuración
/// del dominio que la documentación indexada no respalda. Sólo esta segunda
/// categoría habilita la acción "Enseñar a DesignerIA" en la UI.
/// </summary>
public record ViewSpecKnowledgeGap(string Topic, string Description);
