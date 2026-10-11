using System.Diagnostics;
using DesignerIA.Contracts;
using Microsoft.Data.SqlClient;

namespace DesignerIA.Api.Services.ViewScripts;

/// <summary>
/// Comprueba por lectura la disponibilidad del nombre y, para una grilla, el
/// alias, la conexión configurada y los metadatos del procedimiento y su primer
/// resultado. Devuelve rechazos y comprobaciones superadas; nunca ejecuta el SP.
/// </summary>
public sealed class ViewCreationPreflightService
{
    private readonly string _connectionString;
    private readonly IConfiguration _configuration;
    private readonly StringConnectionCatalogService _catalog;
    private readonly ILogger<ViewCreationPreflightService> _logger;

    public ViewCreationPreflightService(IConfiguration configuration,
        StringConnectionCatalogService catalog, ILogger<ViewCreationPreflightService> logger)
    {
        _connectionString = configuration.GetConnectionString("GestionEngineLocal")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión 'GestionEngineLocal'.");
        _configuration = configuration;
        _catalog = catalog;
        _logger = logger;
    }

    public async Task<ViewCreationPreflightResult> CheckNameAsync(string viewName, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP (1) IdVista FROM dbo.Vistas WHERE IdVista = @viewName OR Nombre = @viewName";
        command.Parameters.AddWithValue("@viewName", viewName);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        var existingViewId = result is null or DBNull ? null : Convert.ToString(result);
        _logger.LogInformation(
            "View name preflight completed in {ElapsedMs} ms. ViewName: {ViewName}. IsAvailable: {IsAvailable}",
            stopwatch.ElapsedMilliseconds, viewName, existingViewId is null);
        return FromExistingViewId(viewName, existingViewId);
    }

