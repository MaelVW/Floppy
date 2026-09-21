using System.Globalization;
using Floppy.Core;
using FloppyHub.App.Art;
using FloppyHub.App.Core;
using FloppyHub.App.Skin;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>
/// "Steam durchsuchen": geht durch die Dateien von Steam und zeigt alle installierten Spiele. Was in der Bibliothek
/// noch fehlt, ist vorausgewaehlt - ein Klick auf "Hinzufuegen" fuellt die Bibliothek, ohne dass man jedes
/// Spiel einzeln eintragen muss. Gelesen werden nur Steams eigene Dateien (kein Konto, kein Internet); der Scan
/// veraendert nichts an Steam und startet nichts.
/// </summary>
public static class SteamScanDialog
{
    /// <param name="changed">Wird aufgerufen, nachdem Eintraege hinzugekommen sind (die Liste der Bibliothek neu zeichnen).</param>
    /// <param name="findRoot">Nur fuer die Vorschau: ersetzt die Suche nach dem Steam-Ordner.</param>
    public static void Open(IAppHost host, Action changed, Func<string?>? findRoot = null) =>
        new Flow(host, changed, findRoot ?? (() => SteamScanner.FindSteamRoot(host.Services.Settings.SteamPath))).Start();

    private sealed class Flow
    {
        private readonly IAppHost _host;
        private readonly Action _changed;
        private readonly Func<string?> _findRoot;
        private readonly RetroDialog _dialog;
        private readonly VBoxContainer _content = Ui.VBox(8);
        private readonly Button _add;
        private readonly List<TreeItem> _choices = [];   // Zeilen mit Haken (die schon vorhandenen haben keinen)
        private readonly Dictionary<TreeItem, SteamGame> _games = [];
        private Label _selection = null!;
        private FileDialog? _picker;

        public Flow(IAppHost host, Action changed, Func<string?> findRoot)
        {
            _host = host;
            _changed = changed;
            _findRoot = findRoot;

            _dialog = new RetroDialog(Loc.T("STEAM_TITLE"), "kind_steam", 660);
            _dialog.Body.AddChild(_content);
            _add = _dialog.AddButton(Loc.T("STEAM_ADD", 0), Add, closes: false, icon: "ok");
            _add.Disabled = true;
            _dialog.AddButton(Loc.T("BTN_CANCEL"), () => { });
        }

        public void Start()
        {
            _dialog.Open(_host.DialogLayer);
            Scan();
        }

        // ------------------------------------------------------------------
        // Suchen
        // ------------------------------------------------------------------

        private async void Scan()
        {
            _add.Visible = true;
            _add.Disabled = true;
            _add.Text = Loc.T("STEAM_ADD", 0);
            ShowMessage("info", Loc.T("STEAM_SEARCHING"));

            SteamScan scan;
            try
            {
                var findRoot = _findRoot;
                scan = await Task.Run(() => SteamScanner.Scan(findRoot()));   // Festplatten koennen langsam sein: nicht im Oberflaechen-Thread
            }
            catch (Exception ex)
            {
                if (!GodotObject.IsInstanceValid(_dialog)) return;
                Log(LogLevel.Error, $"Bibliothek: Steam-Suche fehlgeschlagen: {ex.Message}");
                ShowMessage("error", Loc.T("STEAM_SCAN_FAILED", ex.Message));
                _add.Visible = false;
                return;
            }
            if (!GodotObject.IsInstanceValid(_dialog)) return;   // in der Zwischenzeit geschlossen

            Log(LogLevel.Info, scan.SteamFound
                ? $"Bibliothek: Steam durchsucht - {scan.Games.Count} Spiele in {scan.Libraries.Count} Ordner(n)."
                : "Bibliothek: Steam nicht gefunden.");

            if (!scan.SteamFound) ShowNothing(Loc.T("STEAM_NOT_FOUND"), scan);
            else if (scan.Games.Count == 0) ShowNothing(Loc.T("STEAM_NO_GAMES"), scan);
            else ShowGames(scan);
        }

        private void ShowMessage(string icon, string text)
        {
            Reset();
            _content.AddChild(Ui.HBox(12, Icons.Rect(icon, 2f), Ui.Label(text, wrap: true).Expand()));
        }

