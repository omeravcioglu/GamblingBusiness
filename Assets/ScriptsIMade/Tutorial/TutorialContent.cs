using UnityEngine;

/// <summary>What a tour step points at on the office HUD.</summary>
public enum TourTarget
{
    None,
    Company,    // the company ledger (value, day, staff, meters)
    Cv,         // the CV on top of the pile
    SkipHire,   // Skip and Hire (or Next batch)
    TaskBoard,  // the hiring-task clipboard
    Tabs        // the toolbar's tabs
}

/// <summary>Where a step's sticky note keeps to, next to its spotlight.</summary>
public enum NoteSide
{
    Right,
    Left,
    Above,
    Below
}

/// <summary>The small picture in some Board memos (the design's interruptions memo).</summary>
public enum MemoExample
{
    None,
    RiskCards,  // three risk cards on the felt
    Crisis,     // an URGENT note on the whiteboard
    Board,      // the Safe / Stretch / Moonshot cards
    Heat        // the heat levels
}

/// <summary>One spotlight step of the first-day tour: its words and where things go (design "First-day tour", 1920 x 1080).</summary>
public sealed class TourStep
{
    public TourTarget target;
    public string title;
    public string body;
    /// <summary>The design's spotlight rectangle (x, y from the top-left, y down) and its corner radius.</summary>
    public Rect designSpot;
    public float radius;
    /// <summary>The design's note position (its top-left corner) and its turn in CSS degrees (positive = clockwise).</summary>
    public Vector2 designNote;
    public float rotation;
    public NoteSide side;
    /// <summary>The arrow's start on the note and its end on the spot: (x fraction, x pixels, y fraction, y pixels).</summary>
    public Vector4 arrowFrom;
    public Vector4 arrowTo;
    /// <summary>How far the arrow bows out (the design's bend, pixels; the sign picks the side).</summary>
    public float bend;
}

/// <summary>The welcome memo of the tour (step 0).</summary>
public sealed class WelcomeCard
{
    public string kicker, to, title, body, show, skip, signed;
}

/// <summary>A Board memo about a feature that just opened.</summary>
public sealed class TutorialMemo
{
    public Feature feature;
    public string kicker, to, title, body, button, signed;
    public MemoExample example;
}

/// <summary>
/// The tour's and the memos' words (the "Tutorial" String Table, Assets/Localization/Source/Tutorial.csv) with their
/// numbers taken from the live systems, so every rule a memo states is the one the game plays by. Ask again after a
/// language change: everything is looked up on each call.
/// </summary>
public static class TutorialContent
{
    const string T = FeatureGate.Table;

    /// <summary>Spotlight steps of the tour (the welcome memo comes before them).</summary>
    public const int StepCount = 5;

    // ------------------------------------------------------------------
    // Tour
    // ------------------------------------------------------------------

    public static WelcomeCard Welcome() => Welcome(DemoMode.Active);

    /// <summary>The welcome memo; <paramref name="demo"/>: the Steam demo's words (Demo: also previewed from the debug menu).</summary>
    public static WelcomeCard Welcome(bool demo)
    {
        CompanyManager company = CompanyManager.Instance;
        double value = company != null ? company.companyValue : 50000d;
        // The money in ink with the design's highlighter mark (TextHighlighter reads the "hl" link)
        string money = "<link=\"" + TextHighlighter.LinkId + "\"><color=#1D2433>" + UIFormat.Money(value) + "</color></link>";
        return new WelcomeCard
        {
            kicker = L10n.Get(T, "tutorial.memo.kicker"),
            to = L10n.Get(T, "tutorial.welcome.to", ("company", CompanyName()), ("day", Day())),
            // Demo: what the demo holds and how it ends
            title = demo ? DemoMode.T("demo.welcome.title") : L10n.Get(T, "tutorial.welcome.title"),
            body = demo
                ? DemoMode.T("demo.welcome.body", ("company", CompanyName()), ("money", money),
                             ("days", DemoMode.FinaleDay), ("target", DemoMode.Config.boardFirstDay))
                : L10n.Get(T, "tutorial.welcome.body", ("company", CompanyName()), ("money", money)),
            show = L10n.Get(T, "tutorial.welcome.show"),
            skip = L10n.Get(T, "tutorial.welcome.skip"),
            signed = L10n.Get(T, "tutorial.memo.signed")
        };
    }

