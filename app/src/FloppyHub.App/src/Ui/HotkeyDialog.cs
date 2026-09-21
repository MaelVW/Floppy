using Floppy.Core;
using FloppyHub.App.Core;
using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>
/// "Tastenkombination festlegen": faengt den naechsten Tastendruck auf, zeigt ihn gross an und prueft ihn
/// (<see cref="KeyChord.Validate"/>). Passt sie, wird sie gemeldet und der Dialog schliesst; sonst steht
/// der Grund im Dialog und er wartet weiter. Esc bricht ab.
/// </summary>
public partial class HotkeyDialog : RetroDialog
{
    private readonly Label _live;
    private readonly Label _problem;
    private readonly Action<KeyChord> _chosen;
    private bool _showingRejected;

    public HotkeyDialog() : this(_ => { }) { }

    public HotkeyDialog(Action<KeyChord> chosen) : base(Loc.T("PANIC_CAPTURE_TITLE"), "info", 480)
    {
        _chosen = chosen;

        _live = Ui.Label(HotkeyInput.DisplayHeld(), "TitleLabel");
        _live.HorizontalAlignment = HorizontalAlignment.Center;
        _live.CustomMinimumSize = new Vector2(0, 40);

        _problem = Ui.Label("", wrap: true);
        _problem.AddThemeColorOverride("font_color", Palette.Current.Warn);
        _problem.CustomMinimumSize = new Vector2(0, 36);

        Body.AddChild(Ui.Label(Loc.T("PANIC_CAPTURE_TEXT"), wrap: true));
        Body.AddChild(Ui.Panel("FieldPanel", Ui.Margin(_live, 6)));
        Body.AddChild(_problem);
        Body.AddChild(Ui.Dim(Loc.T("PANIC_CAPTURE_NOTE"), wrap: true));
        AddButton(Loc.T("BTN_CANCEL"), () => { });
    }

    /// <summary>Oeffnet den Dialog; <paramref name="chosen"/> bekommt die gepruefte Kombination.</summary>
    public static void Capture(Control host, Action<KeyChord> chosen) => new HotkeyDialog(chosen).Open(host);

    public override void _EnterTree() => HotkeyInput.Capturing = true;

    public override void _ExitTree() => HotkeyInput.Capturing = false;

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey key) return;

        // Alles abfangen: Leertaste/Enter wuerden sonst den Knopf druecken, Tab den Fokus wandern lassen.
        GetViewport().SetInputAsHandled();
        if (key.Echo) return;

        if (!key.Pressed)
        {
            if (!_showingRejected) _live.Text = HotkeyInput.DisplayHeld();   // Umschalter losgelassen
            return;
        }

        if (key is { Keycode: Key.Escape, CtrlPressed: false, AltPressed: false, ShiftPressed: false, MetaPressed: false })
        {
            Cancel();
            return;
        }

        _showingRejected = false;   // neuer Versuch
        _problem.Text = "";
        var chord = HotkeyInput.FromEvent(key);
        if (chord is null)
        {
            _live.Text = HotkeyInput.DisplayHeld();   // erst ein Umschalter: weiter warten
            return;
        }

        _live.Text = HotkeyInput.Display(chord);
        var problem = chord.Validate(AppShortcuts.InUse);
        if (problem != KeyChordProblem.None)
        {
            _showingRejected = true;   // die abgelehnte Kombination bleibt stehen, bis der naechste Versuch beginnt
            _problem.Text = Loc.T("PANIC_PROBLEM_" + problem.ToString().ToUpperInvariant());
            return;
        }

        _chosen(chord);
        Close();
    }
}
