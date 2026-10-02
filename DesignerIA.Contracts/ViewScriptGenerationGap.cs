namespace DesignerIA.Contracts;

/// <summary>
/// Concepto puntual para el que <see cref="ViewScriptGenerator"/> no tiene todavía
/// soporte o evidencia suficiente para producir SQL de forma segura (por ejemplo un
/// tipo de área no soportado en V1, o un dato del ViewSpec que falta para poder
/// generar). No se convierte automáticamente en Curated Knowledge ni en Pending:
/// es sólo información estructurada devuelta al llamador.
/// </summary>
public record ViewScriptGenerationGap(string Topic, string Description);
