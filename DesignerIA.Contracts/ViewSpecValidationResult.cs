namespace DesignerIA.Contracts;

/// <summary>
/// Resultado de validar determinísticamente un <see cref="ViewSpec"/> con
/// <see cref="ViewSpecValidator"/> (fuera del modelo). <paramref name="Errors"/>
/// son problemas estructurales que impiden considerar la vista válida (por
/// ejemplo un handler que apunta a un estado inexistente). <paramref name="Pending"/>
/// son datos conocidos como faltantes pero que no invalidan la estructura (por
/// ejemplo un Stored Procedure sin indicar).
/// </summary>
public record ViewSpecValidationResult(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Pending);
