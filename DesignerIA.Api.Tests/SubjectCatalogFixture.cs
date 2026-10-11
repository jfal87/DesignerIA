using DesignerIA.Api.Services;
using DesignerIA.Contracts;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Tests;

internal sealed class SubjectCatalogFixture : IConversationSubjectCatalog
{
    public IReadOnlyList<ResolvedConversationSubject> Handlers { get; init; } = [];
    public IReadOnlyList<KnowledgeSearchResultItem> Documents { get; init; } = [];
    public int LiveLookups { get; private set; }
    public int DocumentLookups { get; private set; }

    public Task<IReadOnlyList<ResolvedConversationSubject>> FindHandlersAsync(string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LiveLookups++;
        return Task.FromResult(Handlers);
    }

    public IReadOnlyList<KnowledgeSearchResultItem> FindDocuments(string name)
    {
        DocumentLookups++;
        return Documents;
    }

    public static ResolvedConversationSubject Handler(string name, int id = 1) =>
        new(new ActiveSubject("Handler", name), "Live", $"AccionesHandler: {id}", true);

    public static SubjectCatalogFixture ExistingHandlers() => new()
    {
        Handlers = [Handler("HandlerA"), Handler("HandlerB"), Handler("HandlerC"),
            Handler("HTMLEnCeldaAdvanced"), Handler("HTMLEnCeldaAdvanced_ControlRoom"), Handler("botonUltimoNivel")]
    };
}
