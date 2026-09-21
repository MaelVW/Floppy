using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Floppy.Core;

/// <summary>
/// Ein Knoten im Textformat von Valve (KeyValues, Dateien <c>.vdf</c> und <c>.acf</c>): entweder ein Text
/// (<c>"schluessel" "wert"</c>) oder ein Block (<c>"schluessel" { ... }</c>). Schluessel gelten
/// ohne Beachtung von Gross-/Kleinschreibung - Steam schreibt sie nicht immer gleich.
/// </summary>
public sealed class VdfNode
{
    private readonly List<VdfNode> _children = [];

    internal VdfNode(string key, string? value)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; }

    /// <summary>Der Text; <c>null</c>, wenn der Knoten ein Block ist.</summary>
    public string? Value { get; }

    public bool IsBlock => Value is null;

    public IReadOnlyList<VdfNode> Children => _children;

    internal void Add(VdfNode child) => _children.Add(child);

    /// <summary>Erster Unterknoten mit diesem Schluessel (Gross-/Kleinschreibung egal) oder <c>null</c>.</summary>
    public VdfNode? Find(string key) =>
        _children.FirstOrDefault(c => c.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    /// <summary>Text des ersten Unterknotens mit diesem Schluessel; <c>null</c>, wenn es ihn nicht gibt oder er ein Block ist.</summary>
    public string? GetString(string key) => Find(key)?.Value;
}

/// <summary>
/// Liest das Textformat von Valve. Gebaut fuer die Dateien, die Steam selbst schreibt
/// (<c>libraryfolders.vdf</c>, <c>appmanifest_*.acf</c>) - und dafuer, dass eine kaputte oder halb
/// geschriebene Datei niemals etwas zum Absturz bringt: Wer schlecht formatierten Text liefert, bekommt
/// <c>false</c> zurueck, keine Ausnahme.
/// </summary>
public static class Vdf
{
    /// <summary>Tiefer verschachtelt sich nichts, was Steam schreibt; alles darueber ist Muell (und ein Stapelrisiko).</summary>
    public const int MaxDepth = 32;

    /// <summary>Groesser ist keine Steam-Metadatei. Schutz davor, aus Versehen eine riesige Datei einzulesen.</summary>
    public const int MaxBytes = 4 * 1024 * 1024;

    /// <summary>
    /// Liest den Text. <paramref name="root"/> ist ein namenloser Wurzelknoten, dessen Unterknoten die Paare
    /// oberster Ebene sind (bei Steam-Dateien genau eines: <c>"AppState"</c>, <c>"libraryfolders"</c>).
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out VdfNode? root)
    {
        root = null;
        if (text is null) return false;
        var node = new VdfNode("", null);
        if (!new Parser(text).ReadBlock(node, 0)) return false;
        root = node;
        return true;
    }

    /// <summary>
    /// Liest eine Datei. Sie wird so geoeffnet, dass Steam sie weiter beschreiben darf, waehrend wir lesen.
    /// Jeder Lesefehler (Datei weg, gesperrt, zu gross, kein gueltiger Text) ergibt <c>false</c>.
    /// </summary>
    public static bool TryLoad(string path, [NotNullWhen(true)] out VdfNode? root)
    {
        root = null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > MaxBytes) return false;
            using var reader = new StreamReader(stream, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            return TryParse(reader.ReadToEnd(), out root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private sealed class Parser(string text)
    {
        private enum Kind { Text, Open, Close, End, Bad }

        private int _pos;
        private string _token = "";

        /// <summary>Paare bis zur schliessenden Klammer (Tiefe &gt; 0) bzw. bis zum Textende (Tiefe 0).</summary>
        public bool ReadBlock(VdfNode into, int depth)
        {
            if (depth > MaxDepth) return false;
            while (true)
            {
                switch (Next())
                {
                    case Kind.End: return depth == 0;          // unterwegs endet nur die Datei selbst sauber
                    case Kind.Close: return depth > 0;         // "}" ohne offenen Block ist ein Fehler
                    case Kind.Text: break;
                    default: return false;                     // "{" statt eines Schluessels
                }

                var key = _token;
                switch (Next())
                {
                    case Kind.Text:
                        into.Add(new VdfNode(key, _token));
                        break;
                    case Kind.Open:
                        var block = new VdfNode(key, null);
                        if (!ReadBlock(block, depth + 1)) return false;
                        into.Add(block);
                        break;
                    default:
                        return false;                          // Schluessel ohne Wert
                }
            }
        }

        private Kind Next()
        {
            SkipBlanksAndComments();
            if (_pos >= text.Length) return Kind.End;

            var c = text[_pos];
            switch (c)
            {
                case '{': _pos++; return Kind.Open;
                case '}': _pos++; return Kind.Close;
                case '"': return ReadQuoted() ? Kind.Text : Kind.Bad;
                default: ReadBare(); return Kind.Text;
            }
        }

        private void SkipBlanksAndComments()
        {
            while (_pos < text.Length)
            {
                var c = text[_pos];
                if (c <= ' ' || c == '﻿') _pos++;                                     // Leerraum, auch eine BOM mitten im Text
                else if (c == '/' && _pos + 1 < text.Length && text[_pos + 1] == '/')
                {
                    while (_pos < text.Length && text[_pos] != '\n') _pos++;               // Kommentar bis Zeilenende
                }
                else break;
            }
        }

        /// <summary>Text in Anfuehrungszeichen mit den Escapes, die Steam schreibt (<c>\\</c>, <c>\"</c>, <c>\n</c>, <c>\t</c>).</summary>
        private bool ReadQuoted()
        {
            _pos++;   // oeffnendes "
            var sb = new StringBuilder();
            while (_pos < text.Length)
            {
                var c = text[_pos++];
                if (c == '"')
                {
                    _token = sb.ToString();
                    return true;
                }
                if (c != '\\' || _pos >= text.Length)
                {
                    sb.Append(c);
                    continue;
                }

                var next = text[_pos++];
                switch (next)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '\\': sb.Append('\\'); break;
                    case '"': sb.Append('"'); break;
                    case '\'': sb.Append('\''); break;
                    default: sb.Append('\\').Append(next); break;   // unbekannt (z. B. ein Pfad mit einfachem \): unveraendert lassen
                }
            }
            return false;   // Anfuehrungszeichen nie geschlossen
        }

        /// <summary>Wort ohne Anfuehrungszeichen: bis Leerraum, Anfuehrungszeichen oder Klammer.</summary>
        private void ReadBare()
        {
            var start = _pos;
            while (_pos < text.Length && text[_pos] > ' ' && text[_pos] is not ('"' or '{' or '}')) _pos++;
            _token = text[start.._pos];
        }
    }
}
