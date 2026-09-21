using System.Diagnostics;
using System.Threading;
using Floppy.Core.Updates;
using FloppyHub.App.Art;
using FloppyHub.App.Core;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>
/// Update-Ablauf: Hinweis -> "Jetzt aktualisieren" -> Herunterladen mit Fortschritt (Pruefsumme wird
/// verglichen) -> Setup unbeaufsichtigt starten -> App beendet sich, das Setup startet sie danach neu.
/// Automatisch geht das nur bei einer per Setup installierten App und dem eingebauten GitHub-Feed;
/// sonst (Entwicklung, eigener Feed, unvollstaendiges Release) bleibt es beim Link zur Release-Seite.
/// </summary>
public static class UpdateFlow
{
    public static bool CanInstallAutomatically(AppServices s, UpdateInfo info) =>
        info.InstallerUrl is not null && info.ChecksumUrl is not null &&
        string.IsNullOrWhiteSpace(s.Settings.UpdateFeedUrl) &&
        !s.ReadOnlyMode && !OS.HasFeature("editor") &&
        File.Exists(Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath()) ?? "", "unins000.exe"));   // vom Setup installiert

    /// <summary>Hinweis auf eine neuere Version. Bei schon offenem Update-Fenster passiert nichts.</summary>
    /// <param name="previewAuto">Nur Vorschau/Bildschirmfotos: so tun, als ginge das automatische Update.</param>
    public static void Offer(IAppHost host, UpdateInfo info, bool previewAuto = false)
    {
        foreach (var child in host.DialogLayer.GetChildren())
            if (child is RetroDialog && (child.Name == "Update" || child.Name == "UpdateProgress")) return;

        var s = host.Services;
        var auto = previewAuto || CanInstallAutomatically(s, info);
        var d = new RetroDialog(Loc.T("UPDATE_TITLE"), "cloud", 500) { Name = "Update" };
        var text = Ui.VBox(6, Ui.Label(Loc.T(auto ? "UPDATE_TEXT_AUTO" : "UPDATE_TEXT", info.Version), wrap: true));
        if (auto) text.AddChild(Ui.Dim(Loc.T("UPDATE_AUTO_NOTE"), wrap: true));
        d.Body.AddChild(Ui.HBox(12, Icons.Rect("cloud", 2f), text.Expand()));

        if (auto) d.AddButton(Loc.T("UPDATE_BTN_NOW"), () => Download(host, info), icon: "cloud");
        else d.AddButton(Loc.T("UPDATE_BTN_GET"), () => OS.ShellOpen(info.HtmlUrl), icon: "cloud");
        if (auto && info.HtmlUrl.Length > 0) d.AddButton(Loc.T("UPDATE_BTN_NOTES"), () => OS.ShellOpen(info.HtmlUrl), closes: false, icon: "file");
        d.AddButton(Loc.T("UPDATE_BTN_LATER"), () => s.Updates.Skip(info.Version));
        d.Open(host.DialogLayer);
    }

    /// <summary>Nur Vorschau/Bildschirmfotos: das Fortschrittsfenster bei 42 %, ohne etwas herunterzuladen.</summary>
    public static void PreviewProgress(IAppHost host, UpdateInfo info)
    {
        var d = BuildProgressDialog(host, info, out _, out var bar, out var size, out _, () => { });
        bar.Value = 0.42;
        size.Text = Loc.T("UPDATE_PROGRESS", Ui.Bytes(23_500_000), Ui.Bytes(56_000_000));
        d.Open(host.DialogLayer);
    }

    private static RetroDialog BuildProgressDialog(IAppHost host, UpdateInfo info, out Label status, out ProgressBar bar,
        out Label size, out Button cancel, Action onCancel)
    {
        status = Ui.Label(Loc.T("UPDATE_DOWNLOADING", info.Version), wrap: true);
        bar = new ProgressBar { ShowPercentage = false, MaxValue = 1, CustomMinimumSize = new Vector2(0, 16) };
        size = Ui.Dim(" ");
        var d = new RetroDialog(Loc.T("UPDATE_TITLE"), "cloud", 480) { Name = "UpdateProgress" };
        d.Body.AddChild(Ui.HBox(12, Icons.Rect("cloud", 2f), Ui.VBox(8, status, bar, size).Expand()));
        cancel = d.AddButton(Loc.T("BTN_CANCEL"), onCancel, closes: false);
        d.Cancelled += onCancel;   // Esc oder Schliessen-Knopf
        return d;
    }

    private static void Download(IAppHost host, UpdateInfo info)
    {
        var cts = new CancellationTokenSource();
        var state = new DownloadState();

        var d = BuildProgressDialog(host, info, out var status, out var bar, out var size, out var cancel, cts.Cancel);
        d.Open(host.DialogLayer);

        _ = Task.Run(async () =>
        {
            UpdateDownloadResult result;
            try
            {
                using var downloader = new UpdateDownloader();
                result = await downloader.DownloadAsync(info, UpdateInstaller.DownloadDirectory, state, cts.Token);
            }
            catch (Exception ex)
            {
                result = new UpdateDownloadResult(null, UpdateDownloadError.Network, ex.Message);
            }
            state.Result = result;
        });

        var timer = new Godot.Timer { WaitTime = 0.1, Autostart = true };
        d.AddChild(timer);
        var finished = false;
        timer.Timeout += () =>
        {
            if (finished || !GodotObject.IsInstanceValid(d)) return;
            var (done, total) = state.Progress;
            bar.Value = total > 0 ? (double)done / total : 0;
            size.Text = total > 0 ? Loc.T("UPDATE_PROGRESS", Ui.Bytes(done), Ui.Bytes(total)) : done > 0 ? Ui.Bytes(done) : " ";
            if (state.Result is not { } result) return;

            finished = true;
            timer.Stop();
            Finish(host, d, info, result, status, cancel);
        };
    }

    private static void Finish(IAppHost host, RetroDialog d, UpdateInfo info, UpdateDownloadResult result, Label status, Button cancel)
    {
        if (result.Error == UpdateDownloadError.Cancelled)
        {
            d.Close();
            return;
        }
        if (!result.Ok)
        {
            d.Close();
            Fail(host, info, result.Error switch
            {
                UpdateDownloadError.NotTrusted => "UPDATE_FAIL_NOTRUSTED",
                UpdateDownloadError.NoChecksum => "UPDATE_FAIL_NOCHECKSUM",
                UpdateDownloadError.ChecksumMismatch => "UPDATE_FAIL_CHECKSUM",
                UpdateDownloadError.TooLarge => "UPDATE_FAIL_TOOLARGE",
                _ => "UPDATE_FAIL_NETWORK",
            });
            return;
        }

        status.Text = Loc.T("UPDATE_INSTALLING");
        cancel.Disabled = true;
        try
        {
            // laeuft der Motor gerade? Dann soll ihn das Setup danach wieder starten
            var motorRunning = Process.GetProcessesByName("FloppyLauncher").Length > 0;
            UpdateInstaller.Start(result.Path!, motorRunning);
        }
        catch (Exception ex)
        {
            d.Close();
            host.Services.Log.Write(Floppy.Core.LogLevel.Warn, $"App: Update-Setup konnte nicht gestartet werden: {ex.Message}");
            RetroDialog.Ask(host.DialogLayer, Loc.T("UPDATE_FAILED_TITLE"), Loc.T("UPDATE_FAIL_START", ex.Message), Loc.T("UPDATE_BTN_PAGE"),
                () => OS.ShellOpen(info.HtmlUrl), "warn");
            return;
        }

        // Kurz warten (man soll den Hinweis lesen koennen), dann beenden: das Setup ersetzt die Dateien und startet die App neu.
        d.GetTree().CreateTimer(1.2).Timeout += host.QuitApp;
    }

    private static void Fail(IAppHost host, UpdateInfo info, string messageKey) =>
        RetroDialog.Ask(host.DialogLayer, Loc.T("UPDATE_FAILED_TITLE"), Loc.T(messageKey), Loc.T("UPDATE_BTN_PAGE"),
            () => OS.ShellOpen(info.HtmlUrl), "warn");

    /// <summary>Fortschritt aus dem Download-Thread, vom UI-Thread gelesen.</summary>
    private sealed class DownloadState : IProgress<UpdateProgress>
    {
        private long _done;
        private long _total;

        public volatile UpdateDownloadResult? Result;

        public (long Done, long Total) Progress => (Interlocked.Read(ref _done), Interlocked.Read(ref _total));

        public void Report(UpdateProgress value)
        {
            Interlocked.Exchange(ref _done, value.BytesDone);
            Interlocked.Exchange(ref _total, value.BytesTotal);
        }
    }
}
