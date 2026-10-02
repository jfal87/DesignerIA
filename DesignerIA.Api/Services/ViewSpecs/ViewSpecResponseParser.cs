using System.Text.Json;
using DesignerIA.Contracts;

namespace DesignerIA.Api.Services.ViewSpecs;

/// <summary>
/// Convierte la respuesta en texto de Copilot (que debe ser JSON estricto según las
/// instrucciones del prompt) a un <see cref="ViewSpecGenerationOutput"/> tipado. No
/// usa reflection ni parsers complejos: sólo tolera que el modelo envuelva el JSON
/// en un bloque de código markdown (```json ... ```), algo frecuente en la práctica,
/// y deserializa con System.Text.Json. Si el contenido no es JSON válido, no se
/// inventa nada: se lanza <see cref="ViewSpecParseException"/> para que el llamador
/// informe un error controlado.
///
/// El esquema esperado es un sobre con dos partes claramente separadas:
/// <c>{"spec": {...}, "knowledgeGaps": [{"topic": ..., "description": ...}]}</c>.
/// "spec" es la estructura de la vista propuesta; "knowledgeGaps" son los conceptos
/// del dominio para los que Copilot no encontró evidencia documental suficiente
/// (nunca se reconstruyen parseando texto libre: vienen ya estructurados del modelo).
/// </summary>
public static class ViewSpecResponseParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static ViewSpecGenerationOutput Parse(string rawResponse)
    {
        var json = ExtractJson(rawResponse);

        ViewSpecEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ViewSpecEnvelope>(json, SerializerOptions);
        }
        catch (JsonException ex)
        {
            throw new ViewSpecParseException("La respuesta de Copilot no es JSON válido.", ex);
        }

        if (envelope?.Spec is null)
        {
            throw new ViewSpecParseException("La respuesta de Copilot no incluyó una estructura de vista ('spec').");
        }

        return new ViewSpecGenerationOutput(envelope.Spec, envelope.KnowledgeGaps ?? []);
    }

    private static string ExtractJson(string rawResponse)
    {
        var text = rawResponse.Trim();

        var fenceStart = text.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var afterFence = text[(fenceStart + 3)..];
            // Omite un posible identificador de lenguaje justo después de la cerca (```json).
            var newlineIndex = afterFence.IndexOf('\n');
            if (newlineIndex >= 0 && afterFence[..newlineIndex].Trim().Length <= 10)
            {
                afterFence = afterFence[(newlineIndex + 1)..];
            }

            var fenceEnd = afterFence.IndexOf("```", StringComparison.Ordinal);
            if (fenceEnd >= 0)
            {
                text = afterFence[..fenceEnd].Trim();
            }
        }

        return text;
    }

    private sealed class ViewSpecEnvelope
    {
        public ViewSpec? Spec { get; set; }

        public List<ViewSpecKnowledgeGap>? KnowledgeGaps { get; set; }
    }
}

/// <summary>
/// Salida tipada del parseo de la respuesta de Copilot: el <see cref="ViewSpec"/>
/// propuesto y, por separado, los <see cref="ViewSpecKnowledgeGap"/> que Copilot
/// reportó explícitamente. Nunca se combinan; <see cref="ViewSpecValidator"/> sólo
/// procesa <paramref name="Spec"/>.
/// </summary>
public record ViewSpecGenerationOutput(ViewSpec Spec, IReadOnlyList<ViewSpecKnowledgeGap> KnowledgeGaps);

public sealed class ViewSpecParseException : Exception
{
    public ViewSpecParseException(string message)
        : base(message)
    {
    }

    public ViewSpecParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
