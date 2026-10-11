using System.Text.RegularExpressions;

namespace DesignerIA.Api.Services;

/// <summary>
/// Server-authored replies for purely social messages (greeting, thanks, acknowledgement, identity).
/// A message qualifies only when every word belongs to a closed social vocabulary or it is a short
/// identity question; any other word keeps the semantic route, so compound requests are never absorbed.
/// </summary>
internal static class SocialConversation
{
    public const string Greeting = "Soy DesignerIA. Puedo ayudarte con programación general, documentación y metadata de GestionEngine, y preparar vistas mediante la capacidad existente.";
    public const string HowAreYou = "¡Bien, gracias! ¿En qué puedo ayudarte?";
    public const string Thanks = "¡Con gusto! Si necesitas algo más, aquí estoy.";
    public const string Acknowledgement = "Perfecto. Dime si necesitas algo más.";

    private static readonly HashSet<string> GreetingWords = Words("hola buenas buenos buen dia dias tardes noches saludos hey");
    private static readonly HashSet<string> HowAreYouWords = Words("que tal como estas esta te va andas todo hoy");
    private static readonly HashSet<string> ThanksWords = Words("gracias muchas muchisimas mil thanks agradezco agradecido te lo");
    private static readonly HashSet<string> AcknowledgementWords = Words("ok okay okey vale listo perfecto entendido genial excelente bien claro super de acuerdo muy");
    public const string Closing = "De acuerdo. Aquí estaré si necesitas algo más.";
    private static readonly HashSet<string> Vocatives = Words("bro brother hermano hermana amigo amiga compa crack jefe jefa maestro colega parce pana wey guey man");
    private static readonly HashSet<string> ClosingWords = Words("eso era es todo nada mas por ahora adios chao bye hasta luego pronto nos vemos");
    // An acknowledgement or connector may precede the question ("ok, y qué más sabes hacer?"); nothing may follow it.
    private static readonly Regex Identity = new(
        @"^((ok|okay|vale|bien|bueno|oye|entonces|y) )*(tu )?(quien eres|quien sos|que eres|que (mas )?haces|que (mas )?(sabes|puedes) hacer|como te lamas|para que sirves|que es designeria)( tu)?$",
        RegexOptions.CultureInvariant);

    /// <param name="message">Original text; code-like punctuation disqualifies it.</param>
    /// <param name="normalized">Lowercase text without accents or punctuation.</param>
    public static string? Respond(string message, string normalized)
    {
        if (message.IndexOfAny(['(', ')', '{', '}', ';', '<', '>', '`', '[', ']']) >= 0) { return null; }
        // Repeated letters are collapsed on both sides, so "holaaa" and "erees" match without listing variants.
        var words = Collapse(normalized).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // A vocative at either end ("quién eres brooo", "bro, gracias") is not part of the request. One in the middle
        // stays, so "hola bro, explícame este handler" keeps its semantic route like any other compound message.
        var last = words.Length > 1 && Vocatives.Contains(words[^1]) ? 1 : 0;
        words = words[..^last];
        // At the start it may follow one opening word ("oye bro, quién eres").
        var opening = words.Length > 2 && words[0] is "oye" or "ok" or "okay" or "vale" or "bien" or "bueno" or "entonces" or "y" or "hola" or "hey" ? 1 : 0;
        if (words.Length - opening > 1 && Vocatives.Contains(words[opening]))
        {
            words = [.. words[..opening], .. words[(opening + 1)..]];
        }
        if (words.Length is 0 or > 8) { return null; }
        if (Identity.IsMatch(string.Join(' ', words))) { return DesignerCapabilities.Describe(); }
        if (!words.All(word => GreetingWords.Contains(word) || HowAreYouWords.Contains(word)
                || ThanksWords.Contains(word) || AcknowledgementWords.Contains(word) || ClosingWords.Contains(word)))
        {
            return null;
        }
        if (words.Any(word => word is "gracias" or "thanks" or "agradezco" or "agradecido")) { return Thanks; }
        if (words.Any(HowAreYouWords.Contains) && words.Any(word => word is "tal" or "estas" or "va" or "andas"))
        {
            return HowAreYou;
        }
        if (words.Any(GreetingWords.Contains)) { return Greeting; }
        if (words.Any(word => word is "nada" or "adios" or "chao" or "bye" or "luego" or "pronto" or "vemos")
            || words.Contains("todo") && words.Any(word => word is "eso" or "es" or "era")) { return Closing; }
        return words.All(AcknowledgementWords.Contains) ? Acknowledgement : null;
    }

    private static string Collapse(string text) => Regex.Replace(text, @"(\p{L})\1+", "$1");

    private static HashSet<string> Words(string list) =>
        Collapse(list).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
}
