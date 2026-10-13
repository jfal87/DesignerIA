using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Text.RegularExpressions;
using DesignerIA.Api.Services.ViewSpecs;
using DesignerIA.Contracts;
using Microsoft.Data.SqlClient;

namespace DesignerIA.Api.Services.ViewScripts;

/// <summary>
/// Genera el script de una vista por el camino único (normalizar, validar,
/// preflight, generar) y, cuando la creación está habilitada, lo ejecuta en la
/// base de metadata local y verifica el resultado real.
///
/// El SQL que se ejecuta es siempre el que produce <see cref="ViewScriptGenerator"/>
/// en el servidor a partir del ViewSpec; este servicio no acepta SQL externo.
///
/// La ejecución sigue el procedimiento de sesión de la sección 6 del contrato de
/// fase 2: conexión dedicada sin pool, sin transacción previa, un único batch,
/// comprobación de @@TRANCOUNT en la misma conexión, ROLLBACK si queda transacción
/// y cierre. No usa el token de la petición: cerrar el navegador no cancela el batch.
/// </summary>
public sealed partial class ViewCreationService
{
    // Una sola creación a la vez en esta instancia. No protege frente a otras
    // instancias ni frente a Designer; de eso se ocupa el propio script.
    private static readonly SemaphoreSlim CreationGate = new(1, 1);

    private const string NoChanges = "No se realizaron cambios.";
    private const int SessionCommandTimeoutSeconds = 15;

    private readonly string _connectionString;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly ViewCreationPreflightService _preflightService;
    private readonly ILogger<ViewCreationService> _logger;

    public ViewCreationService(IConfiguration configuration, IHostEnvironment environment,
        ViewCreationPreflightService preflightService, ILogger<ViewCreationService> logger)
    {
        _connectionString = configuration.GetConnectionString("GestionEngineLocal")
            ?? throw new InvalidOperationException("No se encontró la cadena de conexión 'GestionEngineLocal'.");
        _configuration = configuration;
        _environment = environment;
        _preflightService = preflightService;
        _logger = logger;
    }

    /// <summary>
    /// Camino único de generación. El preflight sólo se consulta cuando el ViewSpec
    /// tiene una forma generable; cualquier hueco devuelve el resultado sin script.
    /// </summary>
    public async Task<ViewScriptGenerationResult> GenerateAsync(ViewSpec spec, string? auditUser, CancellationToken cancellationToken)
    {
        var normalizedSpec = ViewSpecNormalizer.Normalize(spec);
        var viewSpecValidation = ViewSpecValidator.Validate(normalizedSpec);
        var generationGaps = ViewScriptGenerationValidator.Validate(normalizedSpec, viewSpecValidation);
        var preflight = generationGaps.Count == 0
            ? await _preflightService.CheckAsync(normalizedSpec, cancellationToken)
            : null;
        return ViewScriptGenerator.Generate(normalizedSpec, preflight, auditUser);
    }

    /// <summary>
    /// Crea la vista descrita por el ViewSpec. Comprueba primero que la creación
    /// esté permitida y que no haya otra en curso; después genera el script, lo
    /// ejecuta y verifica lo que quedó en la base. No reintenta.
    /// </summary>
    public Task<ViewCreationResult> CreateAsync(ViewSpec spec, ClaimsPrincipal user) => CreateAsync(user, async auditUser =>
    {
        // Sin el token de la petición: una petición abortada no debe dejar la creación a medias.
        var generation = await GenerateAsync(spec, auditUser, CancellationToken.None);
        return !generation.CanGenerate || generation.Script is null || generation.Preflight is null
            ? new ViewCreationPreparation(null, null, generation.Gaps)
            : new(ViewCreationPlanFactory.Create(ViewSpecNormalizer.Normalize(spec), generation.Preflight), generation.Script, []);
    });