    public async Task<ViewCreationPreflightResult> CheckAsync(ViewSpec spec, CancellationToken cancellationToken = default)
    {
        var result = await CheckNameAsync(spec.Name!, cancellationToken);
        var area = spec.States![0].Rows?.FirstOrDefault()?.Areas?.FirstOrDefault();
        if (area is null)
        {
            return result;
        }
        var gaps = result.Gaps!.ToList();
        var completed = result.CompletedChecks!.ToList();
        var aliasRows = await _catalog.ReadByAliasAsync(area.ConnectionAlias!, cancellationToken);
        var aliasGap = FromAliasRows(aliasRows.Count, aliasRows.Count == 1 ? aliasRows[0].IsOracle : null);
        AddCheck(2, aliasGap, gaps, completed);
        var connectionKey = _configuration[$"ConnectionAliases:{area.ConnectionAlias}"];
        var spConnection = string.IsNullOrWhiteSpace(connectionKey) ? null : _configuration.GetConnectionString(connectionKey);
        var configurationGap = FromConfiguredConnection(!string.IsNullOrWhiteSpace(spConnection));
        AddCheck(3, configurationGap, gaps, completed);
        string? canonical = null;
        if (aliasGap is null && configurationGap is null)
        {
            ViewScriptGenerationValidator.TryParseStoredProcedure(area.StoredProcedure, out var schema, out var name);
            await using var connection = new SqlConnection(spConnection);
            await connection.OpenAsync(cancellationToken);
            int? objectId = null;
            string? catalogSchema = null;
            string? catalogName = null;
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT p.object_id, s.name, p.name FROM sys.procedures p INNER JOIN sys.schemas s ON s.schema_id = p.schema_id WHERE s.name = @schema AND p.name = @name AND p.type = @type";
                command.Parameters.AddWithValue("@schema", schema);
                command.Parameters.AddWithValue("@name", name);
                command.Parameters.AddWithValue("@type", "P");
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    objectId = reader.GetInt32(0);
                    catalogSchema = reader.GetString(1);
                    catalogName = reader.GetString(2);
                }
            }
            var procedureGap = FromProcedure(catalogSchema, catalogName, out canonical);
            AddCheck(4, procedureGap, gaps, completed);
            if (procedureGap is null && objectId.HasValue)
            {
                var parameters = new List<ViewProcedureParameter>();
                await using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT name, is_output FROM sys.parameters WHERE object_id = @objectId AND parameter_id > 0 ORDER BY parameter_id";
                    command.Parameters.AddWithValue("@objectId", objectId.Value);
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        parameters.Add(new(reader.GetString(0), reader.GetBoolean(1)));
                    }
                }
                AddCheck(5, FromParameters(canonical!, parameters), gaps, completed);
                var determinable = true;
                var visibleColumns = 0;
                try
                {
                    await using var command = connection.CreateCommand();
                    command.CommandText = "SELECT is_hidden, error_number FROM sys.dm_exec_describe_first_result_set_for_object(@objectId, @includeBrowseInformation)";
                    command.Parameters.AddWithValue("@objectId", objectId.Value);
                    command.Parameters.AddWithValue("@includeBrowseInformation", false);
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        if (!reader.IsDBNull(1) || reader.IsDBNull(0))
                        {
                            determinable = false;
                        }
                        else if (!reader.GetBoolean(0))
                        {
                            visibleColumns++;
                        }
                    }
                }
                catch (SqlException exception)
                {
                    determinable = false;
                    _logger.LogWarning("First result metadata could not be determined. SqlErrorNumber: {SqlErrorNumber}", exception.Number);
                }
                AddCheck(6, FromFirstResult(determinable, visibleColumns), gaps, completed);
            }
        }
        _logger.LogInformation("Grid preflight completed. CompletedChecks: {CompletedChecks}, GapCount: {GapCount}", completed.Count, gaps.Count);
        return result with { Gaps = gaps, CanonicalStoredProcedure = canonical, CompletedChecks = completed };
    }

    /// <summary>
    /// Convierte el dato leído para el nombre en disponibilidad, rechazo y
    /// comprobación superada. Se mantiene puro para verificar ambos resultados
    /// sin una conexión SQL; las comprobaciones opcionales de grilla se añaden aparte.
    /// </summary>
    public static ViewCreationPreflightResult FromExistingViewId(string viewName, string? existingViewId) => new(
        existingViewId is null, viewName, existingViewId,
        existingViewId is null ? [] : [Reject("Nombre de vista existente")],
        CompletedChecks: existingViewId is null ? [1] : []);

    public static ViewScriptGenerationGap? FromAliasRows(int rowCount, int? isOracle) =>
        rowCount == 0 ? Reject("Alias inexistente") :
        rowCount != 1 ? Reject("Alias ambiguo") :
        !StringConnectionCatalogService.IsSqlServer(isOracle) ? Reject("Alias no es SQL Server") : null;

    public static ViewScriptGenerationGap? FromConfiguredConnection(bool configured) =>
        configured ? null : Reject("Alias sin conexión configurada");

    public static ViewScriptGenerationGap? FromProcedure(string? schema, string? name, out string? canonical)
    {
        canonical = schema is null || name is null ? null : $"[{schema}].[{name}]";
        if (!ViewScriptGenerationValidator.TryParseStoredProcedure(canonical, out _, out _) || canonical!.Length > 250)
        {
            canonical = null;
            return Reject("SP inexistente o no visible para DesignerIA");
        }
        return null;
    }

    public static ViewScriptGenerationGap? FromParameters(string canonical, IReadOnlyList<ViewProcedureParameter> parameters) =>
        parameters.Count == 0 ||
        (canonical == "[dbo].[PRC_GET_GRID_TEST_SORT_COLUMN]" &&
            parameters.Count == 1 && parameters[0].Name == "@QueryString" && !parameters[0].IsOutput)
        ? null : Reject("SP con parámetros no admitidos por la plantilla");

    public static ViewScriptGenerationGap? FromFirstResult(bool determinable, int visibleColumns) =>
        !determinable ? Reject("Resultado no determinable") :
        visibleColumns < 2 ? Reject("Resultado incompatible") : null;

    private static ViewScriptGenerationGap Reject(string topic) => new(topic, topic switch
    {
        "Nombre de vista existente" => "Ya existe una vista con el nombre indicado; no se generó un script que pueda sobrescribirla.",
        "Alias inexistente" => "No se encontró el alias de conexión indicado. Revisa el alias o solicita que se registre antes de generar el script.",
        "Alias ambiguo" => "El alias indicado corresponde a más de una conexión. Solicita que el alias identifique una sola conexión antes de generar el script.",
        "Alias no es SQL Server" => "El alias indicado corresponde a una conexión Oracle. Selecciona un alias de SQL Server para generar el script.",
        "Alias sin conexión configurada" => "El alias indicado no tiene una conexión configurada para DesignerIA. Solicita su configuración antes de generar el script.",
        "SP inexistente o no visible para DesignerIA" => "No se encontró el procedimiento indicado o DesignerIA no puede verlo. Revisa su nombre y solicita que se compruebe el acceso antes de generar el script.",
        "SP con parámetros no admitidos por la plantilla" => "Los parámetros del procedimiento indicado no están admitidos por esta plantilla. Selecciona un procedimiento compatible con la plantilla antes de generar el script.",
        "Resultado incompatible" => "El primer resultado del procedimiento tiene menos de dos columnas visibles. Selecciona un procedimiento cuyo primer resultado tenga al menos dos columnas visibles.",
        "Resultado no determinable" => "No se pudo determinar la estructura del primer resultado del procedimiento. Solicita que se revise el procedimiento y el acceso a su información antes de generar el script.",
        _ => throw new ArgumentOutOfRangeException(nameof(topic))
    });

    private static void AddCheck(int check, ViewScriptGenerationGap? gap,
        List<ViewScriptGenerationGap> gaps, List<int> completed)
    {
        if (gap is null)
        {
            completed.Add(check);
        }
        else
        {
            gaps.Add(gap);
        }
    }
}

public record ViewProcedureParameter(string Name, bool IsOutput);
