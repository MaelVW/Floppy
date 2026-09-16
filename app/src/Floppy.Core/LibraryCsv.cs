using System.Text;

namespace Floppy.Core;

/// <summary>Ein Eintrag in library.csv (Spalten wie in V1).</summary>
public sealed record LibraryEntry(string Label, string Kind, string Value, string Added, string Notes = "");

/// <summary>
/// Liest und schreibt library.csv kompatibel zu PowerShell Import-Csv/Export-Csv (V1):
/// alle Felder in Anfuehrungszeichen, "" als Escape, UTF-8 mit BOM, CRLF.
/// </summary>
public static class LibraryCsv
{
    public static readonly string[] Columns = ["Label", "Kind", "Value", "Added", "Notes"];
    private static readonly Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    public static IReadOnlyList<LibraryEntry> Read(string path)
    {
        if (!File.Exists(path)) return [];
        var rows = Parse(File.ReadAllText(path, Encoding.UTF8));
        if (rows.Count == 0) return [];

        var header = rows[0].Select(h => h.Trim()).ToList();
        int Col(string name) => header.FindIndex(h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        int iLabel = Col("Label"), iKind = Col("Kind"), iValue = Col("Value"), iAdded = Col("Added"), iNotes = Col("Notes");
        string Cell(List<string> r, int i) => i >= 0 && i < r.Count ? r[i] : string.Empty;

        return rows.Skip(1)
            .Where(r => r.Any(c => c.Length > 0))
            .Select(r => new LibraryEntry(Cell(r, iLabel), Cell(r, iKind), Cell(r, iValue), Cell(r, iAdded), Cell(r, iNotes)))
            .ToList();
    }

    public static void Write(string path, IEnumerable<LibraryEntry> entries)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(",", Columns.Select(Quote))).Append("\r\n");
        foreach (var e in entries)
            sb.Append(string.Join(",", new[] { e.Label, e.Kind, e.Value, e.Added, e.Notes }.Select(Quote))).Append("\r\n");
        File.WriteAllText(path, sb.ToString(), FileEncoding);
    }

    /// <summary>Eintrag hinzufuegen; gleicher Kind+Value (ohne Gross-/Kleinschreibung) wird ersetzt statt doppelt.</summary>
    public static IReadOnlyList<LibraryEntry> Upsert(IEnumerable<LibraryEntry> entries, LibraryEntry entry)
    {
        var list = entries
            .Where(e => !(string.Equals(e.Kind, entry.Kind, StringComparison.OrdinalIgnoreCase) &&
                          string.Equals(e.Value, entry.Value, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        list.Add(entry);
        return list;
    }

    private static string Quote(string? s) => "\"" + (s ?? string.Empty).Replace("\"", "\"\"") + "\"";

    /// <summary>RFC-4180-Parser: Anfuehrungszeichen, "" und Zeilenumbrueche in Feldern.</summary>
    internal static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var i = 0;
        if (text.Length > 0 && text[0] == '﻿') i = 1;

        for (; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"': inQuotes = true; break;
                case ',': row.Add(field.ToString()); field.Clear(); break;
                case '\r': break;
                case '\n':
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row); row = [];
                    break;
                default: field.Append(c); break;
            }
        }
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }
        return rows;
    }
}
