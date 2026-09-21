using Floppy.Core.Chat;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>
/// Die acht Namensfarben im Chat, getrennt fuer hellen und dunklen Modus, damit sie in beiden lesbar bleiben.
/// Wer keine Farbe waehlt, bekommt eine aus seinem Fingerabdruck (wie bisher).
/// </summary>
public static class ChatColors
{
    private static readonly string[] Light = ["#546e7a", "#b33a3a", "#2e8b3e", "#8e44ad", "#b86200", "#00838f", "#6d4c41", "#ad1457"];
    private static readonly string[] Dark = ["#9fb3c8", "#ff7a7a", "#6fd37f", "#c38bff", "#ffae4a", "#4fd6e0", "#d4ae98", "#ff7ab0"];

    static ChatColors()
    {
        if (Light.Length != ChatProfile.ColorCount || Dark.Length != ChatProfile.ColorCount)
            throw new InvalidOperationException("Anzahl der Namensfarben und ChatProfile.ColorCount muessen gleich sein.");
    }

    /// <param name="index">1 bis <see cref="ChatProfile.ColorCount"/>.</param>
    public static Color Get(int index, bool dark) => new((dark ? Dark : Light)[Math.Clamp(index, 1, Light.Length) - 1]);

    /// <summary>Automatische Farbe aus dem Fingerabdruck.</summary>
    public static Color Auto(string fingerprint, bool dark) =>
        Get(Convert.ToInt32(fingerprint[..2], 16) % Light.Length + 1, dark);
}
