using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The manager's permanent side panel: a paper chip on the left edge under the company ledger with the manager's portrait,
/// name, "4 decisions · content" and a red count badge; click it to open the desk (or the candidate pick). While
/// candidates wait it reads "Pick your manager"; while the job is open, "Job opening · new candidates on day 14".
/// Hidden until the Manager feature opens, and (like the heat chip) while a pop-up, crisis, risk card, build mode or a
/// run-end screen is up. Own overlay canvas (sorting order 72); texts are written again when the language changes.
/// As designed (canvas page "Manager", 2026-09-29): as wide as the ledger above it, with an M key cap; M opens the desk too.
/// </summary>
[DisallowMultipleComponent]
public class ManagerChipView : MonoBehaviour
{
    [Tooltip("Sorting order of the chip's canvas")]
    public int sortingOrder = 72;
    [Tooltip("Top-left corner of the chip (reference pixels from the screen's top-left; the ledger ends at y 334)")]
    public Vector2 position = new Vector2(32f, 352f);
    [Tooltip("Key that opens the desk (or the candidate pick) while the chip shows")]
    public KeyCode openKey = KeyCode.M;

    const float Width = 420f, Height = 96f, PortraitSize = 62f;

    Canvas canvas;
    RectTransform root;
    RectTransform body;
    Button button;
    bool dirty = true;
    float bumpAge = 10f;

    void OnEnable()
    {
        ManagerSystem.Changed += MarkDirty;
        ManagerSystem.DecisionAdded += OnDecisionAdded;
        L10n.LanguageChanged += OnLanguageChanged;
        dirty = true;
    }

    void OnDisable()
    {
        ManagerSystem.Changed -= MarkDirty;
        ManagerSystem.DecisionAdded -= OnDecisionAdded;
        L10n.LanguageChanged -= OnLanguageChanged;
        if (root != null) root.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }

    void MarkDirty() => dirty = true;

    // Rebuilt in the language's words and font
    void OnLanguageChanged() => dirty = true;

    void OnDecisionAdded(ManagerPending pending)
    {
        bumpAge = 0f;
        dirty = true;
    }

    void Update()
    {
        ManagerSystem system = ManagerSystem.Instance;
        bool show = system != null && system.IsStateKnown && system.Phase != ManagerPhase.None && HudVisible();
        if (show) EnsureBuilt();
        if (root != null && root.gameObject.activeSelf != show) root.gameObject.SetActive(show);
        if (!show) return;
        if (dirty) Refresh(system);
        Animate(system);
        // The key only while the chip shows (HudVisible: no panel, event, mini-game or build mode) and nothing is typed
        if (openKey != KeyCode.None && Input.GetKeyDown(openKey) && !UIManager.IsTypingInInputField()) OnClick();
    }

    /// <summary>Nothing covers the HUD: no panel, crisis, risk card, build mode or run-end screen.</summary>
    static bool HudVisible()
    {
        if (UIManager.Exists && UIManager.Instance.IsAnyPanelOpen()) return false;
        if (MiniGameManager.Instance != null && MiniGameManager.Instance.IsMiniGameActive()) return false;
        if (EventManager.Instance != null && EventManager.Instance.IsEventInProgress()) return false;
        if (BuildModeCamera.Instance != null && BuildModeCamera.Instance.IsInBuildMode()) return false;
        RunEndDirector director = RunEndDirector.Instance;
        if (director != null && (director.IsRunOver || director.IsAnyScreenOpen)) return false;
        return true;
    }

    void EnsureBuilt()
    {
        if (root != null) return;
        canvas = HeatUI.CreateCanvas("ManagerChipCanvas (Runtime)", sortingOrder, transform, true);
        RectTransform canvasRect = (RectTransform)canvas.transform;

        StatusBox chip = StatusUI.Box(canvasRect, "ManagerChip", UIColors.PaperHi, UIColors.Ink, 2f, ManagerUI.SmallRadii);
        chip.SetShadow(UIColors.Ink, new Vector2(3f, 4f));
        chip.raycastTarget = true;
        root = chip.rectTransform;
        root.anchorMin = root.anchorMax = new Vector2(0f, 1f);
        root.pivot = new Vector2(0f, 1f);
        root.anchoredPosition = new Vector2(position.x, -position.y);
        root.sizeDelta = new Vector2(Width, Height);
        button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = chip;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(OnClick);
        root.gameObject.SetActive(false);
    }

    void OnClick()
    {
        ManagerSystem system = ManagerSystem.Instance;
        if (system == null) return;
        GameSfx.Play(Sfx.UiOpen);
        system.OpenDesk();
    }

