using System.Diagnostics;
using System.Runtime.InteropServices;
using Floppy.Core;

namespace FloppyLauncher;

/// <summary>Die echten Aktionen: Steam, Programme, App wecken, Notfall-Rueckfrage.</summary>
internal sealed partial class SystemActions(FloppyPaths paths, MotorArgs args, LogFile log) : IMotorActions
{
    public void OpenSteam(string appId) => Starter.OpenSteam(appId);

    public void StartProgram(string path, string? arguments) => Starter.StartProgram(path, arguments);

    public bool WakeApp(string command)
    {
        // 1. Laeuft die App schon? Dann nur Bescheid geben.
        if (HubPipe.TrySend(command))
        {
            log.Write(LogLevel.Info, $"Floppy Hub App benachrichtigt ({command}).");
            return true;
        }

        // 2. Sonst starten.
        var exe = args.AppExe ?? Path.Combine(paths.Home, Starter.AppExeName);
        if (!File.Exists(exe))
        {
            log.Write(LogLevel.Warn, $"Floppy Hub App nicht gefunden: {exe}");
            return false;
        }

        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? paths.Home,
            Arguments = MotorArgs.BuildAppArguments(args.AppArgs, command),
        };
        using var _ = Process.Start(psi);
        log.Write(LogLevel.Info, $"Floppy Hub App gestartet ({command}).");
        return true;
    }

    public bool AskWithoutApp(string target, string? arguments, string reason)
    {
        const uint MB_YESNO = 0x04, MB_ICONWARNING = 0x30, MB_DEFBUTTON2 = 0x100,
                   MB_SETFOREGROUND = 0x10000, MB_TOPMOST = 0x40000;
        const int IDYES = 6;

        var text =
            "Auf der Diskette liegt ein Programm, das noch nicht freigegeben ist:\n\n" +
            $"{target} {arguments}".TrimEnd() + "\n\n" +
            $"Grund: {reason}\n\n" +
            "Nur starten, wenn du der Diskette vertraust.\nEinmal starten?";

        // Blockiert, bis geantwortet wird - nur der Notfall ohne installierte App.
        return MessageBoxW(IntPtr.Zero, text, "Floppy Launcher",
            MB_YESNO | MB_ICONWARNING | MB_DEFBUTTON2 | MB_SETFOREGROUND | MB_TOPMOST) == IDYES;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
