using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Floppy.Core;

/// <summary>Startet Steam-Spiele und Programme - gleich fuer Motor und App.</summary>
public static partial class Starter
{
    /// <summary>Dateiname der grafischen App im Programmordner.</summary>
    public const string AppExeName = "FloppyHub.exe";

    public static void OpenSteam(string appId)
    {
        if (appId.Length == 0 || !appId.All(char.IsAsciiDigit))
            throw new ArgumentException($"Ungueltige Steam-ID: {appId}", nameof(appId));
        using var _ = Process.Start(new ProcessStartInfo(SteamApps.RunUri(appId)) { UseShellExecute = true });
    }

    /// <summary>Wie Start-Target in V1: Arbeitsordner = Ordner der Datei.</summary>
    public static void StartProgram(string path, string? arguments)
    {
        var psi = new ProcessStartInfo(path) { UseShellExecute = true };
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) psi.WorkingDirectory = dir;
        if (!string.IsNullOrWhiteSpace(arguments)) psi.Arguments = arguments;
        using var _ = Process.Start(psi);
    }

    /// <summary>
    /// Unterdrueckt den Windows-Dialog "Es befindet sich kein Datentraeger im Laufwerk"
    /// beim Abfragen eines leeren Diskettenlaufwerks (wie SetErrorMode in V1).
    /// </summary>
    public static void SuppressDriveErrorDialogs()
    {
        if (!OperatingSystem.IsWindows()) return;
        const uint SEM_FAILCRITICALERRORS = 0x0001;
        const uint SEM_NOGPFAULTERRORBOX = 0x0002;
        const uint SEM_NOOPENFILEERRORBOX = 0x8000;
        try { SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX); }
        catch { }
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint SetErrorMode(uint mode);
}
