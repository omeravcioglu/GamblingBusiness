using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The manager's desk: a manila folder with the manager's card and the in-tray of pending decisions on the left, and the
/// selected decision on the right: its title, the manager's message next to their portrait, their take (after "Ask the
/// manager"), and 2-4 choices, each with its consequence hint; the manager's recommendation carries an "I'd do this"
/// note, an unavailable choice says why. Urgent decisions wear a stamp with the days left.
/// - A pausing UIManager panel (Escape or Close closes it). Opened from the side chip, or by an urgent decision that
///   interrupts (ManagerSystem decides when). Clicks in the first moments after it opens don't count.
/// - Built at runtime on its own overlay canvas (sorting order 135) and rebuilt whenever something changes (a choice made,
///   a decision added, the language).
/// - As designed (canvas page "Manager", 2026-09-29): a mood meter on the manager's card, "Decided lately" under the in-tray
///   (when there's room), a line under each decision saying what happens without an answer, a red "knocked" note while the
///   decision that interrupted is shown, and M closes the desk as it opens it.
/// </summary>
[DisallowMultipleComponent]
public class ManagerDeskView : MonoBehaviour
{
    [Tooltip("Sorting order of the desk's canvas: above the HUD and the chips, below board meetings and toasts")]
    public int sortingOrder = 135;
    [Tooltip("Key that closes the desk (the side chip opens it with the same key)")]
    public KeyCode toggleKey = KeyCode.M;

    const float SheetW = 1520f, SheetH = 880f;
    const float LeftX = 28f, LeftW = 400f;
    const float PageX = 452f, PageY = 28f, PageW = 1040f, PageH = 824f, Pad = 36f;
    const float InputDelay = 0.35f;
    const float TrayTop = 246f, ColumnBottom = SheetH - 28f;
    const float FootH = 30f;   // the "no answer" line at the page's foot
    const int RecentMax = 3;

    Canvas canvas;
    RectTransform canvasRect;
    RectTransform screen;
    RectTransform sheet;
    RectTransform content;
    int selectedUid;
    int interruptUid;   // the urgent decision that opened the desk by itself (0 = the player opened it)
    bool dirty;
    float openAge;
    float fitScale = 1f;

    public bool IsOpen { get; private set; }
    /// <summary>The last decision page built: its text scale (1, or 0.86 when long texts had to shrink) and whether it fit.</summary>
    public float LastTextScale { get; private set; } = 1f;
    public bool LastFit { get; private set; } = true;

    void OnEnable()
    {
        ManagerSystem.Changed += MarkDirty;
        L10n.LanguageChanged += MarkDirty;
    }

    void OnDisable()
    {
        ManagerSystem.Changed -= MarkDirty;
        L10n.LanguageChanged -= MarkDirty;
        if (IsOpen) Close();
    }

    void OnDestroy()
    {
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
        if (canvas != null) Destroy(canvas.gameObject);
    }

    void MarkDirty() => dirty = true;

    // ------------------------------------------------------------------
    // Opening and closing
    // ------------------------------------------------------------------

    /// <summary>
    /// Open the desk; <paramref name="focusUid"/> selects a decision (0 = the first). <paramref name="interrupted"/>: that
    /// decision opened the desk by itself (an urgent one), so the manager "knocked".
    /// </summary>
    public void Open(int focusUid = 0, bool interrupted = false)
    {
        EnsureBuilt();
        ManagerSystem system = ManagerSystem.Instance;
        if (system == null) return;
        selectedUid = focusUid != 0 && system.FindPending(focusUid) != null ? focusUid : FirstUid(system);
        interruptUid = interrupted && selectedUid == focusUid ? focusUid : 0;
        IsOpen = true;
        openAge = 0f;
        screen.gameObject.SetActive(true);
        UIManager.Instance.OpenPanel(this, true, Close);
        HeatUI.ClearSelection();
        Rebuild();
        GameSfx.Play(Sfx.UiOpen);
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        interruptUid = 0;
        if (screen != null) screen.gameObject.SetActive(false);
        if (UIManager.Exists) UIManager.Instance.ClosePanel(this);
    }

