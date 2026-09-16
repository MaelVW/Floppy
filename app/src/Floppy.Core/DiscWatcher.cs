namespace Floppy.Core;

/// <summary>
/// Merkt sich den zuletzt behandelten Zustand EINES Laufwerks - wie die
/// V1-Hauptschleife: Diskette raus = vergessen, neue Diskette oder geaenderter
/// Inhalt = genau einmal behandeln.
/// </summary>
public sealed class DiscWatcher
{
    private readonly Func<string, bool> _isReady;
    private readonly Func<string, string> _signature;

    public DiscWatcher(string root, Func<string, bool>? isReady = null, Func<string, string>? signature = null)
    {
        Root = root;
        _isReady = isReady ?? IsReady;
        _signature = signature ?? DiskSignature.Compute;
    }

    public string Root { get; }
    public string? LastSignature { get; private set; }
    public bool DiscPresent { get; private set; }

    /// <summary>Einmal nachsehen. true = neuer Zustand, der jetzt behandelt werden soll.</summary>
    public bool Poll()
    {
        bool ready;
        try { ready = _isReady(Root); }
        catch { ready = false; }

        DiscPresent = ready;
        if (!ready)
        {
            LastSignature = null;   // keine Diskette -> naechste wird neu behandelt
            return false;
        }

        var signature = _signature(Root);
        if (signature == LastSignature) return false;
        LastSignature = signature;
        return true;
    }

    /// <summary>
    /// Aktuellen Inhalt als "schon behandelt" merken, z. B. nachdem die App selbst
    /// auf die Diskette geschrieben hat.
    /// </summary>
    /// <summary>Aktuellen Zustand vergessen: der naechste <see cref="Poll"/> behandelt ihn erneut.</summary>
    public void Forget() => LastSignature = null;

    public void MarkHandled()
    {
        try { LastSignature = _isReady(Root) ? _signature(Root) : null; }
        catch { LastSignature = null; }
    }

    /// <summary>
    /// Laufwerkswurzel ("A:\"): DriveInfo.IsReady - ohne Windows-Fehlerdialog, wenn
    /// der Aufrufer SetErrorMode gesetzt hat. Sonst (Testordner): existiert der Ordner?
    /// </summary>
    public static bool IsReady(string root)
    {
        if (root.Length <= 3 && root.Length >= 2 && root[1] == ':')
        {
            try { return new DriveInfo(root).IsReady; }
            catch { return false; }
        }
        return Directory.Exists(root);
    }
}
