using System.Diagnostics;
using System.Globalization;
using System.Text;
using DesignerIA.Api.Services.Knowledge;
using DesignerIA.Api.Services.ViewSpecs;
using DesignerIA.Contracts;
using GitHub.Copilot;

namespace DesignerIA.Api.Services;

/// <summary>
/// Chat libre de DesignerIA. Cada mensaje es una operación de Copilot independiente
/// (sin historial persistente): se crea una sesión corta con acceso ÚNICAMENTE a las
/// custom tools "get_gestionengine_summary" y "search_knowledge" (reutilizadas desde
/// <see cref="GestionEngineSummaryTool"/> y <see cref="KnowledgeSearchTool"/>, sin
/// duplicar la consulta SQL ni la búsqueda documental), un contexto mínimo sobre la
/// identidad y capacidades actuales de DesignerIA, y se libera al terminar. Copilot
/// nunca recibe acceso SQL ni al sistema de archivos directo, ni tools integradas
/// del CLI.
///
/// Cuando el mensaje del usuario expresa una intención clara de crear/diseñar una
/// vista (ver <see cref="DetectIntent"/>, una clasificación determinística
/// limitada a creación o consulta documental), se añade una
/// instrucción adicional para que Copilot devuelva únicamente un JSON estricto
/// (ViewSpec). Esa salida se deserializa con <see cref="ViewSpecResponseParser"/> y
/// se valida de forma determinística con <see cref="ViewSpecValidator"/> (la IA
/// propone, DesignerIA valida). El SDK instalado (1.0.14) no ofrece todavía una
/// forma estable de "structured output" (ver README oficial del SDK), por eso se usa
/// JSON estricto en el prompt en lugar de esa capacidad.
/// </summary>
public class CopilotChatService
{
    // Mismo mínimo aceptado por el runtime del SDK ya validado en checkpoints anteriores.
    private const double MaxAiCredits = 30;

    private const string SystemContext =
        "Te llamas DesignerIA. Eres un asistente pensado para ayudar, en el futuro, a crear vistas de GestionEngine.\n" +
        "Hoy tienes dos herramientas disponibles:\n" +
        "1) get_gestionengine_summary: consulta la cantidad actual de vistas registradas en GestionEngine. Úsala cuando " +
        "el usuario pregunte por la cantidad actual de vistas o cualquier dato real de GestionEngine.\n" +
        "2) search_knowledge: busca fragmentos relevantes en la documentación interna indexada de DesignerIA. Úsala " +
        "cuando el usuario pregunte por cómo funciona algo del dominio (handlers, funcionalidades, configuraciones, etc.).\n" +
        "No inventes el valor de get_gestionengine_summary ni el contenido de la documentación.\n" +
        "Responde utilizando únicamente la información recuperada por esas herramientas. Si la documentación recuperada " +
        "por search_knowledge no soporta una afirmación, o la búsqueda no devolvió evidencia suficiente, dilo claramente " +
        "en lugar de inventar una respuesta.\n" +
        "No inventes información sobre Designer, Motor, handlers, gráficos, metadata u otros conceptos que no estén " +
        "respaldados por la documentación recuperada o por las herramientas disponibles.";

