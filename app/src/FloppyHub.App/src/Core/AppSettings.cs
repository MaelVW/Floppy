using System.Globalization;
using System.Text;
using Floppy.Core;

namespace FloppyHub.App.Core;

/// <summary>Persoenliche App-Einstellungen in %LOCALAPPDATA%\FloppyHub\app.ini.</summary>
public sealed class AppSettings
{
    public const string ThemeSystem = "system";
    public const string ThemeLight = "light";
    public const string ThemeDark = "dark";

    public string Theme { get; set; } = ThemeSystem;
    public string Language { get; set; } = "de";

    /// <summary>0 = automatisch (Windows-Skalierung).</summary>
    public float Scale { get; set; }

    public bool LoadCovers { get; set; } = true;
    public bool FirstRunDone { get; set; }

    /// <summary>Name fuer Rekorde im Minispiel.</summary>
    public string PlayerName { get; set; } = "";

    /// <summary>Gratis-Dienst fuer den Online-Chat (ntfy). Leer = ntfy.sh.</summary>
    public string ChatServer { get; set; } = "";

    /// <summary>Woher die Update-Pruefung ihre Daten holt. Leer = eingebauter GitHub-Feed.</summary>
    public string UpdateFeedUrl { get; set; } = "";

    /// <summary>Diese Version wurde per "Spaeter" abgelehnt - nicht nochmal vorschlagen.</summary>
    public string SkippedUpdateVersion { get; set; } = "";

    public static AppSettings Load(string file)
    {
        var s = new AppSettings();
        if (!File.Exists(file)) return s;
        try
        {
            var ini = IniDocument.Load(file);
            s.Theme = ini.Get("ui", "theme", ThemeSystem)!.ToLowerInvariant() switch
            {
                ThemeLight => ThemeLight,
                ThemeDark => ThemeDark,
                _ => ThemeSystem,
            };
            s.Language = ini.Get("ui", "language", "de")!.ToLowerInvariant();
            s.Scale = float.TryParse(ini.Get("ui", "scale", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
                ? Math.Clamp(f, 0f, 3f) : 0f;
            s.LoadCovers = ini.GetBool("library", "load_covers", true);
            s.FirstRunDone = ini.GetBool("app", "first_run_done", false);
            s.PlayerName = Floppy.Core.Minigame.ScoreBoard.CleanName(ini.Get("game", "player", ""));
            s.ChatServer = (ini.Get("chat", "server", "") ?? "").Trim();
            s.UpdateFeedUrl = (ini.Get("update", "feed_url", "") ?? "").Trim();
            s.SkippedUpdateVersion = (ini.Get("update", "skipped_version", "") ?? "").Trim();
        }
        catch
        {
            // kaputte Datei: Standardwerte, beim naechsten Speichern repariert
        }
        return s;
    }

    public void Save(string file)
    {
        var sb = new StringBuilder()
            .AppendLine("; Floppy Hub App - persoenliche Einstellungen (wird von der App geschrieben)")
            .AppendLine("[ui]")
            .AppendLine($"theme = {Theme}")
            .AppendLine($"language = {Language}")
            .AppendLine(string.Create(CultureInfo.InvariantCulture, $"scale = {Scale}"))
            .AppendLine("[library]")
            .AppendLine($"load_covers = {(LoadCovers ? "true" : "false")}")
            .AppendLine("[app]")
            .AppendLine($"first_run_done = {(FirstRunDone ? "true" : "false")}")
            .AppendLine("[game]")
            .AppendLine($"player = {PlayerName}")
            .AppendLine("[chat]")
            .AppendLine("; leer = https://ntfy.sh (oder eigener ntfy-Server, nur https)")
            .AppendLine($"server = {ChatServer}")
            .AppendLine("[update]")
            .AppendLine("; leer = eingebauter GitHub-Feed")
            .AppendLine($"feed_url = {UpdateFeedUrl}")
            .AppendLine($"skipped_version = {SkippedUpdateVersion}");
        FloppyPaths.EnsureDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
    }
}
