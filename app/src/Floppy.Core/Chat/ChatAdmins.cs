namespace Floppy.Core.Chat;

/// <summary>
/// Wer im Chat Admin ist. Massgeblich ist der VOLLE Fingerabdruck (SHA-256 des oeffentlichen
/// Schluessels, 64 Hex-Zeichen), nicht die kurze ID-Nummer: Die ID hat nur 12 Ziffern und liesse
/// sich mit genug Rechenzeit nachbauen, der Fingerabdruck nicht. Jede Nachricht ist mit dem
/// privaten Schluessel unterschrieben - ein Admin-Zeichen oder eine Admin-Sperre kann also nur
/// von der Installation kommen, die diesen Schluessel wirklich besitzt.
///
/// Die Liste steht fest im Programm: Wer sie aendern will, muss ein eigenes Floppy Hub bauen -
/// das gilt dann aber nur fuer ihn selbst, nicht fuer die anderen im Raum.
/// </summary>
public static class ChatAdmins
{
    private static readonly HashSet<string> Fingerprints = new(StringComparer.OrdinalIgnoreCase)
    {
        "D9BCCB824212C6614B5515181DD67D0E8CBC9BAD955F256CEEB60D47E0D85F9C",   // Mael (ID 7439-3925-1417)
    };

    public static IReadOnlyCollection<string> All => Fingerprints;

    public static bool IsAdmin(string? fingerprint) => fingerprint is { Length: 64 } && Fingerprints.Contains(fingerprint);
}