    static int FirstUid(ManagerSystem system)
    {
        IReadOnlyList<ManagerPending> pending = system.Pending;
        // Urgent ones first, then the oldest
        foreach (ManagerPending p in pending) if (p.urgent) return p.uid;
        return pending.Count > 0 ? pending[0].uid : 0;
    }

    void Update()
    {
        if (!IsOpen) return;
        openAge += Time.unscaledDeltaTime;
        // Something reset the UI state (a debug tool) while the desk was open: keep the game paused
        if (UIManager.Exists && !UIManager.Instance.IsPanelOpen(this)) UIManager.Instance.OpenPanel(this, true, Close);
        // M closes it again (not in the first moments: the key press that opened it is still down)
        if (toggleKey != KeyCode.None && openAge >= InputDelay && Input.GetKeyDown(toggleKey) && !UIManager.IsTypingInInputField())
        {
            Close();
            return;
        }
        if (dirty) Rebuild();
        float scale = ManagerUI.FitScale(canvasRect, SheetW, SheetH + 60f, 16f);
        if (!Mathf.Approximately(scale, fitScale))
        {
            fitScale = scale;
            sheet.localScale = new Vector3(scale, scale, 1f);
        }
    }

    // ------------------------------------------------------------------
    // Building
    // ------------------------------------------------------------------

    void EnsureBuilt()
    {
        if (canvas != null) return;
        canvas = HeatUI.CreateCanvas("ManagerDeskCanvas (Runtime)", sortingOrder, transform, true);
        canvasRect = (RectTransform)canvas.transform;
        screen = StatusUI.NewRect("Desk", canvasRect);
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
        if (system == null || sheet == null)
        {
            Close();
            return;
        }
        if (system.FindPending(selectedUid) == null) selectedUid = FirstUid(system);
        if (content != null)
        {
            content.gameObject.SetActive(false);   // gone this frame (Destroy waits for the end of it)
            Destroy(content.gameObject);
        }
        content = StatusUI.NewRect("Content", sheet);
        StatusUI.Place(content, 0f, 0f, SheetW, SheetH);

        // The folder: its tab, then the folder itself over the tab's lower edge
        StatusBox tab = StatusUI.Box(content, "Tab", UIColors.Manila, UIColors.Ink, 3f, new Vector4(16f, 18f, 0f, 0f));
        StatusUI.Place(tab.rectTransform, 40f, -48f, 360f, 66f);
        ManagerUI.Text(tab.rectTransform, "Label", ManagerText.Get("mgr.ui.desk.tab"), 22f, UIColors.Ink, 22f, 8f, 316f, 36f, TextAlignmentOptions.MidlineLeft, true)
                 .characterSpacing = 4f;
        StatusBox folder = ManagerUI.Sheet(content, "Folder", UIColors.Manila, 0f, 0f, SheetW, SheetH);
        folder.raycastTarget = true;

        BuildManagerCard(system);
        BuildTray(system);
        BuildPage(system);
        if (interruptUid != 0 && interruptUid == selectedUid) BuildKnocked(system);

        RectTransform holder = StatusUI.NewRect("CloseHolder", content);
        StatusUI.Stretch(holder);
        holder.gameObject.SetActive(false);   // MarkerButton reads the face's home position when it wakes up
        Button close = ManagerUI.PaperButton(holder, "Close", ManagerText.Get("mgr.ui.desk.close"), UIColors.PaperHi, 20f, SheetW - 36f - 170f, -44f, 170f, 52f, out _);
        close.onClick.AddListener(Close);
        holder.gameObject.SetActive(true);
    }