    /// <summary>
    /// Ejecución y verificación compartidas por las rutas por ViewSpec y por la entrada por
    /// documento. <paramref name="prepare"/> recibe el usuario de auditoría y devuelve el plan y
    /// su script, o los motivos por los que no se puede crear; se invoca con la creación ya
    /// permitida y reservada, justo antes de ejecutar.
    /// </summary>
    public async Task<ViewCreationResult> CreateAsync(ClaimsPrincipal user, Func<string, Task<ViewCreationPreparation>> prepare)
    {
        var identity = user.Identity;
        var commandTimeoutSeconds = _configuration.GetValue("ViewCreation:CommandTimeoutSeconds", 60);
        var configuredMotorBaseUrl = _configuration["Motor:BaseUrl"];
        var blockReason = GetBlockReason(
            _environment.IsDevelopment(),
            _configuration.GetValue<bool>("ViewCreation:Enabled"),
            commandTimeoutSeconds,
            _configuration["ViewCreation:AllowedUser"],
            identity?.IsAuthenticated == true,
            identity?.Name,
            configuredMotorBaseUrl);
        if (blockReason is not null)
        {
            _logger.LogWarning("View creation blocked before execution. Reason: {Reason}. User: {User}", blockReason, identity?.Name);
            return new(ViewCreationStatus.NotAllowed, null, blockReason, [], []);
        }
        // GetBlockReason ya la validó; null significa que el ambiente no tiene Motor configurado.
        TryNormalizeMotorBaseUrl(configuredMotorBaseUrl, out var motorBaseUrl);

        if (!await CreationGate.WaitAsync(0))
        {
            _logger.LogWarning("View creation rejected because another creation is in progress. User: {User}", identity?.Name);
            return new(ViewCreationStatus.Busy, null, "Ya hay otra creación en curso en esta instancia. Espera a que termine.", [], []);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var auditUser = identity!.Name!;
            var preparation = await prepare(auditUser);
            if (preparation.Plan is not { } plan || preparation.Script is not { } script)
            {
                _logger.LogInformation("View creation rejected before execution. GapCount: {GapCount}", preparation.Gaps.Count);
                return new(ViewCreationStatus.Rejected, null, "La vista no se creó: no se puede generar su script. " + NoChanges, preparation.Gaps, []);
            }

            var execution = await ExecuteScriptAsync(script, commandTimeoutSeconds);
            var snapshot = await ReadSnapshotAsync(plan);
            var result = Decide(plan, auditUser, execution, snapshot, motorBaseUrl, _configuration.GetValue<bool>("Motor:IsMock"));
            _logger.LogInformation(
                "View creation finished in {ElapsedMs} ms. ViewName: {ViewName}. Status: {Status}. Execution: {Execution}. SqlErrors: {SqlErrors}. TranCountAfter: {TranCountAfter}. SnapshotRead: {SnapshotRead}. ViewUrlReturned: {ViewUrlReturned}",
                stopwatch.ElapsedMilliseconds, plan.View.Name, result.Status, execution.Kind,
                string.Join(",", execution.ErrorNumbers), execution.TranCountAfter, snapshot is not null, result.ViewUrl is not null);
            return result;
        }
        finally
        {
            CreationGate.Release();
        }
    }

    /// <summary>
    /// Motivo por el que la creación no está permitida, o null si lo está. Se
    /// comprueba antes de generar o ejecutar nada.
    /// </summary>
    public static string? GetBlockReason(bool isDevelopment, bool enabled, int commandTimeoutSeconds,
        string? allowedUser, bool isAuthenticated, string? userName, string? motorBaseUrl = null)
    {
        if (!isDevelopment)
        {
            return "La creación de vistas sólo está disponible en el ambiente de desarrollo.";
        }
        if (!enabled)
        {
            return "La creación de vistas está deshabilitada en esta instancia.";
        }
        // Cero significaría esperar sin límite y un valor negativo no es válido: el batch debe tener un tiempo máximo.
        if (commandTimeoutSeconds <= 0)
        {
            return "La creación de vistas no tiene un tiempo máximo de ejecución válido configurado.";
        }
        if (string.IsNullOrWhiteSpace(allowedUser))
        {
            return "La creación de vistas no tiene un usuario autorizado configurado.";
        }
        if (!isAuthenticated || !string.Equals(userName, allowedUser, StringComparison.OrdinalIgnoreCase))
        {
            return "Tu usuario no está autorizado para crear vistas.";
        }
        // Se comprueba aquí para no descubrir una URL base inválida después de haber escrito en la base.
        if (!TryNormalizeMotorBaseUrl(motorBaseUrl, out _))
        {
            return "La URL base de Motor configurada no es válida. " + NoChanges;
        }
        return null;
    }

