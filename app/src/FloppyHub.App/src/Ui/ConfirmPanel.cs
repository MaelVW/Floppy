using Floppy.Core;
using FloppyHub.App.Art;
using FloppyHub.App.Core;
using FloppyHub.App.Services;
using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>
/// Rueckfrage vor dem Start eines (noch) nicht freigegebenen Programms:
/// Einmal starten / Immer erlauben / Abbrechen. Laeuft nach ConfirmTimeout ab.
/// </summary>
public partial class ConfirmPanel : VBoxContainer
{
    private AppServices _s = null!;
    private IReadOnlyList<string> _candidates = [];
    private string? _arguments;
    private string _label = "";
    private string? _discRoot;
    private ItemList? _choices;
    private ProgressBar _countdown = null!;
    private Label _countdownText = null!;
    private Label _error = null!;
    private Button _once = null!;
    private Button _always = null!;
    private Timer _timer = null!;
    private int _remaining;
    private bool _done;

    /// <summary>true = Programm wurde gestartet.</summary>
    public event Action<bool>? Finished;

    /// <summary>Rueckfrage fuer die eingelegte Diskette.</summary>
    public void SetupForDisc(AppServices s, DiscState state, IReadOnlyList<LibraryEntry> library)
    {
        var plan = state.Plan!;
        var decision = state.Decision!;
        var onDisc = plan.Candidates.All(c => PathRules.IsUnder(c, state.Root));
        var source = plan.Source is { } src && src != "EXE-Suche" ? src : Loc.T("CONFIRM_SOURCE_SCAN", state.Root);
        Setup(s, plan.Candidates, plan.Arguments, state.DisplayName(library), decision.Trust, source,
            Loc.T(onDisc ? "CONFIRM_TITLE_DISC" : "CONFIRM_TITLE_PC"), state.Root);
    }

    /// <summary>Rueckfrage fuer ein Programm aus der Bibliothek.</summary>
    public void SetupForProgram(AppServices s, string path, string? arguments, string label)
    {
        Setup(s, [path], arguments, label, s.Trust.Check(path, arguments), Loc.T("CONFIRM_SOURCE_LIBRARY"),
            Loc.T("CONFIRM_TITLE_PC"), null);
    }

    private void Setup(AppServices s, IReadOnlyList<string> candidates, string? arguments, string label,
        TrustState trust, string source, string title, string? discRoot)
    {
        _s = s;
        _candidates = candidates;
        _arguments = string.IsNullOrWhiteSpace(arguments) ? null : arguments.Trim();
        _label = label;
        _discRoot = discRoot;
        AddThemeConstantOverride("separation", 10);
        SizeFlagsVertical = SizeFlags.ExpandFill;

        var reason = candidates.Count > 1 ? Loc.T("CONFIRM_REASON_MULTIPLE")
            : trust == TrustState.Changed ? Loc.T("CONFIRM_REASON_CHANGED")
            : Loc.T("CONFIRM_REASON_NEW");
        AddChild(Ui.HBox(12,
            Icons.Rect(trust == TrustState.Changed ? "error" : "warn", 2f),
            Ui.VBox(2, Ui.Label(title, "TitleLabel", wrap: true), Ui.Label(reason, wrap: true)).Expand()));

        // ---- Programm ----
        var details = Ui.VBox(6);
        if (candidates.Count == 1)
        {
            var grid = new GridContainer { Columns = 2 };
            void Row(string key, string value)
            {
                grid.AddChild(Ui.Dim(Loc.T(key)));
                grid.AddChild(Ui.Label(value, wrap: true).Expand());
            }
            var path = candidates[0];
            Row("CONFIRM_NAME", label);
            Row("CONFIRM_PATH", path);
            if (_arguments is not null) Row("CONFIRM_ARGS", _arguments);
            Row("CONFIRM_SOURCE", source);
            try
            {
                var fi = new FileInfo(path);
                if (fi.Exists) Row("CONFIRM_FILE", $"{Ui.Bytes(fi.Length)} · {fi.LastWriteTime:yyyy-MM-dd HH:mm}");
            }
            catch { }
            details.AddChild(grid);
        }
        else
        {
            details.AddChild(Ui.Label(Loc.T("CONFIRM_CHOOSE"), wrap: true));
            _choices = new ItemList { CustomMinimumSize = new Vector2(0, 110), AutoTranslateMode = AutoTranslateModeEnum.Disabled }.Expand(vertical: true);
            foreach (var c in candidates) _choices.AddItem(c, Icons.Get("exe"));
            _choices.ItemSelected += _ => UpdateButtons();
            details.AddChild(_choices);
            if (_arguments is not null) details.AddChild(Ui.Dim(Loc.T("PLAN_ARGS", _arguments)));
        }
        AddChild(new GroupBox(Loc.T("CONFIRM_PROGRAM"), details) { SizeFlagsVertical = SizeFlags.ExpandFill });

        AddChild(Ui.Panel("HintPanel", Ui.IconLine("trust", Loc.T("CONFIRM_HINT"))));

        _error = Ui.Label("", wrap: true);
        _error.AddThemeColorOverride("font_color", Palette.Current.Error);
        _error.Visible = false;
        AddChild(_error);

        // ---- Countdown + Knoepfe ----
        _remaining = s.Options.ConfirmTimeoutSeconds;
        _countdown = new ProgressBar { ShowPercentage = false, MaxValue = _remaining, Value = _remaining, CustomMinimumSize = new Vector2(120, 16) };
        _countdownText = Ui.Dim("");
        _always = Ui.Button(Loc.T("BTN_ALWAYS"), "trust", () => Decide(always: true));
        _once = Ui.Button(Loc.T("BTN_ONCE"), "start", () => Decide(always: false));
        var cancel = Ui.Button(Loc.T("BTN_CANCEL"), null, () => Cancel(Loc.T("CONFIRM_CANCELLED")));
        foreach (var b in new[] { _always, _once, cancel }) b.CustomMinimumSize = new Vector2(96, 0);

        AddChild(Ui.HBox(8, _countdown, _countdownText, Ui.Spacer(), _always, _once, cancel));
        UpdateButtons();
        UpdateCountdown();

        _timer = new Timer { WaitTime = 1, Autostart = true };
        _timer.Timeout += Tick;
        AddChild(_timer);

        _s.Log.Write(LogLevel.Ask, $"App fragt nach: {string.Join(" | ", candidates)} {_arguments}".TrimEnd());
        cancel.CallDeferred(Control.MethodName.GrabFocus);   // sicherer Standard: Abbrechen
    }