    void BuildManagerCard(ManagerSystem system)
    {
        StatusBox card = StatusUI.Box(content, "ManagerCard", UIColors.PaperHi, UIColors.Ink, 2f, ManagerUI.SmallRadii);
        RectTransform c = StatusUI.Place(card.rectTransform, LeftX, 28f, LeftW, 168f);
        if (system.HasManager)
        {
            ManagerPerson m = system.Manager;
            ManagerUI.Portrait(c, "Portrait", m.portraitIndex, m.name, 96f, 18f, 18f);
            TextMeshProUGUI name = ManagerUI.Text(c, "Name", m.name, 24f, UIColors.Ink, 130f, 8f, 256f, 38f, TextAlignmentOptions.MidlineLeft, true);
            name.overflowMode = TextOverflowModes.Ellipsis;
            HeatUI.Shrink(name, 17f);
            ManagerUI.Sticky(c, "Style", ManagerText.PersonalityName(m.personality), ManagerUI.PersonalityColor(m.personality), 16f, 132f, 48f, 2f, out _);
            string mood = "<color=" + HeatUI.Hex(ManagerText.SatisfactionColor(m.satisfaction)) + "><b>" + ManagerText.SatisfactionWord(m.satisfaction) + "</b></color>";
            ManagerUI.Text(c, "Mood", ManagerText.Get("mgr.ui.desk.mood", ("mood", mood)), 18f, UIColors.Ink, 130f, 84f, 256f, 28f, TextAlignmentOptions.MidlineLeft);
            // The mood bands with a pin at their satisfaction (content 70+, fine 50+, restless 35+, unhappy 20+, fed up)
            ManagerUI.MoodMeter(c, m.satisfaction, 132f, 118f, 150f);
            TextMeshProUGUI salary = ManagerUI.Text(c, "Salary", ManagerText.Get("mgr.ui.desk.salary", ("amount", UIFormat.Money(m.salary))), 16f, UIColors.Pencil,
                                                    130f, 138f, 256f, 26f, TextAlignmentOptions.MidlineLeft);
            salary.overflowMode = TextOverflowModes.Ellipsis;
            HeatUI.Shrink(salary, 13f);
        }
        else
        {
            ManagerUI.Portrait(c, "Portrait", -1, "?", 96f, 18f, 18f);
            ManagerUI.Text(c, "Name", ManagerText.Get("mgr.ui.chip.gap"), 24f, UIColors.Ink, 130f, 28f, 256f, 38f, TextAlignmentOptions.MidlineLeft, true);
            string when = system.Phase == ManagerPhase.Gap ? ManagerText.Get("mgr.ui.chip.gap_line", ("day", system.GapUntilDay)) : "";
            TextMeshProUGUI w = ManagerUI.Text(c, "When", when, 17f, UIColors.Pencil, 130f, 72f, 256f, 60f, TextAlignmentOptions.TopLeft);
            HeatUI.Wrapped(w, 1.25f);
        }
    }

