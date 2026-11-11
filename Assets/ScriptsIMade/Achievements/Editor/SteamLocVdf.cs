using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>A node of a Valve KeyValues ("VDF") text: "key" "value", or "key" { children }.</summary>
public sealed class VdfNode
{
    public string Key;
    /// <summary>The value of a "key" "value" pair; null for a block.</summary>
    public string Value;
    /// <summary>The children of a block; null for a pair.</summary>
    public List<VdfNode> Children;

    public bool IsBlock => Children != null;

    public static VdfNode Block(string key) => new VdfNode { Key = key, Children = new List<VdfNode>() };
    public static VdfNode Pair(string key, string value) => new VdfNode { Key = key, Value = value ?? "" };

    /// <summary>The first child with this key (case-insensitive), or null.</summary>
    public VdfNode Find(string key)
    {
        if (Children == null) return null;
        foreach (VdfNode child in Children)
            if (string.Equals(child.Key, key, StringComparison.OrdinalIgnoreCase)) return child;
        return null;
    }

    /// <summary>The child block with this key, created at the end when missing.</summary>
    public VdfNode FindOrAddBlock(string key)
    {
        VdfNode found = Find(key);
        if (found != null && found.IsBlock) return found;
        VdfNode block = Block(key);
        Children.Add(block);
        return block;
    }

    /// <summary>Set a "key" "value" pair (replaced in place when it exists, else added at the end).</summary>
    public void Set(string key, string value)
    {
        VdfNode found = Find(key);
        if (found != null && !found.IsBlock)
        {
            found.Key = key;
            found.Value = value ?? "";
            return;
        }
        Children.Add(Pair(key, value));
    }
}

/// <summary>
/// Fills the Steamworks achievement localization file with our translations (Tools > Game > Achievements > Fill Steamworks
/// Localization...). Steamworks names each achievement's texts with tokens it makes up when the achievement is created
/// (NEW_ACHIEVEMENT_1_0_NAME / _DESC), so this reads the file Steamworks exported, matches every English NAME value to an
/// achievement's English name (its DESC value when the name doesn't match), and writes the schinese / japanese / koreana /
/// turkish blocks from Achievements.csv under the same tokens. Plain C# (no Unity): the editor menu wraps it.
/// Understands both layouts Steamworks uses: "lang" { "english" { "Tokens" {...} } ...} (all languages in one file) and
/// "lang" { "Language" "english" "Tokens" {...} } (one language per file; the output then holds every language in the
/// first layout, and FillPerLanguage writes single-language files too).
/// </summary>
public static class SteamLocVdf
{
    /// <summary>Steam's language names and the Achievements.csv columns that fill them.</summary>
    public static readonly KeyValuePair<string, string>[] Languages =
    {
        new KeyValuePair<string, string>("schinese", "zh-Hans"),
        new KeyValuePair<string, string>("japanese", "ja"),
        new KeyValuePair<string, string>("koreana", "ko"),
        new KeyValuePair<string, string>("turkish", "tr")
    };

    /// <summary>One achievement's texts: English and, per CSV column, the translation.</summary>
    public sealed class Entry
    {
        public string ApiName;
        public string EnglishName;
        public string EnglishDescription;
        public readonly Dictionary<string, string> Names = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>What Fill did.</summary>
    public sealed class Result
    {
        /// <summary>The filled file (null when the input couldn't be used).</summary>
        public string Output;
        /// <summary>Single-language files per Steam language (only for a one-language input), else empty.</summary>
        public readonly Dictionary<string, string> PerLanguage = new Dictionary<string, string>(StringComparer.Ordinal);
        public string Error;
        public int TokenPairs;
        public int Matched;
        public readonly List<string> Report = new List<string>();
        public readonly List<string> UnmatchedTokens = new List<string>();
        public readonly List<string> AchievementsNotInFile = new List<string>();
    }

    // ------------------------------------------------------------------
    // Filling
    // ------------------------------------------------------------------

