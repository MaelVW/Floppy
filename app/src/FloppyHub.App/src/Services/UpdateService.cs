using System.Threading;
using Floppy.Core.Updates;
using FloppyHub.App.Core;

namespace FloppyHub.App.Services;

/// <summary>
/// Prueft einmal beim Start im Hintergrund, ob es eine neuere Version gibt. Laedt nichts herunter
/// und aendert nichts von selbst - meldet nur (per Callback, im Godot-Hauptthread aufzurufen).
/// </summary>
public sealed class UpdateService
{
    private readonly AppServices _s;
    private readonly IUpdateFeed _feed;
    private int _ticket;

    public UpdateService(AppServices services, IUpdateFeed? feed = null)
    {
        _s = services;
        _feed = feed ?? new GitHubReleaseFeed(NullIfBlank(services.Settings.UpdateFeedUrl));
    }

    /// <summary>
    /// <paramref name="onAvailable"/> wird auf einem Threadpool-Thread aufgerufen - der Aufrufer muss
    /// selbst auf den Godot-Hauptthread wechseln (z. B. <c>Callable.From(...).CallDeferred()</c>).
    /// </summary>
    public void CheckInBackground(string currentVersion, Action<UpdateInfo> onAvailable)
    {
        if (_s.ReadOnlyMode || !AppVersion.TryParse(currentVersion, out var current)) return;
        var ticket = ++_ticket;

        Task.Run(async () =>
        {
            UpdateInfo? found;
            try { found = await _feed.GetLatestAsync(CancellationToken.None); }
            catch { found = null; }   // kein Internet, Dienst nicht erreichbar o.ae. - einfach still bleiben

            if (found is null || ticket != _ticket) return;
            if (found.Version == _s.Settings.SkippedUpdateVersion) return;
            if (!AppVersion.TryParse(found.Version, out var latest) || !latest.IsNewerThan(current)) return;

            onAvailable(found);
        });
    }

    /// <summary>
    /// Auf Knopfdruck (Optionen): ruft <paramref name="onResult"/> immer auf, auch wenn nichts Neues da ist
    /// (dann null) - anders als <see cref="CheckInBackground"/> ignoriert das eine zuvor uebersprungene Version.
    /// </summary>
    public void CheckManually(string currentVersion, Action<UpdateInfo?> onResult)
    {
        if (!AppVersion.TryParse(currentVersion, out var current))
        {
            onResult(null);
            return;
        }
        Task.Run(async () =>
        {
            UpdateInfo? found;
            try { found = await _feed.GetLatestAsync(CancellationToken.None); }
            catch { found = null; }
            var isNewer = found is not null && AppVersion.TryParse(found.Version, out var latest) && latest.IsNewerThan(current);
            onResult(isNewer ? found : null);
        });
    }

    /// <summary>"Spaeter": diese Version nicht mehr vorschlagen.</summary>
    public void Skip(string version)
    {
        _s.Settings.SkippedUpdateVersion = version;
        _s.SaveSettings();
    }

    private static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
