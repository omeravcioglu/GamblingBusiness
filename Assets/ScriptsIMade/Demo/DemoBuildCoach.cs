using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Demo only. Two things around build mode:
/// - The FIRST BUILD sticky note: while build mode is open and the checklist isn't done, three rows tick from the plan's
///   state (floor 2, enough budget for an office, an office built), in any order, then the done line. Once build mode
///   closes after a room was built, DemoRun.CoachDone is set and the note never comes back.
/// - "New room unlocked: X" toasts: on each day end and plan change, a catalog room that just became buildable is
///   toasted (the first pass after the run loads only records what is open; one that opens before the Build feature
///   waits for it).
/// It lives in the CompanyManager's scene (added by its bootstrap) and draws on its own overlay canvas above the tower
/// screen (60) and the budget gamble (61), below the toasts.
/// </summary>
[DisallowMultipleComponent]
public class DemoBuildCoach : MonoBehaviour
{
    const int SortingOrder = 62;

    class Row
    {
        public BlueprintBox box;
        public TowerIcon check;
        public TextMeshProUGUI text;
        public bool done;
    }

    TowerBuilder builder;
    CompanyManager company;
    Canvas canvas;
    RectTransform note;
    TextMeshProUGUI titleText, doneText;
    readonly Row[] rows = new Row[3];
    bool coachDone;        // also kept locally: DemoRun may be missing
    bool rowsDirty = true;
    bool shown;