        private void ShowNothing(string text, SteamScan scan)
        {
            _add.Visible = false;   // es gibt nichts hinzuzufuegen - nur "Abbrechen" und die Ordnerwahl
            ShowMessage("warn", text);
            if (scan.SteamFound) _content.AddChild(Ui.Dim(Footer(scan, []), wrap: true));
            _content.AddChild(Ui.HBox(6, Ui.Button(Loc.T("BTN_STEAM_FOLDER"), "folder", ChooseFolder)));
        }

        private void Reset()
        {
            _content.ClearChildren();
            _choices.Clear();
            _games.Clear();
        }

        // ------------------------------------------------------------------
        // Ergebnis
        // ------------------------------------------------------------------

        private void ShowGames(SteamScan scan)
        {
            Reset();
            var p = Palette.Current;
            var known = SteamScanner.KnownAppIds(_host.Library);
            var fresh = scan.Games.Where(g => !known.Contains(g.AppId)).ToList();
            var have = scan.Games.Where(g => known.Contains(g.AppId)).ToList();

            var tree = new Tree
            {
                Columns = 3,
                HideRoot = true,
                ColumnTitlesVisible = true,
                SelectMode = Tree.SelectModeEnum.Row,
                CustomMinimumSize = new Vector2(0, 280),
                AutoTranslateMode = Node.AutoTranslateModeEnum.Disabled,
            }.Expand(vertical: true);
            tree.SetColumnTitle(0, Loc.T("STEAM_COL_GAME"));
            tree.SetColumnTitle(1, Loc.T("STEAM_COL_SIZE"));
            tree.SetColumnTitle(2, Loc.T("STEAM_COL_STATE"));
            tree.SetColumnExpand(0, true);
            tree.SetColumnExpand(1, false);
            tree.SetColumnCustomMinimumWidth(1, 84);
            tree.SetColumnExpand(2, false);
            tree.SetColumnCustomMinimumWidth(2, 150);
            tree.CreateItem();   // unsichtbare Wurzel

            foreach (var game in fresh.Concat(have))
            {
                var isNew = !known.Contains(game.AppId);
                var item = tree.CreateItem(tree.GetRoot());
                // Erst die Art der Zelle setzen - das raeumt den Text der Zelle wieder leer.
                if (isNew)
                {
                    item.SetCellMode(0, TreeItem.TreeCellMode.Check);
                    item.SetEditable(0, true);
                    item.SetChecked(0, game.Complete);
                }
                item.SetText(0, game.Name);
                item.SetText(1, game.SizeOnDisk > 0 ? Ui.Bytes(game.SizeOnDisk) : "–");
                item.SetTextAlignment(1, HorizontalAlignment.Right);
                item.SetText(2, Loc.T(!isNew ? "STEAM_STATE_KNOWN" : game.Complete ? "STEAM_STATE_NEW" : "STEAM_STATE_LOADING"));
                item.SetTooltipText(0, Loc.T("STEAM_TIP", game.AppId, game.InstallPath.Length > 0 ? game.InstallPath : game.LibraryPath));
                if (!isNew)
                    for (var column = 0; column < 3; column++) item.SetCustomColor(column, p.TextDim);
                else
                    _choices.Add(item);
                _games[item] = game;
            }

            tree.ItemEdited += UpdateSelection;
            tree.ItemActivated += () =>   // Doppelklick / Enter: Haken umschalten
            {
                if (tree.GetSelected() is { } item && _choices.Contains(item))
                {
                    item.SetChecked(0, !item.IsChecked(0));
                    UpdateSelection();
                }
            };

            _selection = Ui.Dim("");
            var all = Ui.Button(Loc.T("BTN_ALL"), null, () => SetAll(true));
            var none = Ui.Button(Loc.T("BTN_NONE"), null, () => SetAll(false));
            all.Disabled = none.Disabled = _choices.Count == 0;

            _content.AddChild(Ui.Dim(Loc.T("STEAM_INTRO"), wrap: true));
            _content.AddChild(Ui.Label(Loc.T("STEAM_SUMMARY", scan.Games.Count, fresh.Count, have.Count), "BoldLabel", wrap: true));
            _content.AddChild(tree);
            _content.AddChild(Ui.HBox(6, all, none, Ui.Spacer(), _selection));
            _content.AddChild(Ui.Dim(Footer(scan, fresh), wrap: true));

            UpdateSelection();
            if (_choices.Count == 0) _content.AddChild(Ui.Label(Loc.T("STEAM_ALL_KNOWN"), wrap: true));
        }

