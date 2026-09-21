using System.Globalization;
using System.Text;
using Floppy.Core;
using Floppy.Core.Chat;

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

    /// <summary>
    /// Selbst gewaehlter Steam-Ordner fuer "Steam durchsuchen" (dort, wo steam.exe liegt). Leer = Steam selbst
    /// suchen (Registrierung, Standardordner). Ein Ordner, der nicht (mehr) stimmt, wird uebergangen.
    /// </summary>
    public string SteamPath { get; set; } = "";

    public bool FirstRunDone { get; set; }

    /// <summary>Name fuer Rekorde im Minispiel.</summary>
    public string PlayerName { get; set; } = "";

    /// <summary>Gratis-Dienst fuer den Online-Chat (ntfy). Leer = ntfy.sh.</summary>
    public string ChatServer { get; set; } = "";

    /// <summary>Woher die Update-Pruefung ihre Daten holt. Leer = eingebauter GitHub-Feed.</summary>
    public string UpdateFeedUrl { get; set; } = "";

    /// <summary>Diese Version wurde per "Spaeter" abgelehnt - nicht nochmal vorschlagen.</summary>
    public string SkippedUpdateVersion { get; set; } = "";

    /// <summary>
    /// "Sofort beenden": Tastenkombination, die die App ohne Rueckfrage schliesst. null = nicht festgelegt.
    /// Nur, was <see cref="KeyChord.Validate"/> durchlaesst (nie Alt+F4) - eine von Hand kaputt editierte INI ergibt null.
    /// </summary>
    public KeyChord? QuitHotkey { get; set; }

    // ---- Chat persoenlich machen ("Chat anpassen") ----

    /// <summary>Selbstgewaehlter Anzeigename im Chat (schon gesaeubert, siehe <see cref="ChatProfile.Clean"/>); leer = keiner.</summary>
    public string ChatAlias { get; set; } = "";

    /// <summary>Namensfarbe im Chat: 0 = automatisch, 1 bis 8 = gewaehlt.</summary>
    public int ChatColor { get; set; }

    /// <summary>Ton und blinkende Taskleiste bei neuen Nachrichten, wenn der Chat nicht im Blick ist.</summary>
    public ChatNotifyMode ChatNotify { get; set; } = ChatNotifyMode.Off;

    /// <summary>Nachrichten, in denen der eigene Name vorkommt, bekommen einen farbigen Hintergrund.</summary>
    public bool ChatHighlightMentions { get; set; } = true;

    public ChatFontSize ChatFont { get; set; } = ChatFontSize.Normal;

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
            s.SteamPath = PathRules.StripQuotes(ini.Get("library", "steam_path", ""));
            s.FirstRunDone = ini.GetBool("app", "first_run_done", false);
            s.PlayerName = Floppy.Core.Minigame.ScoreBoard.CleanName(ini.Get("game", "player", ""));
            s.ChatServer = (ini.Get("chat", "server", "") ?? "").Trim();
            s.ChatAlias = ChatProfile.Clean(ini.Get("chat", "alias", ""), allowReserved: true) ?? "";   // Admin-Namen bleiben erhalten, die Sitzung prueft nochmal
            s.ChatColor = ChatProfile.CleanColor(ini.GetInt("chat", "color", 0));
            s.ChatNotify = ChatProfile.ParseNotify(ini.Get("chat", "notify", ""));
            s.ChatHighlightMentions = ini.GetBool("chat", "highlight_mentions", true);
            s.ChatFont = ChatProfile.ParseFontSize(ini.Get("chat", "font", ""));
            s.UpdateFeedUrl = (ini.Get("update", "feed_url", "") ?? "").Trim();
            s.SkippedUpdateVersion = (ini.Get("update", "skipped_version", "") ?? "").Trim();
            s.QuitHotkey = KeyChord.TryParse(ini.Get("app", "quit_hotkey", ""), out var hotkey) &&
                           hotkey.Validate(AppShortcuts.InUse) == KeyChordProblem.None ? hotkey : null;
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
            .AppendLine("; Steam durchsuchen: leer = Steam selbst finden, sonst der Ordner mit steam.exe")
            .AppendLine($"steam_path = {SteamPath}")
            .AppendLine("[app]")
            .AppendLine($"first_run_done = {(FirstRunDone ? "true" : "false")}")
            .AppendLine("; Sofort beenden: z. B. Ctrl+F12 (nie Alt+F4). Leer = nicht festgelegt.")
            .AppendLine($"quit_hotkey = {QuitHotkey}")
            .AppendLine("[game]")
            .AppendLine($"player = {PlayerName}")
            .AppendLine("[chat]")
            .AppendLine("; leer = https://ntfy.sh (oder eigener ntfy-Server, nur https)")
            .AppendLine($"server = {ChatServer}")
            .AppendLine("; Anzeigename und Namensfarbe (0 = automatisch, 1-8) - beides sehen alle im Raum")
            .AppendLine($"alias = {ChatAlias}")
            .AppendLine($"color = {ChatColor}")
            .AppendLine("; Ton + blinkende Taskleiste, wenn der Chat nicht im Blick ist: off | mentions | all")
            .AppendLine($"notify = {ChatNotify.ToString().ToLowerInvariant()}")
            .AppendLine($"highlight_mentions = {(ChatHighlightMentions ? "true" : "false")}")
            .AppendLine("; Schrift im Chatverlauf: small | normal | large | huge")
            .AppendLine($"font = {ChatFont.ToString().ToLowerInvariant()}")
            .AppendLine("[update]")
            .AppendLine("; leer = eingebauter GitHub-Feed")
            .AppendLine($"feed_url = {UpdateFeedUrl}")
            .AppendLine($"skipped_version = {SkippedUpdateVersion}");
        FloppyPaths.EnsureDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
    }
}
