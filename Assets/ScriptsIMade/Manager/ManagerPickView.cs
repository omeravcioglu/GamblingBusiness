using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The candidate pick: a memo from the Board ("hire a manager") over three CV cards, one per candidate: portrait, name,
/// personality note, experience and age, a short bio, how they tend to advise, the salary per year and per day, and a
/// Hire button. "Decide later" (or Escape) closes it; the side chip keeps saying "Pick your manager".
/// A pausing UIManager panel on its own overlay canvas (sorting order 136), built at runtime and rebuilt on language changes.
/// As designed (canvas page "Manager", 2026-09-29): a colour band per personality, a "Would tell you" box with the
/// personality's real recommendation for three situations (<see cref="Examples"/>), and "Decide later" as a paper button.
/// </summary>
[DisallowMultipleComponent]
public class ManagerPickView : MonoBehaviour
{
    [Tooltip("Sorting order of the pick's canvas")]
    public int sortingOrder = 136;

    const float SheetW = 1500f, SheetH = 956f;
    const float CardW = 452f, CardH = 646f, CardGap = 24f, CardTop = 236f;
    const float InputDelay = 0.5f;

    /// <summary>
    /// The situations whose recommendation each candidate shows ("mgr.ui.pick.would.&lt;id&gt;" labels them). A choice text with
    /// placeholders needs a "&lt;its key&gt;.short" entry in Manager.csv: nothing fills the placeholders here.
    /// </summary>
    static readonly string[] Examples = { "casino_night", "nearly_full", "lawsuit" };

    Canvas canvas;
    RectTransform canvasRect;
    RectTransform screen;
    RectTransform sheet;
    RectTransform content;
    bool dirty;
    float openAge;
    float fitScale = 1f;

    public bool IsOpen { get; private set; }

    void OnEnable()
    {
        L10n.LanguageChanged += MarkDirty;
        ManagerSystem.Changed += OnSystemChanged;
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= MarkDirty;
        ManagerSystem.Changed -= OnSystemChanged;
        if (IsOpen) Close();
    }

    void OnDestroy()
    {
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
        if (canvas != null) Destroy(canvas.gameObject);
    }

    void MarkDirty() => dirty = true;

    void OnSystemChanged()
    {
        // The candidates are gone (picked, or a save loaded): nothing left to show
        ManagerSystem system = ManagerSystem.Instance;
        if (IsOpen && (system == null || system.Phase != ManagerPhase.Picking)) Close();
    }

    public void Open()
    {
        ManagerSystem system = ManagerSystem.Instance;
        if (system == null || system.Phase != ManagerPhase.Picking) return;
        EnsureBuilt();
        IsOpen = true;
        openAge = 0f;
        screen.gameObject.SetActive(true);
        UIManager.Instance.OpenPanel(this, true, Close);
        HeatUI.ClearSelection();
        Rebuild();
        GameSfx.Play(Sfx.UiBong);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        if (screen != null) screen.gameObject.SetActive(false);
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
    }

    void Update()
    {
        if (!IsOpen) return;
        openAge += Time.unscaledDeltaTime;
        if (UIManager.Exists && !UIManager.Instance.IsPanelOpen(this)) UIManager.Instance.OpenPanel(this, true, Close);
        if (dirty) Rebuild();
        float scale = ManagerUI.FitScale(canvasRect, SheetW, SheetH + 60f, 16f);
        if (!Mathf.Approximately(scale, fitScale))
        {
            fitScale = scale;
            sheet.localScale = new Vector3(scale, scale, 1f);
        }
    }

    void EnsureBuilt()
    {
        if (canvas != null) return;
        canvas = HeatUI.CreateCanvas("ManagerPickCanvas (Runtime)", sortingOrder, transform, true);
        canvasRect = (RectTransform)canvas.transform;
        screen = StatusUI.NewRect("Pick", canvasRect);
        StatusUI.Stretch(screen);
        Image scrim = HeatUI.Block(screen, "Scrim", UIColors.Scrim, true);
        StatusUI.Stretch(scrim.rectTransform);
        HeatUI.PausedChip(screen, ManagerUI.Font);
        sheet = StatusUI.NewRect("Sheet", screen);
        sheet.anchorMin = sheet.anchorMax = new Vector2(0.5f, 0.5f);
        sheet.pivot = new Vector2(0.5f, 0.5f);
        sheet.sizeDelta = new Vector2(SheetW, SheetH);
        sheet.anchoredPosition = new Vector2(0f, -24f);
        screen.gameObject.SetActive(false);
    }

