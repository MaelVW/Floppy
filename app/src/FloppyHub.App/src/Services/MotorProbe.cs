namespace FloppyHub.App.Services;

/// <summary>Laeuft ein Floppy Launcher (Motor oder Konsole V1)? Erkennbar an der gemeinsamen Sperre.</summary>
public static class MotorProbe
{
    private const string MutexName = @"Local\FloppyLauncherSingleInstance";

    public static bool IsRunning()
    {
        try
        {
            if (!System.Threading.Mutex.TryOpenExisting(MutexName, out var mutex)) return false;
            mutex.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;   // existiert, gehoert aber jemand anderem
        }
        catch
        {
            return false;
        }
    }
}