    void BuildTray(ManagerSystem system)
    {
        IReadOnlyList<ManagerPending> pending = system.Pending;
        TextMeshProUGUI label = ManagerUI.Text(content, "TrayLabel", ManagerText.Get("mgr.ui.desk.tray", ("count", pending.Count)), 17f, UIColors.InkSoft,
                                               LeftX + 4f, 212f, LeftW, 28f, TextAlignmentOptions.MidlineLeft, true);
        label.characterSpacing = 4f;
        // "Decided lately" under the tray, as long as the cards keep their full height with it (fewer entries, or none, otherwise)
        List<ManagerHistory> recent = Recent(system);
        int n = Mathf.Max(1, pending.Count);
        float gap = 8f;
        int shown = recent.Count;
        while (shown > 0 && TrayTop + n * (86f + gap) + 12f + RecentHeight(shown) > ColumnBottom) shown--;
        float top = TrayTop, bottom = shown > 0 ? ColumnBottom - RecentHeight(shown) - 12f : ColumnBottom;
        float cardH = Mathf.Min(86f, (bottom - top) / n - gap);
        if (shown > 0) BuildRecent(recent, shown, ColumnBottom - RecentHeight(shown));
        int day = CompanyManager.Instance != null ? CompanyManager.Instance.currentDay : 0;
        for (int i = 0; i < pending.Count; i++)
        {
            ManagerPending p = pending[i];
            ManagerSituationDef def = system.Library.Get(p.situation);
            if (def == null) continue;
            bool selected = p.uid == selectedUid;
            StatusBox card = StatusUI.Box(content, "Card" + i, selected ? UIColors.StickyYellow : UIColors.PaperHi, UIColors.Ink, selected ? 3f : 2f, ManagerUI.SmallRadii);
            if (selected) card.SetShadow(UIColors.Ink, new Vector2(3f, 4f));
            card.raycastTarget = true;
            RectTransform r = StatusUI.Place(card.rectTransform, LeftX, top + i * (cardH + gap), LeftW, cardH);
            TextMeshProUGUI title = ManagerUI.Text(r, "Title", system.Title(def, p), 18f, UIColors.Ink, 16f, 8f, LeftW - 44f, cardH - 36f, TextAlignmentOptions.TopLeft, true);
            HeatUI.Wrapped(title, 1.15f);
            title.overflowMode = TextOverflowModes.Ellipsis;
            string status;
            Color statusColor = UIColors.Pencil;
            int left = Mathf.Max(0, p.expiresDay - day);
            if (p.urgent)
            {
                status = ManagerText.Get("mgr.ui.desk.urgent_short", ("days", left));
                statusColor = UIColors.Loss;
            }
            else if (!p.seen)
            {
                status = ManagerText.Get("mgr.ui.desk.new");
                statusColor = UIColors.Info;
            }
            else status = ManagerText.Get("mgr.ui.desk.days_left", ("days", left));
            string category = ManagerText.Has("mgr.cat." + def.Category) ? ManagerText.Get("mgr.cat." + def.Category) : def.Category;
            ManagerUI.Text(r, "Category", category, 15f, UIColors.Pencil, 16f, cardH - 30f, 200f, 24f, TextAlignmentOptions.MidlineLeft);
            ManagerUI.Text(r, "Status", status, 15f, statusColor, LeftW - 196f, cardH - 30f, 180f, 24f, TextAlignmentOptions.MidlineRight, true);
            if (!p.seen)
            {
                StatusBox dot = StatusUI.Box(r, "Unseen", UIColors.Loss, UIColors.PaperHi, 1.5f, Vector4.one * 7f);
                StatusUI.Place(dot.rectTransform, LeftW - 26f, 10f, 14f, 14f);
            }
            Button button = r.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.targetGraphic = card;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            int uid = p.uid;
            button.onClick.AddListener(() => Select(uid));
        }
    }

    void Select(int uid)
    {
        if (uid == selectedUid) return;
        selectedUid = uid;
        dirty = true;
    }

    /// <summary>The latest decisions that were worded for the desk (history from before 2026-09-29 has no texts), newest first.</summary>
    static List<ManagerHistory> Recent(ManagerSystem system)
    {
        var list = new List<ManagerHistory>();
        IReadOnlyList<ManagerHistory> history = system.History;
        for (int i = history.Count - 1; i >= 0 && list.Count < RecentMax; i--)
            if (history[i] != null && !string.IsNullOrEmpty(history[i].title)) list.Add(history[i]);
        return list;
    }

    static float RecentHeight(int entries) => 44f + entries * 52f;