    private string? Selected =>
        _candidates.Count == 1 ? _candidates[0]
        : _choices is not null && _choices.GetSelectedItems() is { Length: > 0 } sel ? _candidates[sel[0]] : null;

    private void UpdateButtons()
    {
        var ok = Selected is not null;
        _once.Disabled = !ok;
        _always.Disabled = !ok;
    }

    private void Tick()
    {
        if (_done) return;
        if (_discRoot is not null && !DiscWatcher.IsReady(_discRoot))
        {
            Cancel(Loc.T("CONFIRM_DISC_REMOVED"));
            return;
        }
        _remaining--;
        UpdateCountdown();
        if (_remaining <= 0) Cancel(Loc.T("CONFIRM_TIMEOUT"));
    }

    private void UpdateCountdown()
    {
        _countdown.Value = Math.Max(0, _remaining);
        _countdownText.Text = Loc.T("CONFIRM_COUNTDOWN", Math.Max(0, _remaining));
    }

    private void Decide(bool always)
    {
        var target = Selected;
        if (_done || target is null) return;

        // Hat sich die Diskette inzwischen geaendert? Dann nichts starten.
        if (_discRoot is not null)
        {
            var again = DiscState.Read(_s);
            if (again.Plan is null || !again.Plan.Candidates.Contains(target, StringComparer.OrdinalIgnoreCase) ||
                !string.Equals(again.Plan.Arguments?.Trim(), _arguments, StringComparison.Ordinal))
            {
                ShowError(Loc.T("CONFIRM_DISC_CHANGED"));
                return;
            }
        }
        if (!File.Exists(target))
        {
            ShowError(Loc.T("CONFIRM_MISSING", target));
            return;
        }
        if (_s.ReadOnlyMode)
        {
            Finish(false);
            return;
        }

        try
        {
            if (always)
            {
                _s.Trust.Add(target, _arguments, _label, TrustStore.SourceConfirmed);
                _s.Log.Write(LogLevel.Ok, $"Freigegeben (immer) - starte Programm: {target} {_arguments}".TrimEnd());
            }
            else
            {
                _s.Log.Write(LogLevel.Ok, $"Bestaetigt (einmalig) - starte Programm: {target} {_arguments}".TrimEnd());
            }
            Starter.StartProgram(target, _arguments);
            Finish(true);
        }
        catch (Exception ex)
        {
            _s.Log.Write(LogLevel.Error, $"Start fehlgeschlagen: {ex.Message}");
            ShowError(Loc.T("CONFIRM_START_FAILED", ex.Message));
        }
    }

    public void Cancel(string why)
    {
        if (_done) return;
        _s.Log.Write(LogLevel.Info, $"Kein Programm gestartet ({why}).");
        Finish(false);
    }

    private void ShowError(string text)
    {
        _error.Text = text;
        _error.Visible = true;
    }

    private void Finish(bool started)
    {
        _done = true;
        _timer.Stop();
        Finished?.Invoke(started);
    }
}
