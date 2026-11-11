using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Finds what the tour points at, as rectangles in the tour overlay's space (x, y from its top-left corner, y down, like
/// the design's CSS), grown like the design's spotlights:
/// - the office HUD's own parts first (OfficeHud's references: the ledger around companyNameText, Skip / Hire or Next
///   batch, the six tabs; HudTaskBoard), converted through screen space so any canvas scale or aspect ratio works;
/// - the CV: the sheets of the CV papers on the desk (CVPaperView, a world-space canvas), projected by the camera;
/// - otherwise the design rectangle, anchored like the HUD element it stands for (only while the designed HUD is in the
///   scene); with no HUD at all the step has no spotlight and its note sits in the middle of the screen.
/// </summary>
public static class TutorialTargets
{
    static readonly Vector3[] corners = new Vector3[4];
    static OfficeHud cachedHud;
    static HudTaskBoard cachedBoard;
    static GameFlowManager cachedFlow;

    enum Anchor { TopLeft, TopCenter, TopRight, Center, BottomCenter }

    /// <summary>The office HUD in this scene (null without the designed HUD).</summary>
    public static OfficeHud Hud
    {
        get
        {
            if (cachedHud == null) cachedHud = Object.FindAnyObjectByType<OfficeHud>();
            return cachedHud;
        }
    }

    public static GameFlowManager Flow
    {
        get
        {
            if (cachedFlow == null) cachedFlow = Object.FindAnyObjectByType<GameFlowManager>();
            return cachedFlow;
        }
    }

    /// <summary>The spotlight for a tour step, or false when the step should show without one.</summary>
    public static bool TryFind(TourStep step, RectTransform space, out Rect spot)
    {
        spot = default;
        if (step == null || space == null || step.target == TourTarget.None) return false;
        OfficeHud hud = Hud;
        Rect r;
        switch (step.target)
        {
            case TourTarget.Company:
                if (hud != null && TryRect(FindLedger(hud), space, out r)) { spot = Grow(r, 12f, 12f, 12f, 12f); return true; }
                break;
            case TourTarget.Cv:
                if (TryCvRect(space, out r)) { spot = Grow(r, 14f, 14f, 14f, 14f); return true; }
                break;
            case TourTarget.SkipHire:
                if (hud != null && TryDeskButtons(hud, space, out r)) { spot = Grow(r, 16f, 16f, 16f, 16f); return true; }
                break;
            case TourTarget.TaskBoard:
                if (cachedBoard == null) cachedBoard = Object.FindAnyObjectByType<HudTaskBoard>();
                // The clipboard's clip reaches 22 px above the board
                if (cachedBoard != null && TryRect(cachedBoard.transform as RectTransform, space, out r)) { spot = Grow(r, 14f, 24f, 14f, 16f); return true; }
                break;
            case TourTarget.Tabs:
                if (hud != null && TryTabs(hud, space, out r)) { spot = Grow(r, 12f, 12f, 12f, 12f); return true; }
                break;
        }

        // The designed HUD is here but that part wasn't found: the design's rectangle, anchored like the HUD element
        if (hud == null) return false;
        spot = FromDesign(step.designSpot, AnchorOf(step.target), space);
        return spot.width > 1f && spot.height > 1f;
    }

    /// <summary>The spotlight around a feature's HUD tab (and the tab), for the Board memos.</summary>
    public static bool TryFindTab(Feature feature, RectTransform space, out Rect spot, out HudTab tab)
    {
        spot = default;
        tab = FeatureTabs.TabFor(Hud, feature);
        if (tab == null || !TryRect(tab.transform as RectTransform, space, out Rect r)) return false;
        spot = Grow(r, 12f, 12f, 12f, 12f);
        return true;
    }

    // ------------------------------------------------------------------
    // HUD parts
    // ------------------------------------------------------------------

    /// <summary>The ledger panel: the "Ledger" node around the company name (OfficeHudBuilder), else the name's parent.</summary>
    static RectTransform FindLedger(OfficeHud hud)
    {
        Transform start = hud.companyNameText != null ? hud.companyNameText.transform.parent : null;
        for (Transform t = start; t != null && t != hud.transform; t = t.parent)
            if (t.name == "Ledger") return t as RectTransform;
        Transform found = FindDeep(hud.transform, "Ledger");
        if (found != null) return found as RectTransform;
        return start as RectTransform;
    }

    static bool TryDeskButtons(OfficeHud hud, RectTransform space, out Rect r)
    {
        r = default;
        bool any = false;
        if (hud.reviewBar == null || hud.reviewBar.activeInHierarchy)
        {
            any |= Include(hud.skipButton, space, ref r, any);
            any |= Include(hud.hireButton, space, ref r, any);
        }
        if (!any) any |= Include(hud.nextBatchButton, space, ref r, any);
        return any;
    }

    static bool TryTabs(OfficeHud hud, RectTransform space, out Rect r)
    {
        r = default;
        bool any = false;
        foreach (HudTab tab in new[] { hud.skillsTab, hud.staffTab, hud.stocksTab, hud.casinoTab, hud.buildTab, hud.onlineTab })
            any |= Include(tab, space, ref r, any);
        return any;
    }

    static bool Include(Component part, RectTransform space, ref Rect union, bool haveOne)
    {
        if (part == null || !TryRect(part.transform as RectTransform, space, out Rect r)) return false;
        union = haveOne ? Union(union, r) : r;
        return true;
    }