    /// <summary>"Decided lately": day and title, then what happened, with a dot (green: you, blue: delegated, grey: no answer).</summary>
    void BuildRecent(List<ManagerHistory> recent, int count, float y)
    {
        StatusBox box = StatusUI.Box(content, "Recent", UIColors.WithAlpha(UIColors.PaperHi, 0.55f), UIColors.WithAlpha(UIColors.Ink, 0.45f), 2f, ManagerUI.SmallRadii);
        RectTransform r = StatusUI.Place(box.rectTransform, LeftX, y, LeftW, RecentHeight(count));
        TextMeshProUGUI label = ManagerUI.Text(r, "Label", ManagerText.Get("mgr.ui.desk.recent"), 16f, UIColors.InkSoft, 16f, 10f, LeftW - 32f, 24f,
                                               TextAlignmentOptions.MidlineLeft, true);
        label.characterSpacing = 4f;
        for (int i = 0; i < count; i++)
        {
            ManagerHistory h = recent[i];
            float rowY = 38f + i * 52f;
            if (i > 0)
            {
                Image rule = StatusUI.Block(r, "Rule" + i, UIColors.WithAlpha(UIColors.Ink, 0.14f));
                StatusUI.Place(rule.rectTransform, 16f, rowY - 3f, LeftW - 32f, 2f);
            }
            string what;
            Color dot;
            switch (h.how)
            {
                case "delegated":
                    string who = !string.IsNullOrEmpty(h.manager) ? h.manager : ManagerText.Get("mgr.ui.no_manager_name");
                    what = ManagerText.Get("mgr.ui.desk.recent.delegated", ("manager", who), ("choice", h.choiceText));
                    dot = UIColors.Info;
                    break;
                case "expired":
                    what = ManagerText.Get("mgr.ui.desk.recent.expired", ("choice", h.choiceText));
                    dot = UIColors.Pencil;
                    break;
                case "unanswered":
                    what = ManagerText.Get("mgr.ui.desk.recent.unanswered");
                    dot = UIColors.Pencil;
                    break;
                default:
                    what = h.choiceText;
                    dot = UIColors.Money;
                    break;
            }
            StatusBox mark = StatusUI.Box(r, "Dot" + i, dot, UIColors.Ink, 1.5f, Vector4.one * 6f);
            StatusUI.Place(mark.rectTransform, 16f, rowY + 6f, 12f, 12f);
            // Boxes at least the font's line height (Coming Soon runs tall; an ellipsis box too short for one line shows nothing)
            TextMeshProUGUI when = ManagerUI.Text(r, "When" + i, ManagerText.Get("mgr.ui.desk.recent.day", ("day", h.day), ("title", h.title)), 15f, UIColors.Pencil,
                                                  38f, rowY - 3f, LeftW - 54f, 26f, TextAlignmentOptions.MidlineLeft);
            when.overflowMode = TextOverflowModes.Ellipsis;
            TextMeshProUGUI result = ManagerUI.Text(r, "What" + i, what, 17f, UIColors.Ink, 38f, rowY + 19f, LeftW - 54f, 29f, TextAlignmentOptions.MidlineLeft);
            result.overflowMode = TextOverflowModes.Ellipsis;
        }
    }

    /// <summary>The red note on the folder while the decision that opened the desk by itself is shown.</summary>
    void BuildKnocked(ManagerSystem system)
    {
        if (system.FindPending(interruptUid) == null) return;
        string who = system.HasManager ? system.Manager.name : ManagerText.Get("mgr.ui.no_manager_name");
        string text = ManagerText.Get("mgr.ui.desk.knocked", ("manager", who));
        StatusBox note = StatusUI.Box(content, "Knocked", UIColors.Loss, UIColors.Ink, 3f, ManagerUI.SmallRadii);
        note.SetShadow(UIColors.Ink, new Vector2(3f, 4f));
        TextMeshProUGUI label = StatusUI.Text(note.rectTransform, "Label", text, 20f, UIColors.PaperHi, ManagerUI.Font, TextAlignmentOptions.Center, true);
        float w = Mathf.Min(600f, Mathf.Ceil(label.GetPreferredValues(text, 1200f, 0f).x) + 36f);
        StatusUI.PlaceRotated(note.rectTransform, SheetW - 36f - 170f - 24f - w, -46f, w, 46f, 2f);
        StatusUI.Stretch(label.rectTransform, 14f, 14f, 0f, 0f);
        HeatUI.Shrink(label, 14f);
    }

    // ------------------------------------------------------------------
    // The selected decision
    // ------------------------------------------------------------------

