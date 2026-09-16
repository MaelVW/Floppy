using Floppy.Core;
using FloppyHub.App.Core;
using FloppyHub.App.Services;
using Godot;

namespace FloppyHub.App.Ui;

/// <summary>Was Ansichten vom Hauptfenster brauchen.</summary>
public interface IAppHost
{
    AppServices Services { get; }
    CoverCache Covers { get; }

    /// <summary>Hier werden Dialoge eingehaengt (ueber allem).</summary>
    Control DialogLayer { get; }

    IReadOnlyList<LibraryEntry> Library { get; }
    void ReloadLibrary();

    void ShowView(string key);
    void SetStatusMessage(string text, string icon = "info");

    /// <summary>Diskette in A: starten - mit Rueckfrage, wenn noetig.</summary>
    void LaunchDisc();

    /// <summary>Ein Programm (z. B. aus der Bibliothek) starten - mit Rueckfrage, wenn nicht freigegeben.</summary>
    void LaunchProgram(string path, string? arguments, string label);

    void ApplyTheme(string theme);
    void ApplyScale(float scale);
}
