namespace DesignerIA.Contracts;

/// <summary>
/// Documento JSONMetadata, versión 1.0: la definición lógica de una vista nueva.
/// El documento breve que escribe el usuario y su definición efectiva (con las
/// convenciones ya completadas) tienen esta misma forma. No lleva IDs físicos,
/// servidores ni cadenas de conexión: las conexiones se nombran por alias.
/// La lectura estricta, la expansión y la validación viven en la API; estos tipos
/// sólo describen la forma. Las colecciones se conservan aunque esta versión
/// materialice una fila y un área por estado.
/// Los web controls se definen una vez, en la vista; cada estado referencia por
/// nombre los que tiene activos. Dónde se guardan físicamente lo resuelve la API.
/// </summary>
/// <param name="SchemaVersion">Versión del formato. Fija el significado de las omisiones.</param>
/// <param name="Controls">Web controls de la vista.</param>
public record JsonMetadataDocument(
    string? SchemaVersion,
    JsonMetadataView? View,
    IReadOnlyList<JsonMetadataState>? States = null,
    IReadOnlyList<JsonMetadataControl>? Controls = null);

/// <param name="Id">Identificador de la vista nueva (dbo.Vistas.IdVista y Nombre).</param>
public record JsonMetadataView(
    string? Id,
    string? Description = null,
    string? Type = null,
    bool? UsesSecurity = null,
    string? Js = null);

/// <param name="IsDefault">Marca el estado inicial; no depende de la posición en la lista.</param>
/// <param name="Controls">Nombres de los web controls de la vista activos en este estado.</param>
public record JsonMetadataState(
    string? Name = null,
    bool? IsDefault = null,
    IReadOnlyList<JsonMetadataRow>? Rows = null,
    IReadOnlyList<string>? Controls = null);

public record JsonMetadataRow(
    IReadOnlyList<JsonMetadataArea>? Areas = null);

public record JsonMetadataArea(
    string? Name = null,
    JsonMetadataAreaObject? Object = null);

public record JsonMetadataAreaObject(
    string? Type = null,
    JsonMetadataData? Data = null);

/// <param name="Connection">Alias lógico de conexión (dbo.StringsConnection.StringConnection).</param>
/// <param name="StoredProcedure">Procedimiento existente, como esquema.nombre.</param>
public record JsonMetadataData(
    string? Connection = null,
    string? StoredProcedure = null,
    IReadOnlyList<JsonMetadataParameter>? Parameters = null);

/// <param name="Default">Valor del parámetro. No representa un NULL de SQL.</param>
public record JsonMetadataParameter(
    string? Name = null,
    JsonMetadataSource? Source = null,
    string? Default = null);

public record JsonMetadataSource(
    string? Kind = null,
    string? Name = null);

/// <summary>Web control de la vista con opciones fijas y acciones.</summary>
/// <param name="Type">Tipo de control; esta versión materializa "buttonList".</param>
public record JsonMetadataControl(
    string? Name = null,
    string? Type = null,
    IReadOnlyList<JsonMetadataControlItem>? Items = null,
    IReadOnlyList<JsonMetadataControlAction>? Actions = null);

public record JsonMetadataControlItem(
    string? Text = null,
    string? Value = null);

/// <param name="Name">Acción; esta versión materializa "cambiarEstado".</param>
/// <param name="Parameters">Para "cambiarEstado", el nombre del estado destino.</param>
public record JsonMetadataControlAction(
    string? Name = null,
    IReadOnlyList<string>? Parameters = null);
