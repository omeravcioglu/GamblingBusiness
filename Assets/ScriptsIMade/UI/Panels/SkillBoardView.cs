using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The designed skill board (canvas boards "Skill board: Growth / Productivity / Chaos & Luck"): a cork board with
/// the branch's 30 skills as pinned index cards (5 per tier, red strings to the skills they need), the SP sticky,
/// a manila folder with the selected skill and the sealed secret skill. SkillTreeManager keeps the rules; this view
/// draws them and unlocks through it. It doesn't pause the game but blocks world clicks (UIManager panel).
/// Fixed labels carry L10nText; the texts drawn here come from the "SkillBoard" table (SkillText) and are drawn again
/// on a language change.
/// </summary>
[DisallowMultipleComponent]
public class SkillBoardView : MonoBehaviour
{
    public static SkillBoardView Instance { get; private set; }

    [Tooltip("Scrim + board; the object this component sits on stays active")]
    public GameObject page;
    public Button closeButton;
    public Button backButton;

    [Serializable]
    public class BranchTab
    {
        public Button button;
        public GameObject activeLook;
        public GameObject inactiveLook;
        public TMP_Text activeCount;
        public TMP_Text inactiveCount;
    }

    [Header("Branch tabs: Growth, Productivity, Chaos & Luck")]
    public BranchTab[] tabs = new BranchTab[3];
    public Color[] branchColors = new Color[3];
    public Sprite[] branchIcons = new Sprite[3];

    [Header("SP sticky")]
    public TMP_Text spText;
    public TMP_Text companyValueText;
    public TMP_Text progressText;
    public RectTransform progressFill;

    [Header("Tier chips left of the rows, tier 1 first")]
    public TMP_Text[] tierLabels = new TMP_Text[6];
    public TMP_Text[] tierCosts = new TMP_Text[6];

    [Header("Grid (cork-inner coordinates, y down)")]
    [Tooltip("Tier-major: index = (tier - 1) * 5 + column")]
    public SkillCardView[] cards = new SkillCardView[30];
    public Vector2 innerSize = new Vector2(1692f, 936f);
    public Vector2 gridOrigin = new Vector2(104f, 146f);
    public Vector2 cardPitch = new Vector2(224f, 114f);
    public Vector2 cardSize = new Vector2(204f, 94f);
    public UIPolyline strings;
    public UIPolyline stringShadow;
    public UIPolyline arrowUnder;
    public UIPolyline arrowInk;
    public float arrowEndX = 1212f;

    [Serializable]
    public class ReqRow
    {
        public TMP_Text[] names;
        public GameObject[] statuses;
        public Image[] statusIcons;
        public TMP_Text[] statusTexts;
    }

    [Header("Folder")]
    public Image iconBox;
    public Image skillIcon;
    public Image branchChipFill;
    public TMP_Text branchChip;
    public TMP_Text tierChip;
    public TMP_Text costChip;
    public TMP_Text title;
    public TMP_Text effect;
    public TMP_Text example;
    public TMP_Text quote;
    public ReqRow requires;
    public ReqRow leads;
    public Button actionButton;
    public Image actionIcon;
    public TMP_Text actionLabel;
    public GameObject unlockedStamp;
    public TMP_Text hint;

    [Header("Secret envelope")]
    public Button envelope;
    public TMP_Text envelopeTitle;
    public TMP_Text envelopeText;
    public Image sealIcon;

    [Serializable]
    public struct NamedIcon
    {
        public string name;
        public Sprite icon;
    }

    [Header("Sprites")]
    public NamedIcon[] skillIcons;
    public Sprite fallbackIcon;
    public Sprite sparkleIcon;
    public Sprite lockIcon;
    public Sprite checkIcon;

    const int Columns = 5, Tiers = 6;
    const string SecretId = SkillTreeManager.SecretSkillId;
    const string Table = SkillText.Table;
    static readonly string[] DemoFirstPicks = { "chaos_1_fortune", "chaos_1_skip" };   // Demo: Fortune Cookie, Trash to Treasure

    SkillBranch branch = SkillBranch.Growth;
    Skill selected;
    string justUnlockedId;
    readonly Skill[] slots = new Skill[Columns * Tiers];
    readonly Dictionary<string, int> slotOf = new Dictionary<string, int>();
    Dictionary<string, Sprite> iconLookup;
    SkillTreeManager subscribed;
    double shownValue = double.NaN;