    public static Result Fill(string vdfText, IReadOnlyList<Entry> entries)
    {
        var result = new Result();
        VdfNode root;
        try { root = Parse(vdfText); }
        catch (FormatException e)
        {
            result.Error = "The file isn't a readable VDF: " + e.Message;
            return result;
        }

        VdfNode englishHolder = FindEnglish(root, out VdfNode lang, out bool perLanguageFile);
        VdfNode englishTokens = englishHolder?.Find("Tokens");
        if (englishTokens == null || !englishTokens.IsBlock)
        {
            result.Error = "No English \"Tokens\" block found. Export the localization file from Steamworks' achievements page (after the achievements exist) and pick that file.";
            return result;
        }

        // Token pairs: NEW_ACHIEVEMENT_1_0_NAME + NEW_ACHIEVEMENT_1_0_DESC share the base NEW_ACHIEVEMENT_1_0
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var descs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        var inOrder = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nameTokenCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var descTokenCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (VdfNode token in englishTokens.Children)
        {
            if (token.IsBlock || string.IsNullOrEmpty(token.Key)) continue;
            if (EndsWith(token.Key, "_NAME", out string nameBase))
            {
                names[nameBase] = token.Value;
                nameTokenCase[nameBase] = token.Key;
                if (inOrder.Add(nameBase)) order.Add(nameBase);
            }
            else if (EndsWith(token.Key, "_DESC", out string descBase))
            {
                descs[descBase] = token.Value;
                descTokenCase[descBase] = token.Key;
                if (inOrder.Add(descBase)) order.Add(descBase);
            }
        }
        result.TokenPairs = order.Count;

        var byName = new Dictionary<string, Entry>(StringComparer.Ordinal);
        var byDesc = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (Entry e in entries)
        {
            string n = Normalize(e.EnglishName);
            string d = Normalize(e.EnglishDescription);
            if (n.Length > 0 && !byName.ContainsKey(n)) byName[n] = e;
            if (d.Length > 0 && !byDesc.ContainsKey(d)) byDesc[d] = e;
        }

        var matches = new List<KeyValuePair<string, Entry>>();
        var matchedEntries = new HashSet<Entry>();
        foreach (string tokenBase in order)
        {
            names.TryGetValue(tokenBase, out string nameValue);
            descs.TryGetValue(tokenBase, out string descValue);
            Entry entry = null;
            string how = "";
            if (nameValue != null && byName.TryGetValue(Normalize(nameValue), out entry)) how = "name";
            else if (descValue != null && byDesc.TryGetValue(Normalize(descValue), out entry)) how = "description";
            if (entry == null)
            {
                result.UnmatchedTokens.Add(tokenBase + ": \"" + (nameValue ?? "") + "\"");
                continue;
            }
            if (!matchedEntries.Add(entry))
            {
                result.UnmatchedTokens.Add(tokenBase + ": \"" + (nameValue ?? "") + "\" (" + entry.ApiName + " was already matched by another token)");
                continue;
            }
            matches.Add(new KeyValuePair<string, Entry>(tokenBase, entry));
            result.Report.Add(tokenBase + " = " + entry.ApiName + " (by " + how + ")");
        }
        result.Matched = matches.Count;
        foreach (Entry e in entries)
            if (!matchedEntries.Contains(e)) result.AchievementsNotInFile.Add(e.ApiName + " \"" + e.EnglishName + "\"");

        string TokenName(string tokenBase) => nameTokenCase.TryGetValue(tokenBase, out string t) ? t : tokenBase + "_NAME";
        string TokenDesc(string tokenBase) => descTokenCase.TryGetValue(tokenBase, out string t) ? t : tokenBase + "_DESC";

        if (perLanguageFile)
        {
            // One language per file: write every language in the all-languages layout, plus a file per language
            VdfNode outRoot = VdfNode.Block("");
            VdfNode outLang = outRoot.FindOrAddBlock("lang");
            VdfNode english = outLang.FindOrAddBlock("english");
            english.Children.Add(Clone(englishTokens));
            foreach (KeyValuePair<string, string> language in Languages)
            {
                VdfNode tokens = outLang.FindOrAddBlock(language.Key).FindOrAddBlock("Tokens");
                FillTokens(tokens, matches, language.Value, TokenName, TokenDesc);

                VdfNode single = VdfNode.Block("");
                VdfNode singleLang = single.FindOrAddBlock("lang");
                singleLang.Set("Language", language.Key);
                FillTokens(singleLang.FindOrAddBlock("Tokens"), matches, language.Value, TokenName, TokenDesc);
                result.PerLanguage[language.Key] = Write(single);
            }
            result.Output = Write(outRoot);
        }
        else
        {
            foreach (KeyValuePair<string, string> language in Languages)
            {
                VdfNode tokens = lang.FindOrAddBlock(language.Key).FindOrAddBlock("Tokens");
                FillTokens(tokens, matches, language.Value, TokenName, TokenDesc);
            }
            result.Output = Write(root);
        }
        return result;
    }

