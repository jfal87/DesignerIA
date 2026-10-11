using System.Text.Json;
using System.Text.RegularExpressions;
using DesignerIA.Contracts;
using static DesignerIA.Api.Services.ConversationContextStore;

namespace DesignerIA.Api.Services;

internal static class ChatResponseFormat
{
    public const string MissingGroundedArtifactMessage = "No tengo un artefacto grounded de ese tipo en la conversación.";

    public static string Resolve(string normalizedMessage, string plannerMode = "explanation")
    {
        var exclusive = Regex.IsMatch(normalizedMessage, @"\b(solo|solamente|unicamente|nada mas)\b");
        if (exclusive)
        {
            if (Regex.IsMatch(normalizedMessage, @"\b(json|parametros)\b")) { return "json"; }
            if (Regex.IsMatch(normalizedMessage, @"\bvariable\b")) { return "variable"; }
            if (Regex.IsMatch(normalizedMessage, @"\b(codigo|snippet|fragmento)\b")) { return "code"; }
            if (Regex.IsMatch(normalizedMessage, @"\b(nombres?|identificador)\b")) { return "names"; }
        }

        if (Regex.IsMatch(normalizedMessage, @"\b(breve|brevemente|resumid[ao])\b|\bsolo (el )?resultado\b"))
        {
            return "summary";
        }
        return plannerMode switch
        {
            "json" when Regex.IsMatch(normalizedMessage, @"\b(json|parametros)\b") => "json",
            "code" when Regex.IsMatch(normalizedMessage, @"\b(codigo|snippet)\b") => "code",
            "variable" when Regex.IsMatch(normalizedMessage, @"\bvariable\b") => "variable",
            _ => "explanation"
        };
    }

    public static bool RequiresGroundedArtifact(string mode) => mode is "json" or "variable" or "code" or "names";

    public static bool HasCompatibleArtifact(string mode, IReadOnlyList<GroundedArtifact> artifacts) => artifacts.Any(artifact => mode switch
    {
        "json" => artifact.Kind == GroundedArtifactKind.Json,
        "variable" => artifact.Kind == GroundedArtifactKind.Variable,
        "code" => artifact.Kind is GroundedArtifactKind.Snippet or GroundedArtifactKind.Variable,
        "names" => artifact.Kind is GroundedArtifactKind.TechnicalName or GroundedArtifactKind.Variable,
        _ => false
    });

    public static ChatResult Apply(ChatResult result, string mode, IReadOnlyList<GroundedArtifact> artifacts)
    {
        if (result.Status != "ok" || mode == "explanation" || result.Response is not { } response)
        {
            return result;
        }

        if (mode == "summary")
        {
            return result with { Response = string.Join("\n", response.Split('\n').Where(line => line.Trim().Length > 0).Take(4)) };
        }

        if (mode == "json" && IsJson(response.Trim()))
        {
            return result with { Response = response.Trim(), Sources = null };
        }

        var candidates = artifacts.Where(a => mode switch
        {
            "json" => a.Kind == GroundedArtifactKind.Json,
            "variable" => a.Kind == GroundedArtifactKind.Variable,
            "code" => a.Kind is GroundedArtifactKind.Snippet or GroundedArtifactKind.Variable,
            "names" => a.Kind is GroundedArtifactKind.TechnicalName or GroundedArtifactKind.Variable,
            _ => false
        }).Select(a => mode == "names" ? a.Entity : a.Content).Distinct().ToArray();

        if (candidates.Length == 1)
        {
            return result with { Response = candidates[0], Sources = null };
        }
        if (mode == "names" && candidates.Length > 1)
        {
            return result with { Response = string.Join("\n", candidates), Sources = null };
        }
        if (mode == "code" && artifacts.Count == 0 && !result.ToolInvoked)
        {
            var blocks = Regex.Matches(response, @"```[^\r\n]*\r?\n(?<code>[\s\S]*?)```");
            if (blocks.Count == 1)
            {
                return result with { Response = blocks[0].Groups["code"].Value.TrimEnd('\r', '\n'), Sources = null };
            }
            if (Regex.IsMatch(response.Trim(), @"^(console\.\w+\(|(?:var|let|const)\s+\w+\s*=|function\s+\w+\s*\()")
                && response.TrimEnd().EndsWith(';'))
            {
                return result with { Response = response.Trim(), Sources = null };
            }
        }

        return new ChatResult("error", null, result.ToolInvoked,
            candidates.Length > 1 ? "Hay varios artefactos compatibles. Indica cuál necesitas."
                : "No tengo un artefacto verificado en el formato solicitado.");
    }

    private static bool IsJson(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
