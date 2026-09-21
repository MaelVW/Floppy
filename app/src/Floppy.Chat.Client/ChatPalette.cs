using System.Globalization;
using Floppy.Core.Chat;

namespace Floppy.Chat.Client;

/// <summary>
/// Die acht Namensfarben im Chat, getrennt fuer hellen und dunklen Modus (dieselben Werte wie in der
/// Desktop-App). Wer keine Farbe waehlt, bekommt eine aus seinem Fingerabdruck.
/// </summary>
public static class ChatPalette
{
    private static readonly string[] Light = ["#546e7a", "#b33a3a", "#2e8b3e", "#8e44ad", "#b86200", "#00838f", "#6d4c41", "#ad1457"];
    private static readonly string[] Dark = ["#9fb3c8", "#ff7a7a", "#6fd37f", "#c38bff", "#ffae4a", "#4fd6e0", "#d4ae98", "#ff7ab0"];

    static ChatPalette()
    {
        if (Light.Length != ChatProfile.ColorCount || Dark.Length != ChatProfile.ColorCount)
            throw new InvalidOperationException("Anzahl der Namensfarben und ChatProfile.ColorCount muessen gleich sein.");
    }

    /// <param name="index">1 bis <see cref="ChatProfile.ColorCount"/>.</param>
    public static string Hex(int index, bool dark) => (dark ? Dark : Light)[Math.Clamp(index, 1, Light.Length) - 1];

    /// <summary>Automatische Farbe (1 bis 8) aus dem Fingerabdruck.</summary>
    public static int AutoIndex(string? fingerprint) =>
        fingerprint is { Length: >= 2 } && int.TryParse(fingerprint.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)
            ? b % ChatProfile.ColorCount + 1
            : 1;
}
