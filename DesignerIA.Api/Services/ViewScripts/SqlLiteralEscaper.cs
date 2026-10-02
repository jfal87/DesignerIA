namespace DesignerIA.Api.Services.ViewScripts;

/// <summary>
/// Escapado explícito de literales de texto SQL Server (duplica comillas simples,
/// según el estándar T-SQL: O'Brien -> O''Brien). Ningún valor de texto proveniente
/// de un <c>ViewSpec</c> puede terminar dentro de un script generado sin pasar por
/// aquí primero. No interpreta el valor como SQL: sólo lo convierte en un literal
/// de texto seguro entre comillas simples.
/// </summary>
public static class SqlLiteralEscaper
{
    /// <summary>
    /// Devuelve el valor listo para usarse entre comillas simples en SQL, por
    /// ejemplo: <c>EscapeLiteral("O'Brien")</c> devuelve <c>O''Brien</c>. El
    /// llamador sigue siendo responsable de envolver el resultado entre comillas.
    /// </summary>
    public static string EscapeLiteral(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