    /// <summary>"STEP 2 OF 5".</summary>
    public static string StepLabel(int step, int count)
    {
        return L10n.Get(T, "tutorial.step.label", ("step", step), ("count", count));
    }

    public static string SkipTour => L10n.Get(T, "tutorial.step.skip");
    public static string Next => L10n.Get(T, "tutorial.step.next");
    public static string DoneNote => L10n.Get(T, "tutorial.step.done_note");
    public static string Done => L10n.Get(T, "tutorial.step.done");

    /// <summary>Step 1 to StepCount. The spots, notes and arrows are the design's numbers (they match the HUD artboard).</summary>
    public static TourStep Step(int step) => Step(step, DemoMode.Active);

    /// <summary>A tour step; <paramref name="demo"/>: the Steam demo's words for the CV and tabs steps (same spots).</summary>
    public static TourStep Step(int step, bool demo)
    {
        switch (step)
        {
            case 1:
                return new TourStep
                {
                    target = TourTarget.Company,
                    title = L10n.Get(T, "tutorial.step.company.title"),
                    body = L10n.Get(T, "tutorial.step.company.body"),
                    designSpot = new Rect(20f, 12f, 444f, 334f), radius = 26f,
                    designNote = new Vector2(560f, 140f), rotation = -1.5f, side = NoteSide.Right,
                    arrowFrom = new Vector4(0f, -4f, 0f, 110f), arrowTo = new Vector4(1f, 12f, 0.55f, 0f), bend = 30f
                };
            case 2:
                return new TourStep
                {
                    target = TourTarget.Cv,
                    title = L10n.Get(T, "tutorial.step.cv.title"),
                    body = demo ? DemoMode.T("demo.step.cv.body") : L10n.Get(T, "tutorial.step.cv.body"),   // Demo: the profit line
                    designSpot = new Rect(664f, 136f, 592f, 768f), radius = 14f,
                    designNote = new Vector2(150f, 420f), rotation = 1.5f, side = NoteSide.Left,
                    arrowFrom = new Vector4(1f, 4f, 0f, 100f), arrowTo = new Vector4(0f, -8f, 0.435f, 0f), bend = -24f
                };
            case 3:
                return new TourStep
                {
                    target = TourTarget.SkipHire,
                    title = L10n.Get(T, "tutorial.step.hire.title"),
                    body = L10n.Get(T, "tutorial.step.hire.body"),
                    designSpot = new Rect(650f, 928f, 620f, 120f), radius = 22f,
                    designNote = new Vector2(1300f, 560f), rotation = -1f, side = NoteSide.Above,
                    arrowFrom = new Vector4(0f, 20f, 1f, 19f), arrowTo = new Vector4(1f, -4f, 0f, 20f), bend = 26f
                };
            case 4:
                return new TourStep
                {
                    target = TourTarget.TaskBoard,
                    title = L10n.Get(T, "tutorial.step.tasks.title"),
                    body = L10n.Get(T, "tutorial.step.tasks.body"),
                    designSpot = new Rect(1470f, 88f, 432f, 400f), radius = 22f,
                    designNote = new Vector2(980f, 210f), rotation = 1f, side = NoteSide.Left,
                    arrowFrom = new Vector4(1f, 4f, 0f, 90f), arrowTo = new Vector4(0f, -6f, 0.435f, 0f), bend = -18f
                };
            default:
                // The toolbar's tabs, all still locked on day one (the canvas's skill and build steps became memos)
                return new TourStep
                {
                    target = TourTarget.Tabs,
                    title = L10n.Get(T, "tutorial.step.tabs.title"),
                    body = demo ? DemoMode.T("demo.step.tabs.body") : L10n.Get(T, "tutorial.step.tabs.body"),   // Demo: FULL GAME tabs
                    designSpot = new Rect(494f, -10f, 932f, 104f), radius = 14f,
                    designNote = new Vector2(740f, 170f), rotation = -1.5f, side = NoteSide.Below,
                    arrowFrom = new Vector4(0.5f, 30f, 0f, 6f), arrowTo = new Vector4(0.5f, 0f, 1f, 10f), bend = 22f
                };
        }
    }

