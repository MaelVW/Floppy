using FloppyHub.App.Art;
using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>
/// Dialogfenster innerhalb der App: Verlaufs-Titelleiste wie Windows 2000,
/// abgedunkelter Hintergrund, Knopfreihe rechts unten. Blockiert die App dahinter.
/// </summary>
public partial class RetroDialog : Control
{
    private readonly VBoxContainer _body;
    private readonly HBoxContainer _buttons;
    private readonly PanelContainer _frame;

    public RetroDialog() : this("", "info") { }

    public RetroDialog(string title, string icon, float width = 460)
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        AutoTranslateMode = AutoTranslateModeEnum.Disabled;

        var dim = new ColorRect { Color = new Color(0, 0, 0, Palette.Current.Dark ? 0.45f : 0.28f), MouseFilter = MouseFilterEnum.Ignore };
        dim.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(dim);

        var caption = Ui.Label(title, "CaptionLabel").Expand();
        var close = new Button { Text = "×", FocusMode = FocusModeEnum.None, CustomMinimumSize = new Vector2(20, 18) };
        close.AddThemeConstantOverride("h_separation", 0);
        close.Pressed += Cancel;
        var titleBar = Ui.Panel("TitlebarPanel", Ui.HBox(6, Icons.Rect("app", 0.5f), caption, close));

        _body = Ui.VBox(8);
        _buttons = Ui.HBox(6);
        _buttons.Alignment = BoxContainer.AlignmentMode.End;

        var inner = Ui.VBox(0, titleBar, Ui.Margin(Ui.VBox(12, _body, _buttons), 12, 12, 12, 10));
        _frame = Ui.Panel("DialogPanel", inner);
        _frame.CustomMinimumSize = new Vector2(width, 0);

        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        center.AddChild(_frame);
        AddChild(center);
    }

    /// <summary>Abbrechen (X, Esc).</summary>
    public event Action? Cancelled;

    public VBoxContainer Body => _body;

    public Button AddButton(string text, Action action, bool closes = true, string? icon = null)
    {
        var b = Ui.Button(text, icon, () =>
        {
            action();
            if (closes) Close();
        });
        b.CustomMinimumSize = new Vector2(88, 0);
        _buttons.AddChild(b);
        return b;
    }

    public void Open(Control host)
    {
        host.AddChild(this);
        foreach (var child in _buttons.GetChildren())
        {
            if (child is Button b)
            {
                b.CallDeferred(Control.MethodName.GrabFocus);
                break;
            }
        }
    }

    public void Close() => QueueFree();

    public void Cancel()
    {
        Cancelled?.Invoke();
        Close();
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Cancel();
        }
    }

    /// <summary>Einfache Frage mit Ja/Nein.</summary>
    public static void Ask(Control host, string title, string text, string yes, Action onYes, string icon = "warn")
    {
        var d = new RetroDialog(title, icon);
        d.Body.AddChild(Ui.HBox(12, Icons.Rect(icon, 2f), Ui.Label(text, wrap: true).Expand()));
        d.AddButton(yes, onYes);
        d.AddButton(Core.Loc.T("BTN_CANCEL"), () => { });
        d.Open(host);
    }

    public static void Message(Control host, string title, string text, string icon = "info")
    {
        var d = new RetroDialog(title, icon);
        d.Body.AddChild(Ui.HBox(12, Icons.Rect(icon, 2f), Ui.Label(text, wrap: true).Expand()));
        d.AddButton(Core.Loc.T("BTN_OK"), () => { });
        d.Open(host);
    }
}
