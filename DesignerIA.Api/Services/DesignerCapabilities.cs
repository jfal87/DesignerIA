using System.Text.RegularExpressions;

namespace DesignerIA.Api.Services;

internal static class DesignerCapabilities
{
    // Keep aligned with ViewScriptGenerationValidator and the read-only preflight flow.
    public const string Current =
        "DesignerIA puede interpretar solicitudes, generar y validar ViewSpec, preparar una vista vacía con su estructura base, " +
        "comprobar colisiones mediante preflight y generar un script determinístico sin ejecutarlo. " +
        "La asociación completa de un Stored Procedure (SP) todavía no está implementada en el flujo de creación V1. " +
        "Sí puede consultar determinados datos actuales de GestionEngine mediante capabilities live read-only autorizadas. " +
        "No puede ejecutar SQL arbitrario, ejecutar scripts ni crear directamente la vista en GestionEngine.";

    public const string ExecutionBoundary = "No puedo ejecutar SQL arbitrario ni usar libremente la conexión a GestionEngine. "
        + "Sólo consulto datos actuales mediante capabilities live read-only autorizadas.";

    public static bool IsExecutionOverride(string normalizedMessage) =>
        Regex.IsMatch(normalizedMessage, @"\b(executesql|dba|sql server management studio)\b")
        && Regex.IsMatch(normalizedMessage, @"\b(actua|imagina|finge|simula|considera|autoriz\w*)\b");

    public static bool IsQuestion(string normalizedMessage) =>
        Regex.IsMatch(normalizedMessage, @"\b(puedes|eres capaz|capacidades|que sabes hacer|que (cosas )?puedes hacer)\b")
        && !Regex.IsMatch(normalizedMessage, @"\b(llamada|llamado|nombre|denominada)\b")
        && (Regex.IsMatch(normalizedMessage, @"\b(vistas?|sp|sps|stored procedure|preflight|scripts?|colisiones|designeria|gestionengine|ejecutarlo|crearla)\b")
            || Regex.IsMatch(normalizedMessage, @"que (cosas )?puedes hacer"));

    public static string Describe(string normalizedMessage = "")
    {
        if (Regex.IsMatch(normalizedMessage, @"\b(sp|sps|stored procedure)\b"))
        {
            return "Por ahora puedo preparar una vista vacía con su estructura base. "
                + "La asociación completa de un Stored Procedure (SP) todavía no está implementada en el flujo de creación V1.";
        }
        if (Regex.IsMatch(normalizedMessage, @"\b(ejecut\w*|directamente)\b"))
        {
            return "No puedo ejecutar scripts ni crear directamente la vista en GestionEngine. " + ExecutionBoundary;
        }
        if (Regex.IsMatch(normalizedMessage, @"\bscript\b"))
        {
            return "Sí, puedo generar un script determinístico para una vista base validada, después del preflight y la comprobación de colisiones. No lo ejecuto.";
        }
        return "Soy DesignerIA. " + Current;
    }
}
