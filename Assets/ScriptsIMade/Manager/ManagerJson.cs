using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// A small strict JSON reader for the manager's situation files (plain C#, no Unity: the validator also runs outside
/// Unity). Keeps the order of object fields (situation params are worked out in the order they are written) and the line
/// each value starts on, so the validator can say "Finance.json line 212: unknown effect 'monye'".
/// </summary>
public static class ManagerJson
{
    public enum Kind { Null, Bool, Number, String, Array, Object }

    /// <summary>One JSON value.</summary>
    public sealed class Node
    {
        public Kind Type;
        public bool Bool;
        public double Number;
        public string Str;
        public List<Node> Items;
        public List<KeyValuePair<string, Node>> Fields;
        /// <summary>1-based line where the value starts.</summary>
        public int Line;

        public bool IsNull => Type == Kind.Null;
        public bool IsObject => Type == Kind.Object;
        public bool IsArray => Type == Kind.Array;
        public bool IsString => Type == Kind.String;
        public bool IsNumber => Type == Kind.Number;
        public bool IsBool => Type == Kind.Bool;

        /// <summary>The field's value, or null when this isn't an object or has no such field.</summary>
        public Node Get(string key)
        {
            if (Type != Kind.Object || Fields == null) return null;
            for (int i = 0; i < Fields.Count; i++)
                if (Fields[i].Key == key) return Fields[i].Value;
            return null;
        }

        public bool Has(string key) => Get(key) != null;

        public string GetString(string key, string fallback = null)
        {
            Node n = Get(key);
            return n != null && n.Type == Kind.String ? n.Str : fallback;
        }

        public double GetNumber(string key, double fallback)
        {
            Node n = Get(key);
            return n != null && n.Type == Kind.Number ? n.Number : fallback;
        }

        public int GetInt(string key, int fallback)
        {
            Node n = Get(key);
            if (n == null || n.Type != Kind.Number) return fallback;
            double v = Math.Round(n.Number);
            if (v > int.MaxValue) return int.MaxValue;
            if (v < int.MinValue) return int.MinValue;
            return (int)v;
        }

        public bool GetBool(string key, bool fallback)
        {
            Node n = Get(key);
            return n != null && n.Type == Kind.Bool ? n.Bool : fallback;
        }

        /// <summary>Items of an array field (empty when missing or not an array).</summary>
        public List<Node> GetArray(string key)
        {
            Node n = Get(key);
            return n != null && n.Type == Kind.Array && n.Items != null ? n.Items : Empty;
        }

        /// <summary>A string field or an array of strings, as a list (empty when missing).</summary>
        public List<string> GetStrings(string key)
        {
            var list = new List<string>();
            Node n = Get(key);
            if (n == null) return list;
            if (n.Type == Kind.String) list.Add(n.Str);
            else if (n.Type == Kind.Array && n.Items != null)
                foreach (Node item in n.Items)
                    if (item != null && item.Type == Kind.String) list.Add(item.Str);
            return list;
        }

        public override string ToString()
        {
            switch (Type)
            {
                case Kind.Null: return "null";
                case Kind.Bool: return Bool ? "true" : "false";
                case Kind.Number: return Number.ToString("R", CultureInfo.InvariantCulture);
                case Kind.String: return "\"" + Str + "\"";
                case Kind.Array: return "[" + (Items != null ? Items.Count : 0) + " items]";
                default: return "{" + (Fields != null ? Fields.Count : 0) + " fields}";
            }
        }
    }

    static readonly List<Node> Empty = new List<Node>();

    /// <summary>Parse <paramref name="text"/>. Null with <paramref name="error"/> ("line 12: expected ':'") when it isn't valid JSON.</summary>
    public static Node Parse(string text, out string error)
    {
        error = null;
        if (text == null)
        {
            error = "no text";
            return null;
        }
        var reader = new Reader(text);
        try
        {
            reader.SkipSpace();
            Node root = reader.ReadValue(0);
            reader.SkipSpace();
            if (!reader.AtEnd) throw reader.Fail("unexpected text after the end of the JSON");
            return root;
        }
        catch (FormatException e)
        {
            error = e.Message;
            return null;
        }
    }

    sealed class Reader
    {
        readonly string s;
        int pos;
        int line = 1;
        const int MaxDepth = 64;

        public Reader(string text)
        {
            s = text;
            // A UTF-8 byte order mark read as text
            if (s.Length > 0 && s[0] == '﻿') pos = 1;
        }