    void BuildPage(ManagerSystem system)
    {
        StatusBox page = StatusUI.Box(content, "Page", UIColors.PaperHi, UIColors.Ink, 2f, ManagerUI.SmallRadii);
        page.raycastTarget = true;
        RectTransform pageRect = StatusUI.Place(page.rectTransform, PageX, PageY, PageW, PageH);
        // Faint ruled lines, like a memo pad
        for (float y = 118f; y < PageH - 20f; y += 44f)
        {
            Image rule = StatusUI.Block(pageRect, "Rule", UIColors.WithAlpha(UIColors.Rule, 0.55f));
            StatusUI.Place(rule.rectTransform, 2f, y, PageW - 4f, 2f);
        }

        ManagerPending p = system.FindPending(selectedUid);
        ManagerDecisionView view = p != null ? system.Describe(p) : null;
        if (view == null)
        {
            BuildEmpty(system, pageRect);
            return;
        }
        system.MarkSeen(p.uid);
        // Long texts (Turkish runs ~30% longer): build smaller if the first try doesn't fit
        LastTextScale = 1f;
        LastFit = true;
        if (!BuildDecision(system, pageRect, view, 1f))
        {
            LastTextScale = 0.86f;
            foreach (Transform child in pageRect)
                if (child.name != "Rule") Destroy(child.gameObject);
            LastFit = BuildDecision(system, pageRect, view, 0.86f);
        }
    }

    void BuildEmpty(ManagerSystem system, RectTransform page)
    {
        string title = ManagerText.Get("mgr.ui.desk.empty.title");
        string body = system.HasManager ? ManagerText.Get("mgr.ui.desk.empty.body", ("manager", system.Manager.name))
                                         : ManagerText.Get("mgr.ui.desk.empty.nomanager");
        ManagerUI.Text(page, "EmptyTitle", title, 32f, UIColors.Ink, Pad, 298f, PageW - Pad * 2f, 52f, TextAlignmentOptions.Center, true);
        ManagerUI.Block(page, "EmptyBody", body, 21f, UIColors.InkSoft, Pad + 120f, 360f, PageW - Pad * 2f - 240f, out _)
                 .alignment = TextAlignmentOptions.Top;
    }

    /// <summary>Lay out one decision. False when it doesn't fit the page at this text scale.</summary>
    bool BuildDecision(ManagerSystem system, RectTransform page, ManagerDecisionView view, float k)
    {
        float width = PageW - Pad * 2f;
        float y = 22f;

        // Kicker and the urgent stamp
        ManagerUI.Text(page, "Kicker", view.CategoryLabel, 16f * k, UIColors.Pencil, Pad, y, width, 26f, TextAlignmentOptions.MidlineLeft).characterSpacing = 4f;
        float titleWidth = width;
        if (view.Urgent)
        {
            string stampText = ManagerText.Get("mgr.ui.desk.urgent", ("days", view.DaysLeft));
            StatusBox stamp = StatusUI.Box(page, "Urgent", UIColors.WithAlpha(UIColors.LossSoft, 0.9f), UIColors.Loss, 3f, Vector4.one * 6f);
            TextMeshProUGUI st = StatusUI.Text(stamp.rectTransform, "Label", stampText, 19f * k, UIColors.Loss, ManagerUI.Font, TextAlignmentOptions.Center, true);
            float sw = Mathf.Ceil(st.GetPreferredValues(stampText, 600f, 0f).x) + 30f;
            StatusUI.PlaceRotated(stamp.rectTransform, PageW - Pad - sw, 18f, sw, 44f, -4f);
            StatusUI.Stretch(st.rectTransform, 8f, 8f, 0f, 0f);
            titleWidth = width - sw - 20f;
        }
        y += 28f;

        ManagerUI.Block(page, "Title", view.Title, 31f * k, UIColors.Ink, Pad, y, titleWidth, out float titleH, true, 1.15f, 90f * k);
        y += titleH + 16f * k;

        // The manager's message beside their portrait
        ManagerPerson m = system.Manager;
        float portrait = 78f * k;
        bool hasManager = system.HasManager;
        if (hasManager) ManagerUI.Portrait(page, "Portrait", m.portraitIndex, m.name, portrait, Pad, y);
        float noteX = hasManager ? Pad + portrait + 18f : Pad;
        float noteW = PageW - Pad - noteX;
        float messageH = Note(page, "Message", view.Message, UIColors.LegalPad, noteX, y, noteW, 21f * k, null);
        y += Mathf.Max(hasManager ? portrait : 0f, messageH) + 14f * k;

        if (!string.IsNullOrEmpty(view.Analysis))
        {
            string heading = ManagerText.Get("mgr.ui.desk.analysis", ("manager", hasManager ? m.name : ""));
            float h = Note(page, "Analysis", view.Analysis, UIColors.StickyBlue, noteX, y, noteW, 20f * k, heading);
            y += h + 14f * k;
        }
        if (view.Withheld)
        {
            ManagerUI.Block(page, "Withheld", ManagerText.Get("mgr.ui.desk.withheld", ("manager", m.name)), 17f * k, UIColors.Pencil, noteX, y, noteW, out float wh);
            y += wh + 8f * k;
        }
        if (view.NoManager)
        {
            ManagerUI.Block(page, "NoManager", ManagerText.Get("mgr.ui.desk.no_advice"), 17f * k, UIColors.Pencil, Pad, y, width, out float nh);
            y += nh + 8f * k;
        }
        y += 8f * k;

        // The choices
        foreach (ManagerChoiceView choice in view.Choices)
        {
            float h = ChoiceButton(page, view.Uid, choice, Pad, y, width, k);
            y += h + 14f * k;
        }
        Foot(system, page, view, k);
        return y <= PageH - 18f - FootH - 8f;
    }