    // Room unlocks
    readonly HashSet<string> openRooms = new HashSet<string>();
    bool baselineTaken;
    bool unlockCheckPending;

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
        Unhook();
        SetShown(false);
    }

    void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }

    void Update()
    {
        if (!DemoMode.Active) return;
        HookBuilder();
        HookCompany();
        if (unlockCheckPending) CheckRoomUnlocks();
        UpdateNote();
    }

    void HookBuilder()
    {
        TowerBuilder current = TowerBuilder.Instance;
        if (current == builder) return;
        if (builder != null)
        {
            builder.OpenChanged -= OnOpenChanged;
            builder.PlanChanged -= OnPlanChanged;
        }
        builder = current;
        if (builder == null) return;
        builder.OpenChanged += OnOpenChanged;
        builder.PlanChanged += OnPlanChanged;   // the budget too (it is part of the plan)
        rowsDirty = true;
        unlockCheckPending = true;
    }

    void HookCompany()
    {
        CompanyManager current = CompanyManager.Instance;
        if (current == company) return;
        if (company != null)
        {
            company.DayEnded -= OnDayEnded;
            company.StateLoaded -= OnStateLoaded;
        }
        company = current;
        if (company == null) return;
        company.DayEnded += OnDayEnded;
        company.StateLoaded += OnStateLoaded;
    }

    void Unhook()
    {
        if (builder != null)
        {
            builder.OpenChanged -= OnOpenChanged;
            builder.PlanChanged -= OnPlanChanged;
            builder = null;
        }
        if (company != null)
        {
            company.DayEnded -= OnDayEnded;
            company.StateLoaded -= OnStateLoaded;
            company = null;
        }
    }

    void OnOpenChanged(bool open)
    {
        rowsDirty = true;
        // Closing build mode after the first room was built ends the checklist for good
        if (!open && !IsCoachDone && builder != null && builder.Plan.BuiltRoomCount >= 1)
        {
            coachDone = true;
            DemoRun.CoachDone = true;
            SetShown(false);
        }
    }

    void OnPlanChanged()
    {
        rowsDirty = true;
        unlockCheckPending = true;
    }

    void OnDayEnded(int day, double change)
    {
        unlockCheckPending = true;
    }

    void OnStateLoaded()
    {
        // A loaded run: record what is open again, silently
        baselineTaken = false;
        unlockCheckPending = true;
        rowsDirty = true;
    }

    void OnLanguageChanged()
    {
        rowsDirty = true;
        if (titleText != null) titleText.text = DemoMode.T("demo.build.coach.title");
        if (doneText != null) doneText.text = DemoMode.T("demo.build.coach.done");
    }

    bool IsCoachDone => coachDone || DemoRun.CoachDone;

    // ------------------------------------------------------------------
    // The FIRST BUILD note
    // ------------------------------------------------------------------

    void UpdateNote()
    {
        bool show = ShouldShow();
        if (show && note == null) Build();
        SetShown(show);
        if (show && rowsDirty) RefreshRows();
    }

    bool ShouldShow()
    {
        if (IsCoachDone || builder == null || !builder.IsOpen) return false;
        TowerView view = TowerView.Instance;
        if (view == null || !view.IsShowing) return false;   // the blackjack table hides the tower screen
        // Out of the way of the room card, the room view, the sheets and the budget gamble
        if (builder.Selected != null || builder.InsideRoom != null || builder.UpgradeOpen || builder.ResultOpen || builder.PurchaseOpen) return false;
        BuildGambleView gamble = BuildGambleView.Instance;
        return gamble == null || !gamble.IsOpen;
    }

    void SetShown(bool show)
    {
        if (shown == show && (note == null || note.gameObject.activeSelf == show)) return;
        shown = show;
        if (note != null && note.gameObject.activeSelf != show) note.gameObject.SetActive(show);
    }

    void RefreshRows()
    {
        rowsDirty = false;
        TowerPlan plan = builder.Plan;
        int built = plan.BuiltRoomCount;
        double officePrice = DemoTowerRules.NextOfficePrice(plan, out _);
        bool[] done =
        {
            plan.Floors >= 2,
            built >= 1 || (officePrice > 0.0 && plan.CanAfford(officePrice)),
            built >= 1
        };
        string max = UIFormat.Money(DemoMode.Config.houseFlipMaxBet);
        for (int i = 0; i < rows.Length; i++)
        {
            Row row = rows[i];
            if (row == null) continue;
            row.done = done[i];
            row.text.text = i == 0 ? DemoMode.T("demo.build.coach.floor")
                          : i == 1 ? DemoMode.T("demo.build.coach.office", ("max", max))
                          : DemoMode.T("demo.build.coach.place");
            row.text.color = row.done ? UIColors.Pencil : UIColors.Ink;
            row.text.fontStyle = row.done ? FontStyles.Strikethrough : FontStyles.Normal;
            row.box.Fill = row.done ? UIColors.MoneySoft : UIColors.PaperHi;
            if (row.check.gameObject.activeSelf != row.done) row.check.gameObject.SetActive(row.done);
        }
        bool allDone = done[0] && done[1] && done[2];
        if (doneText != null && doneText.gameObject.activeSelf != allDone) doneText.gameObject.SetActive(allDone);
        if (note != null) LayoutRebuilder.ForceRebuildLayoutImmediate(note);
    }

    void Build()
    {
        TowerView view = TowerView.Instance;
        TMP_FontAsset font = view != null ? view.ResolveFont() : null;
        canvas = BlueprintUI.CreateOverlayCanvas("DemoBuildCoachCanvas (Runtime)", SortingOrder);
        canvas.transform.SetParent(transform, false);
        GraphicRaycaster raycaster = canvas.GetComponent<GraphicRaycaster>();
        if (raycaster != null) raycaster.enabled = false;   // a note, not a control: clicks go to the tower screen

        // A sticky note on the right, under the top bar (where the room card opens; the note hides meanwhile)
        BlueprintBox paper = BlueprintUI.Box(canvas.transform, "FirstBuildNote", UIColors.StickyYellow, Color.clear, 0f, BlueprintUI.Round(1f));
        paper.SetShadow(new Color(0f, 0f, 0f, 0.28f), new Vector2(0f, 12f), 20f);
        note = BlueprintUI.TopRight(paper.rectTransform, 44f, 124f, 400f, 0f);
        note.localEulerAngles = new Vector3(0f, 0f, 1.5f);
        BlueprintUI.VColumn(paper, 12f, new RectOffset(26, 26, 34, 24));
        BlueprintUI.Fit(paper, false, true);

        BlueprintBox band = BlueprintUI.Box(paper.transform, "Band", new Color(0f, 0f, 0f, 0.07f), Color.clear, 0f, BlueprintUI.Round(0f));
        BlueprintUI.Layout(band).ignoreLayout = true;
        band.rectTransform.anchorMin = new Vector2(0f, 1f);
        band.rectTransform.anchorMax = new Vector2(1f, 1f);
        band.rectTransform.pivot = new Vector2(0.5f, 1f);
        band.rectTransform.sizeDelta = new Vector2(0f, 22f);
        band.rectTransform.anchoredPosition = Vector2.zero;

        // Final case comes from the table (no ToUpper: "İLK İNŞAAT")
        titleText = BlueprintUI.Caps(BlueprintUI.Text(paper.transform, "Title", DemoMode.T("demo.build.coach.title"), 26f, UIColors.Ink, font,
                                                      TextAlignmentOptions.Left, true), 0.06f);
        for (int i = 0; i < rows.Length; i++) rows[i] = BuildRow(paper.transform, i, font);
        doneText = BlueprintUI.Wrap(BlueprintUI.Text(paper.transform, "Done", DemoMode.T("demo.build.coach.done"), 22f, UIColors.Money, font,
                                                     TextAlignmentOptions.Left, true));
        doneText.gameObject.SetActive(false);

        L10nFont.AttachAll(note);
        note.gameObject.SetActive(false);
        shown = false;
        rowsDirty = true;
    }

    static Row BuildRow(Transform parent, int index, TMP_FontAsset font)
    {
        RectTransform line = BlueprintUI.NewRect("Row" + (index + 1), parent);
        BlueprintUI.HRow(line, 12f, new RectOffset(0, 0, 0, 0), TextAnchor.UpperLeft);

        var row = new Row();
        row.box = BlueprintUI.Box(line, "Box", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall);
        BlueprintUI.Layout(row.box, 28f, 28f, 0f, 0f, 28f, 28f);
        RectTransform checkRect = BlueprintUI.NewRect("Check", row.box.transform);
        BlueprintUI.Centered(checkRect, 20f, 20f);
        row.check = checkRect.gameObject.AddComponent<TowerIcon>();
        row.check.Kind = TowerIconKind.Check;
        row.check.StrokeWidth = 3.2f;
        row.check.color = UIColors.Money;
        row.check.raycastTarget = false;
        row.check.gameObject.SetActive(false);

        row.text = BlueprintUI.Wrap(BlueprintUI.Text(line, "Text", "", 21f, UIColors.Ink, font));
        row.text.lineSpacing = 6f;
        BlueprintUI.Layout(row.text, 0f, -1f, 1f, -1f, 0f);
        return row;
    }

    // ------------------------------------------------------------------
    // "New room unlocked"
    // ------------------------------------------------------------------

    void CheckRoomUnlocks()
    {
        if (builder == null) return;
        SaveSystem save = SaveSystem.Instance;
        if (save != null && save.IsLoading) return;   // wait for the run: a loaded save's rooms aren't news
        unlockCheckPending = false;

        TowerPlan plan = builder.Plan;
        if (plan.Rules == null || plan.Rules.rooms == null) return;
        // The first pass only records what is open; a room that opens while Build is still locked is announced once it opens
        bool firstPass = !baselineTaken;
        bool announce = !firstPass && FeatureGate.IsUnlocked(Feature.Build);
        baselineTaken = true;
        foreach (TowerRoomDefinition room in plan.Rules.rooms)
        {
            if (room == null || room.isFixed || string.IsNullOrEmpty(room.id)) continue;
            bool open;
            try { open = plan.IsUnlocked(room); }
            catch (Exception e)
            {
                Debug.LogException(e);
                continue;
            }
            if (!open)
            {
                openRooms.Remove(room.id);
                continue;
            }
            if (openRooms.Contains(room.id) || (!firstPass && !announce)) continue;
            openRooms.Add(room.id);
            if (announce)
                GameNotifications.Show(DemoMode.T("demo.build.room_unlocked", ("room", TowerText.RoomName(room))), NotificationKind.Good);
        }
    }

    // ------------------------------------------------------------------
    // Bootstrap: one coach in the scene of the CompanyManager while demo mode is on
    // ------------------------------------------------------------------

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (!DemoMode.Active) return;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Ensure();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Ensure();
    }

    static void Ensure()
    {
        if (!Application.isPlaying || !DemoMode.Active) return;
        CompanyManager company = CompanyManager.Instance != null ? CompanyManager.Instance : FindAnyObjectByType<CompanyManager>();
        if (company == null || FindAnyObjectByType<DemoBuildCoach>(FindObjectsInactive.Include) != null) return;

        GameObject go = new GameObject("Demo Build Coach (Runtime)");
        Scene scene = company.gameObject.scene;
        if (scene.IsValid() && scene.isLoaded && go.scene != scene)
        {
            // Live and die with the company's scene
            try { SceneManager.MoveGameObjectToScene(go, scene); }
            catch (Exception e) { Debug.LogWarning("[Demo] Couldn't move the build coach next to the CompanyManager: " + e.Message); }
        }
        go.AddComponent<DemoBuildCoach>();
    }
}
