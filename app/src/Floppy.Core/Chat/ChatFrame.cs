using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Floppy.Core.Chat;

/// <summary>Eine gepruefte, entschluesselte Nachricht.</summary>
/// <param name="SenderKey">Oeffentlicher Schluessel des Absenders.</param>
/// <param name="SenderFingerprint">SHA-256 davon (Hex) - eindeutig pro Installation.</param>
/// <param name="SenderId">Lesbare ID-Nummer des Absenders.</param>
public sealed record ChatEnvelope(ChatPayload Payload, byte[] SenderKey, string SenderFingerprint, string SenderId);

/// <summary>
/// Verpackung einer Nachricht:
/// <code>
/// "FH" 01 | Nonce (12) | AES-256-GCM( Schluessel-Laenge | oeffentl. Schluessel | Unterschrift-Laenge | Unterschrift | JSON ) | Tag (16)
/// </code>
/// Die Unterschrift (ECDSA) deckt Raum-Kennung + JSON ab: Nachrichten koennen weder
/// veraendert noch in einen anderen Raum kopiert werden. Hoechstens <see cref="MaxFrameBytes"/>
/// Bytes, damit die Base64-Form in eine ntfy-Nachricht (4096 Bytes) passt.
/// </summary>
public static class ChatFrame
{
    public const int MaxFrameBytes = 3072;
    private const int NonceBytes = 12;
    private const int TagBytes = 16;
    private static readonly byte[] Header = [(byte)'F', (byte)'H', 1];

    public static int Overhead => Header.Length + NonceBytes + TagBytes;

    /// <summary>Umlaute nicht als ä (6 Bytes) schreiben - der Text landet nie in HTML.</summary>
    private static readonly JsonTypeInfo<ChatPayload> PayloadInfo = (JsonTypeInfo<ChatPayload>)new JsonSerializerOptions
    {
        TypeInfoResolver = ChatJsonContext.Default,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    }.GetTypeInfo(typeof(ChatPayload));

    public static byte[] Seal(ChatRoomKey room, ChatIdentity sender, ChatPayload payload)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(payload, PayloadInfo);
        var signed = new byte[room.Tag.Length + json.Length];
        room.Tag.CopyTo(signed, 0);
        json.CopyTo(signed, room.Tag.Length);
        var signature = sender.Sign(signed);
        var key = sender.PublicKey;
        if (key.Length > 255 || signature.Length > 255) throw new InvalidOperationException("Schluessel zu gross.");

        var plain = new byte[1 + key.Length + 1 + signature.Length + json.Length];
        var span = plain.AsSpan();
        span[0] = (byte)key.Length;
        key.CopyTo(span[1..]);
        span[1 + key.Length] = (byte)signature.Length;
        signature.CopyTo(span[(2 + key.Length)..]);
        json.CopyTo(span[(2 + key.Length + signature.Length)..]);

        var frame = new byte[Overhead + plain.Length];
        if (frame.Length > MaxFrameBytes) throw new ChatFrameTooLargeException(frame.Length);

        Header.CopyTo(frame, 0);
        var nonce = frame.AsSpan(Header.Length, NonceBytes);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(room.AesKey, TagBytes);
        aes.Encrypt(nonce, plain, frame.AsSpan(Header.Length + NonceBytes, plain.Length),
            frame.AsSpan(frame.Length - TagBytes), Header);
        return frame;
    }

    /// <summary>Entschluesseln + Unterschrift pruefen. <c>false</c> bei allem, was nicht stimmt (falscher Raum, veraendert, Unsinn).</summary>
    public static bool TryOpen(ChatRoomKey room, ReadOnlySpan<byte> frame, out ChatEnvelope? envelope)
    {
        envelope = null;
        if (frame.Length < Overhead + 2 || frame.Length > MaxFrameBytes || !frame[..Header.Length].SequenceEqual(Header)) return false;

        var plain = new byte[frame.Length - Overhead];
        try
        {
            using var aes = new AesGcm(room.AesKey, TagBytes);
            aes.Decrypt(frame.Slice(Header.Length, NonceBytes), frame.Slice(Header.Length + NonceBytes, plain.Length),
                frame[^TagBytes..], plain, Header);
        }
        catch (CryptographicException)
        {
            return false;   // anderer Raum oder veraendert
        }

        var span = plain.AsSpan();
        var keyLength = span[0];
        if (span.Length < 1 + keyLength + 1) return false;
        var key = span.Slice(1, keyLength);
        var sigLength = span[1 + keyLength];
        var jsonStart = 2 + keyLength + sigLength;
        if (span.Length <= jsonStart) return false;
        var signature = span.Slice(2 + keyLength, sigLength);
        var json = span[jsonStart..];

        var signed = new byte[room.Tag.Length + json.Length];
        room.Tag.CopyTo(signed, 0);
        json.CopyTo(signed.AsSpan(room.Tag.Length));
        if (!ChatIdentity.Verify(key, signed, signature)) return false;

        ChatPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize(json, ChatJsonContext.Default.ChatPayload);
        }
        catch (JsonException)
        {
            return false;
        }
        if (payload is null || !IsWellFormedSafe(payload)) return false;

        var keyBytes = key.ToArray();
        envelope = new ChatEnvelope(payload, keyBytes, ChatIdentity.FingerprintOf(keyBytes), ChatIdentity.IdOf(keyBytes));
        return true;
    }

    /// <summary>
    /// Jeder, der den Raumschluessel kennt (im offenen Chat: jeder), kann Nachrichten mit
    /// beliebigem JSON schicken - z. B. <c>null</c> statt Text. Das darf nie zu einer Ausnahme
    /// im Empfaenger fuehren, sondern nur dazu, dass die Nachricht verworfen wird.
    /// </summary>
    private static bool IsWellFormedSafe(ChatPayload payload)
    {
        try { return payload.IsWellFormed(); }
        catch (NullReferenceException) { return false; }
    }

    /// <summary>Textform fuer den Dienst.</summary>
    public static string ToText(byte[] frame) => Convert.ToBase64String(frame);

    public static byte[]? FromText(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > 4096) return null;
        try { return Convert.FromBase64String(text.Trim()); }
        catch (FormatException) { return null; }
    }

    /// <summary>Neue zufaellige Nachrichten-ID (16 Hex-Zeichen).</summary>
    public static string NewId() => Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
}

public sealed class ChatFrameTooLargeException(int bytes)
    : Exception($"Nachricht zu gross ({bytes} Bytes, erlaubt {ChatFrame.MaxFrameBytes}).")
{
    public int Bytes { get; } = bytes;
}