    // ------------------------------------------------------------------
    // Memos
    // ------------------------------------------------------------------

    public static TutorialMemo Memo(Feature feature)
    {
        var memo = new TutorialMemo
        {
            feature = feature,
            kicker = L10n.Get(T, "tutorial.memo.kicker"),
            to = L10n.Get(T, "tutorial.memo.to", ("company", CompanyName()), ("day", Day())),
            button = L10n.Get(T, "tutorial.memo.got_it"),
            signed = L10n.Get(T, "tutorial.memo.signed")
        };

        switch (feature)
        {
            case Feature.RiskCards:
                memo.title = L10n.Get(T, "tutorial.memo.risk.title");
                memo.body = L10n.Get(T, "tutorial.memo.risk.body");
                memo.example = MemoExample.RiskCards;
                break;
            case Feature.Crises:
                memo.title = L10n.Get(T, "tutorial.memo.crisis.title");
                memo.body = L10n.Get(T, "tutorial.memo.crisis.body");
                memo.example = MemoExample.Crisis;
                break;
            case Feature.Skills:
            {
                SkillTreeManager skills = SkillTreeManager.Instance;
                int perPoint = skills != null ? Mathf.Max(1, skills.hiresPerSkillPoint) : 3;
                int iq = skills != null ? skills.iqThreshold : 100;
                if (DemoMode.Active)
                {
                    // Demo: the Board's signing bonus, and where the demo's tree ends
                    DemoConfig config = DemoMode.Config;
                    memo.title = DemoMode.T("demo.memo.skills.title", ("count", config.skillGiftPoints));
                    memo.body = DemoMode.T("demo.memo.skills.body", ("per", perPoint), ("iq", iq), ("tier", config.maxSkillTier));
                    break;
                }
                memo.title = L10n.Get(T, "tutorial.memo.skills.title");
                memo.body = L10n.Get(T, "tutorial.memo.skills.body", ("hires", perPoint), ("iq", iq));
                break;
            }
            case Feature.Staff:
            {
                float days = StaffLedger.Instance != null ? Mathf.Max(0f, StaffLedger.Instance.severanceDays) : 30f;
                memo.title = L10n.Get(T, "tutorial.memo.staff.title");
                memo.body = L10n.Get(T, "tutorial.memo.staff.body", ("days", Number(days)));
                break;
            }
            case Feature.Build:
            {
                string key = BuildModeCamera.Instance != null ? KeyName(BuildModeCamera.Instance.toggleKey) : "Q";
                memo.title = L10n.Get(T, "tutorial.memo.build.title");
                memo.body = DemoMode.Active   // Demo: the Board's construction grant and the free first flip
                    ? DemoMode.T("demo.memo.build.body", ("grant", UIFormat.Money(DemoMode.Config.startingBuildBudget)),
                                 ("max", UIFormat.Money(DemoMode.Config.houseFlipMaxBet)))
                    : L10n.Get(T, "tutorial.memo.build.body", ("key", key));
                break;
            }
            case Feature.BoardMeetings:
            {
                int days = BoardMeetings.Instance != null ? Mathf.Max(1, BoardMeetings.Instance.meetingInterval) : 10;
                memo.title = L10n.Get(T, "tutorial.memo.board.title");
                memo.body = DemoMode.Active   // Demo: the verdict day is the demo's final exam
                    ? DemoMode.T("demo.memo.board.body", ("day", DemoMode.FinaleDay))
                    : L10n.Get(T, "tutorial.memo.board.body", ("days", days),
                    ("safe", BoardMeetings.TierName(BoardMeetings.TierSafe)),
                    ("stretch", BoardMeetings.TierName(BoardMeetings.TierStretch)),
                    ("moonshot", BoardMeetings.TierName(BoardMeetings.TierMoonshot)));
                memo.example = MemoExample.Board;
                break;
            }
            case Feature.Heat:
            {
                HeatMeter meter = HeatMeter.Instance;
                float freeze = meter != null ? Mathf.Min(meter.lockoutAt, meter.MaxHeat) : 100f;
                float thaw = meter != null ? meter.lockoutReleaseBelow : 70f;
                memo.title = L10n.Get(T, "tutorial.memo.heat.title");
                memo.body = L10n.Get(T, "tutorial.memo.heat.body", ("freeze", Number(freeze)), ("thaw", Number(thaw)));
                memo.example = MemoExample.Heat;
                break;
            }
            case Feature.Stocks:
            {
                // Buying still needs the Stock Market Access skill (StockMarketUI): say so while it's missing
                float fee = StockMarketManager.Instance != null ? StockMarketManager.Instance.FeeRate * 100f : 1f;
                string key = StockMarketManager.IsTradingUnlocked() ? "tutorial.memo.stocks.body" : "tutorial.memo.stocks.body.locked";
                memo.title = L10n.Get(T, "tutorial.memo.stocks.title");
                memo.body = L10n.Get(T, key, ("fee", fee.ToString("0.##", L10n.Culture)));
                break;
            }
            case Feature.Casino:
            {
                double keep = CasinoManager.Instance != null ? System.Math.Max(0.0, CasinoManager.Instance.keepInCompany) : 1000.0;
                if (DemoMode.Active)
                {
                    // Demo: the Board's fast-tracked license; this memo also stands in for the heat memo
                    keep = CasinoManager.Instance != null ? CasinoManager.Instance.KeepInCompany : 1000.0;
                    memo.title = DemoMode.T("demo.memo.casino.title");
                    memo.body = DemoMode.T("demo.memo.casino.body", ("keep", UIFormat.Money(keep)));
                    break;
                }
                memo.title = L10n.Get(T, "tutorial.memo.casino.title");
                memo.body = L10n.Get(T, "tutorial.memo.casino.body", ("keep", UIFormat.Money(keep)));
                break;
            }
            default:
                memo.title = "";
                memo.body = "";
                break;
        }
        return memo;
    }