        private static string Footer(SteamScan scan, IReadOnlyList<SteamGame> fresh)
        {
            var lines = new List<string>();
            if (scan.Libraries.Count > 0) lines.Add(Loc.T("STEAM_FOOTER_SEARCHED", string.Join(", ", scan.Libraries)));
            if (scan.Unreachable.Count > 0) lines.Add(Loc.T("STEAM_FOOTER_UNREACHABLE", string.Join(", ", scan.Unreachable)));
            if (scan.Tools > 0) lines.Add(Loc.T("STEAM_FOOTER_TOOLS", scan.Tools));
            if (scan.Damaged > 0) lines.Add(Loc.T("STEAM_FOOTER_DAMAGED", scan.Damaged));
            if (fresh.Any(g => !g.Complete)) lines.Add(Loc.T("STEAM_FOOTER_LOADING"));
            return string.Join("\n", lines);
        }

        private void SetAll(bool on)
        {
            foreach (var item in _choices) item.SetChecked(0, on);
            UpdateSelection();
        }

        private List<SteamGame> Chosen() => _choices.Where(i => i.IsChecked(0)).Select(i => _games[i]).ToList();

        private void UpdateSelection()
        {
            var n = _choices.Count(i => i.IsChecked(0));
            _selection.Text = Loc.T("STEAM_SELECTED", n, _choices.Count);
            _add.Text = Loc.T("STEAM_ADD", n);
            _add.Disabled = n == 0;
        }

        // ------------------------------------------------------------------
        // Hinzufuegen
        // ------------------------------------------------------------------

        private void Add()
        {
            var chosen = Chosen();
            if (chosen.Count == 0) return;

            var s = _host.Services;
            if (s.ReadOnlyMode)   // Vorschau (Bildschirmfoto): nichts dauerhaft veraendern
            {
                _dialog.Close();
                return;
            }

            try
            {
                var count = SteamScanner.AddToLibraryFile(s.Paths.LibraryFile, chosen, DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                _host.ReloadLibrary();
                _changed();
                Log(LogLevel.Ok, $"Bibliothek: {count} Steam-Spiele per Steam-Scan hinzugefuegt.");
                _host.SetStatusMessage(Loc.T("STEAM_ADDED", count), "ok");
                _dialog.Close();
            }
            catch (Exception ex)
            {
                Log(LogLevel.Error, $"Bibliothek: Steam-Spiele nicht eingetragen: {ex.Message}");
                RetroDialog.Message(_host.DialogLayer, Loc.T("STEAM_TITLE"), Loc.T("STEAM_ADD_FAILED", ex.Message), "error");
            }
        }

        // ------------------------------------------------------------------
        // Steam-Ordner selbst waehlen (wenn Steam nicht automatisch gefunden wird)
        // ------------------------------------------------------------------

        private void ChooseFolder()
        {
            _picker?.QueueFree();
            _picker = new FileDialog
            {
                FileMode = FileDialog.FileModeEnum.OpenDir,
                Access = FileDialog.AccessEnum.Filesystem,
                UseNativeDialog = true,
                Title = Loc.T("STEAM_FOLDER_TITLE"),
            };
            _picker.DirSelected += dir =>
            {
                dir = dir.Replace('/', '\\');
                if (!SteamScanner.IsSteamRoot(dir))
                {
                    RetroDialog.Message(_host.DialogLayer, Loc.T("STEAM_FOLDER_TITLE"), Loc.T("STEAM_FOLDER_INVALID", dir), "warn");
                    return;
                }
                _host.Services.Settings.SteamPath = dir;
                _host.Services.SaveSettings();
                Scan();
            };
            _dialog.AddChild(_picker);
            _picker.PopupCentered();
        }

        private void Log(LogLevel level, string message)
        {
            if (!_host.Services.ReadOnlyMode) _host.Services.Log.Write(level, message);
        }
    }
}