    /// <summary>What happens without an answer, at the page's foot: the default choice, or the moment passing.</summary>
    void Foot(ManagerSystem system, RectTransform page, ManagerDecisionView view, float k)
    {
        ManagerSituationDef def = system.Library.Get(view.SituationId);
        string choiceText = null;
        if (def != null && !string.IsNullOrEmpty(def.DefaultChoice))
        {
            foreach (ManagerChoiceView c in view.Choices)
            {
                if (c.Id != def.DefaultChoice || !c.Available) continue;
                choiceText = c.Text;
                break;
            }
        }
        bool today = view.DaysLeft <= 0;
        string text = choiceText != null
            ? ManagerText.Get(today ? "mgr.ui.desk.expire.default.today" : "mgr.ui.desk.expire.default", ("days", view.DaysLeft), ("choice", choiceText))
            : ManagerText.Get(today ? "mgr.ui.desk.expire.none.today" : "mgr.ui.desk.expire.none", ("days", view.DaysLeft));
        Image rule = StatusUI.Block(page, "FootRule", UIColors.WithAlpha(UIColors.Ink, 0.15f));
        StatusUI.Place(rule.rectTransform, Pad, PageH - 18f - FootH - 6f, PageW - Pad * 2f, 2f);
        TextMeshProUGUI foot = ManagerUI.Text(page, "Foot", text, 17f * k, UIColors.InkSoft, Pad, PageH - 18f - FootH, PageW - Pad * 2f, FootH, TextAlignmentOptions.MidlineLeft);
        foot.overflowMode = TextOverflowModes.Ellipsis;
        HeatUI.Shrink(foot, 13f);
    }

    /// <summary>A note (paper with a thin border) holding wrapped text, with an optional bold heading. Returns its height.</summary>
    float Note(RectTransform parent, string name, string text, Color fill, float x, float y, float width, float size, string heading)
    {
        StatusBox note = StatusUI.Box(parent, name, fill, UIColors.WithAlpha(UIColors.Ink, 0.55f), 2f, ManagerUI.SmallRadii);
        note.SetShadow(new Color(0f, 0f, 0f, 0.18f), new Vector2(2f, 4f), 6f);
        RectTransform r = note.rectTransform;
        float pad = 18f, inner = width - pad * 2f, top = 14f;
        if (!string.IsNullOrEmpty(heading))
        {
            ManagerUI.Text(r, "Heading", heading, size * 0.85f, UIColors.InkSoft, pad, top, inner, size * 1.4f, TextAlignmentOptions.MidlineLeft, true);
            top += size * 1.5f;
        }
        ManagerUI.Block(r, "Text", text, size, UIColors.Ink, pad, top, inner, out float h, false, 1.3f);
        float height = top + h + 14f;
        StatusUI.Place(r, x, y, width, height);
        return height;
    }

