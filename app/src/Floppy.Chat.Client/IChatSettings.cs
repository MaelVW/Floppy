using Floppy.Core.Chat;

namespace Floppy.Chat.Client;

/// <summary>Einstellungen, die der Chat selbst braucht (die Handy-App speichert sie in ihren Einstellungen).</summary>
public interface IChatSettings
{
    /// <summary>Anzeigename wie eingegeben (wird mit <see cref="ChatProfile.Clean"/> gesaeubert), null/leer = keiner.</summary>
    string? Alias { get; }

    /// <summary>0 = automatisch, 1 bis 8.</summary>
    int Color { get; }

    ChatNotifyMode Notify { get; }

    /// <summary>Nachrichten mit meinem Namen farbig hinterlegen.</summary>
    bool HighlightMentions { get; }

    /// <summary>Adresse des ntfy-Dienstes; leer = Standard (ntfy.sh).</summary>
    string Server { get; }
}

/// <summary>Einstellungen nur im Speicher (Tests, Vorschau).</summary>
public sealed class MemoryChatSettings : IChatSettings
{
    public string? Alias { get; set; }
    public int Color { get; set; }
    public ChatNotifyMode Notify { get; set; } = ChatNotifyMode.Off;
    public bool HighlightMentions { get; set; } = true;
    public string Server { get; set; } = "";
}
