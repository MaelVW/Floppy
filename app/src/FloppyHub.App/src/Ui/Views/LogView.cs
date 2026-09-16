using Floppy.Core;
using FloppyHub.App.Core;
using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Ui.Views;

/// <summary>Launcher-Log (gleiche Datei wie Motor und Konsole), farbig nach Stufe.</summary>
public partial class LogView : ViewBase
{
    public override string Key => "log";

    private RichTextLabel _text = null!;
    private CheckBox _auto = null!;
    private Label _info = null!;
    private (long Length, DateTime Written) _seen;

    private string? LogPath => Host.Services.Log.FilePath;

    protected override void Build()
    {
        _auto = new CheckBox { Text = Loc.T("LOG_AUTO"), ButtonPressed = true };
        _info = Ui.Dim("");
        _info.ClipText = true;
        _info.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;

        var clear = Ui.Button(Loc.T("BTN_CLEAR_LOG"), "remove", () =>
            RetroDialog.Ask(Host.DialogLayer, Loc.T("LOG_CLEAR_TITLE"), Loc.T("LOG_CLEAR_TEXT"), Loc.T("BTN_CLEAR_LOG"), () =>
            {
                if (Host.Services.ReadOnlyMode) return;
                var result = LogFile.Clear(LogPath);
                Host.SetStatusMessage(result.Message, result.Cleared ? "ok" : "warn");
                Load();
            }));

        AddChild(Ui.HBox(8,
            Ui.Button(Loc.T("BTN_REFRESH"), "refresh", Load),
            clear,
            _auto,
            _info.Expand()));

        _text = new RichTextLabel
        {
            BbcodeEnabled = false,
            ScrollFollowing = true,
            SelectionEnabled = true,
            ContextMenuEnabled = true,
            AutoTranslateMode = AutoTranslateModeEnum.Disabled,
        }.Expand(vertical: true);
        _text.AddThemeFontOverride("normal_font", new SystemFont
        {
            FontNames = ["Lucida Console", "Consolas", "Courier New"],
            Hinting = TextServer.Hinting.Normal,
            Antialiasing = TextServer.FontAntialiasing.Gray,
        });
        AddChild(_text);
    }

    public override void OnShown() => Load();

    public override void OnTick()
    {
        if (!_auto.ButtonPressed || LogPath is null) return;
        var fi = new FileInfo(LogPath);
        var now = fi.Exists ? (fi.Length, fi.LastWriteTimeUtc) : (0L, DateTime.MinValue);
        if (now != _seen) Load();
    }

    private void Load()
    {
        var path = LogPath;
        _text.Clear();
        if (path is null)
        {
            _info.Text = Loc.T("LOG_DISABLED");
            return;
        }

        var fi = new FileInfo(path);
        _seen = fi.Exists ? (fi.Length, fi.LastWriteTimeUtc) : (0L, DateTime.MinValue);
        _info.Text = fi.Exists ? $"{path}  ({Ui.Bytes(fi.Length)})" : Loc.T("LOG_MISSING", path);
        _info.TooltipText = path;

        var p = Palette.Current;
        foreach (var line in LogFile.ReadTail(path, 400))
        {
            var color = line.Contains("[ERROR]") ? p.Error
                : line.Contains("[WARN ]") ? p.Warn
                : line.Contains("[OK   ]") ? p.Ok
                : line.Contains("[ASK  ]") ? p.Accent
                : p.Text;
            _text.PushColor(color);
            _text.AddText(line + "\n");
            _text.Pop();
        }
    }
}
