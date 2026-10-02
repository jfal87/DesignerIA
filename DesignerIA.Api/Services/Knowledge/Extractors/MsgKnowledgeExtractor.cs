using MsgReader.Outlook;

namespace DesignerIA.Api.Services.Knowledge.Extractors;

/// <summary>
/// Extractor para correos Outlook (.msg) mediante MsgReader (lectura pura del
/// archivo, sin Outlook instalado ni COM automation). Se extrae únicamente Subject,
/// fecha de envío, nombre visible del remitente y el cuerpo de texto; no se procesan
/// attachments ni destinatarios completos, y no se incluyen direcciones de correo
/// en el contenido salvo que sea la única identificación disponible del remitente.
/// </summary>
public static class MsgKnowledgeExtractor
{
    public static IReadOnlyList<KnowledgeSection> Extract(string filePath)
    {
        using var message = new Storage.Message(filePath);

        var subject = string.IsNullOrWhiteSpace(message.Subject)
            ? Path.GetFileNameWithoutExtension(filePath)
            : message.Subject.Trim();

        var sentOn = message.SentOn;
        var sourceDate = sentOn.HasValue ? sentOn.Value.ToString("yyyy-MM-dd") : null;

        var senderName = message.Sender?.DisplayName;
        if (string.IsNullOrWhiteSpace(senderName))
        {
            senderName = message.Sender?.Email;
        }

        var bodyText = GetBodyText(message);
        if (bodyText.Length == 0)
        {
            return Array.Empty<KnowledgeSection>();
        }

        var header = new List<string> { subject };
        if (sourceDate is not null)
        {
            header.Add($"Fecha: {sourceDate}");
        }
        if (!string.IsNullOrWhiteSpace(senderName))
        {
            header.Add($"De: {senderName}");
        }

        var content = string.Join('\n', header) + "\n\n" + bodyText;
        return [new KnowledgeSection(Heading: subject, Content: content, SourceDate: sourceDate)];
    }

    private static string GetBodyText(Storage.Message message)
    {
        if (!string.IsNullOrWhiteSpace(message.BodyText))
        {
            return message.BodyText.Trim();
        }

        if (!string.IsNullOrWhiteSpace(message.BodyHtml))
        {
            return HtmlKnowledgeExtractor.StripHtml(message.BodyHtml);
        }

        return string.Empty;
    }
}