    void Refresh(ManagerSystem system)
    {
        dirty = false;
        if (body != null)
        {
            body.gameObject.SetActive(false);   // gone this frame (Destroy waits for the end of it)
            Destroy(body.gameObject);
        }
        body = StatusUI.NewRect("Body", root);
        StatusUI.Stretch(body);

        string phase = system.Phase;
        ManagerPerson m = system.Manager;
        int count = system.PendingCount;
        string kicker = ManagerText.Get("mgr.ui.chip.kicker");
        string title, line;
        Color lineColor = UIColors.InkSoft;

        if (phase == ManagerPhase.Picking)
        {
            ManagerUI.Portrait(body, "Portrait", -1, "?", PortraitSize, 14f, (Height - PortraitSize) * 0.5f);
            title = ManagerText.Get("mgr.ui.chip.pick");
            line = ManagerText.Get("mgr.ui.chip.pick_line", ("count", system.Candidates.Count));
            lineColor = UIColors.Info;
        }
        else if (phase == ManagerPhase.Gap)
        {
            ManagerUI.Portrait(body, "Portrait", -1, "?", PortraitSize, 14f, (Height - PortraitSize) * 0.5f);
            title = ManagerText.Get("mgr.ui.chip.gap");
            int days = Mathf.Max(0, system.GapUntilDay - (CompanyManager.Instance != null ? CompanyManager.Instance.currentDay : 0));
            line = days <= 1 ? ManagerText.Get("mgr.ui.chip.gap_soon") : ManagerText.Get("mgr.ui.chip.gap_line", ("day", system.GapUntilDay));
            if (count > 0) line = ManagerText.Get("mgr.ui.chip.join", ("first", line), ("second", Decisions(count)));
        }
        else
        {
            ManagerUI.Portrait(body, "Portrait", m.portraitIndex, m.name, PortraitSize, 14f, (Height - PortraitSize) * 0.5f);
            title = m.name;
            string mood = "<color=" + HeatUI.Hex(ManagerText.SatisfactionColor(m.satisfaction)) + ">" + ManagerText.SatisfactionWord(m.satisfaction) + "</color>";
            line = ManagerText.Get("mgr.ui.chip.join", ("first", count > 0 ? Decisions(count) : ManagerText.Get("mgr.ui.chip.clear")), ("second", mood));
        }

        float keyRoom = openKey != KeyCode.None ? 56f : 0f;
        float left = 14f + PortraitSize + 14f, width = Width - left - 12f - keyRoom;
        if (openKey != KeyCode.None) ManagerUI.Keycap(body, KeyLabel(openKey), 19f, Width - 16f, (Height - 36f) * 0.5f, true);
        TextMeshProUGUI k = ManagerUI.Text(body, "Kicker", kicker, 15f, UIColors.Pencil, left, 7f, width, 24f, TextAlignmentOptions.MidlineLeft);
        k.characterSpacing = 6f;
        TextMeshProUGUI t = ManagerUI.Text(body, "Title", title, 21f, UIColors.Ink, left, 28f, width, 34f, TextAlignmentOptions.MidlineLeft, true);
        t.overflowMode = TextOverflowModes.Ellipsis;
        HeatUI.Shrink(t, 16f);
        TextMeshProUGUI l = ManagerUI.Text(body, "Line", line, 17f, lineColor, left, 61f, width, 28f, TextAlignmentOptions.MidlineLeft);
        l.overflowMode = TextOverflowModes.Ellipsis;
        HeatUI.Shrink(l, 13f);

        // The count badge on the portrait (a "!" while candidates wait)
        string badge = phase == ManagerPhase.Picking ? "!" : count > 0 ? count.ToString() : null;
        if (badge != null)
        {
            bool urgent = phase == ManagerPhase.Picking || system.UrgentCount > 0 || system.UnseenCount > 0;
            StatusBox dot = StatusUI.Box(body, "Badge", urgent ? UIColors.Loss : UIColors.InkSoft, UIColors.PaperHi, 2f, Vector4.one * 14f);
            StatusUI.Place(dot.rectTransform, 14f + PortraitSize - 22f, 8f, 28f, 28f);
            TextMeshProUGUI n = StatusUI.Text(dot.rectTransform, "Count", badge, 16f, UIColors.PaperHi, ManagerUI.Font, TextAlignmentOptions.Center, true);
            StatusUI.Stretch(n.rectTransform);
        }
    }

    static string Decisions(int count) => ManagerText.Get("mgr.ui.chip.decisions", ("count", count));

    /// <summary>The key cap's label: "M", "1" for Alpha1, "F2"...</summary>
    static string KeyLabel(KeyCode key)
    {
        string s = key.ToString();
        if (s.StartsWith("Alpha", System.StringComparison.Ordinal) && s.Length == 6) return s.Substring(5);
        return s;
    }

    void Animate(ManagerSystem system)
    {
        if (root == null) return;
        bumpAge += Time.unscaledDeltaTime;
        float s = 1f;
        if (bumpAge < 0.4f) s = 1f + 0.08f * Mathf.Sin(bumpAge / 0.4f * Mathf.PI);
        else if (system.Phase == ManagerPhase.Picking || system.UrgentCount > 0)
            s = 1f + 0.025f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4f));
        root.localScale = new Vector3(s, s, 1f);
    }
}
