namespace Floppy.Core.Tests;

/// <summary>Temporaerer Ordner, der eine Diskette spielt. Wird nach dem Test geloescht.</summary>
internal sealed class TempDisk : IDisposable
{
    public string Root { get; }

    public TempDisk()
    {
        Root = Path.Combine(Path.GetTempPath(), "floppy-test-" + Guid.NewGuid().ToString("N")) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(Root);
    }

    public string Write(string relativePath, string content = "x")
    {
        var full = Path.Combine(Root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public string GameTxt(params string[] lines) => Write("game.txt", string.Join("\r\n", lines));

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { }
    }
}
