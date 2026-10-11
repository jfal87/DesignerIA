using System.Diagnostics;
using DesignerIA.Contracts;
using Microsoft.Data.SqlClient;

namespace DesignerIA.Api.Services;

/// <summary>Controlled, read-only lookup of real handler examples.</summary>
public class HandlerExamplesService
{
    private const int MaximumResults = 3;
    private readonly string _connectionString;
    private readonly ILogger<HandlerExamplesService> _logger;

    public HandlerExamplesService(IConfiguration configuration, ILogger<HandlerExamplesService> logger)
    {
        _connectionString = configuration.GetConnectionString("GestionEngineLocal")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión 'GestionEngineLocal'.");
        _logger = logger;
    }

    public async Task<HandlerExamplesResult> FindAsync(string handler, int? maxResults, string? configurationMarker = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(handler)) throw new ArgumentException("Handler is required.", nameof(handler));
        var requestedHandler = handler.Trim();
        var limit = Math.Clamp(maxResults ?? MaximumResults, 1, MaximumResults);
        var stopwatch = Stopwatch.StartNew();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var action = await FindActionAsync(connection, requestedHandler, cancellationToken);
        if (action is null)
        {
            return new HandlerExamplesResult(requestedHandler, false, null, null, null, null, null, 0, 0, []);
        }

        var counts = await GetUsageCountsAsync(connection, action.IdActionHandler, cancellationToken);
        var examples = await GetExamplesAsync(connection, action, limit, configurationMarker, cancellationToken);
        stopwatch.Stop();
        _logger.LogInformation("Handler examples lookup completed in {ElapsedMs} ms. RequestedHandler: {RequestedHandler}, Action: {Action}, ExamplesCount: {ExamplesCount}", stopwatch.ElapsedMilliseconds, requestedHandler, action.Action, examples.Count);
        return new HandlerExamplesResult(requestedHandler, true, action.Action, action.Description, action.ParameterTemplate, action.IdActionHandler, action.IdTypeHandler, counts.TotalUsesCount, counts.ExcludedDesignerExamplesCount, examples);
    }

    private static async Task<ActionDefinition?> FindActionAsync(SqlConnection connection, string handler, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP (1) IdAccionHandler, Accion, Descripcion, ParametrosJson, IdTipoHandler FROM dbo.AccionesHandler WHERE UPPER(Accion) = UPPER(@handler) ORDER BY IdAccionHandler";
        command.Parameters.Add("@handler", System.Data.SqlDbType.NVarChar, 250).Value = handler;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new ActionDefinition(reader.GetInt32(0), reader.GetString(1), GetNullableString(reader, 2), GetNullableString(reader, 3), reader.IsDBNull(4) ? null : reader.GetInt32(4))
            : null;
    }

    private static async Task<UsageCounts> GetUsageCountsAsync(SqlConnection connection, int idActionHandler, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*), SUM(CASE WHEN v.IdVista LIKE N'%Designer[_]%' THEN 1 ELSE 0 END)
            FROM dbo.Handlers h JOIN dbo.ObjetosDeArea oa ON oa.IdObjetoDeArea=h.IdObjetoDeArea
            JOIN dbo.Areas a ON a.IdArea=oa.IdArea JOIN dbo.Filas f ON f.IdFila=a.IdFila
            JOIN dbo.Estados e ON e.IdEstado=f.IdEstado JOIN dbo.Vistas v ON v.IdVista=e.IdVista
            WHERE h.IdAccionHandler=@idActionHandler;
            """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add("@idActionHandler", System.Data.SqlDbType.Int).Value = idActionHandler;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new UsageCounts(reader.GetInt32(0), reader.IsDBNull(1) ? 0 : reader.GetInt32(1));
    }

    private static async Task<IReadOnlyList<HandlerExample>> GetExamplesAsync(SqlConnection connection, ActionDefinition action, int limit, string? configurationMarker, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (@maxResults) h.IdHandler,h.IdAccionHandler,h.IdTipoHandler,h.Parametros,h.IdsAplicarAccion,h.orden,
              oa.IdObjetoDeArea,oa.Nombre,a.IdArea,a.Nombre,f.IdFila,f.Nombre,e.IdEstado,e.Descripcion,v.IdVista,v.Nombre
            FROM dbo.Handlers h JOIN dbo.ObjetosDeArea oa ON oa.IdObjetoDeArea=h.IdObjetoDeArea
            JOIN dbo.Areas a ON a.IdArea=oa.IdArea JOIN dbo.Filas f ON f.IdFila=a.IdFila
            JOIN dbo.Estados e ON e.IdEstado=f.IdEstado JOIN dbo.Vistas v ON v.IdVista=e.IdVista
            WHERE h.IdAccionHandler=@idActionHandler AND v.IdVista NOT LIKE N'%Designer[_]%'
              AND (@configurationMarker IS NULL OR UPPER(h.Parametros) LIKE N'%' + UPPER(@configurationMarker) + N'%')
            ORDER BY h.IdHandler;
            """;
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.Add("@maxResults", System.Data.SqlDbType.Int).Value = limit;
        command.Parameters.Add("@idActionHandler", System.Data.SqlDbType.Int).Value = action.IdActionHandler;
        command.Parameters.Add("@configurationMarker", System.Data.SqlDbType.NVarChar, 50).Value = (object?)configurationMarker ?? DBNull.Value;
        var examples = new List<HandlerExample>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var parameters = GetNullableString(reader, 3);
            examples.Add(new HandlerExample(reader.GetInt32(0), reader.GetInt32(1), action.Action, action.Description, action.ParameterTemplate, reader.GetInt32(2), parameters, GetNullableString(reader, 4), reader.IsDBNull(5) ? null : reader.GetInt32(5), Contains(parameters, "MultiHandler"), Contains(parameters, "Conditional"), reader.GetInt32(6), GetNullableString(reader, 7), reader.GetInt32(8), GetNullableString(reader, 9), reader.GetInt32(10), GetNullableString(reader, 11), reader.GetInt32(12), GetNullableString(reader, 13), reader.GetString(14), GetNullableString(reader, 15)));
        }
        return examples;
    }

    private static bool Contains(string? value, string text) => value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;
    private static string? GetNullableString(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private sealed record ActionDefinition(int IdActionHandler, string Action, string? Description, string? ParameterTemplate, int? IdTypeHandler);
    private sealed record UsageCounts(int TotalUsesCount, int ExcludedDesignerExamplesCount);
}