    static void FillTokens(VdfNode tokens, List<KeyValuePair<string, Entry>> matches, string column,
                           Func<string, string> nameToken, Func<string, string> descToken)
    {
        foreach (KeyValuePair<string, Entry> match in matches)
        {
            if (match.Value.Names.TryGetValue(column, out string name) && !string.IsNullOrWhiteSpace(name))
                tokens.Set(nameToken(match.Key), name);
            if (match.Value.Descriptions.TryGetValue(column, out string desc) && !string.IsNullOrWhiteSpace(desc))
                tokens.Set(descToken(match.Key), desc);
        }
    }

    /// <summary>
    /// The block holding the English "Tokens": "lang" &gt; "english" (all languages in one file, perLanguageFile = false,
    /// <paramref name="lang"/> = the "lang" block) or a block with "Language" "english" (perLanguageFile = true).
    /// </summary>
    static VdfNode FindEnglish(VdfNode root, out VdfNode lang, out bool perLanguageFile)
    {
        lang = null;
        perLanguageFile = false;
        var queue = new Queue<VdfNode>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            VdfNode node = queue.Dequeue();
            if (!node.IsBlock) continue;
            VdfNode language = node.Find("Language");
            if (language != null && !language.IsBlock && string.Equals(language.Value?.Trim(), "english", StringComparison.OrdinalIgnoreCase) &&
                node.Find("Tokens") != null)
            {
                perLanguageFile = true;
                lang = node;
                return node;
            }
            VdfNode english = node.Find("english");
            if (english != null && english.IsBlock && english.Find("Tokens") != null)
            {
                lang = node;
                return english;
            }
            foreach (VdfNode child in node.Children) queue.Enqueue(child);
        }
        return null;
    }

    static VdfNode Clone(VdfNode node)
    {
        if (!node.IsBlock) return VdfNode.Pair(node.Key, node.Value);
        VdfNode copy = VdfNode.Block(node.Key);
        foreach (VdfNode child in node.Children) copy.Children.Add(Clone(child));
        return copy;
    }

    static bool EndsWith(string key, string suffix, out string tokenBase)
    {
        tokenBase = null;
        if (key.Length <= suffix.Length || !key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
        tokenBase = key.Substring(0, key.Length - suffix.Length);
        return true;
    }

    /// <summary>
    /// How texts are compared: case, spacing, typographic quotes and dashes and a final full stop don't matter (what the
    /// owner typed into Steamworks may differ from the CSV in those).
    /// </summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var b = new StringBuilder(text.Length);
        bool space = false;
        foreach (char raw in text.Normalize(NormalizationForm.FormKC))
        {
            char c = raw;
            switch (c)
            {
                case '\u2018': case '\u2019': case '\u02BC': case '`': c = '\''; break;
                case '\u201C': case '\u201D': c = '"'; break;
                case '\u2013': case '\u2014': case '\u2212': c = '-'; break;
                case '\u00A0': c = ' '; break;
            }
            if (char.IsWhiteSpace(c))
            {
                space = b.Length > 0;
                continue;
            }
            if (space) b.Append(' ');
            space = false;
            b.Append(char.ToLowerInvariant(c));
        }
        while (b.Length > 0 && (b[b.Length - 1] == '.' || b[b.Length - 1] == '!')) b.Length--;
        return b.ToString();
    }

    // ------------------------------------------------------------------
    // KeyValues text
    // ------------------------------------------------------------------