    private const string ViewSpecInstructions =
        "\n\nEl usuario está pidiendo crear o diseñar una vista de GestionEngine. Antes de responder, usa " +
        "search_knowledge para fundamentar en la documentación real los conceptos del dominio que necesites " +
        "(por ejemplo el nombre correcto de un handler o el tipo de un área) en lugar de asumirlos.\n" +
        "Tu ÚNICA salida debe ser un JSON válido, sin texto antes ni después, sin explicaciones y sin bloques de " +
        "código markdown, con exactamente este esquema (usa null cuando no tengas la información; nunca inventes " +
        "un valor para rellenar un campo):\n" +
        "{\n" +
        "  \"spec\": {\n" +
        "    \"name\": string|null,\n" +
        "    \"description\": string|null,\n" +
        "    \"initialState\": string|null,\n" +
        "    \"states\": [\n" +
        "      {\n" +
        "        \"name\": string|null,\n" +
        "        \"rows\": [\n" +
        "          {\n" +
        "            \"areas\": [\n" +
        "              {\n" +
        "                \"type\": string|null,\n" +
        "                \"storedProcedure\": string|null,\n" +
        "                \"connectionAlias\": string|null,\n" +
        "                \"handlers\": [ { \"type\": string|null, \"targetState\": string|null } ]\n" +
        "              }\n" +
        "            ]\n" +
        "          }\n" +
        "        ]\n" +
        "      }\n" +
        "    ]\n" +
        "  },\n" +
        "  \"knowledgeGaps\": [ { \"topic\": string, \"description\": string } ]\n" +
        "}\n" +
        "Distingue dos situaciones completamente distintas:\n" +
        "1) Un dato que el usuario simplemente no proporcionó (por ejemplo el Stored Procedure de una grilla): deja " +
        "el campo en null dentro de 'spec'. No es un problema de conocimiento, así que NO lo agregues a " +
        "'knowledgeGaps'.\n" +
        "2) Una relación o concepto del dominio que el usuario sí pidió, pero que la documentación recuperada por " +
        "search_knowledge no respalda (por ejemplo qué handler usar para navegar entre estados): deja el campo " +
        "relacionado en null dentro de 'spec' Y además agrega una entrada en 'knowledgeGaps' con 'topic' (el " +
        "concepto puntual) y 'description' (qué evidencia faltó), por ejemplo: {\"topic\": \"Navegación entre " +
        "estados mediante ClickEnCelda\", \"description\": \"La documentación recuperada describe ClickEnCelda para " +
        "PopUp, pero no respalda navegación hacia otro estado.\"}.\n" +
        "Si el usuario no indicó description, usa null: C# la derivará del name. ConnectionAlias sólo aplica cuando " +
        "storedProcedure tenga valor; en cualquier otro caso usa null. Para una vista mínima no agregues estados, filas, " +
        "áreas, Stored Procedures, ConnectionAlias ni handlers: C# normalizará el estado default vacío.\n" +
        "No inventes Stored Procedures, nombres de estados no mencionados por el usuario, ni tipos de handler o de " +
        "área que no estén respaldados por la documentación recuperada. No agregues un knowledgeGap por cada dato " +
        "faltante: sólo cuando falte evidencia documental sobre un concepto del dominio.";

    private readonly GestionEngineHealthService _gestionEngineHealthService;
    private readonly KnowledgeSearchService _knowledgeSearchService;
    private readonly ILogger<CopilotChatService> _logger;

    public CopilotChatService(
        GestionEngineHealthService gestionEngineHealthService,
        KnowledgeSearchService knowledgeSearchService,
        ILogger<CopilotChatService> logger)
    {
        _gestionEngineHealthService = gestionEngineHealthService;
        _knowledgeSearchService = knowledgeSearchService;
        _logger = logger;
    }

