namespace DesignerIA.Contracts;

/// <summary>
/// Entrada de conocimiento curado aportada manualmente por una persona del equipo
/// ("Enseñar a DesignerIA"). DesignerIA no tiene autenticación: <see cref="Contributor"/>
/// y <see cref="ReviewedBy"/> son texto declarado por quien usa la aplicación, nunca
/// una identidad autenticada. El contenido se trata siempre como texto plano: nunca
/// se ejecuta ni se interpreta como código.
/// </summary>
public record CuratedKnowledgeEntry(
    string Id,
    string Topic,
    string Content,
    string? Example,
    string Contributor,
    CuratedKnowledgeStatus Status,
    DateTime CreatedAtUtc,
    string? ReviewedBy,
    DateTime? ReviewedAtUtc);
