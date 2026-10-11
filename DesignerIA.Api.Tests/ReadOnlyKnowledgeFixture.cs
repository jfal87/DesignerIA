using System.Text.Json;
using DesignerIA.Api.Services;
using DesignerIA.Api.Services.CuratedKnowledge;
using DesignerIA.Api.Services.Knowledge;
using DesignerIA.Contracts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DesignerIA.Api.Tests;

// Reads the checkpoint's existing local corpus; never rebuilds or writes its index.
internal sealed class ReadOnlyKnowledgeFixture
{
    private static readonly string ApiRoot = Path.Combine(
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..")), "DesignerIA.Api");
    private static readonly Lazy<IReadOnlyDictionary<string, KnowledgeChunk>> Chunks = new(() =>
        File.ReadLines(Path.Combine(ApiRoot, "Data", "Knowledge", "knowledge-index.jsonl"))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<KnowledgeChunk>(line, new JsonSerializerOptions(JsonSerializerDefaults.Web))!)
            .ToDictionary(chunk => chunk.Id));

    public KnowledgeSearchService Search { get; }

    public ReadOnlyKnowledgeFixture()
    {
        foreach (var status in Enum.GetValues<CuratedKnowledgeStatus>())
        {
            Assert.True(Directory.Exists(Path.Combine(ApiRoot, "App_Data", "CuratedKnowledge", status.ToString())),
                "The read-only corpus fixture requires the existing curated directories.");
        }
        var environment = new TestEnvironment();
        var curated = new CuratedKnowledgeService(environment, NullLogger<CuratedKnowledgeService>.Instance);
        Search = new KnowledgeSearchService(environment, NullLogger<KnowledgeSearchService>.Instance, curated);
    }

    public static KnowledgeSearchResultItem Document(string id)
    {
        var chunk = Chunks.Value[id];
        return new(chunk.Title, chunk.SourceFile, chunk.RelativePath, chunk.Content, 1);
    }

    public CopilotChatService Chat(ConversationContextStore store)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:GestionEngineLocal"] = "Server=unused;Database=unused;Integrated Security=True"
        }).Build();
        // These constructors only parse configuration; the tested evidence formatter never opens SQL or SDK sessions.
        return new(new GestionEngineHealthService(configuration, NullLogger<GestionEngineHealthService>.Instance), store,
            new HandlerExamplesService(configuration, NullLogger<HandlerExamplesService>.Instance),
            new GestionEngineMetadataService(configuration, NullLogger<GestionEngineMetadataService>.Instance),
            Search, new CopilotClientProvider(), NullLogger<CopilotChatService>.Instance);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "DesignerIA.Api";
        public string ContentRootPath { get; set; } = ApiRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
