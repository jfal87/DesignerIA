using System.Text.Json;
using System.Text.Json.Serialization;
using DesignerIA.Contracts;

namespace DesignerIA.Api.Services.CuratedKnowledge;

/// <summary>
/// Persiste Curated Knowledge como un archivo JSON por entrada, dentro de
/// App_Data\CuratedKnowledge\{Pending|Approved|Rejected}\{id}.json. El Id lo genera
/// siempre la aplicación (Guid); nunca se usa Topic, Contributor ni ningún texto de
/// usuario como ruta o nombre de archivo, para evitar path traversal. No usa SQL, no
/// crea tablas ni modifica GestionEngine. No hay delete físico: rechazar mueve la
/// entrada a la carpeta Rejected conservando su contenido para trazabilidad.
///
/// Registrado como singleton en Program.cs: un único <see cref="_fileLock"/> protege
/// las transiciones de Status (mover un archivo entre carpetas) de condiciones de
/// carrera entre requests concurrentes.
/// </summary>
public class CuratedKnowledgeService
{
    private const int MaxTopicLength = 200;
    private const int MaxContentLength = 4000;
    private const int MaxExampleLength = 4000;
    private const int MaxAttributionLength = 200;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IHostEnvironment _environment;
    private readonly ILogger<CuratedKnowledgeService> _logger;
    private readonly object _fileLock = new();

    public CuratedKnowledgeService(IHostEnvironment environment, ILogger<CuratedKnowledgeService> logger)
    {
        _environment = environment;
        _logger = logger;

        foreach (CuratedKnowledgeStatus status in Enum.GetValues<CuratedKnowledgeStatus>())
        {
            Directory.CreateDirectory(CuratedKnowledgePaths.GetFolder(_environment, status));
        }
    }

    public CuratedKnowledgeResult Create(CuratedKnowledgeCreateRequest request)
    {
        var topic = request.Topic?.Trim();
        var content = request.Content?.Trim();
        var example = string.IsNullOrWhiteSpace(request.Example) ? null : request.Example.Trim();
        var contributor = request.Contributor?.Trim();

        if (string.IsNullOrWhiteSpace(topic) || topic.Length > MaxTopicLength)
        {
            return new CuratedKnowledgeResult("error", null, $"Topic es requerido (máximo {MaxTopicLength} caracteres).");
        }

        if (string.IsNullOrWhiteSpace(content) || content.Length > MaxContentLength)
        {
            return new CuratedKnowledgeResult("error", null, $"Content es requerido (máximo {MaxContentLength} caracteres).");
        }

        if (example is { Length: > MaxExampleLength })
        {
            return new CuratedKnowledgeResult("error", null, $"Example debe tener como máximo {MaxExampleLength} caracteres.");
        }

        if (string.IsNullOrWhiteSpace(contributor) || contributor.Length > MaxAttributionLength)
        {
            return new CuratedKnowledgeResult("error", null, "Contributor es requerido.");
        }

        var entry = new CuratedKnowledgeEntry(
            Id: Guid.NewGuid().ToString("N"),
            Topic: topic,
            Content: content,
            Example: example,
            Contributor: contributor,
            Status: CuratedKnowledgeStatus.Pending,
            CreatedAtUtc: DateTime.UtcNow,
            ReviewedBy: null,
            ReviewedAtUtc: null);

        lock (_fileLock)
        {
            Save(entry);
        }

        _logger.LogInformation(
            "Curated knowledge entry created. Id: {Id}, Status: {Status}, TopicLength: {TopicLength}, ContentLength: {ContentLength}",
            entry.Id,
            entry.Status,
            entry.Topic.Length,
            entry.Content.Length);

        return new CuratedKnowledgeResult("ok", entry, null);
    }

    public CuratedKnowledgeListResult List(CuratedKnowledgeStatus status)
    {
        var entries = ReadFolder(status);
        return new CuratedKnowledgeListResult("ok", entries.OrderByDescending(e => e.CreatedAtUtc).ToList(), null);
    }

