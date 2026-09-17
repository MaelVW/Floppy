using System.Globalization;
using System.Security.Cryptography;

namespace Floppy.Core.Chat;

/// <summary>
/// Die Chat-Identitaet dieser Installation: ein Schluesselpaar (ECDSA P-256).
/// Jede Nachricht wird damit unterschrieben. Die <see cref="Id"/> (z. B. <c>4827-1935-0062</c>)
/// wird aus dem oeffentlichen Schluessel berechnet - niemand kann sie faelschen, ohne den
/// privaten Schluessel zu haben. So erkennt man Leute bei erneutem Kontakt wieder.
/// </summary>
public sealed class ChatIdentity : IDisposable
{
    public const string FileName = "identity.bin";

    private readonly ECDsa _key;

    private ChatIdentity(ECDsa key)
    {
        _key = key;
        PublicKey = key.ExportSubjectPublicKeyInfo();
        Fingerprint = FingerprintOf(PublicKey);
        Id = IdOf(PublicKey);
    }

    /// <summary>Oeffentlicher Schluessel (SubjectPublicKeyInfo, DER).</summary>
    public byte[] PublicKey { get; }

    /// <summary>SHA-256 des oeffentlichen Schluessels (Hex) - eindeutiger Vergleichswert fuer Kontakte.</summary>
    public string Fingerprint { get; }

    /// <summary>Lesbare ID-Nummer, z. B. <c>4827-1935-0062</c>.</summary>
    public string Id { get; }

    public static ChatIdentity CreateNew() => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    /// <summary>
    /// Identitaet aus <paramref name="file"/> laden (per Windows-Datenschutz verschluesselt) oder
    /// neu anlegen. Ist die Datei nicht lesbar (anderes Konto, kaputt), wird sie als
    /// <c>.kaputt</c> gesichert und eine neue Identitaet erzeugt.
    /// </summary>
    public static ChatIdentity LoadOrCreate(string file)
    {
        if (File.Exists(file))
        {
            try
            {
                var plain = LocalSecret.Unprotect(File.ReadAllBytes(file));
                if (plain is not null)
                {
                    try
                    {
                        var key = ECDsa.Create();
                        key.ImportPkcs8PrivateKey(plain, out _);
                        if (key.KeySize == 256) return new ChatIdentity(key);
                        key.Dispose();
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(plain);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                // unten neu anlegen
            }
            try { File.Copy(file, file + ".kaputt", overwrite: true); } catch { }
        }

        var identity = CreateNew();
        identity.Save(file);
        return identity;
    }

    public void Save(string file)
    {
        FloppyPaths.EnsureDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var plain = _key.ExportPkcs8PrivateKey();
        try
        {
            var temp = file + ".tmp";
            File.WriteAllBytes(temp, LocalSecret.Protect(plain));
            File.Move(temp, file, overwrite: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    public byte[] Sign(ReadOnlySpan<byte> data) =>
        _key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    public static bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(publicKey, out var read);
            if (read != publicKey.Length || key.KeySize != 256) return false;
            return key.VerifyData(data, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public static string FingerprintOf(ReadOnlySpan<byte> publicKey) => Convert.ToHexString(SHA256.HashData(publicKey));

    /// <summary>12 Ziffern aus dem Schluessel-Hash, in Viererbloecken.</summary>
    public static string IdOf(ReadOnlySpan<byte> publicKey)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(publicKey, hash);
        var number = BitConverter.ToUInt64(hash[..8]) % 1_000_000_000_000UL;
        var digits = number.ToString("D12", CultureInfo.InvariantCulture);
        return $"{digits[..4]}-{digits[4..8]}-{digits[8..]}";
    }

    /// <summary>Kurzform fuer enge Listen: <c>4827-…-0062</c>.</summary>
    public static string ShortId(string id) => id.Length == 14 ? $"{id[..4]}…{id[10..]}" : id;

    public void Dispose() => _key.Dispose();
}