    /// <summary>
    /// Valida la URL base de Motor del ambiente. Vacía es válida y significa que no
    /// hay Motor: la vista se crea sin URL. Si tiene valor debe ser una URL absoluta
    /// http o https, sin credenciales, consulta ni fragmento.
    /// </summary>
    public static bool TryNormalizeMotorBaseUrl(string? configured, out string? baseUrl)
    {
        baseUrl = null;
        if (string.IsNullOrWhiteSpace(configured))
        {
            return true;
        }
        if (!Uri.TryCreate(configured.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            return false;
        }
        baseUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        return true;
    }

    /// <summary>
    /// URL de la vista en Motor. El IdVista viaja codificado como valor de consulta;
    /// los parámetros adicionales de la vista no forman parte de esta URL.
    /// </summary>
    public static string BuildViewUrl(string motorBaseUrl, string viewId) =>
        $"{motorBaseUrl}/GestionEngine/DefaultV2.aspx?Limpiarcache={Uri.EscapeDataString(viewId)}&StyleVersion=V2";

    /// <summary>
    /// Clasifica cómo terminó el batch a partir de los errores SQL recibidos. El
    /// script reenvía todo error de su CATCH como 50000 con el número y estado
    /// originales en el prefijo «[número/estado]».
    /// </summary>
    public static ScriptExecution Classify(IReadOnlyList<(int Number, string Message)> errors)
    {
        var numbers = errors.Select(error => error.Number).ToArray();
        if (numbers.Length == 0)
        {
            return new(ScriptExecutionKind.Completed, numbers);
        }
        if (numbers.Contains(-2))
        {
            return new(ScriptExecutionKind.Timeout, numbers);
        }
        foreach (var error in errors.Where(error => error.Number == 50000))
        {
            var prefix = ErrorPrefix().Match(error.Message);
            if (prefix.Success)
            {
                return new(ScriptExecutionKind.RolledBackByScript, numbers,
                    int.Parse(prefix.Groups[1].Value, CultureInfo.InvariantCulture),
                    int.Parse(prefix.Groups[2].Value, CultureInfo.InvariantCulture));
            }
        }
        return new(ScriptExecutionKind.Unknown, numbers);
    }

    /// <summary>
    /// Decide el estado real. «Creada» exige que el batch terminara sin error y que
    /// la verificación coincida por completo; encontrar una vista con ese nombre
    /// nunca se atribuye a esta solicitud. La URL sólo acompaña a «Creada», y sólo
    /// si se recibe una URL base de Motor ya validada.
    /// </summary>
    public static ViewCreationResult Decide(ViewCreationPlan plan, string auditUser, ScriptExecution execution, ViewCreationSnapshot? snapshot,
        string? motorBaseUrl = null, bool motorIsMock = false)
    {
        var diagnostics = new List<string>(execution.Diagnostics ?? []);
        var sessionConfirmed = execution.TranCountAfter == 0 && !execution.RolledBackAfter;
        if (execution.TranCountAfter is null)
        {
            diagnostics.Add("No se pudo comprobar la sesión después de ejecutar.");
        }
        else if (execution.RolledBackAfter)
        {
            diagnostics.Add($"La sesión quedó con transacción abierta (@@TRANCOUNT={execution.TranCountAfter}) y se revirtió en la misma conexión.");
        }
        if (snapshot is null)
        {
            diagnostics.Add("No se pudo leer el estado de la vista después de ejecutar.");
        }

        ViewCreationResult Indeterminate(string reason)
        {
            if (snapshot is not null)
            {
                diagnostics.Add(ViewCreationVerifier.IsAbsent(snapshot.Counts)
                    ? "En la base no hay registros con ese nombre de vista."
                    : "En la base existe una vista con ese nombre; no se atribuye a esta solicitud. Conteos: " +
                      string.Join(", ", snapshot.Counts.Select(count => $"{count.Key}={count.Value}")) + ".");
            }
            return new(ViewCreationStatus.Indeterminate, null,
                $"No se pudo confirmar si la vista se creó: {reason} Comprueba la base antes de volver a intentarlo.", [], diagnostics);
        }

        switch (execution.Kind)
        {
            case ScriptExecutionKind.Completed:
                if (snapshot is null)
                {
                    return Indeterminate("el script terminó sin error, pero no se pudo verificar el resultado.");
                }
                var mismatches = ViewCreationVerifier.Compare(plan, auditUser, snapshot.Counts, snapshot.ValueRows, snapshot.Values,
                    snapshot.ParameterRows, snapshot.MappingRows, snapshot.StateValues, snapshot.ControlRows, snapshot.ControlItemRows);
                if (mismatches.Count > 0)
                {
                    diagnostics.AddRange(mismatches);
                    return Indeterminate("el script terminó sin error, pero la verificación no coincide con lo esperado.");
                }
                if (!sessionConfirmed)
                {
                    return Indeterminate("el script terminó sin error, pero el estado de su sesión no se pudo confirmar.");
                }
                var records = ViewCreationVerifier.ExpectedCounts(plan).Where(count => count.Key is not ("Vistas por IdVista o Nombre" or "Filas con el mismo IdFila")).Sum(count => count.Value);
                diagnostics.Add($"Verificados {records} registros, sus relaciones y sus valores.");
                var viewUrl = motorBaseUrl is null ? null : BuildViewUrl(motorBaseUrl, plan.View.Name);
                if (viewUrl is null)
                {
                    diagnostics.Add("Este ambiente no tiene una URL base de Motor configurada: la vista se creó sin URL.");
                }
                if (viewUrl is not null && motorIsMock)
                {
                    diagnostics.Add("El enlace de Motor es de referencia (mock); no corresponde a la base local ni confirma su apertura.");
                }
                return new(ViewCreationStatus.Created, plan.View.Name, $"Vista «{plan.View.Name}» creada y verificada en la base de metadata.", [], diagnostics, viewUrl, viewUrl is not null && motorIsMock);

            case ScriptExecutionKind.RolledBackByScript:
                diagnostics.Add($"El script revirtió sus cambios. Error original {execution.OriginalNumber}, estado {execution.OriginalState}.");
                var alreadyExists = execution.OriginalNumber == 50000 && execution.OriginalState == 1;
                if (!sessionConfirmed || snapshot is null || (!alreadyExists && !ViewCreationVerifier.IsAbsent(snapshot.Counts)))
                {
                    return Indeterminate("el script informó un error y revirtió, pero el estado posterior no se pudo confirmar.");
                }
                return new(ViewCreationStatus.NotCreated, null, DescribeRollback(execution.OriginalNumber, execution.OriginalState), [], diagnostics);

            case ScriptExecutionKind.NotExecuted:
                return new(ViewCreationStatus.NotCreated, null, "La creación no se ejecutó: la sesión no estaba en condiciones. " + NoChanges, [], diagnostics);

            case ScriptExecutionKind.Timeout:
                return Indeterminate("la ejecución superó el tiempo máximo.");

            default:
                return Indeterminate("la ejecución terminó con un error no previsto.");
        }
    }

    private static string DescribeRollback(int? number, int? state) => (number, state) switch
    {
        (50000, 1) => "Ya existe una vista con ese nombre. " + NoChanges,
        (50000, 2) => "El alias de conexión no existe o es ambiguo en la base. " + NoChanges,
        (50000, 3) or (50000, 4) => "Otra creación entró en conflicto con ésta. " + NoChanges,
        (50000, 5) => "El catálogo de tipos de web control de la base no es el esperado. " + NoChanges,
        (1222, _) => "La base estaba ocupada y la espera se agotó. " + NoChanges,
        (1205, _) => "La creación entró en conflicto con otra operación y fue revertida. " + NoChanges,
        (2627, _) or (2601, _) => "Otra operación usó los mismos identificadores. " + NoChanges,
        _ => "La creación falló y fue revertida. " + NoChanges
    };

    private async Task<ScriptExecution> ExecuteScriptAsync(string script, int commandTimeoutSeconds)
    {
        var diagnostics = new List<string>();
        var errors = new List<(int Number, string Message)>();
        var executed = false;
        var unexpected = false;
        int? tranCountAfter = null;
        var rolledBackAfter = false;
        // Sin pool: al cerrar, la sesión termina y no se reutiliza con las opciones que haya dejado el script.
        var builder = new SqlConnectionStringBuilder(_connectionString) { Pooling = false, ApplicationName = "DesignerIA-CrearVista" };
        await using var connection = new SqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync(CancellationToken.None);
            var tranCountBefore = await ReadTranCountAsync(connection);
            if (tranCountBefore != 0)
            {
                diagnostics.Add($"La sesión nueva tenía @@TRANCOUNT={tranCountBefore}; el script no se ejecutó.");
                return new(ScriptExecutionKind.NotExecuted, [], Diagnostics: diagnostics, TranCountAfter: tranCountBefore);
            }
            await using var command = connection.CreateCommand();
            command.CommandText = script;
            command.CommandTimeout = commandTimeoutSeconds;
            executed = true;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch (SqlException exception)
        {
            foreach (SqlError error in exception.Errors)
            {
                errors.Add((error.Number, error.Message));
                diagnostics.Add($"Error SQL {error.Number}, estado {error.State}, clase {error.Class}.");
            }
            _logger.LogWarning(exception, "View creation batch ended with SQL errors. Numbers: {Numbers}", string.Join(",", errors.Select(error => error.Number)));
        }
        catch (Exception exception)
        {
            unexpected = true;
            diagnostics.Add($"Error no SQL durante la ejecución: {exception.GetType().Name}.");
            _logger.LogError(exception, "View creation batch ended with an unexpected error.");
        }

        // Misma conexión: comprobación de sesión y ROLLBACK condicional. No depende de la petición.
        try
        {
            if (connection.State == ConnectionState.Open)
            {
                tranCountAfter = await ReadTranCountAsync(connection);
                if (tranCountAfter > 0)
                {
                    await using var rollback = connection.CreateCommand();
                    rollback.CommandText = "ROLLBACK TRANSACTION";
                    rollback.CommandTimeout = SessionCommandTimeoutSeconds;
                    await rollback.ExecuteNonQueryAsync(CancellationToken.None);
                    rolledBackAfter = true;
                    _logger.LogWarning("View creation session had an open transaction after the batch and was rolled back. TranCount: {TranCount}", tranCountAfter);
                }
            }
            else
            {
                diagnostics.Add("La conexión no quedó abierta después de ejecutar.");
            }
        }
        catch (Exception exception)
        {
            tranCountAfter = null;
            diagnostics.Add($"Error al comprobar la sesión: {exception.GetType().Name}.");
            _logger.LogError(exception, "View creation session check failed.");
        }

        if (!executed)
        {
            return new(ScriptExecutionKind.NotExecuted, errors.Select(error => error.Number).ToArray(),
                Diagnostics: diagnostics, TranCountAfter: tranCountAfter, RolledBackAfter: rolledBackAfter);
        }
        var classified = unexpected ? new ScriptExecution(ScriptExecutionKind.Unknown, []) : Classify(errors);
        return classified with { Diagnostics = diagnostics, TranCountAfter = tranCountAfter, RolledBackAfter = rolledBackAfter };
    }

    private static async Task<int> ReadTranCountAsync(SqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT @@TRANCOUNT";
        command.CommandTimeout = SessionCommandTimeoutSeconds;
        return Convert.ToInt32(await command.ExecuteScalarAsync(CancellationToken.None), CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Lee, desde otra conexión, los conteos y los valores de la vista. Devuelve
    /// null si la lectura falla: en ese caso el resultado no se puede confirmar.
    /// </summary>
    private async Task<ViewCreationSnapshot?> ReadSnapshotAsync(ViewCreationPlan plan)
    {
        var viewName = plan.View.Name;
        var blocks = plan.StateBlocks;
        var connectionAlias = blocks[0].Grid?.ConnectionAlias;
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(CancellationToken.None);
            var counts = new Dictionary<string, int>();
            await using (var command = CreateReadCommand(connection, ViewCreationVerifier.CountsSql, viewName, null))
            await using (var reader = await command.ExecuteReaderAsync(CancellationToken.None))
            {
                if (await reader.ReadAsync(CancellationToken.None))
                {
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        counts[reader.GetName(i)] = reader.GetInt32(i);
                    }
                }
            }
            var valueRows = 0;
            Dictionary<string, string?>? values = null;
            await using (var command = CreateReadCommand(connection, ViewCreationVerifier.ValuesSql, viewName, connectionAlias ?? string.Empty))
            await using (var reader = await command.ExecuteReaderAsync(CancellationToken.None))
            {
                while (await reader.ReadAsync(CancellationToken.None))
                {
                    valueRows++;
                    if (valueRows > 1)
                    {
                        continue;
                    }
                    values = [];
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        values[reader.GetName(i)] = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
                    }
                }
            }
            var parameterRows = new List<IReadOnlyDictionary<string, string?>>();
            var mappingRows = new List<IReadOnlyDictionary<string, string?>>();
            await using (var command = CreateReadCommand(connection, ViewCreationVerifier.ParametersSql, viewName, null))
            await using (var reader = await command.ExecuteReaderAsync(CancellationToken.None))
            {
                await ReadRowsAsync(reader, parameterRows);
                if (!await reader.NextResultAsync(CancellationToken.None))
                {
                    throw new InvalidOperationException("The parameter verification read did not return the mappings result set.");
                }
                await ReadRowsAsync(reader, mappingRows);
            }
            // Con varios estados, la lectura de valores se repite con el alias de cada grilla
            // y de cada lectura se toma la fila de su estado.
            Dictionary<string, IReadOnlyDictionary<string, string?>>? stateValues = null;
            if (blocks.Count > 1)
            {
                stateValues = new(StringComparer.Ordinal);
                foreach (var block in blocks)
                {
                    var rows = new List<IReadOnlyDictionary<string, string?>>();
                    await using var command = CreateReadCommand(connection, ViewCreationVerifier.ValuesSql, viewName, block.Grid?.ConnectionAlias ?? string.Empty);
                    await using var reader = await command.ExecuteReaderAsync(CancellationToken.None);
                    await ReadRowsAsync(reader, rows);
                    if (rows.SingleOrDefault(row => row.GetValueOrDefault("Estados.Descripcion") == block.State.Name) is { } row)
                    {
                        stateValues[block.State.Name] = row;
                    }
                }
            }
            var controlRows = new List<IReadOnlyDictionary<string, string?>>();
            var controlItemRows = new List<IReadOnlyDictionary<string, string?>>();
            await using (var command = CreateReadCommand(connection, ViewCreationVerifier.ControlsSql, viewName, null))
            await using (var reader = await command.ExecuteReaderAsync(CancellationToken.None))
            {
                await ReadRowsAsync(reader, controlRows);
                if (!await reader.NextResultAsync(CancellationToken.None))
                {
                    throw new InvalidOperationException("The control verification read did not return the items result set.");
                }
                await ReadRowsAsync(reader, controlItemRows);
            }
            return new(counts, valueRows, values, parameterRows, mappingRows, stateValues, controlRows, controlItemRows);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "View creation verification read failed. ViewName: {ViewName}", viewName);
            return null;
        }
    }

    private static async Task ReadRowsAsync(SqlDataReader reader, List<IReadOnlyDictionary<string, string?>> rows)
    {
        while (await reader.ReadAsync(CancellationToken.None))
        {
            var row = new Dictionary<string, string?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                row[reader.GetName(i)] = reader.IsDBNull(i) ? null : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture);
            }
            rows.Add(row);
        }
    }

    private static SqlCommand CreateReadCommand(SqlConnection connection, string sql, string viewName, string? connectionAlias)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = SessionCommandTimeoutSeconds;
        command.Parameters.Add("@viewName", SqlDbType.NVarChar, 250).Value = viewName;
        if (connectionAlias is not null)
        {
            command.Parameters.Add("@alias", SqlDbType.NVarChar, 250).Value = connectionAlias;
        }
        return command;
    }

    [GeneratedRegex(@"^\[(\d+)/(\d+)\] ")]
    private static partial Regex ErrorPrefix();
}