    /// <summary>
    /// Únicamente entradas Approved: es lo único que <c>KnowledgeSearchService</c>
    /// puede recuperar. Se lee directamente del filesystem en cada búsqueda (sin
    /// caché) para que una aprobación quede disponible de inmediato, sin necesidad
    /// de reconstruir el índice documental completo.
    /// </summary>
    public IReadOnlyList<CuratedKnowledgeEntry> GetApproved()
    {
        return ReadFolder(CuratedKnowledgeStatus.Approved);
    }

    public CuratedKnowledgeResult Approve(string id, string reviewedBy)
    {
        return TransitionStatus(id, CuratedKnowledgeStatus.Approved, reviewedBy, [CuratedKnowledgeStatus.Pending]);
    }

    /// <summary>
    /// Rechaza una entrada Pending o desactiva una entrada Approved. En ambos casos
    /// el resultado es Rejected: nunca vuelve a participar en search_knowledge.
    /// </summary>
    public CuratedKnowledgeResult Reject(string id, string reviewedBy)
    {
        return TransitionStatus(id, CuratedKnowledgeStatus.Rejected, reviewedBy, [CuratedKnowledgeStatus.Pending, CuratedKnowledgeStatus.Approved]);
    }

    private CuratedKnowledgeResult TransitionStatus(
        string id,
        CuratedKnowledgeStatus newStatus,
        string reviewedBy,
        IReadOnlyList<CuratedKnowledgeStatus> allowedCurrentStatuses)
    {
        if (!IsValidId(id))
        {
            return new CuratedKnowledgeResult("error", null, "Id inválido.");
        }

        var trimmedReviewedBy = reviewedBy?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedReviewedBy) || trimmedReviewedBy.Length > MaxAttributionLength)
        {
            return new CuratedKnowledgeResult("error", null, "ReviewedBy es requerido.");
        }

        lock (_fileLock)
        {
            CuratedKnowledgeEntry? current = null;
            string? currentPath = null;

            foreach (var status in allowedCurrentStatuses)
            {
                var path = Path.Combine(CuratedKnowledgePaths.GetFolder(_environment, status), $"{id}.json");
                if (File.Exists(path))
                {
                    current = ReadEntry(path);
                    currentPath = path;
                    break;
                }
            }

            if (current is null || currentPath is null)
            {
                return new CuratedKnowledgeResult("error", null, "No se encontró la entrada en un estado válido para esta acción.");
            }

            var updated = current with
            {
                Status = newStatus,
                ReviewedBy = trimmedReviewedBy,
                ReviewedAtUtc = DateTime.UtcNow,
            };

            Save(updated);
            File.Delete(currentPath);

            _logger.LogInformation(
                "Curated knowledge entry reviewed. Id: {Id}, NewStatus: {NewStatus}",
                id,
                newStatus);

            return new CuratedKnowledgeResult("ok", updated, null);
        }
    }

    private List<CuratedKnowledgeEntry> ReadFolder(CuratedKnowledgeStatus status)
    {
        var folder = CuratedKnowledgePaths.GetFolder(_environment, status);
        var entries = new List<CuratedKnowledgeEntry>();

        if (!Directory.Exists(folder))
        {
            return entries;
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
        {
            var entry = ReadEntry(file);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    private void Save(CuratedKnowledgeEntry entry)
    {
        var folder = CuratedKnowledgePaths.GetFolder(_environment, entry.Status);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{entry.Id}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(entry, SerializerOptions));
    }

    private static CuratedKnowledgeEntry? ReadEntry(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<CuratedKnowledgeEntry>(File.ReadAllText(path), SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// El Id sólo puede ser un Guid en formato "N" (32 caracteres hexadecimales)
    /// generado por esta misma clase. Se valida estrictamente porque se usa
    /// directamente para construir una ruta de archivo a partir de un parámetro de
    /// URL.
    /// </summary>
    private static bool IsValidId(string? id)
    {
        return !string.IsNullOrEmpty(id) && id.Length == 32 && id.All(Uri.IsHexDigit);
    }
}