    void Rebuild()
    {
        dirty = false;
        ManagerSystem system = ManagerSystem.Instance;
        if (system == null || system.Phase != ManagerPhase.Picking)
        {
            Close();
            return;
        }
        if (content != null)
        {
            content.gameObject.SetActive(false);   // gone this frame (Destroy waits for the end of it)
            Destroy(content.gameObject);
        }
        content = StatusUI.NewRect("Content", sheet);
        StatusUI.Place(content, 0f, 0f, SheetW, SheetH);

        // The Board's memo across the top
        StatusBox memo = ManagerUI.Sheet(content, "Memo", UIColors.PaperHi, 0f, 0f, SheetW, 208f);
        memo.raycastTarget = true;
        RectTransform m = memo.rectTransform;
        TextMeshProUGUI kicker = ManagerUI.Text(m, "Kicker", ManagerText.Get("mgr.ui.pick.kicker"), 17f, UIColors.Pencil, 40f, 20f, 900f, 28f, TextAlignmentOptions.MidlineLeft);
        kicker.characterSpacing = 6f;
        ManagerUI.Text(m, "Title", ManagerText.Get("mgr.ui.pick.title"), 34f, UIColors.Ink, 40f, 47f, SheetW - 80f, 54f, TextAlignmentOptions.MidlineLeft, true);
        ManagerUI.Block(m, "Body", ManagerText.Get("mgr.ui.pick.body"), 20f, UIColors.InkSoft, 40f, 104f, SheetW - 80f, out _, false, 1.3f, 96f);

        IReadOnlyList<ManagerPerson> candidates = system.Candidates;
        int n = candidates.Count;
        float total = n * CardW + (n - 1) * CardGap;
        float x = (SheetW - total) * 0.5f;
        for (int i = 0; i < n; i++)
        {
            float tilt = i == 0 ? 0.8f : i == 1 ? -0.5f : 0.6f;
            BuildCard(candidates[i], i, x + i * (CardW + CardGap), CardTop, tilt);
        }

        // "Decide later" [Esc]: a paper button under the cards, the key cap on its right
        string laterLabel = ManagerText.Get("mgr.ui.pick.later");
        TextMeshProUGUI probe = ManagerUI.Text(content, "Probe", laterLabel, 21f, UIColors.Ink, 0f, 0f, 900f, 40f, TextAlignmentOptions.MidlineLeft, true);
        float laterW = Mathf.Clamp(Mathf.Ceil(probe.GetPreferredValues(laterLabel, 1200f, 0f).x) + 18f + 12f + 52f + 16f, 220f, 520f);
        probe.gameObject.SetActive(false);   // gone this frame (Destroy waits for the end of it)
        Destroy(probe.gameObject);
        RectTransform holder = StatusUI.NewRect("LaterHolder", content);
        StatusUI.Stretch(holder);
        holder.gameObject.SetActive(false);   // MarkerButton reads the face's home position when it wakes up
        Button later = ManagerUI.PaperButton(holder, "Later", laterLabel, UIColors.PaperHi, 21f, (SheetW - laterW) * 0.5f, SheetH - 58f, laterW, 52f, out TextMeshProUGUI laterText);
        StatusUI.Stretch(laterText.rectTransform, 18f, 18f + 52f + 12f, 0f, 0f);
        holder.gameObject.SetActive(true);
        Transform face = later.transform.Find("Face");
        if (face != null) ManagerUI.Keycap((RectTransform)face, "Esc", 17f, laterW - 16f, (52f - 36f) * 0.5f, true);
        later.onClick.AddListener(Close);
    }

