namespace DesignerIA.Contracts;

/// <summary>
/// Conteos simples de la forma del <see cref="ViewSpec"/> procesado, útiles para
/// mostrar en la UI y para logging sin exponer el contenido completo.
/// </summary>
public record ViewScriptGenerationSummary(int Views, int States, int Rows, int Areas, int Handlers);
