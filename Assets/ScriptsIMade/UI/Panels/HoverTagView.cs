using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The designed hover tag: a luggage tag tied with a string to the office item under the pointer (name, level pips,
/// what it earns, how to upgrade and the next price; texts: Panels table). It sits beside the item's on-screen bounds, on the right when
/// there is room, otherwise mirrored on the left. Hovering doesn't pause, so there is no scrim.
/// Replaces the items' old world-space hover UI (OfficeItemInteractable.UseDesignedHoverTag).
/// </summary>
[DisallowMultipleComponent]
public class HoverTagView : MonoBehaviour
{
    public UpgradeFlowView flow;

    [Header("Tag")]
    [Tooltip("The 340 x 260 tag (rotated -3 degrees)")]
    public RectTransform tagRect;
    [Tooltip("Tag sprite and eyelet; mirrored when the tag hangs on the left of the item")]
    public RectTransform art;
    public RectTransform shadow;
    [Tooltip("Text column (x 70 on the right side, 14 when mirrored)")]
    public RectTransform content;
    public float contentLeft = 70f;
    public float contentLeftMirrored = 14f;
    public Vector2 tagSize = new Vector2(340f, 260f);
    [Tooltip("Eyelet centre in the tag's top-left coordinates (unmirrored)")]
    public Vector2 eyelet = new Vector2(30f, 130f);
    [Tooltip("Tag's top-left corner relative to the knot on the item")]
    public Vector2 tagOffset = new Vector2(48f, -90f);
    public float screenMargin = 16f;

    [Header("String")]
    public UIStringCurve stringCurve;
    public RectTransform knot;

    [Header("Texts")]
    public Image icon;
    public TMP_Text itemName;
    public TMP_Text levelText;
    public Image[] pips;
    public TMP_Text bonusText;
    public GameObject hintRow;
    public TMP_Text keyText;
    public TMP_Text nextText;
    [Tooltip("How far the last line moves up when the item is maxed out (the hint row is hidden then)")]
    public float maxedLift = 35.6f;

    const string L10nTable = "Panels";

    OfficeItemInteractable shown;
    int shownLevel = -1;
    Vector2 nextHome;
    bool nextHomeKnown;
    readonly List<Renderer> renderers = new List<Renderer>();
    Camera cam;

    void Awake()
    {
        if (flow == null) flow = GetComponentInParent<UpgradeFlowView>(true);
        L10nFont.AttachAll(this);   // each language's font (English keeps the design font)
    }

    void OnEnable()
    {
        OfficeItemInteractable.UseDesignedHoverTag = true;
        L10n.LanguageChanged += OnLanguageChanged;
        SetVisible(false);
        shown = null;
    }

    void OnDisable()
    {
        OfficeItemInteractable.UseDesignedHoverTag = false;
        L10n.LanguageChanged -= OnLanguageChanged;
    }

    // the tag is filled again on the next frame
    void OnLanguageChanged() => shownLevel = -1;

    void SetVisible(bool on)
    {
        if (tagRect != null && tagRect.gameObject.activeSelf != on) tagRect.gameObject.SetActive(on);
        if (stringCurve != null && stringCurve.gameObject.activeSelf != on) stringCurve.gameObject.SetActive(on);
        if (knot != null && knot.gameObject.activeSelf != on) knot.gameObject.SetActive(on);
    }

    void LateUpdate()
    {
        var item = OfficeItemInteractable.Hovered;
        if (item == null || item.itemData == null || !item.isActiveAndEnabled)
        {
            if (shown != null) shown = null;
            SetVisible(false);
            return;
        }

        if (item != shown)
        {
            shown = item;
            shownLevel = -1;
            renderers.Clear();
            foreach (var r in item.GetComponentsInChildren<Renderer>())
                if (!(r is ParticleSystemRenderer) && r.GetComponent<TMP_Text>() == null)
                    renderers.Add(r);
        }
        if (item.itemData.currentLevel != shownLevel)
        {
            shownLevel = item.itemData.currentLevel;
            Show(item.itemData, item.interactKey);
        }

        SetVisible(true);
        var root = (RectTransform)transform;
        Rect area = root.rect;
        if (!ItemRect(root, out Rect bounds))
        {
            // No bounds on screen: hang the tag from the pointer
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, Input.mousePosition, null, out Vector2 p);
            Vector2 tl = new Vector2(p.x - area.xMin, area.yMax - p.y);
            bounds = new Rect(tl.x - 1f, tl.y - 1f, 2f, 2f);
        }
        Place(bounds, new Vector2(area.width, area.height));
    }

    /// <summary>The item's on-screen bounds in this rect's top-left coordinates (y down).</summary>
    bool ItemRect(RectTransform root, out Rect r)
    {
        r = default;
        if (cam == null || !cam.isActiveAndEnabled) cam = Camera.main;
        if (cam == null || renderers.Count == 0) return false;

        bool any = false;
        Bounds b = default;
        foreach (var rend in renderers)
        {
            if (rend == null || !rend.enabled || !rend.gameObject.activeInHierarchy) continue;
            if (!any) { b = rend.bounds; any = true; }
            else b.Encapsulate(rend.bounds);
        }
        if (!any) return false;

        Rect area = root.rect;
        float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
        Vector3 c = b.center, e = b.extents;
        for (int i = 0; i < 8; i++)
        {
            var corner = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            Vector3 sp = cam.WorldToScreenPoint(corner);
            if (sp.z <= cam.nearClipPlane) return false;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, sp, null, out Vector2 lp);
            float x = lp.x - area.xMin, y = area.yMax - lp.y;
            xMin = Mathf.Min(xMin, x); xMax = Mathf.Max(xMax, x);
            yMin = Mathf.Min(yMin, y); yMax = Mathf.Max(yMax, y);
        }
        r = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        // A huge item (camera inside it) gives no useful edge: use the pointer instead
        return r.width < area.width * 0.7f && r.height < area.height * 0.8f;
    }

    /// <summary>Hang the tag beside an item's bounds (top-left coordinates of a screen of the given size).</summary>
    public void Place(Rect item, Vector2 screen)
    {
        float m = screenMargin;
        float knotY = Mathf.Clamp(item.yMin + item.height * 0.38f, m, screen.y - m);
        var knotRight = new Vector2(Mathf.Min(item.xMax - item.width * 0.045f, screen.x - m), knotY);
        var knotLeft = new Vector2(Mathf.Max(item.xMin + item.width * 0.045f, m), knotY);

        bool mirrored = false;
        Vector2 knotPos = knotRight;
        float left = knotRight.x + tagOffset.x;
        if (left + tagSize.x > screen.x - m)
        {
            float mirroredLeft = knotLeft.x - tagOffset.x - tagSize.x;
            if (mirroredLeft >= m || mirroredLeft > screen.x - m - tagSize.x - left)
            {
                mirrored = true;
                knotPos = knotLeft;
                left = mirroredLeft;
            }
        }
        left = Mathf.Clamp(left, m, Mathf.Max(m, screen.x - m - tagSize.x));
        float top = Mathf.Clamp(knotPos.y + tagOffset.y, m, Mathf.Max(m, screen.y - m - tagSize.y));

        // top-left coordinates -> this rect's local space (pivot in the middle of the screen-sized rect)
        Vector2 Local(Vector2 tl) => new Vector2(tl.x - screen.x * 0.5f, screen.y * 0.5f - tl.y);

        if (tagRect != null)
        {
            tagRect.anchorMin = tagRect.anchorMax = new Vector2(0.5f, 0.5f);
            tagRect.anchoredPosition = Local(new Vector2(left + tagSize.x * 0.5f, top + tagSize.y * 0.5f));
        }
        if (art != null) art.localScale = new Vector3(mirrored ? -1f : 1f, 1f, 1f);
        if (shadow != null) shadow.localScale = new Vector3(mirrored ? -1f : 1f, 1f, 1f);
        if (content != null)
            content.anchoredPosition = new Vector2(mirrored ? contentLeftMirrored : contentLeft, content.anchoredPosition.y);

        // eyelet in the view's local space (the tag is rotated about its centre)
        Vector2 eye = new Vector2(mirrored ? tagSize.x - eyelet.x : eyelet.x, eyelet.y);
        Vector2 eyeFromCentre = new Vector2(eye.x - tagSize.x * 0.5f, tagSize.y * 0.5f - eye.y);
        float angle = tagRect != null ? tagRect.localEulerAngles.z * Mathf.Deg2Rad : 0f;
        Vector2 rotated = new Vector2(eyeFromCentre.x * Mathf.Cos(angle) - eyeFromCentre.y * Mathf.Sin(angle),
                                      eyeFromCentre.x * Mathf.Sin(angle) + eyeFromCentre.y * Mathf.Cos(angle));
        Vector2 tagCentre = Local(new Vector2(left + tagSize.x * 0.5f, top + tagSize.y * 0.5f));
        Vector2 eyeLocal = tagCentre + rotated;
        Vector2 knotLocal = Local(knotPos);

        if (knot != null) knot.anchoredPosition = knotLocal;
        if (stringCurve != null) SetString(eyeLocal, knotLocal);
    }

    /// <summary>The loop from the eyelet to the knot: two strands through the hole, sagging a little.</summary>
    void SetString(Vector2 eye, Vector2 end)
    {
        // local space is y up; the design's curves (y down) sag towards the bottom of the screen
        Vector2 d = end - eye;
        Vector2 perp = new Vector2(-d.y, d.x);
        if (perp.y > 0f) perp = -perp;          // the side that points down
        var hole = new Vector2(1f, 3.5f);
        Vector2 a0 = eye + hole, b0 = eye - hole;
        stringCurve.SetStrands(
            a0, eye + d * 0.22f + perp * 0.084f, eye + d * 0.55f + perp * 0.089f, end,
            b0, eye + d * 0.18f + perp * 0.24f, eye + d * 0.54f + perp * 0.237f, end);
    }

    /// <summary>Fill the tag for an item (also used for previews).</summary>
    public void Show(OfficeItem data, KeyCode key)
    {
        if (flow == null) flow = GetComponentInParent<UpgradeFlowView>(true);
        int level = data.currentLevel;
        if (icon != null && flow != null)
        {
            var s = flow.IconFor(data.itemName);
            if (s != null) icon.sprite = s;
        }
        if (itemName != null) itemName.text = data.DisplayName;
        if (levelText != null) levelText.text = data.GetCurrentLevelName() + " · " + UpgradeFlowView.LevelOf(level, data.maxLevel);
        UpgradeFlowView.SetPips(pips, level, data.maxLevel);
        if (bonusText != null)
            bonusText.text = "<color=" + UpgradeFlowView.Hex(UIColors.Money) + ">" + UpgradeFlowView.Daily(data, level) + "</color><space=8>· " +
                             UpgradeFlowView.Productivity(data, level);

        bool canUpgrade = data.CanUpgrade();
        if (hintRow != null) hintRow.SetActive(canUpgrade);
        if (keyText != null) keyText.text = KeyLabel(key);
        if (nextText != null)
        {
            nextText.text = canUpgrade
                ? L10n.Get(L10nTable, "upgrade.tag.next", ("price", "<link=\"" + TextHighlighter.LinkId + "\"><b>" + UIFormat.Money(data.GetUpgradeCost()) + "</b></link>"))
                : "<b>" + L10n.Get(L10nTable, "upgrade.tag.maxed") + "</b>";
            var rt = nextText.rectTransform;
            if (!nextHomeKnown)
            {
                nextHome = rt.anchoredPosition;
                nextHomeKnown = true;
            }
            rt.anchoredPosition = nextHome + new Vector2(0f, canUpgrade ? 0f : maxedLift);
        }
    }

    static string KeyLabel(KeyCode key)
    {
        string s = key.ToString();
        if (s.StartsWith("Alpha")) return s.Substring(5);
        return s;
    }
}
