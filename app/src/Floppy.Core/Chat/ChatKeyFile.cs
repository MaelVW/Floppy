using System.Text;

namespace Floppy.Core.Chat;

/// <summary>
/// Schluessel-Diskette: eine Textdatei <c>chat-key.txt</c> mit der Verschluesselung.
/// Der Motor beachtet sie nicht - eine Schluessel-Diskette ist beim Einlegen eine ganz normale
/// Diskette. Geladen wird sie nur, wenn man im Chat auf "Von Datentraeger laden" klickt.
/// </summary>
public static class ChatKeyFile
{
    public const string FileName = "chat-key.txt";
    public const string Key = "chatkey";
    private const int MaxBytes = 4 * 1024;

    public static string Render(string secret) => new StringBuilder()
        .Append("# Floppy Hub - Chat-Schluessel\r\n")
        .Append("# Wer diese Datei hat, kann im Chatraum mitlesen und mitschreiben.\r\n")
        .Append("# In Floppy Hub: Chat -> \"Von Datentraeger laden\".\r\n")
        .Append("\r\n")
        .Append(Key).Append(" = ").Append(ChatRoomKey.Normalize(secret)).Append("\r\n")
        .ToString();

    public static void Write(string root, string secret)
    {
        if (ChatRoomKey.Problem(secret) != SecretProblem.None) throw new ArgumentException("Ungueltige Verschluesselung.", nameof(secret));
        File.WriteAllText(Path.Combine(root, FileName), Render(secret), new UTF8Encoding(true));
    }

    /// <summary>Verschluesselung aus einer Datei (null = keine/ungueltig).</summary>
    public static string? Read(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > MaxBytes) return null;
            return Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static string? Parse(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith('#') || line.StartsWith(';')) continue;
            var eq = line.IndexOf('=');
            if (eq <= 0 || !line[..eq].Trim().Equals(Key, StringComparison.OrdinalIgnoreCase)) continue;
            var value = line[(eq + 1)..].Trim();
            return ChatRoomKey.Problem(value) == SecretProblem.None ? ChatRoomKey.Normalize(value) : null;
        }
        return null;
    }

    /// <summary>Wurzelverzeichnisse durchsuchen (Disketten, USB-Sticks ...). Nur das Wurzelverzeichnis, nichts rekursiv.</summary>
    public static IReadOnlyList<(string Root, string Secret)> FindOn(IEnumerable<string> roots)
    {
        var found = new List<(string, string)>();
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!DiscWatcher.IsReady(root)) continue;
            if (Read(Path.Combine(root, FileName)) is { } secret) found.Add((root, secret));
        }
        return found;
    }
}
