/// <summary>
/// The crisis mini-games' texts, from the String Table "Crisis" (Assets/Localization/Source/Crisis.csv):
/// - Hand-written texts ("crisis." keys: result lines and tags, status lines, units, the CRT's labels) come through Get
///   (L10n.Get: the CSV holds their English).
/// - Content whose English is game data shows through accessors that fall back to that English: the business problems
///   and their result lines (MiniGameManager's arrays, "problem." keys: MiniGameManager.ProblemText / OutcomeLine) and
///   the games' names (CrtScreenView.gameTitle / MiniGameBase.gameName: GameName). English always shows the data itself.
///   The Editor's CrisisTextSource keeps the "problem." rows of Crisis.csv in step with the arrays
///   (Tools > Game > Localization > Sync Content CSVs); the "crisis." rows are hand-written.
/// - A game's texts are keyed "crisis.game." + L10n.Slug(its English name) + "." + part: both spellings of a name
///   ("Stop the Bar" on the prefab, "Stop The Bar" in the script) give "crisis.game.stop_the_bar".
/// Every text is written in its final case (capitals in the CSV). Only names are upper-cased in code (L10n.ToUpper).
/// </summary>
public static class CrisisText
{
    /// <summary>The String Table / CSV name.</summary>
    public const string Table = "Crisis";

    /// <summary>A hand-written entry of the Crisis table in the current language.</summary>
    public static string Get(string key) => L10n.Get(Table, key);

    /// <summary>A hand-written entry with named placeholders: Get("crisis.result.it_was", ("count", 7)).</summary>
    public static string Get(string key, params (string name, object value)[] args) => L10n.Get(Table, key, args);

    /// <summary>Key prefix of a mini-game's texts: "crisis.game." + L10n.Slug(englishName); "" for a name without letters.</summary>
    public static string GameKey(string englishName)
    {
        string slug = L10n.Slug(englishName);
        return slug.Length == 0 ? "" : "crisis.game." + slug;
    }

    /// <summary>
    /// A mini-game's name in the current language ("crisis.game.&lt;id&gt;.name"), else the English name given. English shows
    /// the name exactly as given (the prefab's gameTitle, or the script's gameName).
    /// </summary>
    public static string GameName(string englishName) => GameText(englishName, "name", englishName);

    /// <summary>One of a mini-game's texts in the current language ("crisis.game.&lt;id&gt;." + part), else english.</summary>
    public static string GameText(string englishName, string part, string english)
    {
        if (english == null) english = "";
        if (english.Length == 0 || L10n.CurrentCode == L10n.English) return english;
        string key = GameKey(englishName);
        return key.Length == 0 ? english : L10n.GetOr(Table, key + "." + part, english);
    }

    /// <summary>
    /// How a crisis ended: "crisis.result." + id (the line the old panels show big: "TIME'S UP!") and its ".tag" (the
    /// CRT's result tag: "TIME'S UP"), each written in its final case. Ids: success, failed, time_up, gave_up, missed,
    /// too_slow, wrong_color... (see Crisis.csv).
    /// </summary>
    public static CrisisResult Result(string id, params (string name, object value)[] args)
    {
        string key = "crisis.result." + id;
        return new CrisisResult(Get(key, args), Get(key + ".tag", args));
    }

    /// <summary>"1.0 s": seconds in the current language ("1.0秒", "1,0 sn"), the number formatted with format.</summary>
    public static string Seconds(float seconds, string format = "0.0")
    {
        return Get("crisis.unit.seconds", ("seconds", seconds.ToString(format, L10n.Culture)));
    }
}

/// <summary>
/// How a crisis ended, in the current language: the line the old panels show ("TIME'S UP!") and the tag of the CRT's
/// result ("TIME'S UP"). Each is its own entry written in its final case: the tag is never derived from the line.
/// </summary>
public readonly struct CrisisResult
{
    public readonly string Message;
    public readonly string Tag;

    public CrisisResult(string message, string tag)
    {
        Message = message ?? "";
        Tag = tag ?? "";
    }
}
