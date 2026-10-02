using System.Diagnostics;
using Microsoft.Data.SqlClient;

namespace DesignerIA.Api.Services;

/// <summary>
/// Capacidad de solo lectura, pequeña y específica, para resolver el alias
/// lógico de una conexión (dbo.StringsConnection.StringConnection, por ejemplo
/// "GestionEngine", "DW_TERNIUM") al ID físico del catálogo
/// (dbo.StringsConnection.IdStringConnection) en el ambiente local de
/// GestionEngine. No expone SQL a Copilot, no acepta consultas arbitrarias, no
/// devuelve ni transporta cadenas de conexión físicas (esas pertenecen al
/// Motor/ambiente, no a DesignerIA - ver AGENTS.md sección 5.4.3).
///
/// Evidencia de nombres reales (no inventados):
///  - Tabla dbo.StringsConnection: confirmada en
///    KnowledgeSource\DesignerRealEvidence\Clases\DTO\StoredProcedureDTO.cs
///    (dm.GetSelectQuery("StringsConnection", "IdStringConnection = ...")).
///  - Columna StringConnection (el alias): confirmada en
///    KnowledgeSource\Wiki\DocumentacionEngineNET_V4.html, procedimiento real
///    [dbo].[crearStringConnection], parámetro "@StringConnection nvarchar(250)".
///  - Columna IdStringConnection: confirmada en el mismo DTO y en las 3
///    exportaciones reales de Designer (AppPrecios.sql, vPreciosSemanales_CDA.sql,
///    vpruebaJFAL.sql), columna "IdStringConnection" de dbo.StoredProcedure.
///
/// Nota: Designer posee un procedimiento [dbo].[crearStringConnection] que
/// inserta el alias si no existe ("Verifica si existe un string de conexión...
/// En caso que no exista lo inserta"); DesignerIA NO lo usa porque es una
/// operación de escritura y AGENTS.md exige lectura como comportamiento por
/// defecto. Este servicio sólo hace SELECT.
/// </summary>
public class StringConnectionCatalogService
{
    private readonly string _connectionString;
    private readonly ILogger<StringConnectionCatalogService> _logger;

    public StringConnectionCatalogService(IConfiguration configuration, ILogger<StringConnectionCatalogService> logger)
    {
        _connectionString = configuration.GetConnectionString("GestionEngineLocal")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión 'GestionEngineLocal'.");
        _logger = logger;
    }

    /// <summary>
    /// Resuelve el ID físico (IdStringConnection) correspondiente a un alias
    /// lógico (StringConnection). Devuelve null si el alias no existe en el
    /// catálogo del ambiente local. Sólo lectura (SELECT parametrizado).
    /// </summary>
    public async Task<int?> ResolveIdByAliasAsync(string alias, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT IdStringConnection FROM dbo.StringsConnection WHERE StringConnection = @alias";
        command.Parameters.AddWithValue("@alias", alias);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        stopwatch.Stop();
        _logger.LogInformation(
            "StringsConnection alias lookup completed in {ElapsedMs} ms. Alias: {Alias}, Found: {Found}",
            stopwatch.ElapsedMilliseconds,
            alias,
            result is not null);

        return result is null or DBNull ? null : Convert.ToInt32(result);
    }
}
