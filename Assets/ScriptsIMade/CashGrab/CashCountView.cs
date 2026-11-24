using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The Count (design canvas board "TheCount", 2026-11-24), on the cash floor:
/// - every day: a tally strip under the boost banner, who banked what today (five seconds, then it fades);
/// - every CashRules.CountEveryDays days: the Count, a ledger card with a stack per side, the winner's stamp and, when the
///   CEO won, three perk cards to pick from (the perk lasts until the next Count). A department that wins works faster
///   until then. A Count that ends while the CEO is at the desk waits for the floor to open (and says so in a toast).
/// The floor keeps running behind the card (it isn't a UIManager panel): it only takes the clicks over itself.
/// </summary>
public class CashCountView : MonoBehaviour
{
    const float TallySeconds = 5f;
    const float StackMax = 210f;

    CashFloorView view;
    CashGrabSystem system;
    TMP_FontAsset font;
    RectTransform layer;

    // Daily tally strip
    RectTransform strip;
    CanvasGroup stripGroup;
    TextMeshProUGUI stripDay;
    readonly TextMeshProUGUI[] stripTakes = new TextMeshProUGUI[4];
    float stripTimer;

    // The Count card
    RectTransform card;
    TextMeshProUGUI title, subtitle, stampText, rows, footnote, perkHead;
    BlueprintBox stamp;
    readonly RectTransform[] stacks = new RectTransform[4];
    readonly TextMeshProUGUI[] stackAmounts = new TextMeshProUGUI[4];
    readonly TextMeshProUGUI[] stackNames = new TextMeshProUGUI[4];
    readonly BlueprintBox[] stackPills = new BlueprintBox[4];
    readonly Button[] perkButtons = new Button[3];
    readonly TextMeshProUGUI[] perkNames = new TextMeshProUGUI[3];
    readonly TextMeshProUGUI[] perkBlurbs = new TextMeshProUGUI[3];
    Button closeButton;
    TextMeshProUGUI closeLabel;
    float cardAppear;

    CashGrabSystem.DayCount weekly;   // the last weekly Count, waiting for the floor or on screen
    bool weeklyWaiting;
    CashGrabSystem.DayCount tally;

    static readonly CashPerk[] Perks = { CashPerk.WideSafe, CashPerk.LuckyReels, CashPerk.LongLunch };
    // Sides in the order the board shows them: the CEO first
    static readonly int[] SideOrder = { 3, 0, 1, 2 };

    public bool CardOpen => card != null && card.gameObject.activeSelf;

    public static CashCountView Create(CashFloorView owner, CashGrabSystem system, RectTransform parent, TMP_FontAsset font)
    {
        RectTransform rect = BlueprintUI.Stretch(BlueprintUI.NewRect("Count", parent));
        var v = rect.gameObject.AddComponent<CashCountView>();
        v.view = owner;
        v.system = system;
        v.font = font;
        v.layer = rect;
        v.Build();
        CashGrabSystem.CountReady += v.OnCount;
        return v;
    }

    void OnDestroy()
    {
        CashGrabSystem.CountReady -= OnCount;
    }

    TextMeshProUGUI Label(Transform parent, string name, string text, float size, Color color, TextAlignmentOptions align = TextAlignmentOptions.Left, bool bold = false)
    {
        return BlueprintUI.Text(parent, name, text, size, color, font, align, bold);
    }

    static string SideName(int side) => side == 3 ? CashText.Get("cash.count.you") : CashText.DeptName(side);
    static Color SideColor(int side) => side == 3 ? CashRules.PlayerColor : CashRules.DeptColors[side];
    static Color SideInk(int side) => side == 3 ? UIColors.Ink : CashRules.DeptInk(side);

    // ------------------------------------------------------------------
    // Building
    // ------------------------------------------------------------------

