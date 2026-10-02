namespace DesignerIA.Api.Services.Knowledge;

/// <summary>
/// Divide las secciones extraídas de un documento en fragmentos de tamaño razonable
/// para enviar pocos resultados a Copilot. No usa IA para el chunking: es una
/// división determinística por tamaño de caracteres, con un pequeño overlap sólo
/// cuando una sección debe partirse a la fuerza.
/// </summary>
public static class KnowledgeChunker
{
    public const int MaxChunkChars = 1200;
    public const int OverlapChars = 150;

    /// <summary>
    /// Devuelve pares (Heading, Content) listos para convertirse en KnowledgeChunk.
    /// </summary>
    public static IReadOnlyList<(string? Heading, string Content, string? SourceDate)> Split(IReadOnlyList<KnowledgeSection> sections)
    {
        var result = new List<(string? Heading, string Content, string? SourceDate)>();

        foreach (var section in sections)
        {
            var content = section.Content.Trim();
            if (content.Length == 0)
            {
                continue;
            }

            if (content.Length <= MaxChunkChars)
            {
                result.Add((section.Heading, content, section.SourceDate));
                continue;
            }

            var start = 0;
            while (start < content.Length)
            {
                var length = Math.Min(MaxChunkChars, content.Length - start);
                var piece = content.Substring(start, length).Trim();
                if (piece.Length > 0)
                {
                    result.Add((section.Heading, piece, section.SourceDate));
                }

                if (start + length >= content.Length)
                {
                    break;
                }

                start += length - OverlapChars;
            }
        }

        return result;
    }
}
