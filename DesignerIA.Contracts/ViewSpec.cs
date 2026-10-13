namespace DesignerIA.Contracts;

/// <summary>
/// Estructura tipada de una vista de GestionEngine, generada por Copilot a partir de
/// un requerimiento en lenguaje natural. Todos los campos son deliberadamente
/// nullable: <c>null</c> significa "no determinado todavía", nunca un valor
/// inventado. La validación estructural y la detección de pendientes ocurren en
/// <see cref="ViewSpecValidator"/>, fuera del modelo (ver AGENTS.md).
/// </summary>
/// <param name="Name">Nombre técnico de la vista. Evidencia real (EngineUpdater.cs,
/// GetViewInsertQry) confirma que dbo.Vistas.Nombre y dbo.Vistas.IdVista usan
/// siempre el mismo valor (this.ID); no hace falta un campo separado para IdVista.</param>
/// <param name="Description">Descripción libre de la vista (dbo.Vistas.Descripcion).
/// Campo distinto de <see cref="Name"/>, confirmado por evidencia real
/// (AppPrecios.sql, vPreciosSemanales_CDA.sql, vpruebaJFAL.sql: Nombre y
/// Descripcion siempre difieren). Para la vista mínima, si no se provee,
/// ViewSpecNormalizer la deriva determinísticamente del nombre.</param>
public record ViewSpec(
    string? Name,
    string? Description,
    string? InitialState,
    IReadOnlyList<ViewSpecState>? States,
    string? ViewType = null);

/// <param name="Name">Nombre/clave del estado. Evidencia real (EngineUpdater.cs,
/// CreateStates) confirma que dbo.Estados.Descripcion reutiliza siempre este mismo
/// valor (no existe una Descripcion de Estado distinta de su nombre); por eso no
/// se agrega un campo Description separado a nivel de Estado.</param>
public record ViewSpecState(
    string? Name,
    IReadOnlyList<ViewSpecRow>? Rows);

public record ViewSpecRow(
    IReadOnlyList<ViewSpecArea>? Areas,
    string? Name = null);

/// <param name="StoredProcedure">Nombre del Stored Procedure real de SQL Server
/// que alimenta esta área.</param>
/// <param name="ConnectionAlias">Alias lógico de la conexión contra la que
/// ejecuta el Stored Procedure (dbo.StringsConnection.StringConnection en
/// GestionEngine, por ejemplo "GestionEngine", "DW_TERNIUM"). NO es el ID
/// numérico físico (IdStringConnection) ni la cadena de conexión física: esas
/// pertenecen al ambiente/Motor, no al ViewSpec (ver AGENTS.md, sección 5.4.3).</param>
/// <param name="Parameters">Parámetros del Stored Procedure con valor fijo. Nulo o
/// vacío significa que la vista no declara parámetros. Si se informa, debe cubrir
/// todos los parámetros de entrada del procedimiento: es una decisión de alcance
/// de DesignerIA, no una regla de Motor.</param>
public record ViewSpecArea(
    string? Type,
    string? StoredProcedure,
    string? ConnectionAlias,
    IReadOnlyList<ViewSpecHandler>? Handlers,
    string? Name = null,
    string? ObjectName = null,
    string? ConfigurationPreset = null,
    IReadOnlyList<ViewSpecParameter>? Parameters = null);

/// <summary>
/// Parámetro del Stored Procedure con un valor fijo: un valor configurado sin
/// fuente asociada (dbo.MapeoParametros.ValorDefault con Variable vacía). No
/// describe tipos SQL ni IDs físicos, que se resuelven en la base destino.
/// </summary>
/// <param name="Name">Nombre del parámetro, con o sin «@» inicial.</param>
/// <param name="DefaultValue">Texto del valor, tal como se capturó. Nulo significa
/// «sin valor informado»; no representa un NULL de SQL.</param>
public record ViewSpecParameter(
    string? Name,
    string? DefaultValue);

public record ViewSpecHandler(
    string? Type,
    string? TargetState);
