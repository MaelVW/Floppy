namespace Floppy.Core;

/// <summary>
/// Leichter Fingerabdruck des Disketteninhalts (nur Wurzelverzeichnis, wie V1).
/// Bleibt er gleich, wurde dieser Zustand schon behandelt - kein erneutes Starten.
/// </summary>
public static class DiskSignature
{
    public const string Empty = "empty";

    public static string Compute(string root)
    {
        var parts = new List<string>();
        try
        {
            foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
            {
                var len = entry is FileInfo fi ? fi.Length.ToString() : "DIR";
                parts.Add($"{entry.Name}|{len}|{entry.LastWriteTimeUtc.Ticks}");
            }
        }
        catch
        {
            return Empty;
        }

        if (parts.Count == 0) return Empty;
        parts.Sort(StringComparer.OrdinalIgnoreCase);
        return string.Join("\n", parts);
    }
}
