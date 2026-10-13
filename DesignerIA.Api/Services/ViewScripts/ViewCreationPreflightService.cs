using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using DesignerIA.Contracts;
using Microsoft.Data.SqlClient;

namespace DesignerIA.Api.Services.ViewScripts;

/// <summary>
/// Comprueba por lectura la disponibilidad del nombre y, para una grilla, el
/// alias, la conexión configurada y los metadatos del procedimiento, sus
/// parámetros y su primer resultado. Devuelve rechazos y comprobaciones
/// superadas; nunca ejecuta el SP.
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
        return area is null
            ? result
            : await CheckGridAsync(result, area.ConnectionAlias!, area.StoredProcedure, area.Parameters, cancellationToken);
    }

    /// <summary>
    /// Añade a <paramref name="result"/> las comprobaciones 2 a 6 de una grilla: alias,
    /// conexión configurada, procedimiento, parámetros y primer resultado. Una vista con
    /// varias grillas la invoca una vez por grilla.
    /// </summary>
    public async Task<ViewCreationPreflightResult> CheckGridAsync(ViewCreationPreflightResult result, string connectionAlias,
        string? storedProcedure, IReadOnlyList<ViewSpecParameter>? requested, CancellationToken cancellationToken = default)
    {
        var gaps = result.Gaps!.ToList();
        var completed = result.CompletedChecks!.ToList();
        var aliasRows = await _catalog.ReadByAliasAsync(connectionAlias, cancellationToken);
        var aliasGap = FromAliasRows(aliasRows.Count, aliasRows.Count == 1 ? aliasRows[0].IsOracle : null);
        AddCheck(2, aliasGap, gaps, completed);
        var connectionKey = _configuration[$"ConnectionAliases:{connectionAlias}"];
        var spConnection = string.IsNullOrWhiteSpace(connectionKey) ? null : _configuration.GetConnectionString(connectionKey);
        var configurationGap = FromConfiguredConnection(!string.IsNullOrWhiteSpace(spConnection));
        AddCheck(3, configurationGap, gaps, completed);
        string? canonical = null;
        IReadOnlyList<ViewCreationPreflightParameter>? resolvedParameters = null;
        IReadOnlyList<ViewCreationPreflightDeclaredParameter>? declaredParameters = null;
        if (aliasGap is null && configurationGap is null)
        {
            ViewScriptGenerationValidator.TryParseStoredProcedure(storedProcedure, out var schema, out var name);
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
                    command.CommandText = "SELECT p.name, p.is_output, TYPE_NAME(p.system_type_id), p.max_length, CAST(ISNULL(t.is_table_type, 0) AS bit) FROM sys.parameters p LEFT JOIN sys.types t ON t.user_type_id = p.user_type_id WHERE p.object_id = @objectId AND p.parameter_id > 0 ORDER BY p.parameter_id";
                    command.Parameters.AddWithValue("@objectId", objectId.Value);
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                    {
                        parameters.Add(new(reader.GetString(0), reader.GetBoolean(1),
                            reader.IsDBNull(2) ? null : reader.GetString(2), reader.GetInt16(3), reader.GetBoolean(4)));
                    }
                }
                declaredParameters = parameters
                    .Select(parameter => new ViewCreationPreflightDeclaredParameter(parameter.Name, parameter.TypeName, parameter.IsOutput))
                    .ToList();
                var requestedParameters = requested ?? [];
                if (requestedParameters.Count == 0)
                {
                    AddCheck(5, FromParameters(canonical!, parameters), gaps, completed);
                }
                else
                {
                    var parameterGaps = ResolveParameters(requestedParameters, parameters, out resolvedParameters);
                    if (parameterGaps.Count == 0)
                    {
                        completed.Add(5);
                    }
                    else
                    {
                        gaps.AddRange(parameterGaps);
                    }
                }
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
        return result with { Gaps = gaps, CanonicalStoredProcedure = canonical, CompletedChecks = completed, Parameters = resolvedParameters, DeclaredParameters = declaredParameters };
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

    /// <summary>Límite propio de DesignerIA para el valor de un parámetro de tipo (max).</summary>
    public const int MaxTypeValueLength = 500;

    /// <summary>Tipos de cadena: Designer los marca con UsaComillas = 1; los enteros y bit, con 0.</summary>
    public static bool UsesQuotes(string type) => type is "varchar" or "nvarchar" or "char" or "nchar";

    /// <summary>
    /// Contrasta los parámetros con valor fijo del ViewSpec con los que declara el
    /// procedimiento y los devuelve en su orden de declaración, con el nombre del
    /// catálogo sin «@» y su tipo base. Exige cobertura exacta de los parámetros de
    /// entrada: no se usa sys.parameters.has_default_value para decidir omisiones.
    /// Es puro: la lectura de sys.parameters la hace <see cref="CheckAsync"/>.
    /// </summary>
    public static IReadOnlyList<ViewScriptGenerationGap> ResolveParameters(
        IReadOnlyList<ViewSpecParameter> requested, IReadOnlyList<ViewProcedureParameter> declared,
        out IReadOnlyList<ViewCreationPreflightParameter>? resolved)
    {
        var gaps = new List<ViewScriptGenerationGap>();
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var parameter in requested)
        {
            values[ViewScriptGenerationValidator.TryParseParameterName(parameter?.Name, out var requestedName)
                ? requestedName : parameter?.Name ?? string.Empty] = parameter?.DefaultValue;
        }
        var declaration = declared.Count == 0
            ? "El procedimiento no declara parámetros."
            : "El procedimiento declara: " + string.Join(", ", declared.Select(parameter => $"{parameter.Name} ({parameter.TypeName ?? "tipo desconocido"})")) + ".";
        var declaredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ViewCreationPreflightParameter>();
        foreach (var parameter in declared)
        {
            var name = parameter.Name.StartsWith('@') ? parameter.Name[1..] : parameter.Name;
            var type = parameter.TypeName;
            if (!declaredNames.Add(name) || !ViewScriptGenerationValidator.TryParseParameterName(name, out _))
            {
                gaps.Add(new("Nombre de parámetro no admitido",
                    $"El procedimiento declara el parámetro «{name}», cuyo nombre no es un identificador ASCII único de hasta 75 caracteres."));
            }
            else if (parameter.IsOutput)
            {
                gaps.Add(new("Parámetro de salida no admitido", $"El parámetro «{name}» es de salida; sólo se admiten parámetros de entrada."));
            }
            else if (parameter.IsTableType)
            {
                gaps.Add(new("Parámetro de tipo tabla no admitido", $"El parámetro «{name}» es de tipo tabla; no admite un valor fijo."));
            }
            else if (name.ToLowerInvariant() is "logon_user" or "gruposvistausuario")
            {
                gaps.Add(new("Parámetro resuelto por Motor",
                    $"Motor sustituye el valor del parámetro «{name}» con datos del contexto de ejecución; no admite un valor fijo."));
            }
            else if (type is not ("varchar" or "nvarchar" or "char" or "nchar" or "int" or "bigint" or "smallint" or "tinyint" or "bit"))
            {
                gaps.Add(new("Tipo de parámetro no admitido",
                    $"El parámetro «{name}» es de tipo {type ?? "desconocido"}. Se admiten varchar, nvarchar, char, nchar, int, bigint, smallint, tinyint y bit."));
            }
            else if (!values.TryGetValue(name, out var value))
            {
                gaps.Add(new("Parámetros no coinciden con el procedimiento", $"Falta el valor del parámetro «{name}». {declaration}"));
            }
            else if (DescribeTypedValueProblem(type, parameter.MaxLength, value) is { } problem)
            {
                gaps.Add(new("Valor de parámetro no admitido", $"El valor del parámetro «{name}» {problem}"));
            }
            else
            {
                result.Add(new(name, type, value!));
            }
        }
        foreach (var name in values.Keys.Where(name => !declaredNames.Contains(name)))
        {
            gaps.Add(new("Parámetros no coinciden con el procedimiento", $"El procedimiento no tiene el parámetro «{name}». {declaration}"));
        }
        resolved = gaps.Count == 0 ? result : null;
        return gaps;
    }

    /// <summary>
    /// Motivo por el que un valor no es válido para el tipo del parámetro, o null si
    /// lo es. Los enteros y bit se envían a Motor sin comillas, por eso sólo admiten
    /// su representación decimal estricta. La longitud se mide en caracteres: con
    /// ASCII, max_length equivale a caracteres en varchar/char y al doble en nvarchar/nchar.
    /// </summary>
    public static string? DescribeTypedValueProblem(string type, int maxLength, string? value)
    {
        if (value is null)
        {
            return "no está informado.";
        }
        switch (type)
        {
            case "bit":
                return value is "0" or "1" ? null : "debe ser 0 o 1 para un parámetro bit.";
            case "tinyint":
                return DescribeIntegerProblem(value, byte.MinValue, byte.MaxValue, type);
            case "smallint":
                return DescribeIntegerProblem(value, short.MinValue, short.MaxValue, type);
            case "int":
                return DescribeIntegerProblem(value, int.MinValue, int.MaxValue, type);
            case "bigint":
                return DescribeIntegerProblem(value, long.MinValue, long.MaxValue, type);
            default:
                if (maxLength < 0)
                {
                    return value.Length <= MaxTypeValueLength ? null
                        : $"tiene {value.Length} caracteres; para parámetros {type}(max) DesignerIA admite hasta {MaxTypeValueLength}. Es un límite propio de DesignerIA.";
                }
                var limit = type is "nvarchar" or "nchar" ? maxLength / 2 : maxLength;
                return value.Length <= limit ? null
                    : $"tiene {value.Length} caracteres y el parámetro {type}({limit}) admite hasta {limit}.";
        }
    }

    private static string? DescribeIntegerProblem(string value, long minimum, long maximum, string type) =>
        Regex.IsMatch(value, @"\A(?:0|-?[1-9][0-9]{0,18})\z")
            && long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
            && number >= minimum && number <= maximum
            ? null
            : $"debe ser un entero decimal entre {minimum} y {maximum} para un parámetro {type}, sin separadores, espacios, signo «+» ni ceros a la izquierda.";

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

/// <summary>Parámetro tal como lo declara el procedimiento en sys.parameters.</summary>
/// <param name="TypeName">Tipo base (TYPE_NAME del system_type_id), o null si no se pudo resolver.</param>
/// <param name="MaxLength">max_length en bytes; -1 para los tipos (max).</param>
public record ViewProcedureParameter(string Name, bool IsOutput, string? TypeName = null, int MaxLength = 0, bool IsTableType = false);
