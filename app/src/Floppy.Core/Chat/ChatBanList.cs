using System.Globalization;
using System.Text;

namespace Floppy.Core.Chat;

/// <param name="Until">Ende der Sperre (Unix-Millisekunden), 0 = dauerhaft.</param>
/// <param name="At">Wann entschieden wurde (Unix-Millisekunden) - die neueste Entscheidung gilt.</param>
/// <param name="Lifted">Aufgehoben (bleibt als Eintrag, damit eine alte, wieder auftauchende Sperre nichts zurueckholt).</param>
public sealed record ChatBan(string Fingerprint, string MemberId, long Until, long At, string Reason, bool Lifted)
{
    public bool IsActive(DateTimeOffset now) => !Lifted && (Until == 0 || Until > now.ToUnixTimeMilliseconds());
}

/// <summary>
/// Die Sperren im offenen Chat. Sie kommen ausschliesslich als Admin-Nachricht (siehe
/// <see cref="ChatAdmins"/>); jede Installation merkt sie sich in einer kleinen Textdatei, weil
/// Chatnachrichten selbst nirgends gespeichert werden - sonst wuesste ein spaeter gestartetes
/// Floppy Hub nichts von einer Sperre. Gespeichert werden nur Fingerabdruck, ID, Ende und Grund.
/// Bei widerspruechlichen Eintraegen (Sperre / Aufhebung) gewinnt der mit dem spaeteren Zeitpunkt.
/// </summary>
public sealed class ChatBanList
{
    public const string FileName = "bans.txt";
    private static readonly TimeSpan KeepOldEntries = TimeSpan.FromDays(30);

    private readonly string? _file;
    private readonly Dictionary<string, ChatBan> _bans = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="file">Datei zum Speichern; null = nur im Speicher (Tests, Vorschau).</param>
    public ChatBanList(string? file = null, DateTimeOffset? now = null)
    {
        _file = file;
        Load(now ?? DateTimeOffset.UtcNow);
    }

    public int Version { get; private set; }

    /// <summary>Aktuell gueltige Sperren, aelteste zuerst.</summary>
    public IReadOnlyList<ChatBan> Active(DateTimeOffset now) => _bans.Values.Where(b => b.IsActive(now)).OrderBy(b => b.At).ToList();

    public bool IsBanned(string? fingerprint, DateTimeOffset now, out ChatBan? ban)
    {
        ban = null;
        if (fingerprint is null || !_bans.TryGetValue(fingerprint, out var found) || !found.IsActive(now)) return false;
        ban = found;
        return true;
    }

    /// <summary>Der Eintrag zu diesem Fingerabdruck - auch ein aufgehobener oder abgelaufener (null = nie gesperrt).</summary>
    public ChatBan? Get(string? fingerprint) => fingerprint is not null && _bans.TryGetValue(fingerprint, out var b) ? b : null;

    /// <summary>Sperre eintragen. false = nichts geaendert (es gibt schon einen gleich neuen oder neueren Eintrag).</summary>
    public bool Apply(ChatBan entry)
    {
        if (_bans.TryGetValue(entry.Fingerprint, out var old) && old.At >= entry.At) return false;
        _bans[entry.Fingerprint] = entry;
        Version++;
        Save();
        return true;
    }

    /// <summary>Sperre aufheben (zum Zeitpunkt <paramref name="at"/>). false = nichts geaendert.</summary>
    public bool Lift(string fingerprint, string memberId, long at)
    {
        var old = _bans.GetValueOrDefault(fingerprint);
        return Apply(new ChatBan(fingerprint, old?.MemberId is { Length: > 0 } id ? id : memberId, 0, at, "", true));
    }

    // ------------------------------------------------------------------
    // Datei: eine Zeile je Eintrag, Felder durch Tabulator getrennt (bewusst kein JSON -
    // der Motor wird per NativeAOT gebaut, und die Datei soll von Hand lesbar bleiben).
    // ------------------------------------------------------------------

    private void Load(DateTimeOffset now)
    {
        if (_file is null || !File.Exists(_file)) return;
        try
        {
            var oldest = now.Add(-KeepOldEntries).ToUnixTimeMilliseconds();
            foreach (var raw in File.ReadAllLines(_file))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length == 0 || line[0] == '#') continue;
                var f = line.Split('\t');
                if (f.Length < 6 || f[0].Length != 64 || !f[0].All(char.IsAsciiHexDigit)) continue;
                if (!long.TryParse(f[2], NumberStyles.None, CultureInfo.InvariantCulture, out var until) ||
                    !long.TryParse(f[3], NumberStyles.None, CultureInfo.InvariantCulture, out var at)) continue;
                var ban = new ChatBan(f[0], f[1], until, at, f[5], f[4] == "1");
                if (!ban.IsActive(now) && at < oldest) continue;   // abgelaufen/aufgehoben und lange her
                _bans[ban.Fingerprint] = ban;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // nicht lesbar: ohne gespeicherte Sperren weitermachen
        }
    }

    private void Save()
    {
        if (_file is null) return;
        try
        {
            var sb = new StringBuilder()
                .AppendLine("# Floppy Hub - Sperren im offenen Chat (kommen nur als Admin-Nachricht, siehe ChatAdmins)")
                .AppendLine("# Fingerabdruck <TAB> ID <TAB> Ende (Unix-ms, 0 = dauerhaft) <TAB> Zeitpunkt (Unix-ms) <TAB> aufgehoben (0/1) <TAB> Grund");
            foreach (var b in _bans.Values.OrderBy(b => b.At))
                sb.Append(b.Fingerprint).Append('\t').Append(Clean(b.MemberId)).Append('\t')
                    .Append(b.Until.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(b.At.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(b.Lifted ? '1' : '0').Append('\t').AppendLine(Clean(b.Reason));

            FloppyPaths.EnsureDirectory(Path.GetDirectoryName(Path.GetFullPath(_file))!);
            var temp = _file + ".tmp";
            File.WriteAllText(temp, sb.ToString(), new UTF8Encoding(false));
            File.Move(temp, _file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Speichern ist nur eine Bequemlichkeit - die Sperre gilt trotzdem bis zum Beenden
        }
    }

    private static string Clean(string? text) => (text ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
}
