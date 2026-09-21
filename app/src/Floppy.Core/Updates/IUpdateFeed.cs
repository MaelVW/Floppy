namespace Floppy.Core.Updates;

/// <summary>
/// Ein verfuegbares Release: Versionsnummer (ohne "v"), Anzeigename, Link zur Release-Seite - und, wenn
/// das Release die Setup-Datei samt Pruefsumme enthaelt, deren Download-Links (fuer das automatische Update).
/// </summary>
public sealed record UpdateInfo(string Version, string Name, string HtmlUrl, string? InstallerUrl = null, string? ChecksumUrl = null)
{
    /// <summary>Dateiname der Setup-Datei im Release (auch der Name in der Pruefsummen-Datei).</summary>
    public string InstallerFileName => $"FloppyHubSetup-{Version}.exe";
}

/// <summary>Woher die App erfaehrt, welches die neueste veroeffentlichte Version ist.</summary>
public interface IUpdateFeed
{
    /// <summary>Neuestes Release oder null (nichts gefunden, kein Netz, Dienst nicht erreichbar).</summary>
    Task<UpdateInfo?> GetLatestAsync(CancellationToken ct);
}
