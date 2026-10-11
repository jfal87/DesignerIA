namespace DesignerIA.Contracts;

/// <summary>
/// Resultado de las comprobaciones de lectura previas a generar un script.
/// La disponibilidad del nombre por sí sola no acredita el preflight de una grilla.
/// </summary>
/// <param name="IsAvailable">Indica si el nombre de vista está disponible.</param>
/// <param name="ViewName">Nombre de la vista comprobada.</param>
/// <param name="ExistingViewId">Identificador de la vista encontrada, o null si no existe.</param>
/// <param name="Gaps">Rechazos concretos; null indica que no se proporcionó esta información.</param>
/// <param name="CanonicalStoredProcedure">Nombre [esquema].[nombre] del procedimiento encontrado, o null si no se resolvió.</param>
/// <param name="CompletedChecks">Comprobaciones superadas: 1 nombre disponible, 2 alias único de SQL Server,
/// 3 conexión configurada, 4 procedimiento visible, 5 parámetros admitidos y 6 primer resultado compatible.
/// Null indica que no se proporcionó esta información.</param>
public record ViewCreationPreflightResult(
    bool IsAvailable,
    string ViewName,
    string? ExistingViewId,
    IReadOnlyList<ViewScriptGenerationGap>? Gaps = null,
    string? CanonicalStoredProcedure = null,
    IReadOnlyList<int>? CompletedChecks = null);
