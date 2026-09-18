namespace Floppy.Core;

public enum MediaKind
{
    Floppy,
    /// <summary>USB-Stick (Wechseldatentraeger).</summary>
    Usb,
    /// <summary>Speicherkarte (SD, microSD, MMC ...).</summary>
    Sd,
    /// <summary>Externe Festplatte/SSD - Windows meldet sie als "fest", der Anschluss ist aber USB.</summary>
    ExternalDisk,
    /// <summary>CD, DVD, Blu-ray.</summary>
    Optical,
    Fixed,
    Network,
    Other,
}

/// <summary>Anschluss laut Windows (STORAGE_BUS_TYPE).</summary>
public enum BusKind { Unknown, Usb, Sd, Atapi, Ata, Sata, Nvme, Scsi, FireWire, Raid, Virtual, Other }

/// <summary>Genauere Art des eingelegten Mediums.</summary>
public enum MediaFormat
{
    Unknown,
    Floppy35HD,
    Floppy35DD,
    Floppy35ED,
    Floppy525HD,
    Floppy525DD,
    Cd,
    AudioCd,
    Dvd,
    BluRay,
}

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
    public BusKind Bus { get; init; }
    public string Vendor { get; init; } = "";
    public string Model { get; init; } = "";
    public string Revision { get; init; } = "";
    public uint? VolumeSerial { get; init; }
    public long ClusterBytes { get; init; }
    public MediaFormat Format { get; init; }

    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);
    public double UsedRatio => TotalBytes > 0 ? (double)UsedBytes / TotalBytes : 0;

    /// <summary>"Hersteller Modell", ohne doppelte Leerzeichen.</summary>
    public string DeviceName => string.Join(' ', new[] { Vendor, Model }.Where(s => s.Length > 0));

    /// <summary>Volume-Seriennummer im Windows-Format "1A2B-3C4D".</summary>
    public string? SerialText => VolumeSerial is { } s ? $"{s >> 16:X4}-{s & 0xFFFF:X4}" : null;

    /// <summary>Nur eine Momentaufnahme zum Anzeigen - loest selbst kein Ueberwachen aus.</summary>
    public static DriveSnapshot Read(string root)
    {
        var normalized = PathRules.DriveRoot(root);
        try
        {
            var info = new DriveInfo(normalized);
            var device = DriveProbe.QueryDevice(normalized);
            var bus = device?.Bus ?? BusKind.Unknown;
            var model = device?.Product ?? "";

            if (!info.IsReady)
            {
                return new(normalized, Classify(info.DriveType, normalized, 0, bus, model), false, "", "", 0, 0)
                {
                    Bus = bus, Vendor = device?.Vendor ?? "", Model = model, Revision = device?.Revision ?? "",
                };
            }

            var total = info.TotalSize;
            var kind = Classify(info.DriveType, normalized, total, bus, model);
            var fs = info.DriveFormat;
            return new(normalized, kind, true, SafeLabel(info), fs, total, info.AvailableFreeSpace)
            {
                Bus = bus,
                Vendor = device?.Vendor ?? "",
                Model = model,
                Revision = device?.Revision ?? "",
                VolumeSerial = DriveProbe.VolumeSerial(normalized),
                ClusterBytes = DriveProbe.ClusterBytes(normalized),
                Format = FormatOf(kind, total, kind == MediaKind.Optical && HasAudioTracks(normalized)),
            };
        }
        catch
        {
            return new(normalized, MediaKind.Other, false, "", "", 0, 0);
        }
    }

    /// <summary>Alle Wechseldatentraeger: Disketten, USB-Sticks, Speicherkarten, CD/DVD/Blu-ray, externe Festplatten.</summary>
    public static IReadOnlyList<DriveSnapshot> RemovableDrives() => ListDrives(includeInternal: false);

    /// <param name="includeInternal">auch interne Festplatten und Netzlaufwerke.</param>
    public static IReadOnlyList<DriveSnapshot> ListDrives(bool includeInternal)
    {
        var list = new List<DriveSnapshot>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (d.DriveType is DriveType.NoRootDirectory) continue;
                var snapshot = Read(d.Name);
                if (includeInternal || IsRemovableKind(snapshot.Kind)) list.Add(snapshot);
            }
        }
        catch { }
        return list.OrderBy(d => d.Kind).ThenBy(d => d.Root, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static bool IsRemovableKind(MediaKind kind) =>
        kind is MediaKind.Floppy or MediaKind.Usb or MediaKind.Sd or MediaKind.ExternalDisk or MediaKind.Optical;

    /// <summary>
    /// Disketten erkennt man an A:/B: oder an der typischen Groesse (bis 2,88 MB).
    /// Externe USB-Festplatten meldet Windows als "fest" - der Anschluss verraet sie.
    /// </summary>
    public static MediaKind Classify(DriveType type, string root, long totalBytes, BusKind bus = BusKind.Unknown, string model = "") => type switch
    {
        DriveType.Removable when IsFloppyLetter(root) || (totalBytes > 0 && totalBytes <= 2_880L * 1024) => MediaKind.Floppy,
        DriveType.Removable when bus == BusKind.Sd || LooksLikeCardReader(model) => MediaKind.Sd,
        DriveType.Removable => MediaKind.Usb,
        DriveType.CDRom => MediaKind.Optical,
        DriveType.Fixed when bus == BusKind.Sd => MediaKind.Sd,
        DriveType.Fixed when bus is BusKind.Usb or BusKind.FireWire => MediaKind.ExternalDisk,
        DriveType.Fixed => MediaKind.Fixed,
        DriveType.Network => MediaKind.Network,
        _ => MediaKind.Other,
    };

    /// <summary>Diskettenformat aus der Groesse, CD/DVD/Blu-ray aus der Kapazitaet.</summary>
    public static MediaFormat FormatOf(MediaKind kind, long totalBytes, bool hasAudioTracks = false)
    {
        if (totalBytes <= 0) return MediaFormat.Unknown;
        switch (kind)
        {
            case MediaKind.Floppy:
                (long Bytes, MediaFormat Format)[] floppies =
                [
                    (1_457_664, MediaFormat.Floppy35HD), (730_112, MediaFormat.Floppy35DD), (2_915_328, MediaFormat.Floppy35ED),
                    (1_213_952, MediaFormat.Floppy525HD), (362_496, MediaFormat.Floppy525DD),
                ];
                var best = floppies.MinBy(f => Math.Abs(f.Bytes - totalBytes));
                return Math.Abs(best.Bytes - totalBytes) <= best.Bytes * 0.08 ? best.Format : MediaFormat.Unknown;

            case MediaKind.Optical:
                if (hasAudioTracks) return MediaFormat.AudioCd;
                if (totalBytes <= 900L * 1024 * 1024) return MediaFormat.Cd;
                return totalBytes <= 9_500L * 1024 * 1024 ? MediaFormat.Dvd : MediaFormat.BluRay;

            default:
                return MediaFormat.Unknown;
        }
    }

    private static bool LooksLikeCardReader(string model) =>
        model.Contains("SD", StringComparison.Ordinal) ||
        model.Contains("Card", StringComparison.OrdinalIgnoreCase) ||
        model.Contains("MMC", StringComparison.OrdinalIgnoreCase) ||
        model.Contains("Reader", StringComparison.OrdinalIgnoreCase);

    private static bool IsFloppyLetter(string root) =>
        root.Length >= 2 && root[1] == ':' && char.ToUpperInvariant(root[0]) is 'A' or 'B';

    private static bool HasAudioTracks(string root)
    {
        try { return Directory.EnumerateFiles(root, "*.cda").Any(); }
        catch { return false; }
    }

    private static string SafeLabel(DriveInfo info)
    {
        try { return info.VolumeLabel; }
        catch { return ""; }
    }
}
