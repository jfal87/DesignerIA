using System.Diagnostics;
using System.Text.Json;
using DesignerIA.Contracts;

namespace DesignerIA.Api.Services.Knowledge;

/// <summary>
/// Reconstruye el índice local de KnowledgeSource bajo demanda (no en cada pregunta
/// del chat). Escanea únicamente C:\Code\DesignerAI\KnowledgeSource, aplica la
/// exclusión por nombre sensible, extrae y fragmenta los formatos soportados, y
/// persiste el resultado como JSONL. No usa SQL ni ninguna base de datos nueva.
/// </summary>
public class KnowledgeIndexService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly IHostEnvironment _environment;
    private readonly ILogger<KnowledgeIndexService> _logger;

    public KnowledgeIndexService(IHostEnvironment environment, ILogger<KnowledgeIndexService> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public Task<KnowledgeRebuildResult> RebuildAsync(CancellationToken cancellationToken = default)
    {
        var sourceRoot = KnowledgePaths.GetSourceRoot(_environment);
        var indexFilePath = KnowledgePaths.GetIndexFilePath(_environment);
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Knowledge index rebuild started");

        if (!Directory.Exists(sourceRoot))
        {
            _logger.LogWarning("Knowledge source folder not found");
            return Task.FromResult(new KnowledgeRebuildResult("error", 0, 0, 0, 0, 0, stopwatch.ElapsedMilliseconds, Error: "No se encontró la carpeta KnowledgeSource."));
        }

        var filesScanned = 0;
        var filesIndexed = 0;
        var filesExcluded = 0;
        var filesUnsupported = 0;
        var chunksCreated = 0;
        var msgIndexed = 0;
        var pdfIndexed = 0;
        var msgFailed = 0;
        var pdfFailed = 0;

        Directory.CreateDirectory(Path.GetDirectoryName(indexFilePath)!);

        using (var writer = new StreamWriter(indexFilePath, append: false))
        {
            foreach (var filePath in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                filesScanned++;

                var relativePath = Path.GetRelativePath(sourceRoot, filePath);

                if (KnowledgeSensitiveNameFilter.IsExcluded(relativePath))
                {
                    filesExcluded++;
                    continue;
                }

                var extension = Path.GetExtension(filePath);
                var extensionLower = extension.ToLowerInvariant();
                if (!KnowledgeExtractorRegistry.IsSupported(extension))
                {
                    filesUnsupported++;
                    _logger.LogDebug("Knowledge file skipped: unsupported extension {Extension}", extension);
                    continue;
                }

                try
                {
                    var sections = KnowledgeExtractorRegistry.Extract(filePath, extension);
                    var pieces = KnowledgeChunker.Split(sections);

                    if (pieces.Count == 0)
                    {
                        filesUnsupported++;
                        if (extensionLower == ".msg")
                        {
                            msgFailed++;
                        }
                        else if (extensionLower == ".pdf")
                        {
                            pdfFailed++;
                        }
                        continue;
                    }

                    var sourceFile = Path.GetFileName(filePath);
                    var chunkIndex = 0;
                    foreach (var piece in pieces)
                    {
                        var chunk = new KnowledgeChunk(
                            Id: $"{relativePath}::chunk-{chunkIndex}",
                            SourceFile: sourceFile,
                            RelativePath: relativePath,
                            SourceType: extension.TrimStart('.').ToUpperInvariant(),
                            Title: piece.Heading ?? Path.GetFileNameWithoutExtension(sourceFile),
                            Content: piece.Content,
                            ChunkIndex: chunkIndex,
                            SourceDate: piece.SourceDate,
                            Heading: piece.Heading);

                        writer.WriteLine(JsonSerializer.Serialize(chunk, SerializerOptions));
                        chunkIndex++;
                        chunksCreated++;
                    }

                    filesIndexed++;
                    if (extensionLower == ".msg")
                    {
                        msgIndexed++;
                    }
                    else if (extensionLower == ".pdf")
                    {
                        pdfIndexed++;
                    }
                }
                catch (Exception ex)
                {
                    filesUnsupported++;
                    if (extensionLower == ".msg")
                    {
                        msgFailed++;
                    }
                    else if (extensionLower == ".pdf")
                    {
                        pdfFailed++;
                    }
                    _logger.LogWarning(ex, "Failed to extract knowledge file. Extension: {Extension}", extension);
                }
            }
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Knowledge index rebuild completed in {ElapsedMs} ms. FilesScanned: {FilesScanned}, FilesIndexed: {FilesIndexed}, FilesExcluded: {FilesExcluded}, FilesUnsupported: {FilesUnsupported}, ChunksCreated: {ChunksCreated}, MsgIndexed: {MsgIndexed}, PdfIndexed: {PdfIndexed}, MsgFailed: {MsgFailed}, PdfFailed: {PdfFailed}",
            stopwatch.ElapsedMilliseconds,
            filesScanned,
            filesIndexed,
            filesExcluded,
            filesUnsupported,
            chunksCreated,
            msgIndexed,
            pdfIndexed,
            msgFailed,
            pdfFailed);

        return Task.FromResult(new KnowledgeRebuildResult(
            "ok", filesScanned, filesIndexed, filesExcluded, filesUnsupported, chunksCreated, stopwatch.ElapsedMilliseconds,
            MsgIndexed: msgIndexed, PdfIndexed: pdfIndexed, MsgFailed: msgFailed, PdfFailed: pdfFailed));
    }
}
