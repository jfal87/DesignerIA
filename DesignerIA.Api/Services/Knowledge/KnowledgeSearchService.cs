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
    // The chat retrieval asks for a wider pool and lets the evidence policy choose; other callers keep the short list.
    internal const int WideResultCount = 10;
    private const int SnippetRadius = 600;
    private const string CuratedSentinelSourceType = "CURATED";
    private const string CuratedDisplaySourceFile = "Conocimiento del equipo";
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "al", "ahora", "como", "con", "cual", "de", "del", "desde", "donde", "el", "en",
        "es", "esta", "estas", "este", "estos", "la", "las", "lo", "los", "me", "mi", "mis",
        "no", "para", "por", "puedo", "que", "quiero", "saber", "se", "segun", "su", "sus",
        "un", "una", "unas", "unos", "y", "documentacion", "documentado", "documentada", "documentados", "documentadas",
        // Question scaffolding: these words shape the sentence, they do not name what is asked.
        "puede", "pueden", "podria", "poner", "pongo", "hacer", "hago", "hace", "tengo", "tiene", "hay", "sirve",
        "necesito", "dame", "dime", "cuando", "cuales", "son", "ser", "sobre", "si", "le", "les", "o", "ya"
    };

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

    public IReadOnlyList<KnowledgeSearchResultItem> Search(string query) => SearchCore(query, TopResultCount);

    internal IReadOnlyList<KnowledgeSearchResultItem> SearchWide(string query) => SearchCore(query, WideResultCount);

    private IReadOnlyList<KnowledgeSearchResultItem> SearchCore(string query, int maxResults)
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
        var chunks = LoadIndex().Concat(approvedEntries.Select(entry => Indexed(ToChunk(entry)))).ToList();

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

        // Each query term counts once per chunk and weighs more the rarer it is in the corpus, so a distinctive
        // term in the body outranks a generic word that happens to be in many titles. The file name counts like the
        // title: for mails and attachments it is the descriptive name given by the team.
        var matches = new List<(KnowledgeChunk Chunk, bool TitleHasQuery, bool[] Named, int[] Occurrences)>();
        var documentFrequency = new int[tokens.Count];
        foreach (var (chunk, normalizedTitle, normalizedFile, normalizedContent) in chunks)
        {
            if (KnowledgeEvidencePolicy.IsInternalWikiTitle(chunk.Title))
            {
                continue;
            }
            var named = new bool[tokens.Count];
            var occurrences = new int[tokens.Count];
            var any = false;
            for (var index = 0; index < tokens.Count; index++)
            {
                named[index] = CountTerm(normalizedTitle, tokens[index]) > 0 || CountTerm(normalizedFile, tokens[index]) > 0;
                occurrences[index] = CountTerm(normalizedContent, tokens[index]);
                if (named[index] || occurrences[index] > 0)
                {
                    documentFrequency[index]++;
                    any = true;
                }
            }
            if (any)
            {
                matches.Add((chunk, normalizedTitle.Contains(normalizedQuery, StringComparison.Ordinal), named, occurrences));
            }
        }

        var weights = documentFrequency.Select(frequency => Math.Log(1 + chunks.Count / (1.0 + frequency))).ToArray();
        var rarestFirst = tokens.Select((token, index) => (token, Weight: weights[index]))
            .OrderByDescending(entry => entry.Weight).Select(entry => entry.token).ToArray();
        var scored = new List<(KnowledgeChunk Chunk, double Score)>(matches.Count);
        foreach (var match in matches)
        {
            double score = 0;
            for (var index = 0; index < tokens.Count; index++)
            {
                if (match.Named[index] || match.Occurrences[index] > 0)
                {
                    score += weights[index] * (1 + (match.Named[index] ? 1 : 0) + Math.Min(match.Occurrences[index], 3) * 0.25);
                }
            }
            scored.Add((match.Chunk, match.TitleHasQuery ? score * 1.25 : score));
        }

        var results = scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Chunk.RelativePath, StringComparer.Ordinal)
            .Where(WithSourceFileDiversity())
            .Take(maxResults)
            .Select(s => MapResult(s.Chunk, s.Score, rarestFirst, curatedById))
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

    /// <summary>
    /// Rarity of each term of a question in the whole corpus. The evidence policy uses it to tell the words that
    /// identify a topic from the ones any mail contains ("necesito", "agregar").
    /// </summary>
    internal IReadOnlyDictionary<string, double> TermWeights(string text)
    {
        var tokens = Tokenize(Normalize(text ?? string.Empty));
        var weights = new Dictionary<string, double>(StringComparer.Ordinal);
        if (tokens.Count == 0) { return weights; }
        var frequency = new int[tokens.Count];
        var total = 0;
        foreach (var (chunk, title, file, content) in LoadIndex())
        {
            if (KnowledgeEvidencePolicy.IsInternalWikiTitle(chunk.Title)) { continue; }
            total++;
            for (var index = 0; index < tokens.Count; index++)
            {
                if (CountTerm(content, tokens[index]) > 0 || CountTerm(title, tokens[index]) > 0
                    || CountTerm(file, tokens[index]) > 0) { frequency[index]++; }
            }
        }
        for (var index = 0; index < tokens.Count; index++)
        {
            weights[tokens[index]] = Math.Log(1 + total / (1.0 + frequency[index]));
        }
        return weights;
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

    // A chunk with its title, file name and content already normalized for matching.
    private sealed record IndexedChunk(KnowledgeChunk Chunk, string Title, string File, string Content);

    private static IndexedChunk Indexed(KnowledgeChunk chunk) =>
        new(chunk, Normalize(chunk.Title), Normalize(chunk.SourceFile), Normalize(chunk.Content));

    private static readonly object IndexLock = new();
    private static (string Path, DateTime Stamp, long Length, IndexedChunk[] Chunks)? _loadedIndex;

    /// <summary>
    /// The index file is parsed and normalized once per version of the file. A documentary question runs four
    /// searches plus the term weights, and each of them used to read and normalize the whole corpus again.
    /// </summary>
    private IndexedChunk[] LoadIndex()
    {
        var indexFilePath = KnowledgePaths.GetIndexFilePath(_environment);
        var file = new FileInfo(indexFilePath);
        if (!file.Exists)
        {
            return [];
        }

        lock (IndexLock)
        {
            var stamp = file.LastWriteTimeUtc;
            var length = file.Length;
            if (_loadedIndex is { } loaded && loaded.Path == indexFilePath && loaded.Stamp == stamp && loaded.Length == length)
            {
                return loaded.Chunks;
            }
            var index = ReadIndex(indexFilePath);
            _loadedIndex = (indexFilePath, stamp, length, index);
            return index;
        }
    }

    private static IndexedChunk[] ReadIndex(string indexFilePath)
    {
        var chunks = new List<IndexedChunk>();
        foreach (var line in File.ReadLines(indexFilePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var chunk = JsonSerializer.Deserialize<KnowledgeChunk>(line, SerializerOptions);
            if (chunk is not null)
            {
                chunks.Add(Indexed(chunk));
            }
        }

        return chunks.ToArray();
    }

    private static string BuildSnippet(string content, IReadOnlyList<string> tokens)
    {
        // Index chunks are about one snippet long: cutting them around a token only loses evidence.
        if (content.Length <= SnippetRadius * 2 + 200)
        {
            return content;
        }

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

    /// <summary>
    /// Occurrences of a query term in normalized text. A short term ("eje", "fin", "sp") only counts as a whole
    /// word: as a substring it appears inside "ejemplo" or "define" and stops identifying anything.
    /// </summary>
    internal static int CountTerm(string normalizedText, string term)
    {
        if (term.Length > 3 && term.Length < 8) { return CountOccurrences(normalizedText, term); }
        var count = 0;
        var index = 0;
        if (term.Length >= 8)
        {
            // A long term also matches by its stem at the start of a word, so number, gender and close cognates
            // still count ("referencia" / "Reference", "comentarios" / "comentario") without matching the stem
            // inside another word ("relaciones" / "correlación").
            var stem = term[..^2];
            while ((index = normalizedText.IndexOf(stem, index, StringComparison.Ordinal)) >= 0)
            {
                if (index == 0 || normalizedText[index - 1] == ' '
                    || string.CompareOrdinal(normalizedText, index, term, 0, term.Length) == 0) { count++; }
                index += stem.Length;
            }
            return count;
        }
        while ((index = normalizedText.IndexOf(term, index, StringComparison.Ordinal)) >= 0)
        {
            var end = index + term.Length;
            if ((index == 0 || normalizedText[index - 1] == ' ') && (end == normalizedText.Length || normalizedText[end] == ' ')) { count++; }
            index = end;
        }
        return count;
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

    internal static List<string> Tokenize(string normalizedQuery)
    {
        return normalizedQuery
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2 && !StopWords.Contains(t))
            .Select(t => t == "js" ? "javascript" : t)
            .Distinct()
            .ToList();
    }

    internal static string Normalize(string value)
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
