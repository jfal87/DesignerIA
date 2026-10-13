namespace DesignerIA.Contracts;

/// <summary>
/// Texto de un documento JSONMetadata tal como se leyó del archivo o como lo
/// devolvió una preparación anterior. Viaja como texto para que la API haga la
/// lectura estricta y pueda señalar un JSON inválido con su ubicación.
/// </summary>
public record JsonMetadataRequest(string? Content);

public enum JsonMetadataSeverity
{
    /// <summary>El documento está mal: hay que corregirlo.</summary>
    Error,

    /// <summary>Falta un dato que alguien debe aportar.</summary>
    Pendiente,

    /// <summary>Es válido en el contrato, pero esta versión no lo materializa.</summary>
    NoSoportado
}

public enum JsonMetadataOrigin
{
    Recibido,
    Default,
    Derivado,
    Catalogo
}

/// <param name="Path">Ubicación en el documento, por ejemplo <c>states[0].rows[0].areas[0].object.data.connection</c>.</param>
/// <param name="Code">Identificador estable del diagnóstico.</param>
/// <param name="Cause">Por qué ocurre.</param>
/// <param name="Needed">El dato o la corrección necesarios.</param>
public record JsonMetadataDiagnostic(
    string Path,
    JsonMetadataSeverity Severity,
    string Code,
    string Cause,
    string Needed);

/// <summary>De dónde sale un valor de la definición efectiva o un dato físico derivado de ella.</summary>
public record JsonMetadataAnnotation(
    string Path,
    JsonMetadataOrigin Origin,
    string Note);

/// <summary>
/// Borrador efectivo de un documento JSONMetadata: el mismo documento con las
/// convenciones completadas, el origen de cada dato y lo que falta. No autoriza
/// la creación: la API vuelve a leer y validar el documento al crear.
/// </summary>
/// <param name="EffectiveDocument">Documento efectivo como texto JSON; null si el contenido no se pudo leer.</param>
/// <param name="CanCreate">Lo decide el backend: verdadero sólo si no hay diagnósticos.</param>
public record JsonMetadataDraft(
    string? ViewId,
    string? EffectiveDocument,
    IReadOnlyList<JsonMetadataAnnotation> Annotations,
    IReadOnlyList<JsonMetadataDiagnostic> Diagnostics,
    bool CanCreate,
    string? Script = null);
