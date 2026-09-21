using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Floppy.Core;

/// <summary>Warum eine Tastenkombination nicht als "Sofort beenden" taugt.</summary>
public enum KeyChordProblem
{
    None,

    /// <summary>Keine Taste dabei (nur Strg/Alt/Umschalt) oder Unsinn.</summary>
    NoKey,

    /// <summary>Alt+F4: Das Schliessen von Windows soll es nicht sein.</summary>
    AltF4,

    /// <summary>Kombinationen mit der Windows-Taste faengt Windows selbst ab - die App sieht sie nie.</summary>
    WindowsKey,

    /// <summary>Alt+Tab, Alt+Esc, Alt+Leertaste, Strg+Esc, Strg+Umschalt+Esc, Strg+Alt+Entf: gehoeren Windows.</summary>
    SystemReserved,

    /// <summary>Einzelne Taste oder Umschalt+Taste: wuerde schon beim Tippen schliessen.</summary>
    NeedsModifier,

    /// <summary>Strg+Alt + Zeichentaste ist AltGr (@, €, { [ ] } ...): wuerde beim Tippen im Chat schliessen.</summary>
    AltGr,

    /// <summary>Die App benutzt diese Kombination schon (z. B. F5 = Aktualisieren).</summary>
    InUse,
}

/// <summary>
/// Eine Tastenkombination wie <c>Ctrl+Alt+F12</c> als reiner Text: Umschalter (Strg, Alt, Umschalt, Windows)
/// plus genau eine Taste. Die Taste steht mit dem Namen, den Godot (<c>OS.GetKeycodeString</c>) liefert -
/// dieser Kern kennt Godot nicht, er vergleicht nur Namen. Steht so in <c>app.ini</c> (<c>[app] quit_hotkey</c>).
/// <para>
/// <see cref="Validate"/> sagt, ob die Kombination als "Sofort beenden" taugt: nichts, was Windows selbst
/// abfaengt oder was schon beim normalen Tippen ausgeloest wuerde (Chat!).
/// </para>
/// </summary>
public sealed class KeyChord : IEquatable<KeyChord>
{
    private const int MaxKeyLength = 24;

    private static readonly string[] CtrlNames = ["ctrl", "control", "strg"];
    private static readonly string[] AltNames = ["alt"];
    private static readonly string[] ShiftNames = ["shift", "umschalt"];
    private static readonly string[] MetaNames = ["meta", "win", "windows", "super", "cmd"];

    /// <summary>Tasten, die (mit AltGr) ein Zeichen tippen - ausser den Ein-Zeichen-Namen (A, 7, #).</summary>
    private static readonly string[] CharacterKeyNames =
    [
        "Comma", "Period", "Slash", "Backslash", "Semicolon", "Apostrophe", "Minus", "Plus", "Equal", "Less", "Greater",
        "BracketLeft", "BracketRight", "QuoteLeft", "QuoteDbl", "Question", "Colon", "Underscore", "Asterisk",
        "AsciiTilde", "AsciiCircum", "Ampersand", "ParenLeft", "ParenRight", "BraceLeft", "BraceRight", "Bar",
        "NumberSign", "Dollar", "Percent", "At", "Exclam", "Ssharp",
    ];

    private KeyChord(bool ctrl, bool alt, bool shift, bool meta, string key)
    {
        Ctrl = ctrl;
        Alt = alt;
        Shift = shift;
        Meta = meta;
        Key = key;
    }

    public bool Ctrl { get; }
    public bool Alt { get; }
    public bool Shift { get; }
    public bool Meta { get; }

    /// <summary>Name der Taste, z. B. <c>F12</c>, <c>Q</c>, <c>Escape</c>.</summary>
    public string Key { get; }

    /// <summary>null, wenn die "Taste" leer, unsinnig oder selbst ein Umschalter ist.</summary>
    public static KeyChord? Create(bool ctrl, bool alt, bool shift, bool meta, string? key)
    {
        var name = NormalizeKey(key);
        if (name is null || IsModifierName(name)) return null;
        return new KeyChord(ctrl, alt, shift, meta, name);
    }

