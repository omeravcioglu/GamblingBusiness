using System.Collections.Generic;

/// <summary>
/// The crisis lines for Crisis.csv (Tools > Game > Localization > Sync Content CSVs): MiniGameManager's business problems in
/// array order, each one's line and its result lines when solved and when failed, under MiniGameManager.ProblemKey(i) +
/// ".text" / ".solved" / ".failed" (= "problem." + L10n.Slug(problem)); MiniGameManager.ProblemText / OutcomeLine read them.
/// The table's "crisis." rows (the games' names and screens, result tags, status lines, units...) are hand-written: the sync
/// leaves them alone (it only owns the "problem." keys).
/// </summary>
public sealed class CrisisTextSource : IContentTextSource
{
    public string Table => CrisisText.Table;

    public IEnumerable<ContentText> Collect()
    {
        for (int i = 0; i < MiniGameManager.ProblemCount; i++)
        {
            string key = MiniGameManager.ProblemKey(i);
            string problem = MiniGameManager.EnglishProblem(i);
            if (key == null || problem.Length == 0) continue;
            yield return new ContentText(key + ".text", ProblemContext, problem);
            string solved = MiniGameManager.EnglishOutcome(i, true);
            if (solved.Length > 0) yield return new ContentText(key + ".solved", SolvedContext(problem), solved);
            string failed = MiniGameManager.EnglishOutcome(i, false);
            if (failed.Length > 0) yield return new ContentText(key + ".failed", FailedContext(problem), failed);
        }
    }

    const string ProblemContext =
        "Crisis line (a business problem) on the \"Crisis incoming\" card (44 px bold, 2 lines in 600 px, shrinks to 30 px) " +
        "and on the CRT's pink sticky (26 px, wraps in 200 px). An urgent office disaster, then an order: punchy deadpan " +
        "satire, transcreate. Max ~60 characters (CJK ~26, ko ~32).";

    static string SolvedContext(string problem)
    {
        return "Result line after the player solved the crisis “" + problem + "”: one line on the CRT under CRISIS SOLVED " +
               "(32 px, 1380 px wide). A short dry joke: transcreate. Max ~40 characters (CJK ~20, ko ~24).";
    }

    static string FailedContext(string problem)
    {
        return "Result line after the crisis “" + problem + "” failed, timed out or was given up: one line on the CRT under " +
               "CRISIS FAILED (32 px, 1380 px wide). A short dry joke: transcreate. Max ~40 characters (CJK ~20, ko ~24).";
    }
}
