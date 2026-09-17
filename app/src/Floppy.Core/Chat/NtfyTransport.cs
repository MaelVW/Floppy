using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace Floppy.Core.Chat;

internal sealed record NtfyEvent
{
    [JsonPropertyName("event")] public string? Event { get; init; }
    [JsonPropertyName("message")] public string? Message { get; init; }
}

[JsonSerializable(typeof(NtfyEvent))]
internal sealed partial class NtfyJsonContext : JsonSerializerContext;

/// <summary>
/// Weg ueber den Gratis-Dienst ntfy (Open Source, https://ntfy.sh). Empfang als
/// JSON-Stream (<c>GET /thema/json</c>), Senden per <c>POST /thema</c>.
///
/// Jede Nachricht geht mit <c>Cache: no</c> raus: der Dienst speichert nichts und liefert nur
/// an gerade verbundene Teilnehmer (passt zu "nichts wird gespeichert"). <c>Firebase: no</c>
/// verhindert die Weiterleitung an Google.
///
/// Limits von ntfy.sh (Stand 2026-09): 4096 Bytes pro Nachricht, ohne Konto etwa 250 Nachrichten
/// pro Tag und 30 offene Verbindungen pro IP-Adresse (eine Schule teilt sich oft eine IP!).
/// </summary>
public sealed class NtfyTransport : IChatTransport
{
    public static readonly Uri DefaultServer = new("https://ntfy.sh/");

    /// <summary>ntfy schickt alle 45 s ein Lebenszeichen - bleibt es viel laenger aus, neu verbinden.</summary>
    public TimeSpan SilenceTimeout { get; init; } = TimeSpan.FromSeconds(100);

    public TimeSpan MaxReconnectDelay { get; init; } = TimeSpan.FromSeconds(30);

    private readonly HttpClient _http;
    private readonly Uri _topic;
    private readonly Channel<byte[]> _outbox = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropWrite });
    private readonly CancellationTokenSource _stop = new();
    private Task _receiving = Task.CompletedTask;
    private Task _sending = Task.CompletedTask;
    private int _started;
    private bool _disposed;

    public NtfyTransport(Uri server, string topic, HttpMessageHandler? handler = null)
    {
        if (topic.Length is 0 or > 64 || !topic.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            throw new ArgumentException("Ungueltiger Themenname.", nameof(topic));
        var baseUri = server.AbsoluteUri.EndsWith('/') ? server : new Uri(server.AbsoluteUri + "/");
        _topic = new Uri(baseUri, topic);
        _http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        }, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("FloppyHub/2.0");
    }

    public ChatMode Mode => ChatMode.Online;

    public event Action<byte[], object?>? FrameReceived;
    public event Action<ChatLinkState, string?>? StateChanged;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        _receiving = Task.Run(() => ReceiveLoop(_stop.Token));
        _sending = Task.Run(() => SendLoop(_stop.Token));
    }

    public void Send(byte[] frame)
    {
        if (!_outbox.Writer.TryWrite(frame)) StateChanged?.Invoke(ChatLinkState.Limited, "outbox-full");
    }

    public void Relay(byte[] frame, object? origin) { }

    public void Reject(object? origin) { }

    public void Close(TimeSpan flush)
    {
        _outbox.Writer.TryComplete();
        try { _sending.Wait(flush); } catch { }
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _outbox.Writer.TryComplete();
        _stop.Cancel();
        StateChanged?.Invoke(ChatLinkState.Closed, null);
        _http.Dispose();
    }

    // ------------------------------------------------------------------

    private async Task ReceiveLoop(CancellationToken stop)
    {
        var delay = TimeSpan.FromSeconds(1);
        var first = true;
        while (!stop.IsCancellationRequested)
        {
            StateChanged?.Invoke(first ? ChatLinkState.Connecting : ChatLinkState.Reconnecting, null);
            first = false;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _topic.AbsoluteUri + "/json");
                using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stop).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    StateChanged?.Invoke(ChatLinkState.Limited, "subscribe");
                    delay = TimeSpan.FromSeconds(60);
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                    await using var stream = await response.Content.ReadAsStreamAsync(stop).ConfigureAwait(false);
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    await ReadEvents(reader, stop).ConfigureAwait(false);
                    delay = TimeSpan.FromSeconds(1);   // war verbunden: schnell wieder versuchen
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                StateChanged?.Invoke(ChatLinkState.Reconnecting, ex.Message);
            }

            try { await Task.Delay(delay, stop).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxReconnectDelay.Ticks));
        }
    }

    private async Task ReadEvents(StreamReader reader, CancellationToken stop)
    {
        while (true)
        {
            string? line;
            using (var silence = CancellationTokenSource.CreateLinkedTokenSource(stop))
            {
                silence.CancelAfter(SilenceTimeout);
                try
                {
                    line = await reader.ReadLineAsync(silence.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!stop.IsCancellationRequested)
                {
                    return;   // zu lange still: neu verbinden
                }
            }
            if (line is null) return;   // Dienst hat getrennt
            if (line.Length is 0 or > 16_000) continue;

            NtfyEvent? e;
            try { e = JsonSerializer.Deserialize(line, NtfyJsonContext.Default.NtfyEvent); }
            catch (JsonException) { continue; }

            switch (e?.Event)
            {
                case "open":
                    StateChanged?.Invoke(ChatLinkState.Connected, null);
                    break;
                case "message" when ChatFrame.FromText(e.Message) is { } frame:
                    FrameReceived?.Invoke(frame, null);
                    break;
            }
        }
    }

    private async Task SendLoop(CancellationToken stop)
    {
        try
        {
            await foreach (var frame in _outbox.Reader.ReadAllAsync(stop).ConfigureAwait(false))
            {
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    var result = await Publish(frame, stop).ConfigureAwait(false);
                    if (result == HttpStatusCode.OK) break;
                    if (result == HttpStatusCode.TooManyRequests)
                    {
                        StateChanged?.Invoke(ChatLinkState.Limited, "publish");
                        break;   // nicht wiederholen, das zaehlt sonst weiter aufs Limit
                    }
                    if (attempt == 2)
                    {
                        StateChanged?.Invoke(ChatLinkState.Limited, "send-failed");
                        break;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(1 + attempt * 2), stop).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // geschlossen
        }
    }

    /// <returns>OK, TooManyRequests oder einen anderen Code fuer "hat nicht geklappt".</returns>
    private async Task<HttpStatusCode> Publish(byte[] frame, CancellationToken stop)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var request = new HttpRequestMessage(HttpMethod.Post, _topic)
            {
                Content = new StringContent(ChatFrame.ToText(frame), Encoding.UTF8, "text/plain"),
            };
            request.Headers.Add("Cache", "no");
            request.Headers.Add("Firebase", "no");
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return HttpStatusCode.OK;
            return response.StatusCode;
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return HttpStatusCode.ServiceUnavailable;
        }
    }
}
