using Floppy.Core;
using FloppyHub.App.Core;

namespace FloppyHub.App.Services;

/// <summary>Alles ueber die eingelegte Diskette auf einen Blick - fuer Ansicht und Rueckfrage.</summary>
public sealed record DiscState(
    string Root,
    bool Present,
    DriveSnapshot? Drive,
    PlanResult? Result,
    GateDecision? Decision,
    IReadOnlyList<FileSystemInfo> Entries,
    string? Title,
    string Signature)
{
    public LaunchPlan? Plan => Result?.Plan;

    public static DiscState Read(AppServices s)
    {
        var root = s.DriveRoot;
        if (!DiscWatcher.IsReady(root))
            return new DiscState(root, false, IsDriveRoot(root) ? DriveSnapshot.Read(root) : null, null, null, [], null, DiskSignature.Empty);

        var result = s.Planner.Plan(root);
        var decision = LaunchGate.Decide(result.Plan, s.Trust.Check);

        IReadOnlyList<FileSystemInfo> entries;
        try
        {
            entries = new DirectoryInfo(root).EnumerateFileSystemInfos()
                .Where(e => !e.Attributes.HasFlag(FileAttributes.System))
                .OrderBy(e => e is FileInfo)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .Take(200)
                .ToArray();
        }
        catch
        {
            entries = [];
        }

        var drive = IsDriveRoot(root)
            ? DriveSnapshot.Read(root)
            : new DriveSnapshot(root, MediaKind.Floppy, true, "TEST", Loc.T("DISC_TESTFOLDER"), 1_457_664,
                Math.Max(0, 1_457_664 - entries.OfType<FileInfo>().Sum(f => f.Length)));

        return new DiscState(root, true, drive, result, decision, entries, ReadTitle(result.Plan?.Source, s),
            DiskSignature.Compute(root));
    }

    /// <summary>Anzeigename: Titel-Kommentar aus game.txt, Bibliothek oder bekannte Steam-Namen.</summary>
    private static string? ReadTitle(string? referenceFile, AppServices s)
    {
        if (referenceFile is not null && File.Exists(referenceFile))
        {
            try
            {
                foreach (var line in File.ReadLines(referenceFile).Take(5))
                {
                    var t = line.Trim();
                    if (!t.StartsWith('#')) continue;
                    t = t.TrimStart('#').Trim();
                    if (t.Length > 0 && !t.StartsWith("geschrieben von", StringComparison.OrdinalIgnoreCase)) return t;
                }
            }
            catch { }
        }
        return null;
    }

    public string DisplayName(IReadOnlyList<LibraryEntry> library)
    {
        if (Title is not null) return Title;
        var plan = Plan;
        if (plan is null) return Loc.T("DISC_NOTHING");
        if (plan.Kind == LaunchKind.Steam && plan.SteamId is { } id)
        {
            var fromLibrary = library.FirstOrDefault(e => e.Kind.Equals("steam", StringComparison.OrdinalIgnoreCase) && e.Value == id);
            return fromLibrary?.Label ?? SteamApps.TryGetKnownName(id) ?? Loc.T("KIND_STEAM_ID", id);
        }
        if (plan.Kind == LaunchKind.Hub) return Loc.T("KIND_HUB");
        if (plan.Kind == LaunchKind.Game)
        {
            var title = Floppy.Core.Minigame.LevelPack.Load(plan.Candidates[0]).Title;
            return Loc.T("KIND_GAME_TITLE", title);
        }
        if (plan.Candidates.Count == 1)
        {
            var target = plan.Candidates[0];
            var fromLibrary = library.FirstOrDefault(e => string.Equals(PathRules.StripQuotes(e.Value), target, StringComparison.OrdinalIgnoreCase));
            return fromLibrary?.Label ?? System.IO.Path.GetFileNameWithoutExtension(target);
        }
        return Loc.T("DISC_MULTIPLE", plan.Candidates.Count);
    }

    private static bool IsDriveRoot(string root) => root.Length <= 3 && root.Length >= 2 && root[1] == ':';
}
