namespace DesignerIA.Contracts;

/// <summary>
/// Resultado de intentar transformar un <see cref="ViewSpec"/> en un script SQL
/// determinístico mediante <c>ViewScriptGenerator</c>. El script nunca se ejecuta:
/// es únicamente texto para previsualización humana. <paramref name="Script"/> es
/// null cuando <paramref name="CanGenerate"/> es false; en ese caso
/// <paramref name="Gaps"/> explica qué falta, ya sea un dato del ViewSpec o
/// conocimiento/soporte del generador (nunca se mezclan ambos conceptos en el mismo
/// mensaje).
/// </summary>
public record ViewScriptGenerationResult(
    bool CanGenerate,
    string? Script,
    IReadOnlyList<ViewScriptGenerationGap> Gaps,
    ViewScriptGenerationSummary Summary,
    ViewCreationPreflightResult? Preflight = null);
