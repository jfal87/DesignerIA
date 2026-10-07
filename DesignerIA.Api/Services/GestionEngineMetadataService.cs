using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace DesignerIA.Api.Services;

/// <summary>Small allowlisted, read-only catalogue lookup for GestionEngine handler metadata.</summary>
public sealed class GestionEngineMetadataService
{
    private const int MaximumListSize = 50;
    private readonly string _connectionString;
    private readonly ILogger<GestionEngineMetadataService> _logger;

    public GestionEngineMetadataService(IConfiguration configuration, ILogger<GestionEngineMetadataService> logger)
    {
        _connectionString = configuration.GetConnectionString("GestionEngineLocal")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión 'GestionEngineLocal'.");
        _logger = logger;
    }

    public async Task<GestionEngineMetadataResult> QueryAsync(
        string resource,
        string operation,
        string? search,
        CancellationToken cancellationToken)
    {
        if (!IsSupported(resource, operation))
        {
            throw new ArgumentException("Unsupported GestionEngine metadata request.");
        }

        var stopwatch = Stopwatch.StartNew();
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        if (operation == "Count")
        {
            await using var command = connection.CreateCommand();
            command.CommandText = resource == "HandlerActions"
                ? "SELECT COUNT(*) FROM dbo.AccionesHandler;"
                : "SELECT COUNT(*) FROM dbo.Handlers;";
            var count = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
            stopwatch.Stop();
            _logger.LogInformation("GestionEngine metadata count completed in {ElapsedMs} ms. Resource: {Resource}, Count: {Count}", stopwatch.ElapsedMilliseconds, resource, count);
            return new GestionEngineMetadataResult(resource, operation, search, count, []);
        }

        await using var listCommand = connection.CreateCommand();
        listCommand.Parameters.Add("@limit", System.Data.SqlDbType.Int).Value = MaximumListSize;
        listCommand.Parameters.Add("@search", System.Data.SqlDbType.NVarChar, 250).Value = (object?)search?.Trim() ?? DBNull.Value;
        var rankActionNames = resource == "HandlerActions" && operation == "Search";
        listCommand.CommandText = rankActionNames
            ? "SELECT IdAccionHandler, Accion, Descripcion, ParametrosJson, IdTipoHandler FROM dbo.AccionesHandler ORDER BY Accion, IdAccionHandler;"
            : BuildListSql(resource, operation == "Search");

        var items = new List<GestionEngineMetadataItem>();
        await using var reader = await listCommand.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(resource == "HandlerActions"
                ? new GestionEngineMetadataItem(
                    reader.GetInt32(0),
                    reader.GetString(1),
                    GetNullableString(reader, 2),
                    GetNullableString(reader, 3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4))
                : new GestionEngineMetadataItem(
                    reader.GetInt32(0),
                    $"Handler #{reader.GetInt32(0)}",
                    $"IdAccionHandler: {reader.GetInt32(1)}",
                    GetNullableString(reader, 3),
                    reader.GetInt32(2)));
        }

        if (rankActionNames)
        {
            items = HandlerNameMatcher.Rank(items, search ?? string.Empty, MaximumListSize).ToList();
        }
        stopwatch.Stop();
        _logger.LogInformation("GestionEngine metadata {Operation} completed in {ElapsedMs} ms. Resource: {Resource}, Items: {Items}", operation, stopwatch.ElapsedMilliseconds, resource, items.Count);
        return new GestionEngineMetadataResult(resource, operation, search, null, items);
    }

    public static bool IsSupported(string? resource, string? operation) =>
        resource is "HandlerActions" or "HandlerUsages"
        && operation is "Count" or "List" or "Search";

    private static string BuildListSql(string resource, bool isSearch) => resource == "HandlerActions"
        ? $"""
            SELECT TOP (@limit) IdAccionHandler, Accion, Descripcion, ParametrosJson, IdTipoHandler
            FROM dbo.AccionesHandler
            {(isSearch ? "WHERE Accion LIKE N'%' + @search + N'%' OR Descripcion LIKE N'%' + @search + N'%'" : string.Empty)}
            ORDER BY Accion, IdAccionHandler;
            """
        : $"""
            SELECT TOP (@limit) IdHandler, IdAccionHandler, IdTipoHandler, Parametros
            FROM dbo.Handlers
            {(isSearch ? "WHERE Parametros LIKE N'%' + @search + N'%'" : string.Empty)}
            ORDER BY IdHandler;
            """;

    private static string? GetNullableString(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}

public sealed record GestionEngineMetadataResult(
    string Resource,
    string Operation,
    string? Search,
    int? Count,
    IReadOnlyList<GestionEngineMetadataItem> Items);

public sealed record GestionEngineMetadataItem(
    int Id,
    string Name,
    string? Description,
    string? Parameters,
    int? TypeId);