    void Build()
    {
        // ---- Tally strip ----
        BlueprintBox s = BlueprintUI.Box(layer, "Tally", UIColors.PaperHi, UIColors.Ink, 2.5f, BlueprintUI.RadiusLarge);
        strip = s.rectTransform;
        strip.anchorMin = strip.anchorMax = new Vector2(0.5f, 1f);
        strip.pivot = new Vector2(0.5f, 1f);
        strip.anchoredPosition = new Vector2(0f, -78f);
        strip.sizeDelta = new Vector2(940f, 56f);
        s.SetShadow(UIColors.Dark(0.45f), new Vector2(3f, -5f));
        stripGroup = s.gameObject.AddComponent<CanvasGroup>();
        stripGroup.blocksRaycasts = false;
        stripDay = BlueprintUI.Caps(Label(strip, "Day", "", 16f, UIColors.Pencil, TextAlignmentOptions.Left, true));
        BlueprintUI.TopLeft(stripDay.rectTransform, 16f, 8f, 150f, 40f);
        BlueprintUI.AutoSize(stripDay, 10f);
        for (int k = 0; k < 4; k++)
        {
            int side = SideOrder[k];
            BlueprintBox chip = BlueprintUI.Box(strip, "Side" + side, SideColor(side), UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
            BlueprintUI.TopLeft(chip.rectTransform, 168f + k * 190f, 9f, 180f, 38f);
            TextMeshProUGUI t = Label(chip.transform, "Text", "", 16f, SideInk(side), TextAlignmentOptions.Center, true);
            BlueprintUI.Stretch(t.rectTransform, 6f, 2f, 6f, 2f);
            BlueprintUI.AutoSize(t, 10f);
            stripTakes[side] = t;
        }
        strip.gameObject.SetActive(false);

        // ---- The Count card ----
        BlueprintBox c = BlueprintUI.Box(layer, "Card", UIColors.Ledger, UIColors.Ink, 3f, BlueprintUI.RadiusCard, true);
        card = c.rectTransform;
        BlueprintUI.Centered(card, 900f, 660f, 0f, 0f);
        c.SetShadow(UIColors.Dark(0.6f), new Vector2(8f, -12f));
        c.SetStripes(UIColors.WithAlpha(UIColors.Hex(0xA9C9A0), 0.45f), 34f, 2f);   // ledger lines

        title = Label(card, "Title", "", 52f, UIColors.Ink, TextAlignmentOptions.Left, true);
        BlueprintUI.TopLeft(title.rectTransform, 40f, 22f, 520f, 64f);
        BlueprintUI.AutoSize(title, 28f);
        subtitle = Label(card, "Subtitle", "", 19f, UIColors.InkSoft);
        BlueprintUI.TopLeft(subtitle.rectTransform, 42f, 88f, 520f, 28f);
        BlueprintUI.AutoSize(subtitle, 12f);

        stamp = BlueprintUI.Box(card, "Stamp", Color.clear, UIColors.Loss, 4f, BlueprintUI.Round(8f));
        BlueprintUI.TopRight(stamp.rectTransform, 40f, 34f, 300f, 66f);
        stamp.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 6f);
        stampText = BlueprintUI.Caps(Label(stamp.transform, "Text", "", 24f, UIColors.Loss, TextAlignmentOptions.Center, true));
        BlueprintUI.Stretch(stampText.rectTransform, 8f, 4f, 8f, 4f);
        BlueprintUI.AutoSize(stampText, 12f);

        // Four stacks of bills on a baseline
        const float baseline = 370f;
        for (int k = 0; k < 4; k++)
        {
            int side = SideOrder[k];
            float cx = 140f + k * 160f;
            BlueprintBox stack = BlueprintUI.Box(card, "Stack" + side, UIColors.Hex(0xCFE9C6), UIColors.Ink, 3f, BlueprintUI.Round(4f));
            stack.rectTransform.anchorMin = stack.rectTransform.anchorMax = new Vector2(0f, 1f);
            stack.rectTransform.pivot = new Vector2(0.5f, 0f);
            stack.rectTransform.anchoredPosition = new Vector2(cx, -baseline);
            stack.rectTransform.sizeDelta = new Vector2(110f, 10f);
            stack.SetStripes(UIColors.WithAlpha(UIColors.Ink, 0.55f), 13f, 2f);   // the bills' edges
            stack.SetShadow(UIColors.Dark(0.3f), new Vector2(7f, -5f));
            stacks[side] = stack.rectTransform;

            TextMeshProUGUI amount = Label(card, "Amount" + side, "", 28f, UIColors.Ink, TextAlignmentOptions.Center, true);
            amount.rectTransform.anchorMin = amount.rectTransform.anchorMax = new Vector2(0f, 1f);
            amount.rectTransform.pivot = new Vector2(0.5f, 0f);
            amount.rectTransform.sizeDelta = new Vector2(156f, 36f);
            BlueprintUI.AutoSize(amount, 14f);
            stackAmounts[side] = amount;

            BlueprintBox pill = BlueprintUI.Box(card, "Name" + side, SideColor(side), UIColors.Ink, 2.5f, BlueprintUI.RadiusSmall);
            BlueprintUI.TopLeft(pill.rectTransform, cx - 72f, baseline + 12f, 144f, 40f);
            stackPills[side] = pill;
            TextMeshProUGUI name = Label(pill.transform, "Text", "", 19f, SideInk(side), TextAlignmentOptions.Center, true);
            BlueprintUI.Stretch(name.rectTransform, 6f, 2f, 6f, 2f);
            BlueprintUI.AutoSize(name, 11f);
            stackNames[side] = name;
        }
        BlueprintBox line = BlueprintUI.Box(card, "Baseline", UIColors.Ink, Color.clear, 0f, BlueprintUI.Round(2f));
        BlueprintUI.TopLeft(line.rectTransform, 50f, baseline - 2f, 660f, 5f);

        // Side notes (bonuses, hands, stamps)
        rows = BlueprintUI.Wrap(Label(card, "Rows", "", 17f, UIColors.InkSoft));
        BlueprintUI.TopLeft(rows.rectTransform, 724f, 140f, 150f, 260f);
        BlueprintUI.AutoSize(rows, 11f);

        // Perks (when the CEO won)
        perkHead = BlueprintUI.Caps(Label(card, "PerkHead", "", 16f, UIColors.Loss, TextAlignmentOptions.Left, true));
        BlueprintUI.TopLeft(perkHead.rectTransform, 42f, 436f, 820f, 24f);
        BlueprintUI.AutoSize(perkHead, 11f);
        for (int p = 0; p < Perks.Length; p++)
        {
            CashPerk perk = Perks[p];
            BlueprintBox box = BlueprintUI.Box(card, "Perk" + p, UIColors.PaperHi, UIColors.Ink, 3f, BlueprintUI.Round(12f), true);
            BlueprintUI.TopLeft(box.rectTransform, 42f + p * 226f, 466f, 214f, 130f);
            box.SetShadow(UIColors.Dark(0.35f), new Vector2(4f, -6f));
            box.rectTransform.localRotation = Quaternion.Euler(0f, 0f, (p - 1) * -2.5f);
            Button b = BlueprintUI.MakeButton(box);
            b.onClick.AddListener(() => Pick(perk));
            perkButtons[p] = b;
            TextMeshProUGUI corner = Label(box.transform, "Corner", "AKQ".Substring(p, 1) + "♥", 17f, UIColors.Loss, TextAlignmentOptions.Left, true);
            BlueprintUI.TopLeft(corner.rectTransform, 10f, 6f, 40f, 24f);
            perkNames[p] = Label(box.transform, "Name", "", 22f, UIColors.Ink, TextAlignmentOptions.Center, true);
            BlueprintUI.TopLeft(perkNames[p].rectTransform, 34f, 14f, 170f, 34f);
            BlueprintUI.AutoSize(perkNames[p], 13f);
            perkBlurbs[p] = BlueprintUI.Wrap(Label(box.transform, "Blurb", "", 15f, UIColors.InkSoft, TextAlignmentOptions.Center));
            BlueprintUI.TopLeft(perkBlurbs[p].rectTransform, 12f, 52f, 190f, 70f);
            BlueprintUI.AutoSize(perkBlurbs[p], 10f);
        }

        footnote = BlueprintUI.Wrap(Label(card, "Footnote", "", 19f, UIColors.Ink));
        BlueprintUI.TopLeft(footnote.rectTransform, 42f, 446f, 600f, 120f);
        BlueprintUI.AutoSize(footnote, 12f);

        BlueprintBox close = BlueprintUI.Box(card, "Close", UIColors.Highlight, UIColors.Ink, 3f, BlueprintUI.RadiusLarge, true);
        close.rectTransform.anchorMin = close.rectTransform.anchorMax = new Vector2(1f, 0f);
        close.rectTransform.pivot = new Vector2(1f, 0f);
        close.rectTransform.anchoredPosition = new Vector2(-30f, 24f);
        close.rectTransform.sizeDelta = new Vector2(150f, 54f);
        close.SetShadow(UIColors.Dark(0.4f), new Vector2(0f, -5f));
        closeButton = BlueprintUI.MakeButton(close);
        closeButton.onClick.AddListener(CloseCard);
        closeLabel = Label(close.transform, "Text", "", 20f, UIColors.Ink, TextAlignmentOptions.Center, true);
        BlueprintUI.Stretch(closeLabel.rectTransform, 8f, 2f, 8f, 2f);
        BlueprintUI.AutoSize(closeLabel, 12f);

        card.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Showing
    // ------------------------------------------------------------------

    void OnCount(CashGrabSystem.DayCount count)
    {
        if (count == null) return;
        if (count.weekly)
        {
            weekly = count;
            weeklyWaiting = true;
            if (view == null || !view.IsOpen)
            {
                string key = count.winner == 3 ? "cash.toast.count_won" : count.winner >= 0 ? "cash.toast.count_lost" : null;
                if (key != null) GameNotifications.Show(CashText.Get(key, ("dept", count.winner >= 0 && count.winner < 3 ? CashText.DeptName(count.winner) : "")),
                                                        count.winner == 3 ? NotificationKind.Special : NotificationKind.Info);
            }
            return;
        }
        tally = count;
        if (view != null && view.IsOpen) ShowTally(count);
    }

    void ShowTally(CashGrabSystem.DayCount count)
    {
        stripDay.text = CashText.Get("cash.count.tally_day", ("day", count.day));
        for (int side = 0; side < 4; side++)
            stripTakes[side].text = SideName(side) + " " + CashText.Compact(count.takes[side]);
        strip.gameObject.SetActive(true);
        strip.SetAsLastSibling();
        stripTimer = TallySeconds;
        stripGroup.alpha = 1f;
        view?.Coach?.OnCount(false);
    }

    void ShowCard(CashGrabSystem.DayCount count)
    {
        weeklyWaiting = false;
        int from = Math.Max(1, count.day - CashRules.CountEveryDays + 1);
        title.text = CashText.Get("cash.count.title");
        subtitle.text = CashText.Get("cash.count.subtitle", ("from", from), ("to", count.day));

        double max = 1.0;
        for (int s = 0; s < 4; s++) max = Math.Max(max, count.takes[s]);
        for (int s = 0; s < 4; s++)
        {
            float h = Mathf.Max(8f, (float)(count.takes[s] / max) * StackMax);
            stacks[s].sizeDelta = new Vector2(110f, h);
            stackAmounts[s].text = CashText.Compact(count.takes[s]);
            Vector2 p = stacks[s].anchoredPosition;
            stackAmounts[s].rectTransform.anchoredPosition = new Vector2(p.x, p.y + h + 8f);
            stackNames[s].text = SideName(s);
            stackPills[s].SetRings(s == count.winner ? UIColors.Ink : Color.clear, s == count.winner ? 3f : 0f, Color.clear, 0f);
        }

        bool youWon = count.winner == 3;
        stamp.gameObject.SetActive(count.winner >= 0);
        stampText.text = youWon ? CashText.Get("cash.count.stamp_you") : count.winner >= 0 ? CashText.Get("cash.count.stamp_dept", ("dept", L10n.ToUpper(CashText.DeptName(count.winner)))) : "";

        int bestHand = system != null ? system.BestHand : count.bestHand;
        string best = bestHand > 0 ? CashText.Get("cash.poker." + CashPoker.Id((CashPoker.Rank)bestHand)) : CashText.Get("cash.count.none");
        rows.text = CashText.Get("cash.count.rows", ("bonus", CashText.Compact(system != null ? system.LifetimeBonus : 0.0)), ("best", best),
                                 ("stamps", system != null ? system.WrittenUpCount : 0));

        bool picking = youWon && system != null && system.PerkPending;
        perkHead.gameObject.SetActive(picking);
        perkHead.text = CashText.Get("cash.count.pick");
        for (int p = 0; p < Perks.Length; p++)
        {
            perkButtons[p].gameObject.SetActive(picking);
            perkNames[p].text = CashText.Get("cash.perk." + PerkId(Perks[p]) + ".name");
            perkBlurbs[p].text = CashText.Get("cash.perk." + PerkId(Perks[p]) + ".blurb");
        }
        footnote.gameObject.SetActive(!picking);
        footnote.text = youWon ? CashText.Get("cash.count.you_picked", ("perk", system != null && system.ActivePerk != CashPerk.None ? CashText.Get("cash.perk." + PerkId(system.ActivePerk) + ".name") : "-"))
                      : count.winner >= 0 ? CashText.Get("cash.count.dept_won", ("dept", CashText.DeptName(count.winner)), ("percent", Mathf.RoundToInt((CashRules.WinnerSpeed - 1f) * 100f)))
                      : CashText.Get("cash.count.nobody");
        closeLabel.text = CashText.Get(picking ? "cash.count.later" : "cash.count.back");

        card.gameObject.SetActive(true);
        card.SetAsLastSibling();
        cardAppear = 0f;
        system?.Audio?.Play(youWon ? "combo" : "dept_levelup", 0.8f, 1f, 0.3f, 0f);
        if (youWon) GameSfx.Play(Sfx.ChipsWin, 0.9f);
        view?.Coach?.OnCount(true);
    }

    void Pick(CashPerk perk)
    {
        if (system == null || !system.PickPerk(perk)) return;
        GameSfx.Play(Sfx.CardPlace, 1f);
        if (weekly != null) ShowCard(weekly);   // shows the picked perk and the "Back to the floor" button
    }

    void CloseCard()
    {
        card.gameObject.SetActive(false);
    }

    static string PerkId(CashPerk perk)
    {
        switch (perk)
        {
            case CashPerk.WideSafe: return "wide_safe";
            case CashPerk.LuckyReels: return "lucky_reels";
            case CashPerk.LongLunch: return "long_lunch";
            default: return "none";
        }
    }

    // ------------------------------------------------------------------

    void Update()
    {
        if (view == null || !view.IsOpen)
        {
            if (strip.gameObject.activeSelf) strip.gameObject.SetActive(false);
            if (card.gameObject.activeSelf) card.gameObject.SetActive(false);
            return;
        }

        // A Count that happened at the desk, or a perk still to pick, shows when the floor opens
        if (!card.gameObject.activeSelf)
        {
            if (weekly == null && system != null && system.PerkPending)
            {
                // A loaded save still owes the CEO a perk: a Count card without the week's numbers
                weekly = new CashGrabSystem.DayCount { weekly = true, winner = 3, day = CompanyManager.Instance != null ? Math.Max(1, CompanyManager.Instance.currentDay - 1) : 1 };
                weeklyWaiting = true;
            }
            if (weeklyWaiting && weekly != null) ShowCard(weekly);
            else if (system != null && system.PerkPending && weekly != null && reopenTimer <= 0f) ShowCard(weekly);
        }
        reopenTimer = card.gameObject.activeSelf ? 30f : Mathf.Max(0f, reopenTimer - Time.unscaledDeltaTime);

        if (card.gameObject.activeSelf)
        {
            cardAppear = Mathf.MoveTowards(cardAppear, 1f, Time.unscaledDeltaTime * 5f);
            float s = 0.9f + 0.1f * Mathf.SmoothStep(0f, 1f, cardAppear);
            card.localScale = new Vector3(s, s, 1f);
            card.localRotation = Quaternion.Euler(0f, 0f, -1.2f * (1f - cardAppear));
        }

        if (strip.gameObject.activeSelf)
        {
            stripTimer -= Time.unscaledDeltaTime;
            stripGroup.alpha = Mathf.Clamp01(stripTimer / 0.6f);
            if (stripTimer <= 0f) strip.gameObject.SetActive(false);
        }
    }

    float reopenTimer;
}