    /// <summary>Parse KeyValues text into a root block (key ""). Throws FormatException on broken text.</summary>
    public static VdfNode Parse(string text)
    {
        if (text == null) throw new FormatException("no text");
        int pos = 0;
        if (text.Length > 0 && text[0] == '\uFEFF') pos = 1;
        VdfNode root = VdfNode.Block("");
        var stack = new Stack<VdfNode>();
        stack.Push(root);
        string pendingKey = null;
        while (true)
        {
            SkipSpaceAndComments(text, ref pos);
            if (pos >= text.Length) break;
            char c = text[pos];
            if (c == '{')
            {
                pos++;
                if (pendingKey == null) throw new FormatException("a '{' without a key at " + LineOf(text, pos));
                VdfNode block = VdfNode.Block(pendingKey);
                stack.Peek().Children.Add(block);
                stack.Push(block);
                pendingKey = null;
                continue;
            }
            if (c == '}')
            {
                pos++;
                if (pendingKey != null) throw new FormatException("key \"" + pendingKey + "\" has no value at " + LineOf(text, pos));
                if (stack.Count <= 1) throw new FormatException("an extra '}' at " + LineOf(text, pos));
                stack.Pop();
                continue;
            }
            string token = ReadToken(text, ref pos);
            // Conditionals like [$WIN32] after a value are skipped
            SkipSpaceAndComments(text, ref pos);
            if (pos < text.Length && text[pos] == '[')
            {
                int end = text.IndexOf(']', pos);
                pos = end < 0 ? text.Length : end + 1;
            }
            if (pendingKey == null)
            {
                pendingKey = token;
            }
            else
            {
                stack.Peek().Children.Add(VdfNode.Pair(pendingKey, token));
                pendingKey = null;
            }
        }
        if (pendingKey != null) throw new FormatException("key \"" + pendingKey + "\" has no value at the end");
        if (stack.Count != 1) throw new FormatException("a block isn't closed ('}' missing)");
        return root;
    }

    static void SkipSpaceAndComments(string text, ref int pos)
    {
        while (pos < text.Length)
        {
            char c = text[pos];
            if (char.IsWhiteSpace(c) || c == '\uFEFF') { pos++; continue; }
            if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
            {
                while (pos < text.Length && text[pos] != '\n') pos++;
                continue;
            }
            break;
        }
    }

    static string ReadToken(string text, ref int pos)
    {
        var b = new StringBuilder();
        if (text[pos] == '"')
        {
            pos++;
            while (pos < text.Length)
            {
                char c = text[pos++];
                if (c == '"') return b.ToString();
                if (c == '\\' && pos < text.Length)
                {
                    char e = text[pos++];
                    switch (e)
                    {
                        case 'n': b.Append('\n'); break;
                        case 't': b.Append('\t'); break;
                        case '\\': b.Append('\\'); break;
                        case '"': b.Append('"'); break;
                        default: b.Append('\\').Append(e); break;
                    }
                    continue;
                }
                b.Append(c);
            }
            throw new FormatException("a quoted text isn't closed");
        }
        while (pos < text.Length)
        {
            char c = text[pos];
            if (char.IsWhiteSpace(c) || c == '{' || c == '}' || c == '"') break;
            b.Append(c);
            pos++;
        }
        if (b.Length == 0) throw new FormatException("unexpected '" + text[pos] + "' at " + LineOf(text, pos));
        return b.ToString();
    }

    static string LineOf(string text, int pos)
    {
        int line = 1;
        for (int i = 0; i < pos && i < text.Length; i++)
            if (text[i] == '\n') line++;
        return "line " + line.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>KeyValues text of a root block: tabs for indentation, every key and value quoted, CRLF line ends.</summary>
    public static string Write(VdfNode root)
    {
        var b = new StringBuilder();
        foreach (VdfNode child in root.Children) WriteNode(b, child, 0);
        return b.ToString();
    }

    static void WriteNode(StringBuilder b, VdfNode node, int depth)
    {
        b.Append('\t', depth).Append('"').Append(Escape(node.Key)).Append('"');
        if (!node.IsBlock)
        {
            b.Append("\t\"").Append(Escape(node.Value)).Append("\"\r\n");
            return;
        }
        b.Append("\r\n").Append('\t', depth).Append("{\r\n");
        foreach (VdfNode child in node.Children) WriteNode(b, child, depth + 1);
        b.Append('\t', depth).Append("}\r\n");
    }

    static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n").Replace("\t", "\\t");
    }
}