        public bool AtEnd => pos >= s.Length;

        public FormatException Fail(string message)
        {
            return new FormatException("line " + line + ": " + message);
        }

        public void SkipSpace()
        {
            while (pos < s.Length)
            {
                char c = s[pos];
                if (c == '\n') { line++; pos++; }
                else if (c == ' ' || c == '\t' || c == '\r') pos++;
                else break;
            }
        }

        public Node ReadValue(int depth)
        {
            if (depth > MaxDepth) throw Fail("nested too deeply");
            if (AtEnd) throw Fail("unexpected end of the text");
            char c = s[pos];
            int startLine = line;
            switch (c)
            {
                case '{': return ReadObject(depth, startLine);
                case '[': return ReadArray(depth, startLine);
                case '"': return new Node { Type = Kind.String, Str = ReadString(), Line = startLine };
                case 't': Expect("true"); return new Node { Type = Kind.Bool, Bool = true, Line = startLine };
                case 'f': Expect("false"); return new Node { Type = Kind.Bool, Bool = false, Line = startLine };
                case 'n': Expect("null"); return new Node { Type = Kind.Null, Line = startLine };
                default:
                    if (c == '-' || (c >= '0' && c <= '9')) return new Node { Type = Kind.Number, Number = ReadNumber(), Line = startLine };
                    throw Fail("unexpected character '" + c + "'");
            }
        }

        void Expect(string word)
        {
            if (string.CompareOrdinal(s, pos, word, 0, word.Length) != 0) throw Fail("expected '" + word + "'");
            pos += word.Length;
        }

        Node ReadObject(int depth, int startLine)
        {
            var node = new Node { Type = Kind.Object, Fields = new List<KeyValuePair<string, Node>>(), Line = startLine };
            pos++; // {
            SkipSpace();
            if (!AtEnd && s[pos] == '}') { pos++; return node; }
            var seen = new HashSet<string>();
            while (true)
            {
                SkipSpace();
                if (AtEnd || s[pos] != '"') throw Fail("expected a field name in quotes");
                string key = ReadString();
                if (!seen.Add(key)) throw Fail("field \"" + key + "\" appears twice");
                SkipSpace();
                if (AtEnd || s[pos] != ':') throw Fail("expected ':' after \"" + key + "\"");
                pos++;
                SkipSpace();
                Node value = ReadValue(depth + 1);
                node.Fields.Add(new KeyValuePair<string, Node>(key, value));
                SkipSpace();
                if (AtEnd) throw Fail("unexpected end of the text inside an object");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == '}') { pos++; return node; }
                throw Fail("expected ',' or '}' after the value of \"" + key + "\"");
            }
        }

        Node ReadArray(int depth, int startLine)
        {
            var node = new Node { Type = Kind.Array, Items = new List<Node>(), Line = startLine };
            pos++; // [
            SkipSpace();
            if (!AtEnd && s[pos] == ']') { pos++; return node; }
            while (true)
            {
                SkipSpace();
                node.Items.Add(ReadValue(depth + 1));
                SkipSpace();
                if (AtEnd) throw Fail("unexpected end of the text inside an array");
                if (s[pos] == ',') { pos++; continue; }
                if (s[pos] == ']') { pos++; return node; }
                throw Fail("expected ',' or ']' in an array");
            }
        }

        string ReadString()
        {
            pos++; // opening quote
            var sb = new StringBuilder();
            while (true)
            {
                if (AtEnd) throw Fail("unterminated string");
                char c = s[pos++];
                if (c == '"') return sb.ToString();
                if (c == '\n') throw Fail("line break inside a string (write \\n)");
                if (c != '\\')
                {
                    sb.Append(c);
                    continue;
                }
                if (AtEnd) throw Fail("unterminated escape");
                char e = s[pos++];
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (pos + 4 > s.Length) throw Fail("bad \\u escape");
                        if (!int.TryParse(s.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            throw Fail("bad \\u escape");
                        sb.Append((char)code);
                        pos += 4;
                        break;
                    default: throw Fail("unknown escape \\" + e);
                }
            }
        }

        double ReadNumber()
        {
            int start = pos;
            if (s[pos] == '-') pos++;
            while (pos < s.Length)
            {
                char c = s[pos];
                if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') pos++;
                else break;
            }
            string token = s.Substring(start, pos - start);
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                double.IsNaN(value) || double.IsInfinity(value))
                throw Fail("bad number '" + token + "'");
            return value;
        }
    }
}
