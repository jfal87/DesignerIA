using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using DesignerIA.Api.Services.ViewScripts;
using DesignerIA.Contracts;
using static DesignerIA.Api.Services.ViewDocuments.JsonMetadataNode;

namespace DesignerIA.Api.Services.ViewDocuments;

/// <summary>
/// Prepara y crea una vista a partir de un documento JSONMetadata.
/// Recorrido: lectura estricta, expansión de convenciones, validación de
/// estructura y soporte, y catálogo del ambiente. La creación repite el mismo
/// recorrido sobre el documento que recibe: un borrador revisado no la autoriza.
/// No interviene Copilot, no acepta SQL y no completa datos que falten.
/// El plan que resulta se genera, ejecuta y verifica por el mismo camino que las
/// rutas por ViewSpec: <see cref="ViewScriptGenerator"/> y <see cref="ViewCreationService"/>.
/// </summary>
public sealed class JsonMetadataService
{
    private static readonly JsonSerializerOptions DocumentOptions = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerOptions EffectiveTextOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
    };

    private readonly ViewCreationService _creationService;
    private readonly ViewCreationPreflightService _preflightService;
    private readonly ILogger<JsonMetadataService> _logger;

    public JsonMetadataService(ViewCreationService creationService, ViewCreationPreflightService preflightService,
        ILogger<JsonMetadataService> logger)
    {
        _creationService = creationService;
        _preflightService = preflightService;
        _logger = logger;
    }

    /// <summary>
    /// Lectura, expansión y validación sin acceso a datos. Es pura: el mismo
    /// contenido produce siempre el mismo documento efectivo y los mismos diagnósticos.
    /// El límite de tamaño se aplica también a la definición efectiva: un borrador
    /// que se puede crear siempre puede volver a recibirse.
    /// </summary>
    public static JsonMetadataReading ReadDocument(string? content)
    {
        var diagnostics = new List<JsonMetadataDiagnostic>();
        var annotations = new List<JsonMetadataAnnotation>();
        var document = JsonMetadataReader.Read(content, diagnostics);
        if (document is null)
        {
            return new(null, null, annotations, diagnostics);
        }
        JsonMetadataExpander.Expand(document, annotations);
        JsonMetadataValidator.Validate(document, diagnostics);
        var effectiveText = document.ToJsonString(EffectiveTextOptions);
        if (effectiveText.Length > JsonMetadataReader.MaxContentLength)
        {
            diagnostics.Add(Error("", "DocumentoDemasiadoGrande",
                $"La definición efectiva ocupa {effectiveText.Length} caracteres y el máximo admitido es {JsonMetadataReader.MaxContentLength}: no podría volver a recibirse para crear.",
                "Un documento más pequeño."));
        }
        return new(document, effectiveText, annotations, diagnostics);
    }

    public async Task<JsonMetadataDraft> PrepareAsync(string? content, string? auditUser, CancellationToken cancellationToken)
    {
        var reading = ReadDocument(content);
        string? script = null;
        if (string.IsNullOrWhiteSpace(auditUser))
        {
            reading.Diagnostics.Add(Pending("", "IdentidadRequerida", "No se recibió la identidad de auditoría.", "Una sesión autenticada."));
        }
        if (reading.Document is not null && reading.Diagnostics.Count == 0)
        {
            var document = Typed(reading);
            var catalog = await CheckCatalogAsync(document, cancellationToken);
            AddCatalogResults(catalog, reading.Annotations, reading.Diagnostics);
            if (reading.Diagnostics.Count == 0)
            {
                // Sólo genera texto; al crear se reconstruyen el plan y el script desde el documento.
                var plan = JsonMetadataPlanFactory.Create(document, catalog.Grids, reading.Annotations);
                script = ViewScriptGenerator.BuildScript(plan, auditUser!);
            }
        }
        _logger.LogInformation(
            "JSONMetadata preparation completed. Readable: {Readable}. Diagnostics: {DiagnosticCount}. Errors: {Errors}. Pending: {Pending}. Unsupported: {Unsupported}",
            reading.Document is not null, reading.Diagnostics.Count,
            reading.Diagnostics.Count(diagnostic => diagnostic.Severity == JsonMetadataSeverity.Error),
            reading.Diagnostics.Count(diagnostic => diagnostic.Severity == JsonMetadataSeverity.Pendiente),
            reading.Diagnostics.Count(diagnostic => diagnostic.Severity == JsonMetadataSeverity.NoSoportado));
        return new(reading.ViewId, reading.EffectiveText, reading.Annotations, reading.Diagnostics,
            reading.Document is not null && reading.Diagnostics.Count == 0, script);
    }

    /// <summary>
    /// Crea la vista. Vuelve a leer y validar el documento y, con la creación ya
    /// permitida, vuelve a comprobar el catálogo; si queda algún diagnóstico no se
    /// ejecuta nada.
    /// </summary>
    public async Task<ViewCreationResult> CreateAsync(string? content, ClaimsPrincipal user)
    {
        var reading = ReadDocument(content);
        if (reading.Document is null || reading.Diagnostics.Count > 0)
        {
            _logger.LogInformation("JSONMetadata creation rejected before execution. Diagnostics: {DiagnosticCount}", reading.Diagnostics.Count);
            return new(ViewCreationStatus.Rejected, null, "La vista no se creó: el documento tiene pendientes. No se realizaron cambios.",
                ToGaps(reading.Diagnostics), []);
        }
        var document = Typed(reading);
        return await _creationService.CreateAsync(user, async auditUser =>
        {
            // Sin el token de la petición: una petición abortada no debe dejar la creación a medias.
            var catalog = await CheckCatalogAsync(document, CancellationToken.None);
            var diagnostics = new List<JsonMetadataDiagnostic>();
            AddCatalogResults(catalog, [], diagnostics);
            if (diagnostics.Count > 0)
            {
                return new ViewCreationPreparation(null, null, ToGaps(diagnostics));
            }
            var plan = JsonMetadataPlanFactory.Create(document, catalog.Grids, []);
            return new(plan, ViewScriptGenerator.BuildScript(plan, auditUser), []);
        });
    }

    /// <summary>Disponibilidad del nombre y, por cada estado con grilla, alias, procedimiento, firma y primer resultado.</summary>
    private async Task<JsonMetadataCatalog> CheckCatalogAsync(JsonMetadataDocument document, CancellationToken cancellationToken)
    {
        var viewId = document.View!.Id!;
        var name = await _preflightService.CheckNameAsync(viewId, cancellationToken);
        var grids = new List<ViewCreationPreflightResult?>();
        foreach (var state in document.States!)
        {
            var data = state.Rows is { Count: > 0 } rows ? rows[0].Areas![0].Object!.Data! : null;
            grids.Add(data is null ? null : await _preflightService.CheckGridAsync(
                new ViewCreationPreflightResult(true, viewId, null, [], CompletedChecks: []),
                data.Connection!, data.StoredProcedure,
                data.Parameters?.Select(parameter => new ViewSpecParameter(parameter.Name, parameter.Default)).ToList(),
                cancellationToken));
        }
        return new(name, grids);
    }

    /// <summary>
    /// Traslada al borrador lo que resolvió o rechazó el catálogo. Cada rechazo se
    /// ubica en la propiedad del documento que lo origina; los datos resueltos se
    /// anotan sin exponer IDs físicos.
    /// </summary>
    public static void AddCatalogResults(JsonMetadataCatalog catalog, List<JsonMetadataAnnotation> annotations,
        List<JsonMetadataDiagnostic> diagnostics)
    {
        foreach (var gap in catalog.Name.Gaps ?? [])
        {
            diagnostics.Add(gap.Topic == "Nombre de vista existente"
                ? Error("view.id", "NombreExistente", gap.Description, "Otro identificador. DesignerIA crea vistas nuevas: no recupera ni sobrescribe una existente.")
                : Error("", "CatalogoRechazado", $"{gap.Topic}: {gap.Description}", "Revisa el documento."));
        }
        if (catalog.Name.CompletedChecks?.Contains(1) == true)
        {
            annotations.Add(new("view.id", JsonMetadataOrigin.Catalogo, "Identificador disponible en la base de metadata."));
        }
        for (var index = 0; index < catalog.Grids.Count; index++)
        {
            if (catalog.Grids[index] is { } grid)
            {
                AddGridResults(grid, $"states[{index}].rows[0].areas[0].object.data", annotations, diagnostics);
            }
        }
    }

    private static void AddGridResults(ViewCreationPreflightResult grid, string dataPath,
        List<JsonMetadataAnnotation> annotations, List<JsonMetadataDiagnostic> diagnostics)
    {
        var declared = grid.DeclaredParameters;
        var signature = declared is null ? null : string.Join(", ", declared.Select(parameter => $"{parameter.Name} ({parameter.Type ?? "tipo desconocido"})"));
        foreach (var gap in grid.Gaps ?? [])
        {
            diagnostics.Add(gap.Topic switch
            {
                "Alias inexistente" or "Alias ambiguo" or "Alias no es SQL Server" or "Alias sin conexión configurada" =>
                    Error(dataPath + ".connection", "AliasNoResuelto", gap.Description, "Un alias único de SQL Server, con conexión configurada para DesignerIA."),
                "SP inexistente o no visible para DesignerIA" or "Resultado incompatible" or "Resultado no determinable" =>
                    Error(dataPath + ".storedProcedure", "ProcedimientoNoAdmitido", gap.Description,
                        "Un Stored Procedure existente y visible, cuyo primer resultado tenga al menos dos columnas."),
                "SP con parámetros no admitidos por la plantilla" =>
                    Pending(dataPath + ".parameters", "ParametrosRequeridos",
                        "El procedimiento declara parámetros de entrada y el documento no declara ninguno.",
                        "Un valor para cada parámetro de entrada" + (signature is null ? "." : $": {signature}.")),
                "Parámetros no coinciden con el procedimiento" or "Valor de parámetro no admitido" or "Tipo de parámetro no admitido"
                    or "Parámetro de salida no admitido" or "Parámetro de tipo tabla no admitido" or "Parámetro resuelto por Motor"
                    or "Nombre de parámetro no admitido" =>
                    Error(dataPath + ".parameters", "ParametrosNoAdmitidos", gap.Description,
                        "Exactamente los parámetros de entrada del procedimiento, con valores válidos para su tipo."),
                _ => Error(dataPath, "CatalogoRechazado", $"{gap.Topic}: {gap.Description}", "Revisa los datos de la grilla.")
            });
        }

        var completed = grid.CompletedChecks ?? [];
        if (completed.Contains(2) && completed.Contains(3))
        {
            annotations.Add(new(dataPath + ".connection", JsonMetadataOrigin.Catalogo,
                "El alias resuelve a una única conexión de SQL Server, configurada para DesignerIA. Su Id físico es propio del ambiente."));
        }
        if (grid.CanonicalStoredProcedure is { } canonical)
        {
            annotations.Add(new(dataPath + ".storedProcedure", JsonMetadataOrigin.Catalogo, $"Procedimiento encontrado: {canonical}."));
        }
        if (declared is not null)
        {
            var omitted = declared.Count > 0 && completed.Contains(5) && (grid.Parameters is null or { Count: 0 });
            annotations.Add(new(dataPath + ".parameters", JsonMetadataOrigin.Catalogo,
                declared.Count == 0 ? "El procedimiento no declara parámetros."
                : $"Firma del procedimiento: {signature}."
                    + (omitted ? " No se declaran: Motor no los envía y rige su valor por defecto (excepción heredada del procedimiento de prueba)." : "")));
        }
    }

    private static JsonMetadataDocument Typed(JsonMetadataReading reading) =>
        reading.Document!.Deserialize<JsonMetadataDocument>(DocumentOptions)!;

    private static List<ViewScriptGenerationGap> ToGaps(IEnumerable<JsonMetadataDiagnostic> diagnostics) => diagnostics
        .Select(diagnostic => new ViewScriptGenerationGap(diagnostic.Path.Length == 0 ? diagnostic.Code : diagnostic.Path, $"{diagnostic.Cause} {diagnostic.Needed}"))
        .ToList();
}

/// <summary>Documento efectivo, su texto, el origen de sus datos y los diagnósticos de una lectura.</summary>
public sealed record JsonMetadataReading(
    JsonObject? Document,
    string? EffectiveText,
    List<JsonMetadataAnnotation> Annotations,
    List<JsonMetadataDiagnostic> Diagnostics)
{
    public string? ViewId => AsText(AsObject(Document?["view"])?["id"]);
}

/// <summary>
/// Lo que el catálogo del ambiente respondió para un documento: la disponibilidad
/// del nombre y, por cada estado y en su orden, las comprobaciones de su grilla
/// (null si el estado no tiene grilla).
/// </summary>
public sealed record JsonMetadataCatalog(
    ViewCreationPreflightResult Name,
    IReadOnlyList<ViewCreationPreflightResult?> Grids);