public enum ScriptExecutionKind
{
    /// <summary>El batch terminó sin error.</summary>
    Completed,

    /// <summary>El CATCH del script revirtió y reenvió el error original en su prefijo.</summary>
    RolledBackByScript,

    /// <summary>El batch no llegó a enviarse.</summary>
    NotExecuted,

    /// <summary>SqlClient agotó el tiempo máximo del comando.</summary>
    Timeout,

    /// <summary>Cualquier otro final: no se puede afirmar qué ocurrió.</summary>
    Unknown
}

/// <summary>Cómo terminó el batch y en qué estado quedó su sesión.</summary>
public record ScriptExecution(
    ScriptExecutionKind Kind,
    IReadOnlyList<int> ErrorNumbers,
    int? OriginalNumber = null,
    int? OriginalState = null,
    IReadOnlyList<string>? Diagnostics = null,
    int? TranCountAfter = null,
    bool RolledBackAfter = false);

/// <summary>
/// Lo leído de la base después de ejecutar: conteos independientes, valores y los
/// conjuntos completos de parámetros y mapeos de la vista.
/// </summary>
/// <param name="StateValues">Con varios estados, la fila de valores de cada estado por su nombre.</param>
/// <param name="ControlRows">Web controls de la vista con su configuración.</param>
/// <param name="ControlItemRows">Opciones de esos web controls.</param>
public record ViewCreationSnapshot(
    IReadOnlyDictionary<string, int> Counts,
    int ValueRows,
    IReadOnlyDictionary<string, string?>? Values,
    IReadOnlyList<IReadOnlyDictionary<string, string?>>? ParameterRows = null,
    IReadOnlyList<IReadOnlyDictionary<string, string?>>? MappingRows = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string?>>? StateValues = null,
    IReadOnlyList<IReadOnlyDictionary<string, string?>>? ControlRows = null,
    IReadOnlyList<IReadOnlyDictionary<string, string?>>? ControlItemRows = null);

/// <summary>
/// Lo que se va a ejecutar: el plan y su script, o los motivos por los que no se
/// puede crear. El script sale siempre de <see cref="ViewScriptGenerator"/>.
/// </summary>
public record ViewCreationPreparation(
    ViewCreationPlan? Plan,
    string? Script,
    IReadOnlyList<ViewScriptGenerationGap> Gaps);
