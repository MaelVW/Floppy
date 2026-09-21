namespace Floppy.Core.Chat;

/// <summary>
/// Was ein Mitglied im Chat ueber sich zeigt: Anzeigename und Namensfarbe. Reine Anzeige - Rechte
/// (Admin, Sperren) haengen nie daran, sondern immer am Fingerabdruck der Chat-Identitaet.
/// </summary>
/// <param name="Alias">Selbstgewaehlter Anzeigename (schon gesaeubert), null = keiner.</param>
/// <param name="Color">0 = automatisch (aus dem Fingerabdruck), 1 bis <see cref="ChatProfile.ColorCount"/> = gewaehlte Farbe.</param>
public sealed record ChatMemberProfile(string? Alias, int Color)
{
    public static ChatMemberProfile None { get; } = new(null, 0);
}

/// <summary>Wann der Chat einen Ton macht (und die Taskleiste blinkt), wenn er gerade nicht im Blick ist.</summary>
public enum ChatNotifyMode
{
    Off,
    Mentions,
    All,
}

/// <summary>Schriftgroesse im Chatverlauf.</summary>
public enum ChatFontSize
{
    Small,
    Normal,
    Large,
    Huge,
}

/// <summary>Warum ein Anzeigename nicht geht.</summary>
public enum AliasProblem
{
    None,
    Empty,
    TooLong,
    BadCharacters,
    Reserved,
}

/// <summary>
/// Regeln fuer die Personalisierung im Chat: Anzeigename, Namensfarbe, Erwaehnungen, Benachrichtigung.
/// Fremde Anzeigenamen sind nie vertrauenswuerdig - sie werden beim Empfang mit denselben Regeln
/// geprueft (<see cref="Clean"/>), und im Chat steht immer die ID-Endung dahinter (<see cref="IdTag"/>).
/// </summary>
public static class ChatProfile
{
    public const int MaxAliasLength = 20;
    public const int ColorCount = 8;

    /// <summary>Ausser Buchstaben und Ziffern erlaubt (keine Klammern: die gehoeren dem "(Admin)"-Zeichen).</summary>
    public const string AllowedPunctuation = " -_.'!?+*~";

    /// <summary>Namen, die nur Admins bzw. die App tragen sollen (nach Weglassen von Leer- und Satzzeichen, ohne Gross/Klein).</summary>
    private static readonly string[] ReservedWords = ["admin", "moderator", "floppyhub", "system"];

    /// <summary>Leerraum zusammengefasst und getrimmt (Zeilenumbrueche und Tabs werden zu Leerzeichen).</summary>
    public static string Normalize(string? alias) =>
        string.Join(' ', (alias ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <param name="allowReserved">Admins duerfen "Admin" im Namen tragen - alle anderen nicht.</param>
    public static AliasProblem Check(string? alias, bool allowReserved = false)
    {
        var name = Normalize(alias);
        if (name.Length == 0) return AliasProblem.Empty;
        if (name.Length > MaxAliasLength) return AliasProblem.TooLong;

        var hasLetterOrDigit = false;
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c)) hasLetterOrDigit = true;
            else if (!AllowedPunctuation.Contains(c)) return AliasProblem.BadCharacters;
        }
        if (!hasLetterOrDigit) return AliasProblem.BadCharacters;
        return !allowReserved && IsReserved(name) ? AliasProblem.Reserved : AliasProblem.None;
    }

    /// <summary>Der saubere Anzeigename oder null, wenn er nicht geht (dann zeigt der Chat einfach die ID).</summary>
    public static string? Clean(string? alias, bool allowReserved = false) =>
        Check(alias, allowReserved) == AliasProblem.None ? Normalize(alias) : null;

    /// <summary>0 (automatisch) oder 1 bis <see cref="ColorCount"/>.</summary>
    public static int CleanColor(int? color) => color is >= 1 and <= ColorCount ? color.Value : 0;

    /// <summary>
    /// Kurze ID-Endung zum Anzeigenamen, z. B. <c>#1417</c> - damit sich zwei "Tom" meist unterscheiden lassen.
    /// Nur ein Hinweis, kein Beweis: vier Ziffern laesst sich jemand "nachbauen". Verlaesslich ist nur ein
    /// gespeicherter Kontakt (haengt am Fingerabdruck und zeigt seinen Namen ohne Endung).
    /// </summary>
    public static string IdTag(string? memberId) =>
        memberId is { Length: >= 4 } ? "#" + memberId[^4..] : memberId ?? "";

    /// <summary>
    /// Wird der Leser im Text erwaehnt? Sein Anzeigename als ganzes Wort (auch <c>@Tom</c>, ohne Gross/Klein)
    /// oder seine ganze ID-Nummer.
    /// </summary>
    public static bool Mentions(string? text, string? alias, string? memberId = null)
    {
        if (string.IsNullOrEmpty(text)) return false;
        if (!string.IsNullOrEmpty(memberId) && text.Contains(memberId, StringComparison.Ordinal)) return true;
        return !string.IsNullOrEmpty(alias) && ContainsWord(text, alias);
    }

    /// <summary>Soll eine Nachricht Ton/Taskleiste ausloesen?</summary>
    public static bool ShouldNotify(ChatNotifyMode mode, bool isMention) =>
        mode == ChatNotifyMode.All || (mode == ChatNotifyMode.Mentions && isMention);

    /// <summary>Faktor auf die normale Schriftgroesse.</summary>
    public static float FontScale(ChatFontSize size) => size switch
    {
        ChatFontSize.Small => 0.85f,
        ChatFontSize.Large => 1.2f,
        ChatFontSize.Huge => 1.45f,
        _ => 1f,
    };

    public static ChatNotifyMode ParseNotify(string? text) =>
        Enum.TryParse<ChatNotifyMode>(text?.Trim(), ignoreCase: true, out var mode) && Enum.IsDefined(mode) ? mode : ChatNotifyMode.Off;

    public static ChatFontSize ParseFontSize(string? text) =>
        Enum.TryParse<ChatFontSize>(text?.Trim(), ignoreCase: true, out var size) && Enum.IsDefined(size) ? size : ChatFontSize.Normal;

    private static bool IsReserved(string name)
    {
        var key = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return ReservedWords.Any(word => key.Contains(word, StringComparison.Ordinal));
    }

    private static bool ContainsWord(string text, string word)
    {
        var start = 0;
        while (start <= text.Length - word.Length)
        {
            var i = text.IndexOf(word, start, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            var before = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
            var after = i + word.Length >= text.Length || !char.IsLetterOrDigit(text[i + word.Length]);
            if (before && after) return true;
            start = i + 1;
        }
        return false;
    }
}