    // ------------------------------------------------------------------
    // Example words
    // ------------------------------------------------------------------

    public static string RiskName(CardRisk risk)
    {
        switch (risk)
        {
            case CardRisk.HighRisk: return L10n.Get(T, "tutorial.example.risk.high");
            case CardRisk.LowRisk: return L10n.Get(T, "tutorial.example.risk.low");
            default: return L10n.Get(T, "tutorial.example.risk.balanced");
        }
    }

    /// <summary>"25% good": the card's chance right now (skills, trust and heat included) when the event system is there.</summary>
    public static float RiskOdds(CardRisk risk)
    {
        if (EventManager.Instance != null) return Mathf.Clamp01(EventManager.Instance.GetGoodChance(risk));
        switch (risk)
        {
            case CardRisk.HighRisk: return 0.25f;
            case CardRisk.LowRisk: return 0.75f;
            default: return 0.5f;
        }
    }

    public static string RiskOddsText(float chance)
    {
        return L10n.Get(T, "tutorial.example.risk.odds", ("good", Mathf.RoundToInt(chance * 100f)));
    }

    public static string CrisisStamp => L10n.Get(T, "tutorial.example.crisis.stamp");
    public static string CrisisLine => L10n.Get(T, "tutorial.example.crisis.line");
    public static string HeatFreeze => L10n.Get(T, "tutorial.example.heat.freeze");

    // ------------------------------------------------------------------

    /// <summary>The company's name as the player typed it, shown as plain text (never as rich-text tags).</summary>
    static string CompanyName()
    {
        string name = CompanyNameUI.GetCompanyName();
        if (string.IsNullOrWhiteSpace(name)) name = CompanyNameUI.CurrentCompanyName;
        name = CompanyNameUI.DisplayName(name);   // the default "New Company" in the player's language (localization, 2026-09-29)
        return "<noparse>" + (name ?? "").Replace("</noparse>", "") + "</noparse>";
    }

    static int Day()
    {
        return CompanyManager.Instance != null ? Mathf.Max(1, CompanyManager.Instance.currentDay) : 1;
    }

    static string Number(float value)
    {
        return value.ToString("0.#", L10n.Culture);
    }

    /// <summary>"Q", "B", "F2", "Space": a key as printed on it.</summary>
    static string KeyName(KeyCode key)
    {
        string name = key.ToString();
        if (name.StartsWith("Alpha", System.StringComparison.Ordinal) && name.Length == 6) return name.Substring(5);
        return name;
    }
}
