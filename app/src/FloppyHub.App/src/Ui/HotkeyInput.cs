using Floppy.Core;
using FloppyHub.App.Core;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>"Sofort beenden": Godot-Tastendruecke in eine <see cref="KeyChord"/> uebersetzen und anzeigen.</summary>
public static class HotkeyInput
{
    /// <summary>true, solange "Tastenkombination festlegen" offen ist: die Sofort-beenden-Taste ruht dann (sonst schliesst sie schon beim Festlegen).</summary>
    public static bool Capturing { get; set; }

    public static bool IsModifierKey(Key key) => key is Key.Ctrl or Key.Shift or Key.Alt or Key.Meta;

    /// <summary>null bei reinen Umschalter-Tasten oder unbekannten Tasten.</summary>
    public static KeyChord? FromEvent(InputEventKey e) =>
        e.Keycode is Key.None or Key.Unknown || IsModifierKey(e.Keycode)
            ? null
            : KeyChord.Create(e.CtrlPressed, e.AltPressed, e.ShiftPressed, e.MetaPressed, OS.GetKeycodeString(e.Keycode));

    /// <summary>Fuer die Anzeige: "Strg+Alt+F12" bzw. "Ctrl+Alt+F12".</summary>
    public static string Display(KeyChord chord) =>
        chord.ToString(Loc.T("KEY_CTRL"), Loc.T("KEY_ALT"), Loc.T("KEY_SHIFT"), Loc.T("KEY_META"));

    /// <summary>Die gerade gehaltenen Umschalter, z. B. "Strg+Alt+ …" (waehrend die Taste noch fehlt).</summary>
    public static string DisplayHeld()
    {
        var parts = new List<string>();
        if (Input.IsKeyPressed(Key.Ctrl)) parts.Add(Loc.T("KEY_CTRL"));
        if (Input.IsKeyPressed(Key.Alt)) parts.Add(Loc.T("KEY_ALT"));
        if (Input.IsKeyPressed(Key.Shift)) parts.Add(Loc.T("KEY_SHIFT"));
        if (Input.IsKeyPressed(Key.Meta)) parts.Add(Loc.T("KEY_META"));
        return parts.Count == 0 ? Loc.T("PANIC_CAPTURE_WAIT") : string.Join("+", parts) + "+ …";
    }
}