    void BuildCard(ManagerPerson c, int index, float x, float y, float tilt)
    {
        StatusBox card = ManagerUI.Sheet(content, "Candidate" + index, UIColors.PaperHi, x, y, CardW, CardH);
        card.raycastTarget = true;
        RectTransform r = card.rectTransform;
        r.pivot = new Vector2(0.5f, 0.5f);
        r.anchoredPosition = new Vector2(x + CardW * 0.5f, -(y + CardH * 0.5f));
        r.localEulerAngles = new Vector3(0f, 0f, tilt);
        // The personality's colour along the top edge, under the tape
        StatusBox band = StatusUI.Box(r, "Band", ManagerUI.PersonalityColor(c.personality), Color.clear, 0f, new Vector4(20f, 24f, 0f, 0f));
        StatusUI.Place(band.rectTransform, 3f, 3f, CardW - 6f, 12f);
        Image bandEdge = StatusUI.Block(r, "BandEdge", UIColors.WithAlpha(UIColors.Ink, 0.8f));
        StatusUI.Place(bandEdge.rectTransform, 3f, 15f, CardW - 6f, 2f);
        HeatUI.Tape(r, 120f, -tilt * 2f);

        const float pad = 28f;
        float inner = CardW - pad * 2f;
        ManagerUI.Portrait(r, "Portrait", c.portraitIndex, c.name, 124f, pad, 34f);
        float right = pad + 124f + 18f, rightW = CardW - right - pad;
        TextMeshProUGUI name = ManagerUI.Text(r, "Name", c.name, 25f, UIColors.Ink, right, 38f, rightW, 40f, TextAlignmentOptions.MidlineLeft, true);
        name.overflowMode = TextOverflowModes.Ellipsis;
        HeatUI.Shrink(name, 17f);
        ManagerUI.Sticky(r, "Style", ManagerText.PersonalityName(c.personality), ManagerUI.PersonalityColor(c.personality), 17f, right + 2f, 84f, -2f, out _);
        ManagerUI.Text(r, "Years", ManagerText.Get("mgr.ui.pick.years", ("years", c.years), ("age", c.age)), 16f, UIColors.Pencil, right, 128f, rightW, 26f, TextAlignmentOptions.MidlineLeft);

        // "Would tell you": built first (bottom-anchored above the salary rule), so the bio and advice get the room above it
        float boxBottom = CardH - 152f;
        RectTransform would = BuildWould(r, c.personality, pad, inner, out float wouldH);
        float boxTop = would != null ? boxBottom - wouldH : boxBottom;
        if (would != null) StatusUI.Place(would, pad, boxTop, inner, wouldH);

        var args = new Dictionary<string, object> { { "name", c.name }, { "years", c.years.ToString() } };
        float yy = 180f;
        float room = boxTop - 12f - yy;
        ManagerUI.Block(r, "Bio", ManagerText.PersonalityBio(c.personality, c.bio, args), 19f, UIColors.Ink, pad, yy, inner, out float bioH, false, 1.3f,
                        Mathf.Clamp(room - 60f, 26f, 110f));
        yy += bioH + 12f;
        float adviceRoom = boxTop - 10f - yy;
        if (adviceRoom >= 22f)
        {
            string styleHeading = "<b>" + ManagerText.Get("mgr.ui.pick.style") + "</b> ";
            ManagerUI.Block(r, "Advice", styleHeading + ManagerText.PersonalityStyle(c.personality), 17f, UIColors.InkSoft, pad, yy, inner, out _, false, 1.3f,
                            Mathf.Min(adviceRoom, 90f));
        }

        // Salary (per year, and per day on the right) and the Hire button at the bottom
        Image rule = StatusUI.Block(r, "Rule", UIColors.WithAlpha(UIColors.Ink, 0.25f));
        StatusUI.Place(rule.rectTransform, pad, CardH - 140f, inner, 2f);
        TextMeshProUGUI salary = ManagerUI.Text(r, "Salary", ManagerText.Get("mgr.ui.pick.salary", ("amount", UIFormat.Money(c.salary))), 22f, UIColors.Ink,
                                                pad, CardH - 132f, inner * 0.56f, 36f, TextAlignmentOptions.MidlineLeft, true);
        HeatUI.Shrink(salary, 15f);
        TextMeshProUGUI daily = ManagerUI.Text(r, "Daily", ManagerText.Get("mgr.ui.pick.daily", ("amount", UIFormat.Money(c.salary / 365.0))), 16f, UIColors.Pencil,
                                               pad + inner * 0.56f, CardH - 132f, inner * 0.44f, 36f, TextAlignmentOptions.MidlineRight);
        HeatUI.Shrink(daily, 12f);

        RectTransform holder = StatusUI.NewRect("HireHolder", r);
        StatusUI.Stretch(holder);
        holder.gameObject.SetActive(false);   // MarkerButton reads the face's home position when it wakes up
        Button hire = ManagerUI.PaperButton(holder, "Hire", ManagerText.Get("mgr.ui.pick.hire", ("name", FirstName(c.name))), UIColors.Money, 22f, pad, CardH - 84f, inner, 58f, out TextMeshProUGUI label);
        label.color = UIColors.PaperHi;
        MarkerButton marker = hire.GetComponent<MarkerButton>();
        if (marker != null) marker.SetContentColor(0, UIColors.PaperHi);
        holder.gameObject.SetActive(true);
        int i = index;
        hire.onClick.AddListener(() => Pick(i));
    }

