using System.Security.Cryptography;
using System.Text;

namespace Floppy.Core.Chat;

public enum SecretProblem { None, Empty, TooShort, TooLong, InvalidCharacters }

/// <summary>
/// Aus der eingetippten Verschluesselung abgeleitete Raum-Schluessel. Wer dieselbe
/// Verschluesselung eingibt, landet im selben Raum und kann mitlesen - alle anderen
/// (auch der Gratis-Dienst) sehen nur Datensalat.
///
/// Ableitung: PBKDF2-SHA256 (bewusst langsam, damit kurze Verschluesselungen nicht einfach
/// durchprobiert werden koennen), danach HKDF getrennt fuer AES-Schluessel, Themenname beim
/// Dienst, Raum-Kennung (Unterschriften) und Suche im lokalen Netz.
/// </summary>
public sealed class ChatRoomKey
{
    public const int MinSecretLength = 6;
    public const int MaxSecretLength = 200;
    public const int Iterations = 200_000;

    /// <summary>
    /// Offener Chat: feste, oeffentlich bekannte Verschluesselung, damit man sich ohne
    /// eigenen Schluessel an den Chat gewoehnen kann - jeder mit Floppy Hub landet im
    /// selben Raum und kann mitlesen. Fuer private Gespraeche eine eigene Verschluesselung nutzen.
    /// </summary>
    public const string OpenSecret = "floppyhub-open-chat-2026";

    /// <summary>Ob der offene Chat angeboten wird.</summary>
    public const bool OpenRoomAvailable = true;

    private static readonly byte[] Salt = "FloppyHub.Chat.Room.v1"u8.ToArray();
    private const string KeyAlphabet = "23456789ABCDEFGHJKMNPQRSTVWXYZ";   // ohne 0/O, 1/I/L, U

    private static ChatRoomKey? _open;

    private ChatRoomKey(byte[] master, bool isOpen)
    {
        IsOpen = isOpen;
        AesKey = Expand(master, "aes-gcm", 32);
        Tag = Expand(master, "room-tag", 16);
        LanKey = Expand(master, "lan-key", 32);
        Topic = "floppyhub-" + Base32(Expand(master, "ntfy-topic", 20));
        Check = ToCheck(Expand(master, "check", 3));
        CryptographicOperations.ZeroMemory(master);
    }

    public bool IsOpen { get; }

    /// <summary>Themenname beim Dienst (ntfy): <c>floppyhub-</c> + 32 Zeichen, verraet nichts ueber die Verschluesselung.</summary>
    public string Topic { get; }

    /// <summary>Kurze Pruefzahl (z. B. <c>K7Q-M2P</c>): Stimmt sie bei allen ueberein, ist es derselbe Raum.</summary>
    public string Check { get; }

    internal byte[] AesKey { get; }
    internal byte[] Tag { get; }
    /// <summary>Fuer Suche + Handschlag im lokalen Netz (HMAC), getrennt vom Nachrichtenschluessel.</summary>
    internal byte[] LanKey { get; }

    /// <summary>Langsam (PBKDF2) - nicht im UI-Thread aufrufen.</summary>
    public static ChatRoomKey Derive(string secret)
    {
        if (Problem(secret) is var problem and not SecretProblem.None)
            throw new ArgumentException($"Verschluesselung ungueltig: {problem}", nameof(secret));
        var normalized = Normalize(secret);
        var master = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(normalized), Salt, Iterations, HashAlgorithmName.SHA256, 32);
        return new ChatRoomKey(master, normalized == OpenSecret);
    }

    /// <summary>Der offene Chat (einmal abgeleitet, danach gemerkt).</summary>
    public static ChatRoomKey Open => _open ??= Derive(OpenSecret);

    /// <summary>Leerzeichen am Rand weg, Unicode vereinheitlicht (sonst waeren "ä" und "a¨" verschiedene Raeume).</summary>
    public static string Normalize(string secret) => (secret ?? "").Trim().Normalize(NormalizationForm.FormC);

    /// <summary>Warum diese Verschluesselung nicht geht (<see cref="SecretProblem.None"/> = geht).</summary>
    public static SecretProblem Problem(string? secret)
    {
        var s = Normalize(secret ?? "");
        if (s.Length == 0) return SecretProblem.Empty;
        if (s.Length < MinSecretLength) return SecretProblem.TooShort;
        if (s.Length > MaxSecretLength) return SecretProblem.TooLong;
        if (s.Any(char.IsControl)) return SecretProblem.InvalidCharacters;
        return SecretProblem.None;
    }

    /// <summary>true = leicht zu erraten (kurz, nur Ziffern, ...). Nur ein Hinweis, kein Verbot.</summary>
    public static bool IsWeak(string secret)
    {
        var s = Normalize(secret);
        if (s == OpenSecret) return false;
        var classes = (s.Any(char.IsDigit) ? 1 : 0) + (s.Any(char.IsUpper) ? 1 : 0) +
                      (s.Any(char.IsLower) ? 1 : 0) + (s.Any(c => !char.IsLetterOrDigit(c)) ? 1 : 0);
        return s.Length < 12 || (s.Length < 16 && classes < 2) || s.Distinct().Count() < 5;
    }

    /// <summary>Zufaellige, gut abzutippende Verschluesselung: <c>K7QM-P2XD-9HVT-R4WN-C3JA</c> (~98 Bit).</summary>
    public static string Generate()
    {
        var sb = new StringBuilder(24);
        for (var i = 0; i < 20; i++)
        {
            if (i > 0 && i % 4 == 0) sb.Append('-');
            sb.Append(KeyAlphabet[RandomNumberGenerator.GetInt32(KeyAlphabet.Length)]);
        }
        return sb.ToString();
    }

    private static byte[] Expand(byte[] master, string label, int length) =>
        HKDF.Expand(HashAlgorithmName.SHA256, master, length, Encoding.ASCII.GetBytes("FloppyHub.Chat." + label));

    private static string Base32(ReadOnlySpan<byte> data)
    {
        const string alphabet = "abcdefghijklmnopqrstuvwxyz234567";
        var sb = new StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if (bits > 0) sb.Append(alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static string ToCheck(ReadOnlySpan<byte> data)
    {
        var value = (data[0] << 16) | (data[1] << 8) | data[2];
        var sb = new StringBuilder(7);
        for (var i = 0; i < 6; i++)
        {
            if (i == 3) sb.Append('-');
            sb.Append(KeyAlphabet[value % KeyAlphabet.Length]);
            value /= KeyAlphabet.Length;
        }
        return sb.ToString();
    }
}
