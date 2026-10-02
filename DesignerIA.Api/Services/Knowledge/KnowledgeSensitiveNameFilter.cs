using System.Globalization;
using System.Text;

namespace DesignerIA.Api.Services.Knowledge;

/// <summary>
/// Exclusión simple y explícita basada en el nombre/ruta del archivo. Si el nombre
/// sugiere información de acceso sensible, el archivo se excluye por completo del
/// índice sin abrirlo para verificar su contenido ("si existe duda, excluir").
/// </summary>
public static class KnowledgeSensitiveNameFilter
{
    private static readonly string[] Keywords =
    [
        "password", "passwords", "credencial", "credential", "secret",
        "token", "cyberark", "connectionstring", "connection string",
        "clave", "claves", "acceso productivo",
    ];

    public static bool IsExcluded(string relativePath)
    {
        var normalized = Normalize(relativePath);
        return Keywords.Any(keyword => normalized.Contains(Normalize(keyword), StringComparison.Ordinal));
    }

    private static string Normalize(string value)
    {
        var lower = value.ToLowerInvariant();
        var formD = lower.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(formD.Length);
        foreach (var c in formD)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
