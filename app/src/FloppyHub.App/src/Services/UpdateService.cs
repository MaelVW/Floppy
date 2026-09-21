using System.Threading;
using Floppy.Core.Updates;
using FloppyHub.App.Core;

namespace FloppyHub.App.Services;

/// <summary>
/// Prueft im Hintergrund, ob es eine neuere Version gibt (beim Start und danach regelmaessig, weil die App
/// oft tagelang laeuft). Laedt nichts herunter und aendert nichts von selbst - meldet nur (per Callback);
/// Herunterladen und Installieren erst nach dem OK des Benutzers, siehe UpdateFlow.
/// </summary>
public sealed class UpdateService
{
    /// <summary>Wie oft hoechstens nachgefragt wird (GitHub erlaubt ohne Anmeldung 60 Anfragen pro Stunde).</summary>
    private static readonly TimeSpan RecheckAfter = TimeSpan.FromMinutes(20);

    /// <summary>Nach einem Fehlschlag (z. B. WLAN beim Autostart noch nicht da) frueher nochmal versuchen.</summary>
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(5);

    private readonly AppServices _s;
    private readonly IUpdateFeed _feed;
    private int _ticket;
    private long _nextCheckTicks;

    public UpdateService(AppServices services, IUpdateFeed? feed = null)
    {
        _s = services;
        _feed = feed ?? new GitHubReleaseFeed(NullIfBlank(services.Settings.UpdateFeedUrl));
    }

    /// <summary>
    /// <paramref name="onAvailable"/> wird auf einem Threadpool-Thread aufgerufen - der Aufrufer muss
    /// selbst auf den Godot-Hauptthread wechseln (z. B. <c>Callable.From(...).CallDeferred()</c>).
    /// </summary>
    /// <param name="force">Auch wenn gerade erst gefragt wurde (beim Programmstart).</param>
    public void CheckInBackground(string currentVersion, Action<UpdateInfo> onAvailable, bool force = false)
    {
        if (_s.ReadOnlyMode || !AppVersion.TryParse(currentVersion, out var current)) return;
        if (!force && DateTime.UtcNow.Ticks < Interlocked.Read(ref _nextCheckTicks)) return;
        Interlocked.Exchange(ref _nextCheckTicks, DateTime.UtcNow.Add(RecheckAfter).Ticks);
        var ticket = ++_ticket;

        Task.Run(async () =>
        {
            UpdateInfo? found;
            try { found = await _feed.GetLatestAsync(CancellationToken.None); }
            catch { found = null; }   // kein Internet, Dienst nicht erreichbar o.ae. - einfach still bleiben

            if (found is null) Interlocked.Exchange(ref _nextCheckTicks, DateTime.UtcNow.Add(RetryAfterFailure).Ticks);
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
