using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using DesignerIA.Api.Services.CuratedKnowledge;
using DesignerIA.Contracts;

namespace DesignerIA.Api.Services.Knowledge;

/// <summary>
/// Búsqueda léxica simple y determinística sobre el índice local generado por
/// <see cref="KnowledgeIndexService"/>. No usa embeddings ni IA: pondera coincidencias
/// de texto en Title, SourceFile y Content, normalizando mayúsculas/minúsculas y
/// acentos. Sólo devuelve los pocos fragmentos más relevantes, nunca documentos
/// completos.
/// </summary>
public class KnowledgeSearchService
{
    private const int TopResultCount = 5;
    private const int SnippetRadius = 160;
    private const string CuratedSentinelSourceType = "CURATED";
    private const string CuratedDisplaySourceFile = "Conocimiento del equipo";

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHostEnvironment _environment;
    private readonly ILogger<KnowledgeSearchService> _logger;
    private readonly CuratedKnowledgeService _curatedKnowledgeService;

    public KnowledgeSearchService(
        IHostEnvironment environment,
        ILogger<KnowledgeSearchService> logger,
        CuratedKnowledgeService curatedKnowledgeService)
    {
        _environment = environment;
        _logger = logger;
        _curatedKnowledgeService = curatedKnowledgeService;
    }

    public IReadOnlyList<KnowledgeSearchResultItem> Search(string query)
    {
        var stopwatch = Stopwatch.StartNew();
        // No se registra la query completa: sólo su longitud (evita loguear texto potencialmente sensible).
        _logger.LogInformation("Knowledge search started. QueryLength: {QueryLength}", query?.Length ?? 0);

        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var approvedEntries = _curatedKnowledgeService.GetApproved();
        var curatedById = approvedEntries.ToDictionary(e => e.Id, e => e);
        var chunks = LoadIndex();
        chunks.AddRange(approvedEntries.Select(ToChunk));

        if (chunks.Count == 0)
        {
            _logger.LogInformation("Knowledge search completed in {ElapsedMs} ms. ResultsCount: 0 (empty index)", stopwatch.ElapsedMilliseconds);
            return [];
        }

        var normalizedQuery = Normalize(query);
        var tokens = Tokenize(normalizedQuery);
        if (tokens.Count == 0)
        {
            return [];
        }

        var scored = new List<(KnowledgeChunk Chunk, double Score)>();
        foreach (var chunk in chunks)
        {
            var normalizedTitle = Normalize(chunk.Title);
            var normalizedFile = Normalize(chunk.SourceFile);
            var normalizedContent = Normalize(chunk.Content);

            double score = 0;
            if (normalizedTitle.Contains(normalizedQuery, StringComparison.Ordinal))
            {
                score += 10;
            }

            foreach (var token in tokens)
            {
                if (normalizedTitle.Contains(token, StringComparison.Ordinal))
                {
                    score += 3;
                }

                if (normalizedFile.Contains(token, StringComparison.Ordinal))
                {
                    score += 2;
                }

                var occurrences = CountOccurrences(normalizedContent, token);
                if (occurrences > 0)
                {
                    score += Math.Min(occurrences, 5);
                }
            }

            if (score > 0)
            {
                scored.Add((chunk, score));
            }
        }

        var results = scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Chunk.RelativePath, StringComparer.Ordinal)
            .Where(WithSourceFileDiversity())
            .Take(TopResultCount)
            .Select(s => MapResult(s.Chunk, s.Score, tokens, curatedById))
            .ToList();

        stopwatch.Stop();
        var curatedResultsCount = results.Count(r => r.SourceType == "Curated");
        _logger.LogInformation(
            "Knowledge search completed in {ElapsedMs} ms. ResultsCount: {ResultsCount}, CuratedResultsCount: {CuratedResultsCount}",
            stopwatch.ElapsedMilliseconds,
            results.Count,
            curatedResultsCount);

