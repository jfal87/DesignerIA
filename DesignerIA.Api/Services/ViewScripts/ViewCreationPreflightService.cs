using System.Diagnostics;
using DesignerIA.Contracts;
using Microsoft.Data.SqlClient;

namespace DesignerIA.Api.Services.ViewScripts;

/// <summary>Consulta exclusivamente la colisión de IdVista/Nombre; nunca modifica GestionEngine.</summary>
public sealed class ViewCreationPreflightService
{
    private readonly string _connectionString;
    private readonly ILogger<ViewCreationPreflightService> _logger;

    public ViewCreationPreflightService(IConfiguration configuration, ILogger<ViewCreationPreflightService> logger)
    {
        _connectionString = configuration.GetConnectionString("GestionEngineLocal")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión 'GestionEngineLocal'.");
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
        stopwatch.Stop();

        var existingViewId = result is null or DBNull ? null : Convert.ToString(result);
        _logger.LogInformation(
            "View creation preflight completed in {ElapsedMs} ms. ViewName: {ViewName}, IsAvailable: {IsAvailable}",
            stopwatch.ElapsedMilliseconds,
            viewName,
            existingViewId is null);

        return FromExistingViewId(viewName, existingViewId);
    }

    /// <summary>
    /// Convierte el único dato leído por el preflight en el resultado estructurado.
    /// Se mantiene puro para poder verificar ambos resultados sin una conexión SQL.
    /// </summary>
    public static ViewCreationPreflightResult FromExistingViewId(string viewName, string? existingViewId)
    {
        return new ViewCreationPreflightResult(existingViewId is null, viewName, existingViewId);
    }
}
