namespace DesignerIA.Contracts;

/// <summary>
/// Estado de revisión de una entrada de Curated Knowledge. Pending nunca participa
/// en search_knowledge; sólo Approved lo hace; Rejected queda excluida de forma
/// permanente (pero no se elimina físicamente, para conservar trazabilidad).
/// </summary>
public enum CuratedKnowledgeStatus
{
    Pending,
    Approved,
    Rejected,
}
