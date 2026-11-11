using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The designed risk-card choice (built by Tools > Game > New UI > Build Risk Event). EventManager keeps the logic
/// (the three card Buttons are its highRiskCard / balancedCard / lowRiskCard); this view shows the odds honestly:
/// the base chance struck through, the chance after skills and investor trust, a bar split into good / bonus / bad,
/// and a chip naming what changed the odds. It also remembers the card that was played for the result clipping.
/// Texts come from the Hud String Table in the current language (again when it changes).
/// </summary>
[DisallowMultipleComponent]
public class RiskCardsView : MonoBehaviour
{
    const string T = "Hud";   // Assets/Localization/Source/Hud.csv

    [System.Serializable]
    public class Card
    {
        public Button button;
        public RectTransform goodSegment;
        public RectTransform bonusSegment;
        public RectTransform badSegment;
        public TMP_Text odds;
        public GameObject chip;
        public TMP_Text chipText;
    }

    public TMP_Text headline;
    public Card highRisk = new Card();
    public Card balanced = new Card();
    public Card lowRisk = new Card();

    /// <summary>The card played last, for the result clipping ("You played: Balanced (65% good)").</summary>
    public static CardRisk LastRisk { get; private set; }
    /// <summary>The played card's chance of a good outcome in percent; -1 before any card was played.</summary>
    public static int LastGoodPercent { get; private set; } = -1;

    static readonly float[] BaseChance = { 0.25f, 0.5f, 0.75f };
    bool listening;

    void Awake()
    {
        L10nFont.AttachAll(this);   // the language's font on every text of the cards
    }

    void OnEnable()
    {
        if (!listening)
        {
            listening = true;
            Hook(highRisk, CardRisk.HighRisk);
            Hook(balanced, CardRisk.Balanced);
            Hook(lowRisk, CardRisk.LowRisk);
        }
        L10n.LanguageChanged += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= Refresh;
    }

    void Hook(Card card, CardRisk risk)
    {
        if (card.button == null) return;
        card.button.onClick.AddListener(() =>
        {
            LastRisk = risk;
            LastGoodPercent = Mathf.RoundToInt(Chance(risk) * 100f);
        });
    }

    /// <summary>A card's name in the current language (the card titles: High risk, Balanced, Low risk).</summary>
    public static string CardName(CardRisk risk)
    {
        switch (risk)
        {
            case CardRisk.HighRisk: return L10n.Get(T, "hud.risk.card.high.title");
            case CardRisk.LowRisk: return L10n.Get(T, "hud.risk.card.low.title");
            default: return L10n.Get(T, "hud.risk.card.balanced.title");
        }
    }

    static float Chance(CardRisk risk)
    {
        return EventManager.Instance != null ? EventManager.Instance.GetGoodChance(risk) : BaseChance[(int)risk];
    }

    public void Refresh()
    {
        if (headline != null)
            headline.text = L10n.Get(T, "hud.risk.headline", ("company", CompanyNameUI.DisplayName(CompanyNameUI.CurrentCompanyName)));
        string why = BonusSources();
        Fill(highRisk, CardRisk.HighRisk, why);
        Fill(balanced, CardRisk.Balanced, why);
        Fill(lowRisk, CardRisk.LowRisk, why);
    }

    void Fill(Card card, CardRisk risk, string why)
    {
        float baseChance = BaseChance[(int)risk];
        float chance = Chance(risk);
        int basePct = Mathf.RoundToInt(baseChance * 100f);
        int pct = Mathf.RoundToInt(chance * 100f);
        int bonus = pct - basePct;

        // bar: good (base) | bonus (hatched) | bad
        float goodPart = Mathf.Min(baseChance, chance);
        float bonusPart = Mathf.Max(0f, chance - baseChance);
        SetSegment(card.goodSegment, 0f, goodPart);
        SetSegment(card.bonusSegment, goodPart, goodPart + bonusPart);
        SetSegment(card.badSegment, goodPart + bonusPart, 1f);
        if (card.bonusSegment != null) card.bonusSegment.gameObject.SetActive(bonusPart > 0.001f);

        if (card.odds != null)
        {
            string good = "<color=#1E7B45>" + L10n.Get(T, "hud.risk.card.odds.good", ("percent", pct)) + "</color>";
            string bad = "<color=#C0352B>" + L10n.Get(T, "hud.risk.card.odds.bad", ("percent", 100 - pct)) + "</color>";
            string odds = L10n.Get(T, "hud.risk.card.odds", ("good", good), ("bad", bad));
            // the base chance struck through, before the odds after skills and trust
            card.odds.text = bonus != 0
                ? "<color=#676B74><s>" + L10n.Get(T, "hud.ledger.percent", ("percent", basePct)) + "</s></color> " + odds
                : odds;
        }
        if (card.chip != null) card.chip.SetActive(bonus != 0);
        if (card.chipText != null && bonus != 0)
            card.chipText.text = bonus > 0 ? L10n.Get(T, "hud.risk.card.chip.up", ("percent", bonus), ("source", why))
                                           : L10n.Get(T, "hud.risk.card.chip.down", ("percent", -bonus), ("source", why));
    }

    static void SetSegment(RectTransform seg, float from, float to)
    {
        if (seg == null) return;
        seg.anchorMin = new Vector2(from, 0f);
        seg.anchorMax = new Vector2(Mathf.Max(from, to), 1f);
        seg.offsetMin = seg.offsetMax = Vector2.zero;
    }

    /// <summary>Names of what moved the odds, in the current language: the luck skills (as the skill board names them), and investor trust.</summary>
    static string BonusSources()
    {
        var names = new List<string>();
        var skills = SkillTreeManager.Instance;
        if (skills != null && skills.allSkills != null)
        {
            foreach (var s in skills.allSkills)
            {
                if (s == null || !s.unlocked) continue;
                if (s.effectType == SkillEffect.GoodEventChance || s.effectType == SkillEffect.LuckyCharm ||
                    s.effectType == SkillEffect.ProbabilityManipulation || s.effectType == SkillEffect.GamblersFallacy)
                {
                    string name = s.DisplayName;
                    if (!names.Contains(name)) names.Add(name);
                }
            }
        }
        var cm = CompanyManager.Instance;
        bool trust = cm != null && Mathf.Abs(cm.TrustGoodChanceBonus) >= 0.005f;
        if (names.Count == 0) return trust ? L10n.Get(T, "hud.risk.source.trust") : L10n.Get(T, "hud.risk.source.luck");
        string skillsText = names.Count == 1 ? names[0]
            : names.Count == 2 ? L10n.Get(T, "hud.risk.source.pair", ("first", names[0]), ("second", names[1]))
            : L10n.Get(T, "hud.risk.source.many", ("count", names.Count));
        return trust ? L10n.Get(T, "hud.risk.source.with_trust", ("skills", skillsText)) : skillsText;
    }
}