    /// <summary>
    /// Liest <c>Ctrl+Alt+F12</c>. Gross-/Kleinschreibung und Leerzeichen sind egal; deutsche Namen
    /// (<c>Strg</c>, <c>Umschalt</c>) gehen auch. Die Plus-Taste selbst: <c>Ctrl++</c>.
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out KeyChord? chord)
    {
        chord = null;
        var s = text?.Trim();
        if (string.IsNullOrEmpty(s)) return false;

        string modifiers, key;
        if (s == "+") { modifiers = ""; key = "+"; }
        else if (s.EndsWith("++", StringComparison.Ordinal)) { modifiers = s[..^1]; key = "+"; }
        else
        {
            var split = s.LastIndexOf('+');
            modifiers = split < 0 ? "" : s[..split];
            key = split < 0 ? s : s[(split + 1)..];
        }

        bool ctrl = false, alt = false, shift = false, meta = false;
        foreach (var part in modifiers.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Is(part, CtrlNames)) ctrl = true;
            else if (Is(part, AltNames)) alt = true;
            else if (Is(part, ShiftNames)) shift = true;
            else if (Is(part, MetaNames)) meta = true;
            else return false;
        }

        chord = Create(ctrl, alt, shift, meta, key);
        return chord is not null;
    }

    /// <summary>Kanonisch, so wie es in der INI steht: <c>Ctrl+Alt+Shift+Meta+Taste</c>.</summary>
    public override string ToString() => ToString("Ctrl", "Alt", "Shift", "Win");

    /// <summary>Wie <see cref="ToString()"/>, mit anderen Namen fuer die Umschalter (z. B. Strg/Umschalt fuer die Anzeige).</summary>
    public string ToString(string ctrl, string alt, string shift, string meta)
    {
        var sb = new StringBuilder();
        if (Ctrl) sb.Append(ctrl).Append('+');
        if (Alt) sb.Append(alt).Append('+');
        if (Shift) sb.Append(shift).Append('+');
        if (Meta) sb.Append(meta).Append('+');
        return sb.Append(Key).ToString();
    }

    /// <summary>
    /// Taugt die Kombination als "Sofort beenden"?
    /// </summary>
    /// <param name="inUse">Kombinationen, die die App selbst schon belegt (z. B. <c>F5</c>).</param>
    public KeyChordProblem Validate(IEnumerable<string>? inUse = null)
    {
        if (Meta) return KeyChordProblem.WindowsKey;
        if (Alt && Key.Equals("F4", StringComparison.OrdinalIgnoreCase)) return KeyChordProblem.AltF4;

        var reserved =
            (Alt && (Is(Key, "Tab", "Escape", "Space"))) ||
            (Ctrl && Is(Key, "Escape")) ||
            (Ctrl && Alt && Is(Key, "Delete", "Del"));
        if (reserved) return KeyChordProblem.SystemReserved;

        if (!Ctrl && !Alt && !IsFunctionKey(Key)) return KeyChordProblem.NeedsModifier;
        if (Ctrl && Alt && IsCharacterKey(Key)) return KeyChordProblem.AltGr;

        if (inUse is not null)
        {
            foreach (var other in inUse)
                if (TryParse(other, out var used) && Equals(used)) return KeyChordProblem.InUse;
        }
        return KeyChordProblem.None;
    }

    public bool Equals(KeyChord? other) =>
        other is not null && Ctrl == other.Ctrl && Alt == other.Alt && Shift == other.Shift && Meta == other.Meta &&
        Key.Equals(other.Key, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => obj is KeyChord other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Ctrl, Alt, Shift, Meta, StringComparer.OrdinalIgnoreCase.GetHashCode(Key));

    // ------------------------------------------------------------------
    // Helfer
    // ------------------------------------------------------------------

    /// <summary>F1 bis F24: tippen kein Zeichen, duerfen also auch allein (oder mit Umschalt) stehen.</summary>
    public static bool IsFunctionKey(string key) =>
        key.Length is >= 2 and <= 3 && (key[0] is 'F' or 'f') && key[1] != '0' &&
        int.TryParse(key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 24;

    /// <summary>Taste, die (mit AltGr) ein Zeichen tippt.</summary>
    public static bool IsCharacterKey(string key) =>
        key.Length == 1 || CharacterKeyNames.Contains(key, StringComparer.OrdinalIgnoreCase);

    private static bool Is(string value, params string[] names) =>
        names.Contains(value, StringComparer.OrdinalIgnoreCase);

    private static bool IsModifierName(string name) =>
        Is(name, CtrlNames) || Is(name, AltNames) || Is(name, ShiftNames) || Is(name, MetaNames);

    private static string? NormalizeKey(string? key)
    {
        var name = string.Join(' ', (key ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (name.Length == 0 || name.Length > MaxKeyLength || name.Any(char.IsControl)) return null;
        if (name.Length > 1 && name.Contains('+')) return null;
        if (name.Length == 1) return name.ToUpperInvariant();
        if (IsFunctionKey(name)) return "F" + name[1..];

        // "escape" / "kp 5" -> "Escape" / "Kp 5"; gemischte Schreibweise (BracketLeft) bleibt.
        return name == name.ToLowerInvariant()
            ? string.Join(' ', name.Split(' ').Select(w => char.ToUpperInvariant(w[0]) + w[1..]))
            : name;
    }
}
