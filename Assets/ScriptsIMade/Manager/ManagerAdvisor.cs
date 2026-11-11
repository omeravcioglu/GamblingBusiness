using System;
using System.Collections.Generic;

/// <summary>
/// Personality recommendations (plain C#). A situation's "recommend" map says which choice each personality recommends;
/// without an entry (or when that choice isn't available) the manager scores the available choices by their tags: the
/// choice's own "tags" plus tags read from its effects (money in/out, risk, people up/down, growth...), weighted by
/// ManagerSchema.TagWeight for the personality. The same tags tell when a choice clashes with the manager's personality.
/// </summary>
public static class ManagerAdvisor
{
    /// <summary>The choice's tags: its own plus what its effects (and outcomes) imply.</summary>
    public static HashSet<string> Tags(ManagerChoiceDef choice)
    {
        var tags = new HashSet<string>();
        if (choice == null) return tags;
        foreach (string t in choice.Tags) tags.Add(t);
        AddEffectTags(choice.Effects, tags);
        foreach (ManagerOutcomeDef o in choice.Outcomes) AddEffectTags(o.Effects, tags);
        if (choice.Outcomes.Count > 1) tags.Add("risk");
        if (choice.Effects.Count == 0 && choice.Outcomes.Count == 0 && !choice.Delegate && !choice.Ask && choice.Tags.Count == 0) tags.Add("safe");
        return tags;
    }

    static void AddEffectTags(List<ManagerEffectDef> effects, HashSet<string> tags)
    {
        foreach (ManagerEffectDef e in effects)
        {
            ManagerJson.Node n = e.Node;
            double sign = Sign(n, e.Op);
            switch (e.Op)
            {
                case "money":
                    tags.Add(sign >= 0 ? "income" : "cost");
                    break;
                case "salary":
                    if (sign >= 0) { tags.Add("people_up"); tags.Add("cost"); }
                    else { tags.Add("people_down"); tags.Add("cut"); }
                    break;
                case "mood":
                case "happiness":
                    tags.Add(sign >= 0 ? "people_up" : "people_down");
                    break;
                case "trust":
                    if (sign > 0) tags.Add("reputation");
                    break;
                case "heat":
                    if (sign > 0) tags.Add("risk");
                    break;
                case "gamble":
                    tags.Add("risk");
                    tags.Add("gamble");
                    foreach (ManagerGambleOutcome o in e.GambleOutcomes) AddEffectTags(o.Effects, tags);
                    break;
                case "stocks":
                    tags.Add("risk");
                    tags.Add("income");
                    break;
                case "fire":
                case "lose":
                    tags.Add("people_down");
                    tags.Add("cut");
                    break;
                case "hire":
                case "build_mode":
                case "build_perk":
                case "skill_points":
                case "train":
                    tags.Add("growth");
                    break;
                case "rent":
                    tags.Add("growth");
                    tags.Add("cost");
                    break;
                case "policy":
                {
                    bool stop = n.GetString("action", "start") == "stop";
                    if (stop) tags.Add("cut");
                    else if (n.Has("cost_day") || n.Has("cost_employee")) tags.Add("cost");
                    double h = n.GetNumber("happiness", 0);
                    if (!stop && h > 0) tags.Add("people_up");
                    if (!stop && h < 0) tags.Add("people_down");
                    break;
                }
                case "revenue_share":
                    tags.Add("cost");
                    break;
                case "delay":
                    tags.Add("stall");
                    break;
            }
        }
    }

    /// <summary>The sign of an effect's main number (add, pct, amount); +1 when it can't be told (a param).</summary>
    static double Sign(ManagerJson.Node n, string op)
    {
        ManagerJson.Node v = n.Get("add") ?? n.Get("pct") ?? n.Get("amount");
        if (v == null) return 1;
        if (v.IsNumber) return Math.Sign(v.Number) == 0 ? 1 : Math.Sign(v.Number);
        if (v.IsString) return v.Str.TrimStart().StartsWith("-") ? -1 : 1;
        return 1;
    }

    /// <summary>How much a personality likes a choice (the sum of its tag weights).</summary>
    public static double Score(string personality, ManagerChoiceDef choice)
    {
        double score = 0;
        foreach (string tag in Tags(choice)) score += ManagerSchema.TagWeight(personality, tag);
        return score;
    }

    /// <summary>
    /// The choice this personality recommends among the available ones (never an ask or delegate choice), or null when there's
    /// nothing to pick from. <paramref name="available"/> null = all.
    /// </summary>
    public static ManagerChoiceDef Recommend(ManagerSituationDef s, string personality, Func<ManagerChoiceDef, bool> available)
    {
        if (s == null) return null;
        bool Ok(ManagerChoiceDef c) => c != null && !c.Ask && !c.Delegate && (available == null || available(c));

        if (personality != null && s.Recommend.TryGetValue(personality, out string id))
        {
            ManagerChoiceDef named = s.Choice(id);
            if (Ok(named)) return named;
        }
        ManagerChoiceDef best = null;
        double bestScore = double.MinValue;
        foreach (ManagerChoiceDef c in s.Choices)
        {
            if (!Ok(c)) continue;
            double score = Score(personality, c);
            if (score > bestScore + 1e-9)
            {
                best = c;
                bestScore = score;
            }
        }
        return best;
    }

    /// <summary>True when a choice goes against the personality (a tag it strongly dislikes).</summary>
    public static bool Clashes(string personality, ManagerChoiceDef choice)
    {
        foreach (string tag in Tags(choice))
            if (ManagerSchema.TagWeight(personality, tag) <= -3) return true;
        return false;
    }
}