    public async Task<ChatResult> RunAsync(string message, CancellationToken cancellationToken = default)
    {
        var toolInvoked = false;
        var knowledgeUsed = false;
        var sourcesUsed = new List<KnowledgeSourceReference>();
        var stopwatch = Stopwatch.StartNew();
        var intent = DetectIntent(message);
        var isViewCreationRequest = intent == ChatIntent.CreateView;

        _logger.LogInformation(
            "Copilot chat request started. Intent: {Intent}",
            intent);

        try
        {
            var summaryTool = GestionEngineSummaryTool.Create(
                _gestionEngineHealthService,
                _logger,
                onInvoked: _ => toolInvoked = true,
                cancellationToken);

            var knowledgeTool = KnowledgeSearchTool.Create(
                _knowledgeSearchService,
                _logger,
                onInvoked: results =>
                {
                    toolInvoked = true;
                    knowledgeUsed = true;
                    foreach (var result in results)
                    {
                        if (!sourcesUsed.Any(s => s.SourceFile == result.SourceFile && s.Title == result.Title))
                        {
                            sourcesUsed.Add(new KnowledgeSourceReference(result.Title, result.SourceFile, result.SourceType, result.Contributor));
                        }
                    }
                },
                cancellationToken);

            await using var client = new CopilotClient(new CopilotClientOptions
            {
                UseLoggedInUser = true,
            });
            await client.StartAsync();

            var systemContent = isViewCreationRequest
                ? SystemContext + ViewSpecInstructions
                : SystemContext;

            await using var session = await client.CreateSessionAsync(new SessionConfig
            {
                Tools = [summaryTool, knowledgeTool],
                // Allowlist explícita: únicamente nuestras dos custom tools están disponibles.
                // Todas las tools integradas del CLI permanecen deshabilitadas.
                AvailableTools = new List<string> { GestionEngineSummaryTool.Name, KnowledgeSearchTool.Name },
                SystemMessage = new SystemMessageConfig
                {
                    Mode = SystemMessageMode.Append,
                    Content = systemContent,
                },
#pragma warning disable GHCP001 // SessionLimits está en evaluación en el SDK; se usa deliberadamente como protección de costo (AGENTS.md).
                SessionLimits = new SessionLimitsConfig
                {
                    MaxAiCredits = MaxAiCredits,
                },
#pragma warning restore GHCP001
            });

            var response = await session.SendAndWaitAsync(new MessageOptions
            {
                Prompt = message,
            });

            stopwatch.Stop();

            if (response is null)
            {
                _logger.LogWarning(
                    "Copilot chat request completed in {ElapsedMs} ms without a response. ToolInvoked: {ToolInvoked}",
                    stopwatch.ElapsedMilliseconds,
                    toolInvoked);
                return new ChatResult("error", null, toolInvoked, "Copilot no devolvió respuesta.");
            }

            if (!isViewCreationRequest)
            {
                _logger.LogInformation(
                    "Copilot chat request completed in {ElapsedMs} ms. ToolInvoked: {ToolInvoked}, SourcesCount: {SourcesCount}",
                    stopwatch.ElapsedMilliseconds,
                    toolInvoked,
                    sourcesUsed.Count);

                return new ChatResult("ok", response.Data.Content, toolInvoked, null, sourcesUsed.Count > 0 ? sourcesUsed : null);
            }

            return BuildViewSpecChatResult(
                response.Data.Content,
                toolInvoked,
                knowledgeUsed,
                sourcesUsed,
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(
                ex,
                "Copilot chat request failed after {ElapsedMs} ms. ToolInvoked: {ToolInvoked}",
                stopwatch.ElapsedMilliseconds,
                toolInvoked);
            return new ChatResult("error", null, toolInvoked, "No se pudo completar la conversación con Copilot.");
        }
    }

    /// <summary>
    /// Clasificación determinística V1. Normaliza únicamente una copia del texto
    /// para clasificar; el mensaje original llega sin cambios a Copilot.
    /// Las preguntas documentales explícitas tienen prioridad sobre verbos de
    /// creación para no convertir "cómo crear una vista" en una solicitud.
    /// </summary>
    private static ChatIntent DetectIntent(string message)
    {
        var normalized = NormalizeForIntent(message);
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (IsKnowledgeQuestion(normalized, tokens))
        {
            return ChatIntent.KnowledgeQuestion;
        }

        var mentionsView = tokens.Any(token => MatchesToken(token, "vista"));
        var requestsCreation = tokens.Any(token =>
            MatchesToken(token, "crea")
            || MatchesToken(token, "crear")
            || MatchesToken(token, "creame")
            || MatchesToken(token, "quiero")
            || MatchesToken(token, "necesito")
            || MatchesToken(token, "hazme")
            || MatchesToken(token, "genera")
            || MatchesToken(token, "generar")
            || MatchesToken(token, "arma")
            || MatchesToken(token, "armar")
            || MatchesToken(token, "construye")
            || MatchesToken(token, "construir")
            || MatchesToken(token, "disena")
            || MatchesToken(token, "disenar")
            || MatchesToken(token, "nueva"));

        return mentionsView && requestsCreation
            ? ChatIntent.CreateView
            : ChatIntent.KnowledgeQuestion;
    }

    private static bool IsKnowledgeQuestion(string normalized, string[] tokens)
    {
        if (tokens.Length == 0)
        {
            return true;
        }

        if (MatchesToken(tokens[0], "como")
            || MatchesToken(tokens[0], "que")
            || MatchesToken(tokens[0], "cual")
            || (tokens.Length > 1 && tokens[0] == "por" && tokens[1] == "que"))
        {
            return true;
        }

        return normalized.Contains("como crear", StringComparison.Ordinal)
            || normalized.Contains("como funciona", StringComparison.Ordinal)
            || normalized.Contains("como agrego", StringComparison.Ordinal)
            || normalized.Contains("que necesita", StringComparison.Ordinal);
    }

    private static string NormalizeForIntent(string message)
    {
        var decomposed = message.Normalize(NormalizationForm.FormD);
        var normalized = new StringBuilder(decomposed.Length);
        var previousWasSpace = true;

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                normalized.Append(char.ToLowerInvariant(character));
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                normalized.Append(' ');
                previousWasSpace = true;
            }
        }

