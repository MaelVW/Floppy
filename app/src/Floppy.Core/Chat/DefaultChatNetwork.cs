namespace Floppy.Core.Chat;

/// <summary>Die echten Wege: ntfy-Dienst online, TCP/UDP im lokalen Netz.</summary>
public sealed class DefaultChatNetwork(Uri server) : IChatNetwork
{
    public LanOptions LanOptions { get; init; } = new();

    public string ServiceName => server.IsDefaultPort ? server.Host : $"{server.Host}:{server.Port}";

    public IChatTransport CreateOnline(ChatRoomKey room) => new NtfyTransport(server, room.Topic);

    public ILocalChatTransport CreateLocal(ChatRoomKey room) => new LanTransport(room, LanOptions);

    public async Task<IReadOnlyList<string>> ProbeLocalAsync(ChatRoomKey room, TimeSpan timeout) =>
        (await LanTransport.ProbeAsync(room, timeout, LanOptions).ConfigureAwait(false)).Select(r => r.Endpoint).ToList();

    /// <summary>
    /// Server-Adresse aus den Einstellungen: nur https (oder http fuer einen Server auf diesem PC).
    /// Ungueltig = <see cref="NtfyTransport.DefaultServer"/>.
    /// </summary>
    public static Uri ParseServer(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)) return NtfyTransport.DefaultServer;
        if (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            return uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/");
        return NtfyTransport.DefaultServer;
    }
}
