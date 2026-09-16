using System.Diagnostics;
using System.IO.Pipes;
using System.Text;

namespace Floppy.Core;

/// <summary>
/// Die einzige Verbindung Motor -&gt; App: kurze BEFEHLE, nie Pfade oder Programme.
/// Die App wertet die Diskette immer selbst neu aus. Selbst wenn also ein fremdes
/// Programm etwas an die Pipe schickt, kann es damit nichts starten lassen.
/// Die Pipe ist auf das eigene Benutzerkonto beschraenkt (CurrentUserOnly).
/// </summary>
public static class HubPipe
{
    /// <summary>App-Fenster nach vorne holen.</summary>
    public const string Show = "show";

    /// <summary>Hub-Diskette eingelegt: App mit der Disketten-Ansicht oeffnen.</summary>
    public const string Hub = "hub";

    /// <summary>Diskette braucht eine Rueckfrage: App wertet A: aus und fragt.</summary>
    public const string Confirm = "confirm";

    public static IReadOnlyList<string> Commands { get; } = [Show, Hub, Confirm];

    /// <summary>Einzelinstanz-Sperre der App (der Motor nutzt die V1-Sperre).</summary>
    public const string AppMutexName = @"Local\FloppyHubAppSingleInstance";

    private const int MaxMessageBytes = 32;

    public static string PipeName { get; } = BuildName();

    public static bool IsCommand(string? text) => text is not null && Commands.Contains(text);

    /// <summary>Befehl an eine laufende App schicken. false = keine App erreichbar.</summary>
    public static bool TrySend(string command, int timeoutMs = 400) => TrySend(PipeName, command, timeoutMs);

    internal static bool TrySend(string pipeName, string command, int timeoutMs)
    {
        if (!IsCommand(command)) throw new ArgumentException($"Unbekannter Befehl: {command}", nameof(command));
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(timeoutMs);
            client.Write(Encoding.ASCII.GetBytes(command + "\n"));
            client.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Befehle empfangen, bis <paramref name="cancel"/> ausgeloest wird (laeuft in der App).</summary>
    public static Task ServeAsync(Action<string> onCommand, CancellationToken cancel) =>
        ServeAsync(PipeName, onCommand, cancel);

    internal static async Task ServeAsync(string pipeName, Action<string> onCommand, CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancel).ConfigureAwait(false);

                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                readTimeout.CancelAfter(1000);
                var buffer = new byte[MaxMessageBytes];
                var read = 0;
                while (read < buffer.Length)
                {
                    var n = await server.ReadAsync(buffer.AsMemory(read), readTimeout.Token).ConfigureAwait(false);
                    if (n == 0) break;
                    read += n;
                    if (Array.IndexOf(buffer, (byte)'\n', 0, read) >= 0) break;
                }

                var text = Encoding.ASCII.GetString(buffer, 0, read).Split('\n')[0].Trim();
                if (IsCommand(text)) onCommand(text);
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                break;
            }
            catch
            {
                // Kaputte Verbindung o. Ae.: kurz warten, weiter lauschen.
                try { await Task.Delay(250, cancel).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private static string BuildName()
    {
        int session;
        using (var self = Process.GetCurrentProcess()) session = self.SessionId;
        var user = new string(Environment.UserName.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray());
        return $"FloppyHub.App.{session}.{user}";
    }
}
