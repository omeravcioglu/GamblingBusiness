using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Reads the localization sources in Assets/Localization/Source/*.csv (RFC 4180): UTF-8 with or without a BOM, cells
/// separated by commas, a cell in double quotes may hold commas, line breaks and doubled quotes (""), records end with
/// CRLF or LF. Cell text is kept exactly as written (no trimming). Used by the Editor importer and, in the Editor only,
/// by L10n's English fallback before the tables exist.
/// </summary>
public static class L10nCsv
{
    /// <summary>A parsed file: the first row as the header, the rest as rows.</summary>
    public sealed class Sheet
    {
        public readonly List<string> Header;
        public readonly List<List<string>> Rows;

        public Sheet(List<string> header, List<List<string>> rows)
        {
            Header = header ?? new List<string>();
            Rows = rows ?? new List<List<string>>();
        }

        /// <summary>Index of the header cell with this name (trimmed, case-insensitive), or -1.</summary>
        public int Column(string name)
        {
            for (int i = 0; i < Header.Count; i++)
            {
                if (string.Equals(Header[i].Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        /// <summary>The cell of <paramref name="row"/> in column <paramref name="column"/> ("" when the row is shorter).</summary>
        public static string Cell(List<string> row, int column)
        {
            return row != null && column >= 0 && column < row.Count ? row[column] ?? "" : "";
        }
    }

    /// <summary>Parse a whole file into a header and rows.</summary>
    public static Sheet ReadSheet(string text)
    {
        List<List<string>> rows = Parse(text);
        if (rows.Count == 0) return new Sheet(new List<string>(), new List<List<string>>());
        List<string> header = rows[0];
        rows.RemoveAt(0);
        return new Sheet(header, rows);
    }

    /// <summary>Parse CSV text into rows of cells.</summary>
    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        if (string.IsNullOrEmpty(text)) return rows;

        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;     // inside a quoted section
        bool started = false;    // the current cell has content (or an opening quote)
        int i = text[0] == '﻿' ? 1 : 0;
        int n = text.Length;

        while (i < n)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < n && text[i + 1] == '"')
                    {
                        cell.Append('"');   // "" = one quote
                        i += 2;
                        continue;
                    }
                    quoted = false;         // the closing quote
                    i++;
                    continue;
                }
                cell.Append(c);             // commas and line breaks are text inside quotes
                i++;
                continue;
            }

            if (c == '"' && !started)
            {
                quoted = true;              // a cell that starts with a quote is a quoted cell
                started = true;
                i++;
                continue;
            }
            if (c == ',')
            {
                row.Add(cell.ToString());
                cell.Clear();
                started = false;
                i++;
                continue;
            }
            if (c == '\r' || c == '\n')
            {
                row.Add(cell.ToString());
                cell.Clear();
                started = false;
                rows.Add(row);
                row = new List<string>();
                if (c == '\r' && i + 1 < n && text[i + 1] == '\n') i++;
                i++;
                continue;
            }
            cell.Append(c);
            started = true;
            i++;
        }

        // The last record, when the file doesn't end with a line break
        if (started || cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Line breaks inside a cell as "\n" (a cell saved on Windows may hold "\r\n").</summary>
    public static string NormalizeNewlines(string value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? "";
        return value.IndexOf('\r') < 0 ? value : value.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    /// <summary>A cell as it must be written: quoted when it holds a comma, quote, line break or edge spaces.</summary>
    public static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        bool needs = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 || value[0] == ' ' || value[value.Length - 1] == ' ';
        return needs ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }
}