        return results;
    }

    private static KnowledgeChunk ToChunk(CuratedKnowledgeEntry entry)
    {
        var content = string.IsNullOrWhiteSpace(entry.Example)
            ? entry.Content
            : $"{entry.Content}\nEjemplo: {entry.Example}";

        return new KnowledgeChunk(
            Id: entry.Id,
            SourceFile: $"curated:{entry.Id}",
            RelativePath: $"curated:{entry.Id}",
            SourceType: CuratedSentinelSourceType,
            Title: entry.Topic,
            Content: content,
            ChunkIndex: 0,
            SourceDate: entry.CreatedAtUtc.ToString("O"),
            Heading: null);
    }

    private static KnowledgeSearchResultItem MapResult(
        KnowledgeChunk chunk,
        double score,
        IReadOnlyList<string> tokens,
        IReadOnlyDictionary<string, CuratedKnowledgeEntry> curatedById)
    {
        if (chunk.SourceType == CuratedSentinelSourceType && curatedById.TryGetValue(chunk.Id, out var entry))
        {
            return new KnowledgeSearchResultItem(
                Title: chunk.Title,
                SourceFile: CuratedDisplaySourceFile,
                RelativePath: chunk.RelativePath,
                Snippet: BuildSnippet(chunk.Content, tokens),
                Score: score,
                SourceType: "Curated",
                Contributor: entry.Contributor);
        }

        return new KnowledgeSearchResultItem(
            Title: chunk.Title,
            SourceFile: chunk.SourceFile,
            RelativePath: chunk.RelativePath,
            Snippet: BuildSnippet(chunk.Content, tokens),
            Score: score,
            SourceType: "Document",
            Contributor: null);
    }

    /// <summary>
    /// Regla simple de diversidad: si un mismo SourceFile domina el ranking (por
    /// ejemplo, varios chunks casi idénticos de un hilo de correo), se limita a lo
    /// sumo 3 chunks de ese archivo dentro del top final, dejando espacio para otros
    /// documentos relevantes. No es un algoritmo de re-ranking complejo.
    /// </summary>
    private static Func<(KnowledgeChunk Chunk, double Score), bool> WithSourceFileDiversity()
    {
        const int maxPerSourceFile = 3;
        var countsBySourceFile = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        return candidate =>
        {
            var count = countsBySourceFile.GetValueOrDefault(candidate.Chunk.SourceFile);
            if (count >= maxPerSourceFile)
            {
                return false;
            }

            countsBySourceFile[candidate.Chunk.SourceFile] = count + 1;
            return true;
        };
    }

    private List<KnowledgeChunk> LoadIndex()
    {
        var indexFilePath = KnowledgePaths.GetIndexFilePath(_environment);
        if (!File.Exists(indexFilePath))
        {
            return [];
        }

        var chunks = new List<KnowledgeChunk>();
        foreach (var line in File.ReadLines(indexFilePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var chunk = JsonSerializer.Deserialize<KnowledgeChunk>(line, SerializerOptions);
            if (chunk is not null)
            {
                chunks.Add(chunk);
            }
        }

        return chunks;
    }

    private static string BuildSnippet(string content, IReadOnlyList<string> tokens)
    {
        var normalizedContent = Normalize(content);
        var matchIndex = -1;
        foreach (var token in tokens)
        {
            var index = normalizedContent.IndexOf(token, StringComparison.Ordinal);
            if (index >= 0)
            {
                matchIndex = index;
                break;
            }
        }

        if (matchIndex < 0)
        {
            return content.Length <= SnippetRadius * 2
                ? content
                : content[..(SnippetRadius * 2)].Trim() + "…";
        }

        var start = Math.Max(0, matchIndex - SnippetRadius);
        var end = Math.Min(content.Length, matchIndex + SnippetRadius);
        var snippet = content[start..end].Trim();
        return (start > 0 ? "…" : string.Empty) + snippet + (end < content.Length ? "…" : string.Empty);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        if (needle.Length == 0)
        {
            return 0;
        }

        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    private static List<string> Tokenize(string normalizedQuery)
    {
        return normalizedQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2)
            .Distinct()
            .ToList();
    }

    private static string Normalize(string value)
    {
        var lower = value.ToLowerInvariant();
        var formD = lower.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(c) || c == ' ' ? c : ' ');
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
