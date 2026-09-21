using System.Runtime.InteropServices;

namespace Floppy.Core.Chat;

/// <summary>
/// Schutz fuer Geheimnisse auf der Platte. Auf Windows ist es <see cref="LocalSecret.Windows"/>
/// (DPAPI); die Handy-App reicht einen eigenen Schutz nach (Android-Keystore / iOS-Keychain).
/// </summary>
public interface ISecretProtector
{
    byte[] Protect(ReadOnlySpan<byte> plain);

    /// <summary><c>null</c> = nicht lesbar (anderes Konto/Geraet, kaputt).</summary>
    byte[]? Unprotect(ReadOnlySpan<byte> protectedData);
}

/// <summary>
/// Windows-Datenschutz (DPAPI, "nur dieses Benutzerkonto"): Geheimnisse wie der private
/// Chat-Schluessel oder gespeicherte Verschluesselungen liegen damit nie im Klartext auf der
/// Platte. Eine kopierte Datei ist auf einem anderen Konto/PC wertlos.
/// </summary>
public static unsafe partial class LocalSecret
{
    private const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;

    /// <summary>Der Standard-Schutz (DPAPI) als <see cref="ISecretProtector"/>.</summary>
    public static ISecretProtector Windows { get; } = new WindowsProtector();

    private sealed class WindowsProtector : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plain) => LocalSecret.Protect(plain);

        public byte[]? Unprotect(ReadOnlySpan<byte> protectedData) => LocalSecret.Unprotect(protectedData);
    }

    /// <summary>Unterscheidet Floppy-Hub-Daten von anderen DPAPI-Daten desselben Kontos.</summary>
    private static readonly byte[] Entropy = "FloppyHub.LocalSecret.v1"u8.ToArray();

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Size;
        public byte* Data;
    }

    public static byte[] Protect(ReadOnlySpan<byte> plain) => Run(plain, protect: true)
        ?? throw new InvalidOperationException($"Windows-Datenschutz (DPAPI) fehlgeschlagen: {Marshal.GetLastPInvokeError()}");

    /// <summary><c>null</c> = nicht lesbar (anderes Konto, kaputt).</summary>
    public static byte[]? Unprotect(ReadOnlySpan<byte> protectedData) => Run(protectedData, protect: false);

    private static byte[]? Run(ReadOnlySpan<byte> input, bool protect)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("DPAPI gibt es nur unter Windows.");

        fixed (byte* inPtr = input)
        fixed (byte* entropyPtr = Entropy)
        {
            var inBlob = new DataBlob { Size = input.Length, Data = inPtr };
            var entropyBlob = new DataBlob { Size = Entropy.Length, Data = entropyPtr };
            var outBlob = default(DataBlob);

            var ok = protect
                ? CryptProtectData(&inBlob, null, &entropyBlob, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, &outBlob)
                : CryptUnprotectData(&inBlob, IntPtr.Zero, &entropyBlob, IntPtr.Zero, IntPtr.Zero, CRYPTPROTECT_UI_FORBIDDEN, &outBlob);
            if (!ok) return null;

            try
            {
                return new ReadOnlySpan<byte>(outBlob.Data, outBlob.Size).ToArray();
            }
            finally
            {
                new Span<byte>(outBlob.Data, outBlob.Size).Clear();
                LocalFree((IntPtr)outBlob.Data);
            }
        }
    }

    [LibraryImport("crypt32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptProtectData(DataBlob* dataIn, string? description, DataBlob* entropy, IntPtr reserved, IntPtr prompt, int flags, DataBlob* dataOut);

    [LibraryImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CryptUnprotectData(DataBlob* dataIn, IntPtr description, DataBlob* entropy, IntPtr reserved, IntPtr prompt, int flags, DataBlob* dataOut);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr LocalFree(IntPtr memory);
}
