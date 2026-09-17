using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Floppy.Core.Chat;

/// <summary>
/// Ein gespeicherter Kontakt = Name + ID-Nummer (Schluessel-Fingerabdruck) + optional die
/// Verschluesselung des gemeinsamen Raums. Die Verschluesselung liegt per Windows-Datenschutz
/// geschuetzt in der Datei, nie im Klartext.
/// </summary>
public sealed record ChatContact
{
    public const int MaxNameLength = 24;

    public string Name { get; init; } = "";
    public string MemberId { get; init; } = "";

    /// <summary>SHA-256 des oeffentlichen Schluessels - daran wird der Kontakt sicher erkannt.</summary>
    public string Fingerprint { get; init; } = "";

    /// <summary>Verschluesselung des Raums (DPAPI, Base64) oder leer.</summary>
    public string ProtectedSecret { get; init; } = "";

    public DateTime Added { get; init; }

    [JsonIgnore]
    public bool HasSecret => ProtectedSecret.Length > 0;
}

internal sealed class ContactDocument
{
    public int Version { get; set; } = 1;
    public List<ChatContact> Contacts { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ContactDocument))]
internal sealed partial class ContactJsonContext : JsonSerializerContext;

/// <summary>Kontaktliste in <c>%LOCALAPPDATA%\FloppyHub\chat\contacts.json</c>.</summary>
public sealed class ChatContactBook(string filePath)
{
    public const string FileName = "contacts.json";
    public const int MaxContacts = 200;

    public string FilePath { get; } = filePath;

    public IReadOnlyList<ChatContact> Load()
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length > 512 * 1024) return [];
            var doc = JsonSerializer.Deserialize(File.ReadAllText(FilePath), ContactJsonContext.Default.ContactDocument);
            return doc?.Contacts?.Where(c => c is not null && c.Fingerprint.Length == 64 && c.Name.Length > 0).ToList() ?? [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public ChatContact? Find(string fingerprint) =>
        Load().FirstOrDefault(c => string.Equals(c.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));

    /// <summary>Anlegen oder aktualisieren (gleicher Fingerabdruck = derselbe Kontakt).</summary>
    /// <param name="secret">Verschluesselung des Raums mitspeichern (null = vorhandene behalten, "" = entfernen).</param>
    public ChatContact Save(string name, string memberId, string fingerprint, string? secret, DateTime? now = null)
    {
        var contacts = Load().ToList();
        var old = contacts.FirstOrDefault(c => string.Equals(c.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
        var contact = new ChatContact
        {
            Name = CleanName(name),
            MemberId = memberId,
            Fingerprint = fingerprint.ToUpperInvariant(),
            ProtectedSecret = secret switch
            {
                null => old?.ProtectedSecret ?? "",
                "" => "",
                _ => Convert.ToBase64String(LocalSecret.Protect(Encoding.UTF8.GetBytes(ChatRoomKey.Normalize(secret)))),
            },
            Added = old?.Added ?? now ?? DateTime.Now,
        };
        contacts.RemoveAll(c => string.Equals(c.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase));
        if (contacts.Count >= MaxContacts) throw new InvalidOperationException($"Hoechstens {MaxContacts} Kontakte.");
        contacts.Add(contact);
        Write(contacts);
        return contact;
    }

    public bool Remove(string fingerprint)
    {
        var contacts = Load().ToList();
        if (contacts.RemoveAll(c => string.Equals(c.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase)) == 0) return false;
        Write(contacts);
        return true;
    }

    /// <summary>Gespeicherte Verschluesselung entschluesseln (null = keine oder nicht lesbar).</summary>
    public static string? RevealSecret(ChatContact contact)
    {
        if (!contact.HasSecret) return null;
        try
        {
            var plain = LocalSecret.Unprotect(Convert.FromBase64String(contact.ProtectedSecret));
            return plain is null ? null : Encoding.UTF8.GetString(plain);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static string CleanName(string? name)
    {
        var cleaned = ChatSession.CleanText(name);
        if (cleaned.Length > ChatContact.MaxNameLength) cleaned = cleaned[..ChatContact.MaxNameLength].Trim();
        return cleaned;
    }

    private void Write(List<ChatContact> contacts)
    {
        FloppyPaths.EnsureDirectory(Path.GetDirectoryName(Path.GetFullPath(FilePath))!);
        var doc = new ContactDocument { Contacts = contacts.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList() };
        var temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(doc, ContactJsonContext.Default.ContactDocument));
        File.Move(temp, FilePath, overwrite: true);
    }
}