    public bool IsOpen => page != null && page.activeSelf;

    void Awake()
    {
        Instance = this;
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (backButton != null) backButton.onClick.AddListener(Close);
        for (int i = 0; i < tabs.Length; i++)
        {
            int b = i;
            if (tabs[i] != null && tabs[i].button != null)
                tabs[i].button.onClick.AddListener(() => SelectBranch((SkillBranch)b));
        }
        for (int i = 0; i < cards.Length; i++)
        {
            int slot = i;
            if (cards[i] != null && cards[i].button != null)
                cards[i].button.onClick.AddListener(() => SelectSlot(slot));
        }
        if (actionButton != null) actionButton.onClick.AddListener(UnlockSelected);
        if (envelope != null) envelope.onClick.AddListener(SelectSecret);
        L10nFont.AttachAll(this);   // the texts written here take the language's font
        if (page != null && page.activeSelf) page.SetActive(false);
    }

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
    }

    // the fixed labels carry L10nText; everything written here is written again
    void OnLanguageChanged()
    {
        if (IsOpen) Refresh();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Unsubscribe();
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
    }

    // ------------------------------------------------------------------
    // Open / close

    public void Open()
    {
        if (page == null) return;
        if (!page.activeSelf)
        {
            page.SetActive(true);
            justUnlockedId = null;
            if (DemoMode.Active && !demoOpened) OpenDemoOnChaos();   // Demo
        }
        var ui = UIManager.Instance;
        if (ui != null) ui.OpenPanel(this, false, Close);   // browsing: no pause, but no world clicks or events
        Subscribe();
        Refresh();
    }

    bool demoOpened;

    // Demo: the first look at the board, before any skill was bought, opens on Chaos (the Skills memo starts there)
    void OpenDemoOnChaos()
    {
        demoOpened = true;
        var m = SkillTreeManager.Instance;
        if (m == null) return;
        foreach (var s in m.allSkills)
            if (s != null && s.unlocked && !m.IsDemoGift(s)) return;
        branch = SkillBranch.Chaos;
        selected = null;
    }

    public void Close()
    {
        if (page != null && page.activeSelf) page.SetActive(false);
        justUnlockedId = null;
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
    }

    public void Toggle()
    {
        if (IsOpen) Close();
        else Open();
    }

    void Subscribe()
    {
        var m = SkillTreeManager.Instance;
        if (m == null || ReferenceEquals(m, subscribed)) return;
        Unsubscribe();
        m.SkillPointsChanged += OnPointsChanged;
        m.SkillUnlocked += OnSkillUnlocked;
        subscribed = m;
    }

    void Unsubscribe()
    {
        if (!ReferenceEquals(subscribed, null))
        {
            subscribed.SkillPointsChanged -= OnPointsChanged;
            subscribed.SkillUnlocked -= OnSkillUnlocked;
        }
        subscribed = null;
    }

    void OnPointsChanged(int points)
    {
        if (IsOpen) Refresh();
    }

    void OnSkillUnlocked(Skill skill)
    {
        if (IsOpen) Refresh();
    }

    void LateUpdate()
    {
        if (!IsOpen) return;
        if (subscribed == null && SkillTreeManager.Instance != null)
        {
            Subscribe();
            Refresh();
        }
        // the game keeps running under the board
        var c = CompanyManager.Instance;
        if (c != null && companyValueText != null && c.companyValue != shownValue)
        {
            shownValue = c.companyValue;
            companyValueText.text = UIFormat.Money(shownValue);
        }
    }

    // ------------------------------------------------------------------
    // Input

    public void SelectBranch(SkillBranch b)
    {
        branch = b;
        selected = null;
        justUnlockedId = null;
        Refresh();
    }

    void SelectSlot(int slot)
    {
        if (slot < 0 || slot >= slots.Length || slots[slot] == null) return;
        selected = slots[slot];
        Refresh();
    }

    void SelectSecret()
    {
        var m = SkillTreeManager.Instance;
        var secret = m != null ? m.GetSkill(SecretId) : null;
        if (secret == null) return;
        selected = secret;
        Refresh();
    }

    void UnlockSelected()
    {
        var m = SkillTreeManager.Instance;
        if (m == null || selected == null) return;
        if (selected.id == SecretId && !m.IsSecretSkillRevealed()) return;
        string id = selected.id;
        if (m.UnlockSkill(id))
            justUnlockedId = id;
        Refresh();
    }

    /// <summary>Show a branch with a given selection (previews and tests).</summary>
    public void ShowBranch(SkillBranch b, string selectedId, string stampedId)
    {
        branch = b;
        var m = SkillTreeManager.Instance;
        selected = m != null ? m.GetSkill(selectedId) : null;
        justUnlockedId = stampedId;
        Refresh();
    }

    // ------------------------------------------------------------------
    // Drawing

    SkillCardView.State StateOf(SkillTreeManager m, Skill s)
    {
        if (s.unlocked) return SkillCardView.State.Owned;
        if (m.IsDemoLocked(s)) return SkillCardView.State.FullGame;   // Demo: kept for the full game
        return m.ArePrerequisitesMet(s) ? SkillCardView.State.Available : SkillCardView.State.Locked;
    }

    public Sprite IconFor(Skill s)
    {
        if (iconLookup == null)
        {
            iconLookup = new Dictionary<string, Sprite>();
            if (skillIcons != null)
                foreach (var e in skillIcons)
                    if (!string.IsNullOrEmpty(e.name) && e.icon != null)
                        iconLookup[Key(e.name)] = e.icon;
        }
        return s != null && iconLookup.TryGetValue(Key(s.skillName), out var sprite) ? sprite : fallbackIcon;
    }

    static string Key(string name) => (name ?? "").Replace('’', '\'').Trim().ToLowerInvariant();

    // l10n (batch B): every name the board shows is Skill.DisplayName (Skills table) passed through Nice; IconFor keeps the
    // English skillName as its key
    static string Nice(string name) => (name ?? "").Replace("'", "’");

    public void Refresh()
    {
        var m = SkillTreeManager.Instance;
        if (m == null) return;

        // the branch's 30 skills, by tier and order in the tree
        Array.Clear(slots, 0, slots.Length);
        slotOf.Clear();
        var perTier = new int[Tiers + 1];
        foreach (var s in m.allSkills)
        {
            if (s == null || s.branch != branch || s.id == SecretId) continue;
            int t = Mathf.Clamp(s.tier, 1, Tiers);
            int col = perTier[t]++;
            if (col >= Columns) continue;
            int slot = (t - 1) * Columns + col;
            slots[slot] = s;
            slotOf[s.id] = slot;
        }

        if (selected == null || (selected.id != SecretId && !slotOf.ContainsKey(selected.id)))
            selected = DefaultSelection(m);

        for (int i = 0; i < cards.Length; i++)
        {
            var card = cards[i];
            if (card == null) continue;
            var s = slots[i];
            if (card.gameObject.activeSelf != (s != null)) card.gameObject.SetActive(s != null);
            if (s == null) continue;
            card.Show(Nice(s.DisplayName), SkillText.Short(s), IconFor(s), s.cost, StateOf(m, s), s == selected, s.id == justUnlockedId);
            card.ShowGift(m.IsDemoGift(s));   // Demo: the Board's free license wears a BOARD GIFT tape
        }

        DrawStrings(m);
        DrawArrow();
        DrawFolder(m);
        DrawSticky(m);
        DrawTiers();
        DrawTabs(m);
        DrawEnvelope(m);
    }

    Skill DefaultSelection(SkillTreeManager m)
    {
        if (DemoMode.Active)
        {
            // Demo: the Skills memo recommends these two (they pay out where the player can see it)
            foreach (string id in DemoFirstPicks)
                if (slotOf.TryGetValue(id, out int slot) && StateOf(m, slots[slot]) == SkillCardView.State.Available)
                    return slots[slot];
        }
        Skill firstAvail = null, firstLocked = null, first = null;
        foreach (var s in slots)
        {
            if (s == null) continue;
            var st = StateOf(m, s);
            if (st == SkillCardView.State.FullGame) continue;   // Demo: never open on a full-game card
            if (first == null) first = s;
            if (st == SkillCardView.State.Available)
            {
                if (m.skillPoints >= s.cost) return s;
                if (firstAvail == null) firstAvail = s;
            }
            else if (st == SkillCardView.State.Locked && firstLocked == null) firstLocked = s;
        }
        return firstAvail ?? firstLocked ?? first;
    }

    // cork-inner coordinates (y down) -> the graphics' local space (centre origin, y up)
    Vector2 L(float x, float y) => new Vector2(x - innerSize.x * 0.5f, innerSize.y * 0.5f - y);

    Rect CardRect(int slot)
    {
        int t = slot / Columns, c = slot % Columns;
        return new Rect(gridOrigin.x + c * cardPitch.x, gridOrigin.y + t * cardPitch.y, cardSize.x, cardSize.y);
    }

    void DrawStrings(SkillTreeManager m)
    {
        if (strings != null) strings.ClearPaths();
        if (stringShadow != null) stringShadow.ClearPaths();
        foreach (var s in slots)
        {
            if (s == null || s.prerequisites == null) continue;
            Rect to = CardRect(slotOf[s.id]);
            foreach (string pre in s.prerequisites)
            {
                if (pre == null || !slotOf.TryGetValue(pre, out int from)) continue;
                Rect a = CardRect(from);
                // from under the card it needs to this card's pin
                Vector2 p0 = new Vector2(a.center.x, a.yMax), p1 = new Vector2(to.center.x, to.yMin);
                if (strings != null) strings.AddPath(L(p0.x, p0.y), L(p1.x, p1.y));
                if (stringShadow != null) stringShadow.AddPath(L(p0.x + 1f, p0.y + 2f), L(p1.x + 1f, p1.y + 2f));
            }
        }
    }

    void DrawArrow()
    {
        if (arrowUnder != null) arrowUnder.ClearPaths();
        if (arrowInk != null) arrowInk.ClearPaths();
        if (selected == null || !slotOf.TryGetValue(selected.id, out int slot)) return;

        Rect r = CardRect(slot);
        float gapY = r.yMax + 10f;
        var pts = new List<Vector2>();
        Vector2 s0, c1, c2, p1;
        if (slot % Columns < Columns - 1)
        {
            // off the card's right edge, down into the gap under its tier, then across to the folder
            s0 = new Vector2(r.xMax + 4f, r.yMax - 26f);
            c1 = s0 + new Vector2(6f, 18f);
            c2 = s0 + new Vector2(18f, 29f);
            p1 = new Vector2(s0.x + 50f, gapY);
        }
        else
        {
            s0 = new Vector2(r.center.x + 40f, r.yMax + 2f);
            c1 = s0 + new Vector2(2f, 4f);
            c2 = s0 + new Vector2(6f, 8f);
            p1 = new Vector2(s0.x + 16f, gapY);
        }
        Vector2 end = new Vector2(arrowEndX, gapY);
        pts.Add(s0);
        UIPolyline.AppendCubic(pts, s0, c1, c2, p1, 12);
        float dx = end.x - p1.x;
        UIPolyline.AppendCubic(pts, p1, p1 + new Vector2(dx * 0.35f, -3f), end - new Vector2(dx * 0.3f, -2f), end, 20);
        var path = pts.ConvertAll(p => L(p.x, p.y)).ToArray();
        var head = new[] { L(end.x - 20f, end.y - 13f), L(end.x + 3f, end.y), L(end.x - 22f, end.y + 12f) };
        foreach (var g in new[] { arrowUnder, arrowInk })
        {
            if (g == null) continue;
            g.AddPath(path);
            g.AddPath(head);
        }
    }

    void DrawFolder(SkillTreeManager m)
    {
        var s = selected;
        if (s == null) return;
        bool secret = s.id == SecretId;
        var state = StateOf(m, s);
        Color bc = branchColors != null && branchColors.Length > (int)s.branch ? branchColors[(int)s.branch] : UIColors.PaperHi;

        if (iconBox != null) iconBox.color = bc;
        if (skillIcon != null) skillIcon.sprite = secret ? sparkleIcon : IconFor(s);
        if (branchChipFill != null) branchChipFill.color = bc;
        if (branchChip != null) branchChip.text = secret ? L10n.Get(Table, "skillboard.folder.secret") : SkillText.BranchName(s.branch);
        if (tierChip != null) tierChip.text = L10n.Get(Table, "skillboard.tier", ("tier", s.tier));
        if (costChip != null) costChip.text = L10n.Get(Table, "skillboard.cost", ("cost", s.cost));
        if (title != null) title.text = Nice(s.DisplayName);
        if (effect != null) effect.text = SkillText.MarkedEffect(s);
        SetText(example, SkillText.Example(s));
        SetText(quote, SkillText.Quote(s));

        // requires / leads to
        string ownedWord = L10n.Get(Table, "skillboard.link.owned");
        var req = new List<(string, int, string)>();   // name, kind (0 plain, 1 owned, 2 locked, 3 can unlock), status text
        if (s.prerequisites != null)
            foreach (string id in s.prerequisites)
            {
                var p = m.GetSkill(id);
                if (p != null)
                    req.Add((Nice(p.DisplayName), p.unlocked ? 1 : 2, p.unlocked ? ownedWord : L10n.Get(Table, "skillboard.link.locked")));
            }
        if (req.Count == 0) req.Add((L10n.Get(Table, "skillboard.link.none.requires"), 0, ""));
        FillRow(requires, req);

        var lead = new List<(string, int, string)>();
        bool revealed = m.IsSecretSkillRevealed();
        foreach (var d in m.allSkills)
        {
            if (d == null || d.prerequisites == null || Array.IndexOf(d.prerequisites, s.id) < 0) continue;
            if (d.id == SecretId && !revealed) continue;
            var st = StateOf(m, d);
            if (st == SkillCardView.State.FullGame) lead.Add((Nice(d.DisplayName), 2, DemoMode.T("demo.skill.leads_full")));   // Demo
            else if (st == SkillCardView.State.Owned) lead.Add((Nice(d.DisplayName), 1, ownedWord));
            else if (st == SkillCardView.State.Available)
                lead.Add((Nice(d.DisplayName), 3, L10n.Get(Table, "skillboard.link.can_unlock", ("cost", d.cost))));
            else lead.Add((Nice(d.DisplayName), 2, L10n.Get(Table, "skillboard.cost", ("cost", d.cost))));
        }
        if (lead.Count == 0)
            lead.Add((secret ? L10n.Get(Table, "skillboard.link.none.secret") : L10n.Get(Table, "skillboard.link.none.leads"), 0, ""));
        FillRow(leads, lead);

        // action
        int sp = m.skillPoints;
        bool owned = state == SkillCardView.State.Owned;
        if (actionButton != null) actionButton.gameObject.SetActive(!owned);
        if (unlockedStamp != null) unlockedStamp.SetActive(owned);
        string hintText;
        if (owned)
        {
            hintText = m.IsDemoGift(s)   // Demo: the Board's free license explains itself
                ? DemoMode.T("demo.skill.gift_hint")
                : L10n.Get(Table, "skillboard.hint.owned", ("cost", s.cost), ("left", sp));
        }
        else if (state == SkillCardView.State.FullGame)
        {
            // Demo: readable, but it opens only in the full game
            SetAction(false, lockIcon, DemoMode.T("demo.skill.action"));
            hintText = DemoMode.T("demo.skill.hint", ("tier", DemoMode.Config.maxSkillTier));
        }
        else if (secret && !revealed)
        {
            SetAction(false, lockIcon, L10n.Get(Table, "skillboard.action.sealed"));
            hintText = L10n.Get(Table, "skillboard.hint.sealed", ("count", MissingCount(m)));
        }
        else if (state == SkillCardView.State.Available)
        {
            bool afford = sp >= s.cost;
            SetAction(afford, sparkleIcon, afford
                ? L10n.Get(Table, "skillboard.action.unlock", ("cost", s.cost))
                : L10n.Get(Table, "skillboard.action.need", ("cost", s.cost)));
            hintText = afford ? "" : L10n.Get(Table, "skillboard.hint.need_points", ("sp", sp), ("count", m.GetHiresUntilNextPoint()));
        }
        else
        {
            SetAction(false, lockIcon, L10n.Get(Table, "skillboard.action.locked"));
            hintText = L10n.Get(Table, "skillboard.hint.locked", ("skills", MissingChain(m, s)));
        }
        SetText(hint, hintText);
    }

    void SetAction(bool interactable, Sprite icon, string label)
    {
        if (actionButton != null) actionButton.interactable = interactable;
        if (actionIcon != null && icon != null) actionIcon.sprite = icon;
        if (actionLabel != null) actionLabel.text = label;
    }

    static void SetText(TMP_Text t, string text)
    {
        if (t == null) return;
        t.text = text;
        bool on = !string.IsNullOrEmpty(text);
        if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on);
    }

    void FillRow(ReqRow row, List<(string name, int kind, string status)> items)
    {
        if (row == null || row.names == null) return;
        for (int i = 0; i < row.names.Length; i++)
        {
            bool used = i < items.Count;
            var n = row.names[i];
            var st = row.statuses != null && i < row.statuses.Length ? row.statuses[i] : null;
            if (n != null && n.gameObject.activeSelf != used) n.gameObject.SetActive(used);
            if (!used)
            {
                if (st != null && st.activeSelf) st.SetActive(false);
                continue;
            }
            var (name, kind, status) = items[i];
            if (n != null)
            {
                n.text = name;
                n.color = kind == 0 ? UIColors.InkSoft : UIColors.Ink;
            }
            bool hasStatus = kind != 0;
            if (st != null && st.activeSelf != hasStatus) st.SetActive(hasStatus);
            if (!hasStatus) continue;
            Color c = kind == 1 ? UIColors.Money : kind == 3 ? UIColors.Ink : UIColors.Pencil;
            if (row.statusIcons != null && i < row.statusIcons.Length && row.statusIcons[i] != null)
            {
                row.statusIcons[i].sprite = kind == 1 ? checkIcon : kind == 3 ? sparkleIcon : lockIcon;
                row.statusIcons[i].color = c;
            }
            if (row.statusTexts != null && i < row.statusTexts.Length && row.statusTexts[i] != null)
            {
                row.statusTexts[i].text = status;
                row.statusTexts[i].color = c;
            }
        }
    }

    int MissingCount(SkillTreeManager m)
    {
        int n = 0;
        foreach (var s in m.allSkills)
            if (s != null && s.id != SecretId && !s.unlocked) n++;
        return n;
    }

    /// <summary>"Lucky Hire, Corporate Roulette and Chaotic Energy": every locked skill on the way to this one.</summary>
    string MissingChain(SkillTreeManager m, Skill s)
    {
        var missing = new List<Skill>();
        var seen = new HashSet<string>();
        void Walk(Skill k)
        {
            if (k?.prerequisites == null) return;
            foreach (string id in k.prerequisites)
            {
                if (id == null || !seen.Add(id)) continue;
                var p = m.GetSkill(id);
                if (p == null || p.unlocked) continue;
                Walk(p);
                missing.Add(p);
            }
        }
        Walk(s);
        missing.Sort((a, b) => a.tier != b.tier ? a.tier.CompareTo(b.tier) : m.allSkills.IndexOf(a).CompareTo(m.allSkills.IndexOf(b)));
        var names = missing.ConvertAll(k => Nice(k.DisplayName));
        if (names.Count == 0) return L10n.Get(Table, "skillboard.list.before");
        if (names.Count > 4) return L10n.Get(Table, "skillboard.list.more", ("list", JoinNames(names, 3)), ("count", names.Count - 3));
        if (names.Count == 1) return names[0];
        return L10n.Get(Table, "skillboard.list.and", ("list", JoinNames(names, names.Count - 1)), ("last", names[names.Count - 1]));
    }

    /// <summary>The first <paramref name="count"/> names joined with the language's list comma ("A, B", "A、B").</summary>
    static string JoinNames(List<string> names, int count)
    {
        string list = names[0];
        for (int i = 1; i < count; i++)
            list = L10n.Get(Table, "skillboard.list.comma", ("list", list), ("next", names[i]));
        return list;
    }

    void DrawSticky(SkillTreeManager m)
    {
        if (spText != null) spText.text = m.skillPoints.ToString(System.Globalization.CultureInfo.InvariantCulture);
        int per = Mathf.Max(1, m.hiresPerSkillPoint);
        int done = Mathf.Clamp(m.HighIQHiresTowardNextPoint, 0, per);   // Demo: fix for both modes (the modulo was wrong after a take-back)
        if (progressText != null)
            progressText.text = L10n.Get(Table, "skillboard.sticky.progress", ("progress", "<b>" + done + " / " + per + "</b>"));
        if (progressFill != null)
        {
            progressFill.anchorMin = Vector2.zero;
            progressFill.anchorMax = new Vector2(done / (float)per, 1f);
            progressFill.offsetMin = progressFill.offsetMax = Vector2.zero;
        }
        var c = CompanyManager.Instance;
        if (c != null && companyValueText != null)
        {
            shownValue = c.companyValue;
            companyValueText.text = UIFormat.Money(shownValue);
        }
    }

    void DrawTiers()
    {
        for (int i = 0; i < Tiers; i++)
        {
            if (tierLabels != null && i < tierLabels.Length && tierLabels[i] != null)
                tierLabels[i].text = L10n.Get(Table, "skillboard.tier", ("tier", i + 1));
            if (tierCosts != null && i < tierCosts.Length && tierCosts[i] != null)
                tierCosts[i].text = DemoMode.Active && i + 1 > DemoMode.Config.maxSkillTier
                    ? DemoMode.T("demo.skill.tier")   // Demo: tiers kept for the full game
                    : L10n.Get(Table, "skillboard.cost", ("cost", i + 1));   // a skill costs its tier in SP
        }
    }

    void DrawTabs(SkillTreeManager m)
    {
        var owned = new int[3];
        var total = new int[3];
        foreach (var s in m.allSkills)
        {
            if (s == null || s.id == SecretId) continue;
            int b = Mathf.Clamp((int)s.branch, 0, 2);
            if (!m.IsDemoLocked(s)) total[b]++;   // Demo: owned / available in the demo (IsDemoLocked is never true for owned skills)
            if (s.unlocked) owned[b]++;
        }
        for (int i = 0; i < tabs.Length; i++)
        {
            var tab = tabs[i];
            if (tab == null) continue;
            bool active = (int)branch == i;
            if (tab.activeLook != null && tab.activeLook.activeSelf != active) tab.activeLook.SetActive(active);
            if (tab.inactiveLook != null && tab.inactiveLook.activeSelf == active) tab.inactiveLook.SetActive(!active);
            string count = L10n.Get(Table, "skillboard.tab.count", ("owned", owned[i]), ("total", total[i]));
            if (tab.activeCount != null) tab.activeCount.text = count;
            if (tab.inactiveCount != null) tab.inactiveCount.text = count;
        }
    }

    void DrawEnvelope(SkillTreeManager m)
    {
        var secret = m.GetSkill(SecretId);
        if (envelope != null && envelope.gameObject.activeSelf != (secret != null)) envelope.gameObject.SetActive(secret != null);
        if (secret == null) return;
        string percent = SkillText.PercentNumber(secret.effectValue);
        if (secret.unlocked)
        {
            if (envelopeTitle != null) envelopeTitle.text = Nice(secret.DisplayName);
            if (envelopeText != null) envelopeText.text = L10n.Get(Table, "skillboard.secret.text.opened", ("percent", percent));
            if (sealIcon != null) sealIcon.sprite = checkIcon;
        }
        else if (m.IsSecretSkillRevealed())
        {
            if (envelopeTitle != null) envelopeTitle.text = Nice(secret.DisplayName);
            if (envelopeText != null)
                envelopeText.text = L10n.Get(Table, "skillboard.secret.text.revealed", ("cost", secret.cost), ("percent", percent));
            if (sealIcon != null) sealIcon.sprite = sparkleIcon;
        }
        else
        {
            if (envelopeTitle != null)
                envelopeTitle.text = L10n.Get(Table, "skillboard.secret.title.sealed", ("skill", Nice(secret.DisplayName)));
            if (envelopeText != null)
                envelopeText.text = DemoMode.Active   // Demo: sealed until the full game
                    ? DemoMode.T("demo.skill.envelope")
                    : L10n.Get(Table, "skillboard.secret.text.sealed", ("percent", percent));
            if (sealIcon != null) sealIcon.sprite = lockIcon;
        }
    }
}
