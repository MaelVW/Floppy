using System.Diagnostics;

namespace Floppy.Core.Updates;

/// <summary>Startet das heruntergeladene Setup unbeaufsichtigt und raeumt danach auf.</summary>
public static class UpdateInstaller
{
    /// <summary>Ordner fuer heruntergeladene Setups (im Temp-Ordner des Benutzers).</summary>
    public static string DownloadDirectory => Path.Combine(Path.GetTempPath(), "FloppyHub-Update");

    /// <summary>
    /// Ohne Fenster, ohne Rueckfragen. <c>/RELAUNCH=</c> sagt dem Setup, was es danach wieder starten soll
    /// (das Setup selbst kennt die Liste - aeltere Setups ignorieren den Schalter einfach).
    /// </summary>
    public static string BuildArguments(bool relaunchMotor) =>
        "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /VARIANT=app /RELAUNCH=" + (relaunchMotor ? "hub,motor" : "hub");

    /// <summary>Setup starten. Wirft, wenn es nicht geht (z. B. Adminabfrage abgelehnt).</summary>
    public static void Start(string setupPath, bool relaunchMotor)
    {
        if (!File.Exists(setupPath)) throw new FileNotFoundException("Setup nicht gefunden.", setupPath);
        var psi = new ProcessStartInfo(setupPath, BuildArguments(relaunchMotor))
        {
            UseShellExecute = true,   // damit Windows bei Bedarf (Programme-Ordner) nach Adminrechten fragen kann
            WorkingDirectory = Path.GetDirectoryName(setupPath) ?? "",
        };
        using var _ = Process.Start(psi);
    }

    /// <summary>Alte heruntergeladene Setups loeschen (vom letzten Update). Nichts davon ist wichtig - Fehler werden ignoriert.</summary>
    public static void CleanUp()
    {
        try
        {
            if (Directory.Exists(DownloadDirectory)) Directory.Delete(DownloadDirectory, recursive: true);
        }
        catch
        {
            // Setup laeuft vielleicht noch (Datei gesperrt) - beim naechsten Start wieder
        }
    }
}