    /// <summary>The CV papers on the desk (the pile), projected into the overlay.</summary>
    static bool TryCvRect(RectTransform space, out Rect r)
    {
        r = default;
        bool any = false;
        Rect screen = space.rect;
        foreach (CVPaperView cv in Object.FindObjectsByType<CVPaperView>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (cv == null) continue;
            RectTransform sheet = cv.sheet != null ? cv.sheet : cv.transform as RectTransform;
            if (!TryRect(sheet, space, out Rect one)) continue;
            // Papers flying off to the hired box or the bin, or not on screen: not the pile
            if (one.xMax < 0f || one.yMax < 0f || one.xMin > screen.width || one.yMin > screen.height) continue;
            r = any ? Union(r, one) : one;
            any = true;
        }
        // Something odd (a paper right in front of the camera): the design's rectangle is better
        return any && r.width < screen.width * 0.7f && r.height < screen.height * 0.95f;
    }

    // ------------------------------------------------------------------
    // Geometry
    // ------------------------------------------------------------------

    /// <summary>A RectTransform's bounds in the overlay (top-left origin, y down), through screen space.</summary>
    public static bool TryRect(RectTransform rt, RectTransform space, out Rect r)
    {
        r = default;
        if (rt == null || space == null || !rt.gameObject.activeInHierarchy) return false;

        Camera cam = CameraFor(rt);
        rt.GetWorldCorners(corners);
        Rect area = space.rect;
        float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            Vector2 screenPoint;
            if (cam != null)
            {
                Vector3 p = cam.WorldToScreenPoint(corners[i]);
                if (p.z <= 0f) return false;   // behind the camera
                screenPoint = p;
            }
            else
            {
                screenPoint = RectTransformUtility.WorldToScreenPoint(null, corners[i]);
            }
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(space, screenPoint, null, out Vector2 local)) return false;
            float x = local.x - area.xMin, y = area.yMax - local.y;
            xMin = Mathf.Min(xMin, x);
            xMax = Mathf.Max(xMax, x);
            yMin = Mathf.Min(yMin, y);
            yMax = Mathf.Max(yMax, y);
        }
        r = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        return r.width > 4f && r.height > 4f;
    }

    /// <summary>The camera that draws this UI (null for an overlay canvas; the main camera for world-space UI).</summary>
    static Camera CameraFor(RectTransform rt)
    {
        Canvas canvas = rt.GetComponentInParent<Canvas>();
        if (canvas == null) return Camera.main;
        Canvas root = canvas.rootCanvas != null ? canvas.rootCanvas : canvas;
        if (root.renderMode == RenderMode.ScreenSpaceOverlay) return null;
        return root.worldCamera != null ? root.worldCamera : Camera.main;
    }

    /// <summary>A design rectangle (1920 x 1080) placed like the HUD element: kept to its corner or edge on other aspect ratios.</summary>
    static Rect FromDesign(Rect design, Anchor anchor, RectTransform space)
    {
        float w = space.rect.width, h = space.rect.height;
        float x = design.x, y = design.y;
        switch (anchor)
        {
            case Anchor.TopRight: x = w - (1920f - design.x); break;
            case Anchor.TopCenter: x = design.x + (w - 1920f) * 0.5f; break;
            case Anchor.Center: x = design.x + (w - 1920f) * 0.5f; y = design.y + (h - 1080f) * 0.5f; break;
            case Anchor.BottomCenter: x = design.x + (w - 1920f) * 0.5f; y = h - (1080f - design.y); break;
        }
        return new Rect(x, y, design.width, design.height);
    }

    static Anchor AnchorOf(TourTarget target)
    {
        switch (target)
        {
            case TourTarget.Company: return Anchor.TopLeft;
            case TourTarget.Cv: return Anchor.Center;
            case TourTarget.SkipHire: return Anchor.BottomCenter;
            case TourTarget.TaskBoard: return Anchor.TopRight;
            default: return Anchor.TopCenter;
        }
    }

    public static Rect Grow(Rect r, float left, float top, float right, float bottom)
    {
        return new Rect(r.x - left, r.y - top, r.width + left + right, r.height + top + bottom);
    }

    static Rect Union(Rect a, Rect b)
    {
        return Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            if (child.name == name) return child;
        return null;
    }

    // ------------------------------------------------------------------
    // Step 3: Skip and Hire as the player will use them
    // ------------------------------------------------------------------

    /// <summary>
    /// While a pausing panel is open, GameFlowManager greys out Skip and Hire (MarkerButton's disabled look). For the
    /// spotlight on them, show them as they'll be once the tour closes; clicks can't reach them under the overlay, and
    /// GameFlowManager ignores desk clicks while a panel is open anyway. Returns what to put back (RestoreDeskButtons).
    /// </summary>
    public static void ShowDeskButtonsLive(System.Collections.Generic.List<(Button button, bool interactable)> restore)
    {
        OfficeHud hud = Hud;
        GameFlowManager flow = Flow;
        if (hud == null || flow == null || restore == null) return;
        Set(hud.skipButton, flow.CanSkip, restore);
        Set(hud.hireButton, flow.CanHire, restore);
        Set(hud.nextBatchButton, flow.CanStartNextBatch, restore);
    }

    static void Set(Button button, bool interactable, System.Collections.Generic.List<(Button, bool)> restore)
    {
        if (button == null || button.interactable == interactable) return;
        restore.Add((button, button.interactable));
        button.interactable = interactable;
    }

    public static void RestoreDeskButtons(System.Collections.Generic.List<(Button button, bool interactable)> restore)
    {
        if (restore == null) return;
        foreach ((Button button, bool interactable) in restore)
            if (button != null) button.interactable = interactable;
        restore.Clear();
    }
}
