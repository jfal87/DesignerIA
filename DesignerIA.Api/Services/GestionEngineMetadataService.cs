using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace DesignerIA.Api.Services;

/// <summary>Small allowlisted, read-only catalogue lookup for GestionEngine handler metadata.</summary>
public sealed class GestionEngineMetadataService
{
    private const int MaximumListSize = 50;
    // Ceiling for the paged catalogue of actions; reaching it is reported as a partial list.
    private const int MaximumCatalogueSize = 2_000;
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
        int? listTotal = null;
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

        if (operation == "List")
        {
            await using var totalCommand = connection.CreateCommand();
            totalCommand.CommandText = resource == "HandlerActions"
                ? "SELECT COUNT(*) FROM dbo.AccionesHandler;"
                : "SELECT COUNT(*) FROM dbo.Handlers;";
            var total = Convert.ToInt32(await totalCommand.ExecuteScalarAsync(cancellationToken));
            if (resource == "HandlerActions")
            {
                // Keyset pages (no OFFSET/FETCH: the catalogue may live on SQL Server 2008 R2).
                var catalogue = new List<GestionEngineMetadataItem>();
                var exhausted = false;
                while (catalogue.Count < MaximumCatalogueSize)
                {
                    await using var pageCommand = connection.CreateCommand();
                    pageCommand.CommandText = """
                        SELECT TOP (@limit) IdAccionHandler, Accion, Descripcion, ParametrosJson, IdTipoHandler
                        FROM dbo.AccionesHandler
                        WHERE @afterId IS NULL OR Accion > @afterName OR (Accion = @afterName AND IdAccionHandler > @afterId)
                        ORDER BY Accion, IdAccionHandler;
                        """;
                    pageCommand.Parameters.Add("@limit", System.Data.SqlDbType.Int).Value = MaximumListSize;
                    pageCommand.Parameters.Add("@afterName", System.Data.SqlDbType.NVarChar, 4000).Value = catalogue.Count == 0 ? DBNull.Value : catalogue[^1].Name;
                    pageCommand.Parameters.Add("@afterId", System.Data.SqlDbType.Int).Value = catalogue.Count == 0 ? DBNull.Value : catalogue[^1].Id;
                    var before = catalogue.Count;
                    await using (var pageReader = await pageCommand.ExecuteReaderAsync(cancellationToken))
                    {
                        while (await pageReader.ReadAsync(cancellationToken))
                        {
                            catalogue.Add(new GestionEngineMetadataItem(pageReader.GetInt32(0), pageReader.GetString(1),
                                GetNullableString(pageReader, 2), GetNullableString(pageReader, 3), pageReader.IsDBNull(4) ? null : pageReader.GetInt32(4)));
                        }
                    }
                    if (catalogue.Count - before < MaximumListSize)
                    {
                        exhausted = true;
                        break;
                    }
                }
                // The list is complete only when a page came back short. If the ceiling stopped it, the count taken
                // before reading says how many there were; it is never used to call a stopped read complete.
                var known = exhausted ? catalogue.Count : Math.Max(total, catalogue.Count + 1);
                stopwatch.Stop();
                _logger.LogInformation("GestionEngine metadata catalogue completed in {ElapsedMs} ms. Resource: {Resource}, Items: {Items}, Total: {Total}",
                    stopwatch.ElapsedMilliseconds, resource, catalogue.Count, known);
                return new GestionEngineMetadataResult(resource, operation, search, known, catalogue);
            }
            listTotal = total;
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
        return new GestionEngineMetadataResult(resource, operation, search, listTotal, items);
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