    /// <summary>
    /// The "Would tell you" box: for each of <see cref="Examples"/>, its short label and what this personality recommends there
    /// (the situation files' "recommend"). Null when none resolves; placed at y 0 (the caller moves it), height in <paramref name="height"/>.
    /// </summary>
    RectTransform BuildWould(RectTransform card, string personality, float x, float width, out float height)
    {
        height = 0f;
        ManagerSystem system = ManagerSystem.Instance;
        ManagerLibrary library = system != null ? system.Library : null;
        if (library == null) return null;
        var rows = new List<KeyValuePair<string, string>>();
        foreach (string id in Examples)
        {
            ManagerSituationDef def = library.Get(id);
            string labelKey = "mgr.ui.pick.would." + id;
            if (def == null || !ManagerText.Has(labelKey)) continue;
            ManagerChoiceDef recommended = ManagerAdvisor.Recommend(def, personality, null);
            if (recommended == null || string.IsNullOrEmpty(recommended.TextKey)) continue;
            string shortKey = recommended.TextKey + ".short";
            string advice = ManagerText.Has(shortKey) ? ManagerText.Get(shortKey) : ManagerText.Get(recommended.TextKey);
            rows.Add(new KeyValuePair<string, string>(ManagerText.Get(labelKey), advice));
        }
        if (rows.Count == 0) return null;

        StatusBox box = StatusUI.Box(card, "Would", UIColors.Paper, UIColors.WithAlpha(UIColors.Ink, 0.35f), 2f, ManagerUI.SmallRadii);
        RectTransform r = StatusUI.Place(box.rectTransform, x, 0f, width, 40f);
        TextMeshProUGUI label = ManagerUI.Text(r, "Label", ManagerText.Get("mgr.ui.pick.would"), 15f, UIColors.Pencil, 12f, 8f, width - 24f, 20f,
                                               TextAlignmentOptions.MidlineLeft, true);
        label.characterSpacing = 4f;
        const float situationW = 132f, arrowW = 20f;
        float adviceX = 12f + situationW + arrowW, adviceW = width - adviceX - 12f;
        float y = 34f;
        for (int i = 0; i < rows.Count; i++)
        {
            TextMeshProUGUI situation = ManagerUI.Text(r, "Situation" + i, rows[i].Key, 16f, UIColors.Pencil, 12f, y, situationW, 22f, TextAlignmentOptions.TopLeft);
            situation.overflowMode = TextOverflowModes.Ellipsis;
            HeatUI.Shrink(situation, 12f);
            ManagerUI.Text(r, "Arrow" + i, "›", 18f, UIColors.InkSoft, 12f + situationW, y - 2f, arrowW, 24f, TextAlignmentOptions.TopLeft);
            ManagerUI.Block(r, "Advice" + i, rows[i].Value, 17f, UIColors.Ink, adviceX, y - 1f, adviceW, out float h, true, 1.15f, 42f);
            y += Mathf.Max(22f, h) + 4f;
        }
        height = y + 6f;
        return r;
    }

    static string FirstName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        string[] parts = name.Trim().Split(' ');
        return parts[0];
    }

    void Pick(int index)
    {
        if (openAge < InputDelay) return;
        ManagerSystem system = ManagerSystem.Instance;
        if (system == null) return;
        Close();
        if (!system.PickCandidate(index)) return;
        GameSfx.Play(Sfx.UiConfirm);
        system.OpenDesk();   // their first message is on the desk
    }
}