    /// <summary>One choice: its text, its hint (or why it's unavailable) and the "I'd do this" note. Returns its height.</summary>
    float ChoiceButton(RectTransform parent, int uid, ManagerChoiceView choice, float x, float y, float width, float k)
    {
        float noteRoom = choice.Recommended ? 190f * k : 0f;
        float textW = width - 44f - noteRoom;
        string hint = choice.Available ? choice.Hint : choice.Reason;

        // Built and measured while active (TMP measures nothing on inactive objects); inactive only while the MarkerButton
        // is added, so it wakes up with its face in place
        RectTransform rect = StatusUI.NewRect("Choice_" + choice.Id, parent);
        StatusBox shadow = StatusUI.Box(rect, "Shadow", UIColors.Ink, Color.clear, 0f, HeatUI.ButtonRadii);
        StatusBox face = StatusUI.Box(rect, "Face", choice.Delegate || choice.Ask ? UIColors.Paper : UIColors.PaperHi, UIColors.Ink, 3f, HeatUI.ButtonRadii);
        face.raycastTarget = true;

        TextMeshProUGUI text = ManagerUI.Block(face.rectTransform, "Text", choice.Text, 21f * k, UIColors.Ink, 22f, 12f * k, textW, out float textH, true, 1.2f);
        float h = 12f * k + textH;
        TextMeshProUGUI hintText = null;
        if (!string.IsNullOrEmpty(hint))
        {
            hintText = ManagerUI.Block(face.rectTransform, "Hint", hint, 17f * k, choice.Available ? UIColors.InkSoft : UIColors.Loss, 22f, h + 4f, textW, out float hintH, false, 1.25f);
            h += 4f + hintH;
        }
        h = Mathf.Max(66f * k, h + 12f * k);

        StatusUI.Place(rect, x, y, width, h);
        StatusUI.Stretch(shadow.rectTransform);
        shadow.rectTransform.anchoredPosition = new Vector2(4f, -5f);
        StatusUI.Stretch(face.rectTransform);
        if (choice.Recommended)
            ManagerUI.Sticky(face.rectTransform, "Recommended", ManagerText.Get("mgr.ui.desk.recommend"), UIColors.StickyYellow, 17f * k,
                             width - 18f, Mathf.Max(8f, (h - 34f * k) * 0.5f), 3f, out _, true);

        rect.gameObject.SetActive(false);
        Button button = rect.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = face;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        MarkerButton marker = rect.gameObject.AddComponent<MarkerButton>();
        marker.face = face.rectTransform;
        marker.shadow = shadow.gameObject;
        marker.pressOffset = new Vector2(4f, -5f);
        // The reason under an unavailable choice stays red (MarkerButton greys its content while disabled)
        marker.content = hintText != null && choice.Available ? new Graphic[] { text, hintText } : new Graphic[] { text };
        CanvasGroup group = rect.gameObject.AddComponent<CanvasGroup>();
        button.interactable = choice.Available;
        group.alpha = choice.Available ? 1f : 0.62f;
        string id = choice.Id;
        button.onClick.AddListener(() => Choose(uid, id));

        rect.gameObject.SetActive(true);
        return h;
    }

    void Choose(int uid, string choiceId)
    {
        if (openAge < InputDelay) return;
        ManagerSystem system = ManagerSystem.Instance;
        if (system == null) return;
        ManagerChooseResult result = system.Choose(uid, choiceId);
        if (result.Asked)
        {
            openAge = InputDelay * 0.5f;
            dirty = true;
            return;
        }
        if (!result.Done)
        {
            if (!string.IsNullOrEmpty(result.Message)) GameNotifications.Show(result.Message, NotificationKind.Info);
            return;
        }
        GameSfx.Play(Sfx.UiConfirm);
        // The choice opens build mode: close the desk so it can
        if (system.BuildModePending)
        {
            Close();
            return;
        }
        selectedUid = FirstUid(system);
        openAge = 0f;   // a stray double click can't answer the next one
        dirty = true;
    }
}
