using System.Diagnostics;
using DesignerIA.Contracts;
using Microsoft.Data.SqlClient;

namespace DesignerIA.Api.Services;

/// <summary>
/// Servicio sencillo de solo lectura para comprobar el estado de la base de datos
/// GestionEngine local. No realiza escrituras ni utiliza ORM.
/// </summary>
public class GestionEngineHealthService
{
    private readonly string _connectionString;
    private readonly string _serverName;
    private readonly string _databaseName;
    private readonly ILogger<GestionEngineHealthService> _logger;

    public GestionEngineHealthService(IConfiguration configuration, ILogger<GestionEngineHealthService> logger)
    {
        _connectionString = configuration.GetConnectionString("GestionEngineLocal")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión 'GestionEngineLocal'.");

        var builder = new SqlConnectionStringBuilder(_connectionString);
        _serverName = builder.DataSource;
        _databaseName = builder.InitialCatalog;
        _logger = logger;
    }

    public async Task<DatabaseHealthStatus> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("GestionEngine health check started. Database: {Database}", _databaseName);

        try
        {
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM dbo.Vistas";

            var result = await command.ExecuteScalarAsync(cancellationToken);
            var viewsCount = Convert.ToInt32(result);

            stopwatch.Stop();
            _logger.LogInformation(
                "GestionEngine health check completed in {ElapsedMs} ms. ViewsCount: {ViewsCount}",
                stopwatch.ElapsedMilliseconds,
                viewsCount);

            return new DatabaseHealthStatus("ok", _serverName, _databaseName, viewsCount);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "GestionEngine health check failed after {ElapsedMs} ms",
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
