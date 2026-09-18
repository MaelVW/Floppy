namespace Floppy.Core.Updates;

/// <summary>Ein verfuegbares Release: Versionsnummer (ohne "v"), Anzeigename, Link zur Release-Seite.</summary>
public sealed record UpdateInfo(string Version, string Name, string HtmlUrl);

/// <summary>Woher die App erfaehrt, welches die neueste veroeffentlichte Version ist.</summary>
public interface IUpdateFeed
{
    /// <summary>Neuestes Release oder null (nichts gefunden, kein Netz, Dienst nicht erreichbar).</summary>
    Task<UpdateInfo?> GetLatestAsync(CancellationToken ct);
}
