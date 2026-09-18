using Floppy.Core;
using Godot;

namespace FloppyHub.App.Core;

/// <summary>Alles, was die Oberflaeche an Daten und Regeln braucht - einmal erzeugt beim Start.</summary>
public sealed class AppServices
{
    private AppServices(AppArgs args, FloppyPaths paths)
    {
        Args = args;
        Paths = paths;
        Options = paths.LoadOptions(out var problem);
        OptionsProblem = problem;
        Settings = AppSettings.Load(paths.AppSettingsFile);
        Trust = new TrustStore(paths.TrustFile);
        Log = new LogFile(paths.ResolveLogFile(Options), Options.LogMaxKb);
        DriveRoot = args.Drive ?? Options.DriveRoot;
    }

    public AppArgs Args { get; }
    public FloppyPaths Paths { get; }
    public FloppyOptions Options { get; private set; }
    public string? OptionsProblem { get; }
    public AppSettings Settings { get; }
    public TrustStore Trust { get; }
    public LogFile Log { get; }

    /// <summary>Das eine beobachtete Laufwerk (Standard A:\).</summary>
    public string DriveRoot { get; }

    /// <summary>Testbetrieb (Bildschirmfoto): nichts dauerhaft veraendern.</summary>
    public bool ReadOnlyMode => Args.Screenshot is not null;

    public static AppServices Create(AppArgs args)
    {
        var home = args.Home ?? DefaultHome();
        return new AppServices(args, new FloppyPaths(home, args.UserData));
    }

    public void SaveSettings()
    {
        if (ReadOnlyMode) return;
        try { Settings.Save(Paths.AppSettingsFile); }
        catch (Exception ex) { GD.PushWarning($"Einstellungen nicht gespeichert: {ex.Message}"); }
    }

    public LaunchPlanner Planner => new(Options);

    private Services.ChatService? _chat;

    /// <summary>Chat (lebt so lange wie die App, auch wenn die Oberflaeche neu gebaut wird).</summary>
    public Services.ChatService Chat => _chat ??= new Services.ChatService(this);

    private Services.UpdateService? _updates;

    /// <summary>Prueft einmal pro Start im Hintergrund auf ein neueres Release.</summary>
    public Services.UpdateService Updates => _updates ??= new Services.UpdateService(this);

    private static string DefaultHome()
    {
        // Exportierte App: Ordner der FloppyHub.exe. Im Editor: Projektordner (zum Testen).
        if (OS.HasFeature("editor")) return ProjectSettings.GlobalizePath("res://");
        return System.IO.Path.GetDirectoryName(OS.GetExecutablePath()) ?? ".";
    }
}
