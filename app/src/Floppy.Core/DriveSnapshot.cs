namespace Floppy.Core;

public enum MediaKind { Floppy, Usb, Optical, Fixed, Network, Other }

/// <summary>Momentaufnahme eines Laufwerks fuer Anzeige (Statusleiste, Laufwerke-Ansicht).</summary>
public sealed record DriveSnapshot(
    string Root,
    MediaKind Kind,
    bool Ready,
    string Label,
    string FileSystem,
    long TotalBytes,
    long FreeBytes)
{
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
    public double UsedRatio => TotalBytes > 0 ? (double)UsedBytes / TotalBytes : 0;

    /// <summary>
    /// Nur LESEN, nie ueberwachen: Der Motor schaut weiterhin ausschliesslich auf A:.
    /// Die App zeigt andere Wechseldatentraeger nur an, wenn man die Ansicht oeffnet.
    /// </summary>
    public static DriveSnapshot Read(string root)
    {
        var normalized = PathRules.DriveRoot(root);
        try
        {
            var info = new DriveInfo(normalized);
            if (!info.IsReady)
                return new(normalized, Classify(info.DriveType, normalized, 0), false, "", "", 0, 0);

            var total = info.TotalSize;
            return new(normalized, Classify(info.DriveType, normalized, total), true,
                SafeLabel(info), info.DriveFormat, total, info.AvailableFreeSpace);
        }
        catch
        {
            return new(normalized, MediaKind.Other, false, "", "", 0, 0);
        }
    }

    /// <summary>Alle Wechseldatentraeger (Disketten, USB, CD/DVD), A: immer zuerst.</summary>
    public static IReadOnlyList<DriveSnapshot> RemovableDrives()
    {
        var list = new List<DriveSnapshot>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType is DriveType.Removable or DriveType.CDRom)
                    list.Add(Read(d.Name));
            }
        }
        catch { }
        return list.OrderBy(d => d.Kind == MediaKind.Floppy ? 0 : 1)
                   .ThenBy(d => d.Root, StringComparer.OrdinalIgnoreCase)
                   .ToArray();
    }

    /// <summary>Disketten erkennt man an A:/B: oder an der typischen Groesse (bis 2,88 MB).</summary>
    public static MediaKind Classify(DriveType type, string root, long totalBytes) => type switch
    {
        DriveType.Removable when IsFloppyLetter(root) || (totalBytes > 0 && totalBytes <= 2_880L * 1024) => MediaKind.Floppy,
        DriveType.Removable => MediaKind.Usb,
        DriveType.CDRom => MediaKind.Optical,
        DriveType.Fixed => MediaKind.Fixed,
        DriveType.Network => MediaKind.Network,
        _ => MediaKind.Other,
    };

    private static bool IsFloppyLetter(string root) =>
        root.Length >= 2 && root[1] == ':' && char.ToUpperInvariant(root[0]) is 'A' or 'B';

    private static string SafeLabel(DriveInfo info)
    {
        try { return info.VolumeLabel; }
        catch { return ""; }
    }
}
