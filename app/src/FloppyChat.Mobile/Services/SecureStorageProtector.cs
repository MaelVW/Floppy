using System.Security.Cryptography;
using Floppy.Core.Chat;

namespace FloppyChat.Mobile.Services;

/// <summary>
/// Schutz fuer den privaten Chat-Schluessel auf dem Handy (Gegenstueck zu DPAPI am PC): Die Datei enthaelt den
/// Schluessel nur AES-GCM-verschluesselt; der AES-Schluessel selbst liegt im <see cref="SecureStorage"/>
/// (Android-Keystore bzw. iOS-Keychain) und verlaesst das Geraet nie. Eine kopierte Datei ist damit wertlos.
///
/// Aufbau der Datei: <c>[1 Byte Version][12 Byte Nonce][16 Byte Tag][verschluesselte Daten]</c>.
/// </summary>
internal sealed class SecureStorageProtector : ISecretProtector
{
    private const string KeyName = "floppychat.wrapkey.v1";
    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int HeaderSize = 1 + NonceSize + TagSize;

    private readonly object _lock = new();
    private byte[]? _key;

    public byte[] Protect(ReadOnlySpan<byte> plain)
    {
        var key = GetKey(create: true)!;
        var result = new byte[HeaderSize + plain.Length];
        result[0] = Version;
        var nonce = result.AsSpan(1, NonceSize);
        RandomNumberGenerator.Fill(nonce);

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plain, result.AsSpan(HeaderSize), result.AsSpan(1 + NonceSize, TagSize));
        return result;
    }

    public byte[]? Unprotect(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || data[0] != Version) return null;
        var key = GetKey(create: false);
        if (key is null) return null;   // kein Schluessel auf diesem Handy (neu installiert, anderes Geraet)

        try
        {
            var plain = new byte[data.Length - HeaderSize];
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(data.Slice(1, NonceSize), data[HeaderSize..], data.Slice(1 + NonceSize, TagSize), plain);
            return plain;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    /// <summary>Wird aus Hintergrund-Threads aufgerufen (nie im UI-Thread blockieren, siehe AppHost).</summary>
    private byte[]? GetKey(bool create)
    {
        lock (_lock)
        {
            if (_key is not null) return _key;

            try
            {
                var stored = SecureStorage.Default.GetAsync(KeyName).GetAwaiter().GetResult();
                if (stored is not null && Convert.FromBase64String(stored) is { Length: 32 } bytes) return _key = bytes;
            }
            catch (Exception)
            {
                // Keystore verweigert/kaputt: wie "kein Schluessel" behandeln (bei create wird ein neuer angelegt)
            }

            if (!create) return null;
            var fresh = RandomNumberGenerator.GetBytes(32);
            SecureStorage.Default.SetAsync(KeyName, Convert.ToBase64String(fresh)).GetAwaiter().GetResult();
            return _key = fresh;
        }
    }
}