        return normalized.ToString().Trim();
    }

    private static bool MatchesToken(string token, string expected)
    {
        return string.Equals(token, expected, StringComparison.Ordinal)
            || IsOneEditAway(token, expected);
    }

    private static bool IsOneEditAway(string value, string expected)
    {
        if (Math.Abs(value.Length - expected.Length) > 1)
        {
            return false;
        }

        if (value.Length == expected.Length)
        {
            var differences = new List<int>();
            for (var index = 0; index < value.Length; index++)
            {
                if (value[index] != expected[index])
                {
                    differences.Add(index);
                }
            }

            return differences.Count == 1
                || (differences.Count == 2
                    && differences[1] == differences[0] + 1
                    && value[differences[0]] == expected[differences[1]]
                    && value[differences[1]] == expected[differences[0]]);
        }

        var shorter = value.Length < expected.Length ? value : expected;
        var longer = value.Length < expected.Length ? expected : value;
        var shorterIndex = 0;
        var longerIndex = 0;
        var skippedCharacter = false;

        while (shorterIndex < shorter.Length && longerIndex < longer.Length)
        {
            if (shorter[shorterIndex] == longer[longerIndex])
            {
                shorterIndex++;
                longerIndex++;
                continue;
            }

            if (skippedCharacter)
            {
                return false;
            }

            skippedCharacter = true;
            longerIndex++;
        }

        return true;
    }

    private enum ChatIntent
    {
        KnowledgeQuestion,
        CreateView,
    }

    /// <summary>
    /// Convierte la respuesta JSON de Copilot en un <see cref="ViewSpec"/> tipado,
    /// lo valida determinísticamente con <see cref="ViewSpecValidator"/> y arma el
    /// texto breve para el chat. Si el parseo falla, no se inventa una corrección:
    /// se registra el fallo y se devuelve un error controlado para que el usuario
    /// pueda reformular.
    /// </summary>
    private ChatResult BuildViewSpecChatResult(
        string rawResponse,
        bool toolInvoked,
        bool knowledgeUsed,
        List<KnowledgeSourceReference> sourcesUsed,
        long elapsedMs)
    {
        ViewSpecGenerationOutput output;
        try
        {
            output = ViewSpecResponseParser.Parse(rawResponse);
        }
        catch (ViewSpecParseException ex)
        {
            _logger.LogWarning(
                ex,
                "ViewSpec generation failed to parse Copilot response in {ElapsedMs} ms. ResponseLength: {ResponseLength}, KnowledgeUsed: {KnowledgeUsed}",
                elapsedMs,
                rawResponse.Length,
                knowledgeUsed);
            return new ChatResult(
                "error",
                null,
                toolInvoked,
                "No se pudo interpretar la estructura de la vista propuesta por Copilot. Intenta reformular el requerimiento.");
        }

        var spec = ViewSpecNormalizer.Normalize(output.Spec);
        var knowledgeGaps = output.KnowledgeGaps;
        var validation = ViewSpecValidator.Validate(spec);

        var statesCount = spec.States?.Count ?? 0;
        var areasCount = spec.States?.Sum(s => s.Rows?.Sum(r => r.Areas?.Count ?? 0) ?? 0) ?? 0;
        var handlersCount = spec.States?.Sum(s => s.Rows?.Sum(r => r.Areas?.Sum(a => a.Handlers?.Count ?? 0) ?? 0) ?? 0) ?? 0;

        _logger.LogInformation(
            "ViewSpec generation completed in {ElapsedMs} ms. KnowledgeUsed: {KnowledgeUsed}, StatesCount: {StatesCount}, " +
            "AreasCount: {AreasCount}, HandlersCount: {HandlersCount}, IsValid: {IsValid}, ErrorsCount: {ErrorsCount}, " +
            "PendingCount: {PendingCount}, KnowledgeGapsCount: {KnowledgeGapsCount}",
            elapsedMs,
            knowledgeUsed,
            statesCount,
            areasCount,
            handlersCount,
            validation.IsValid,
            validation.Errors.Count,
            validation.Pending.Count,
            knowledgeGaps.Count);

        var summary = BuildUserFacingSummary(spec, validation, knowledgeGaps);

        return new ChatResult(
            "ok",
            summary,
            toolInvoked,
            null,
            sourcesUsed.Count > 0 ? sourcesUsed : null,
            new ViewSpecResult(spec, validation, knowledgeGaps.Count > 0 ? knowledgeGaps : null));
    }

    private static string BuildUserFacingSummary(
        ViewSpec spec,
        ViewSpecValidationResult validation,
        IReadOnlyList<ViewSpecKnowledgeGap> knowledgeGaps)
    {
        if (!validation.IsValid)
        {
            return "No pude construir una estructura de vista válida:\n- " + string.Join("\n- ", validation.Errors);
        }

        var summary = validation.Pending.Count == 0
            ? $"Ok, prepararé la vista {spec.Name}.\n\n" +
              "✓ Solicitud interpretada\n" +
              "✓ Estructura generada\n" +
              "✓ Validación completada\n" +
              "✓ Vista lista para generar script\n\n" +
              "Vista preparada correctamente. Aún no se ha creado en GestionEngine."
            : validation.Pending.Count == 1
                ? $"Preparé la estructura de la vista, pero falta indicar: {validation.Pending[0]}"
                : "Preparé la estructura de la vista, pero falta información:\n- " + string.Join("\n- ", validation.Pending);

        if (knowledgeGaps.Count > 0)
        {
            summary += "\n\nAdemás, no encontré documentación suficiente sobre:\n- " +
                string.Join("\n- ", knowledgeGaps.Select(g => g.Topic));
        }

        return summary;
    }
}

