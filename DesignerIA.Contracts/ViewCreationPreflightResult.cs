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
/// <param name="Parameters">Parámetros con valor fijo resueltos contra el procedimiento, en su orden
/// de declaración. Null o vacío cuando el ViewSpec no declara parámetros o no se resolvieron.</param>
/// <param name="DeclaredParameters">Firma leída del catálogo del procedimiento, en su orden de
/// declaración. Null cuando no se llegó a leer.</param>
public record ViewCreationPreflightResult(
    bool IsAvailable,
    string ViewName,
    string? ExistingViewId,
    IReadOnlyList<ViewScriptGenerationGap>? Gaps = null,
    string? CanonicalStoredProcedure = null,
    IReadOnlyList<int>? CompletedChecks = null,
    IReadOnlyList<ViewCreationPreflightParameter>? Parameters = null,
    IReadOnlyList<ViewCreationPreflightDeclaredParameter>? DeclaredParameters = null);

/// <summary>
/// Parámetro tal como lo declara el procedimiento en el catálogo, con «@». Permite
/// indicar qué valores faltan sin inventarlos. Null en el resultado si la firma no se leyó.
/// </summary>
public record ViewCreationPreflightDeclaredParameter(
    string Name,
    string? Type,
    bool IsOutput);

/// <summary>
/// Parámetro con valor fijo resuelto por el preflight: nombre tal como figura en el
/// catálogo del procedimiento, sin «@»; tipo base de SQL Server; y el valor validado.
/// </summary>
public record ViewCreationPreflightParameter(
    string Name,
    string Type,
    string DefaultValue);
