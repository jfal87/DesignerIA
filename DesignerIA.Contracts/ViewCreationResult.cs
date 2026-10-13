namespace DesignerIA.Contracts;

/// <summary>
/// Estado real de una solicitud de creación de vista. No equivale al código HTTP
/// ni a la ausencia de excepción: <see cref="Created"/> exige que el script haya
/// terminado sin error y que la verificación posterior confirme la vista.
/// </summary>
public enum ViewCreationStatus
{
    /// <summary>El script terminó sin error y la verificación confirmó la vista y sus registros.</summary>
    Created,

    /// <summary>No se ejecutó nada: el ViewSpec o el preflight no permiten generar el script.</summary>
    Rejected,

    /// <summary>No se ejecutó nada: la creación no está habilitada, su configuración no es válida o el usuario no está autorizado.</summary>
    NotAllowed,

    /// <summary>No se ejecutó nada: esta instancia ya está procesando otra creación.</summary>
    Busy,

    /// <summary>El script se ejecutó, falló y revirtió sus cambios.</summary>
    NotCreated,

    /// <summary>No se pudo confirmar el resultado; <see cref="ViewCreationResult.Diagnostics"/> conserva lo observado.</summary>
    Indeterminate
}

/// <summary>
/// Resultado de crear una vista a partir de un <see cref="ViewSpec"/>. El SQL se
/// genera siempre en el servidor; este contrato nunca transporta SQL.
/// </summary>
/// <param name="Status">Estado real de la solicitud.</param>
/// <param name="ViewId">IdVista creado; sólo se informa cuando <paramref name="Status"/> es <see cref="ViewCreationStatus.Created"/>.</param>
/// <param name="Message">Mensaje para el usuario.</param>
/// <param name="Gaps">Rechazos del ViewSpec o del preflight cuando no se llegó a ejecutar.</param>
/// <param name="Diagnostics">Lo observado durante la ejecución y la verificación.</param>
/// <param name="ViewUrl">URL de la vista en Motor; sólo se informa cuando <paramref name="Status"/> es
/// <see cref="ViewCreationStatus.Created"/> y el ambiente tiene una URL base de Motor configurada.</param>
public record ViewCreationResult(
    ViewCreationStatus Status,
    string? ViewId,
    string Message,
    IReadOnlyList<ViewScriptGenerationGap> Gaps,
    IReadOnlyList<string> Diagnostics,
    string? ViewUrl = null,
    bool ViewUrlIsMock = false);
