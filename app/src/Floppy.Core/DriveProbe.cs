using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Floppy.Core;

/// <summary>Was Windows ueber das Geraet hinter einem Laufwerksbuchstaben weiss.</summary>
public sealed record DeviceInfo(BusKind Bus, string Vendor, string Product, string Revision, bool RemovableMedia);

/// <summary>
/// Liest Geraetedaten (Anschluss, Hersteller, Modell) und Volume-Daten per Windows-API.
/// Das Geraet wird OHNE Lese-/Schreibrechte geoeffnet - kein Zugriff auf den Datentraeger,
/// ein leeres Diskettenlaufwerk wird dabei nicht angesprochen.
/// </summary>
public static unsafe partial class DriveProbe
{
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    private const uint FILE_SHARE_READ_WRITE = 0x3;
    private const uint OPEN_EXISTING = 3;

    public static DeviceInfo? QueryDevice(string root)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var letter = PathRules.DriveRoot(root)[0];
        try
        {
            using var handle = CreateFile($@"\\.\{letter}:", 0, FILE_SHARE_READ_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle.IsInvalid) return null;

            // STORAGE_PROPERTY_QUERY { PropertyId = StorageDeviceProperty (0), QueryType = PropertyStandardQuery (0) }
            var query = stackalloc byte[12];
            new Span<byte>(query, 12).Clear();
            var buffer = new byte[1024];
            fixed (byte* output = buffer)
            {
                if (!DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, query, 12, output, buffer.Length, out var returned, IntPtr.Zero)
                    || returned < 36)
                    return null;
            }
            return ParseDescriptor(buffer);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>STORAGE_DEVICE_DESCRIPTOR auswerten (oeffentlich fuer Tests).</summary>
    public static DeviceInfo? ParseDescriptor(ReadOnlySpan<byte> d)
    {
        if (d.Length < 36) return null;
        var removable = d[10] != 0;
        var vendor = ReadString(d, BitConverter.ToInt32(d[12..16]));
        var product = ReadString(d, BitConverter.ToInt32(d[16..20]));
        var revision = ReadString(d, BitConverter.ToInt32(d[20..24]));
        var bus = MapBus(BitConverter.ToInt32(d[28..32]));
        return new DeviceInfo(bus, vendor, product, revision, removable);
    }

    public static BusKind MapBus(int storageBusType) => storageBusType switch
    {
        0x00 => BusKind.Unknown,
        0x01 or 0x0A => BusKind.Scsi,       // SCSI, SAS
        0x02 => BusKind.Atapi,
        0x03 => BusKind.Ata,
        0x04 => BusKind.FireWire,
        0x07 => BusKind.Usb,
        0x08 or 0x10 => BusKind.Raid,       // RAID, Storage Spaces
        0x0B => BusKind.Sata,
        0x0C or 0x0D => BusKind.Sd,         // SD, MMC
        0x0E or 0x0F => BusKind.Virtual,    // VHD, ISO-Datei
        0x11 => BusKind.Nvme,
        _ => BusKind.Other,
    };

    public static uint? VolumeSerial(string root)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            return GetVolumeInformation(PathRules.DriveRoot(root), null, 0, out var serial, out _, out _, null, 0) ? serial : null;
        }
        catch
        {
            return null;
        }
    }

    public static long ClusterBytes(string root)
    {
        if (!OperatingSystem.IsWindows()) return 0;
        try
        {
            return GetDiskFreeSpace(PathRules.DriveRoot(root), out var sectors, out var bytesPerSector, out _, out _)
                ? (long)sectors * bytesPerSector
                : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static string ReadString(ReadOnlySpan<byte> buffer, int offset)
    {
        if (offset <= 0 || offset >= buffer.Length) return "";
        var slice = buffer[offset..];
        var end = slice.IndexOf((byte)0);
        if (end < 0) end = slice.Length;
        var text = Encoding.ASCII.GetString(slice[..end]).Trim();
        // manche Geraete liefern Unsinn: nur druckbare Zeichen behalten
        return new string(text.Where(c => c >= 0x20 && c < 0x7F).ToArray()).Trim();
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string fileName, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, byte* inBuffer, int inSize, byte* outBuffer, int outSize, out int returned, IntPtr overlapped);

    [LibraryImport("kernel32.dll", EntryPoint = "GetVolumeInformationW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetVolumeInformation(string root, char* volumeName, int volumeNameSize, out uint serial, out uint maxComponentLength, out uint flags, char* fileSystemName, int fileSystemNameSize);

    [LibraryImport("kernel32.dll", EntryPoint = "GetDiskFreeSpaceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDiskFreeSpace(string root, out uint sectorsPerCluster, out uint bytesPerSector, out uint freeClusters, out uint totalClusters);
}
