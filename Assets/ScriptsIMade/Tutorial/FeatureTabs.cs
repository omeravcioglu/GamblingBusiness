using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// The office HUD's tabs for FeatureGate. OfficeHud.RefreshToolbar calls Refresh first (four times a second) and then
/// sets each tab's lock from FeatureGate.IsUnlocked (Skills, Staff, Stocks, Build; Casino and Online keep their own rules).
/// Refresh, once per HUD:
/// - gives the Skills, Staff and Build tabs a locked look, which the HUD prefab only has for the tabs that could lock
///   before (Stocks, Casino, Online): a copy of the Stocks tab's dashed look with the tab's own icon and label (drawn in
///   code if there is nothing to copy), assigned to HudTab.lockedLook;
/// - gives Skills, Staff, Stocks, Build and Casino a NEW badge (HudTab.newBadge; only the Online tab has one in the prefab);
/// - puts FeatureTabClick on the gated tabs (clicking a locked tab says when it opens);
/// - Demo: stamps FULL GAME on the locked look of each feature the demo keeps for the full game (DemoMode.IsLocked).
/// Every time: the gated tabs' tooltip hint ("opens on day 3", in the current language) and the NEW badges
/// (FeatureGate.IsNew: the feature opened in this run and its screen hasn't been opened since).
/// A designed HUD that brings its own locked looks or badges keeps them: only missing ones are added.
/// </summary>
public static class FeatureTabs
{
    const string OurBadgeName = "NewBadge (Tutorial)";
    const string OurLockedName = "Locked (Tutorial)";
    const string DemoBadgeName = "DemoBadge (Tutorial)";

    // Demo: Online is here for the demo's FULL GAME stamp only; outside the demo OfficeHud keeps its lock, hint and badge
    static readonly Feature[] TabFeatures = { Feature.Skills, Feature.Staff, Feature.Stocks, Feature.Build, Feature.Casino, Feature.Online };

    static OfficeHud prepared;
    static string shownLanguage;
    static bool warned;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        prepared = null;
        shownLanguage = null;
        warned = false;
    }

    /// <summary>A feature's tab on the office HUD (null when it has none).</summary>
    public static HudTab TabFor(OfficeHud hud, Feature feature)
    {
        if (hud == null) return null;
        switch (feature)
        {
            case Feature.Skills: return hud.skillsTab;
            case Feature.Staff: return hud.staffTab;
            case Feature.Stocks: return hud.stocksTab;
            case Feature.Casino: return hud.casinoTab;
            case Feature.Build: return hud.buildTab;
            case Feature.Online: return hud.onlineTab;
            default: return null;
        }
    }

    /// <summary>OfficeHud.RefreshToolbar: prepare the tabs once, then keep their hints and NEW badges current.</summary>
    public static void Refresh(OfficeHud hud)
    {
        if (hud == null) return;
        try
        {
            if (prepared != hud)
            {
                Prepare(hud);
                prepared = hud;
                shownLanguage = null;
            }

            string language = L10n.CurrentCode;
            bool newLanguage = language != shownLanguage;
            shownLanguage = language;

            foreach (Feature feature in TabFeatures)
            {
                HudTab tab = TabFor(hud, feature);
                if (tab == null) continue;
                if (newLanguage)
                {
                    if (FeatureGate.GatesTab(feature)) tab.lockedHint = FeatureGate.LockedHint(feature);
                    WriteBadge(tab);
                    WriteDemoBadge(tab);   // Demo
                }
                if (feature != Feature.Online) tab.SetNew(FeatureGate.IsNew(feature));   // Demo: OfficeHud owns Online's badge
            }
        }
        catch (Exception e)
        {
            if (!warned) Debug.LogWarning("[Tutorial] Couldn't prepare the HUD tabs: " + e.Message);
            warned = true;
        }
    }

    static void Prepare(OfficeHud hud)
    {
        // Taken before any tab gets a look of ours: the designed locked look with a lock badge
        GameObject template = null;
        if (hud.stocksTab != null && hud.stocksTab.lockedLook != null) template = hud.stocksTab.lockedLook;
        else if (hud.casinoTab != null && hud.casinoTab.lockedLook != null) template = hud.casinoTab.lockedLook;

        foreach (Feature feature in TabFeatures)
        {
            HudTab tab = TabFor(hud, feature);
            if (tab == null) continue;
            if (FeatureGate.GatesTab(feature))
            {
                if (tab.lockedLook == null) tab.lockedLook = BuildLockedLook(tab, template);
                if (tab.button != null)
                {
                    FeatureTabClick click = tab.button.GetComponent<FeatureTabClick>();
                    if (click == null) click = tab.button.gameObject.AddComponent<FeatureTabClick>();
                    click.feature = feature;
                    click.tab = tab;
                }
            }
            if (tab.newBadge == null && feature != Feature.Online) tab.newBadge = BuildNewBadge(tab, feature);
        }

        // Demo: the FULL GAME stamp on the locked look, once every look exists (the Build tab's is copied from the Stocks
        // tab's, which must not carry the stamp yet)
        foreach (Feature feature in TabFeatures)
        {
            HudTab tab = TabFor(hud, feature);
            if (tab != null && DemoMode.IsLocked(feature)) BuildDemoBadge(tab);
        }
    }

    // ------------------------------------------------------------------
    // Demo: FULL GAME stamp
    // ------------------------------------------------------------------

    /// <summary>An ink pill hanging from the locked tab's bottom edge (the locked look is all the demo ever shows of it).</summary>
    static void BuildDemoBadge(HudTab tab)
    {
        if (tab.lockedLook == null || tab.lockedLook.transform.Find(DemoBadgeName) != null) return;
        Transform look = tab.lockedLook.transform;
        Transform price = look.Find("PriceChip");   // Online's "$1B" chip would promise an unlock the demo never gives
        if (price != null) price.gameObject.SetActive(false);

        RectTransform pill = StatusUI.NewRect(DemoBadgeName, look);
        pill.anchorMin = pill.anchorMax = new Vector2(0.5f, 0f);
        pill.pivot = new Vector2(0.5f, 1f);
        pill.anchoredPosition = new Vector2(0f, 8f);
        pill.sizeDelta = new Vector2(96f, 26f);

        StatusBox box = pill.gameObject.AddComponent<StatusBox>();
        box.SetStyle(UIColors.Ink, UIColors.PaperHi, 2f);
        box.Radii = Vector4.one * 13f;
        box.raycastTarget = false;

        TMP_Text lockedLabel = ChildComponent<TMP_Text>(look, "Label");
        TMP_FontAsset font = lockedLabel != null && lockedLabel.font != null ? lockedLabel.font : BoardUI.ResolveFont(null);
        TextMeshProUGUI text = StatusUI.Text(pill, "Label", "", 15f, UIColors.PaperHi, font, TextAlignmentOptions.Center, true);
        StatusUI.Stretch(text.rectTransform);
        L10nFont.Attach(text);

        StatusFitWidth fit = pill.gameObject.AddComponent<StatusFitWidth>();   // as wide as the word (it differs per language)
        fit.text = text;
        fit.padding = 20f;
        WriteDemoBadge(tab);
    }

    static void WriteDemoBadge(HudTab tab)
    {
        if (tab.lockedLook == null) return;
        Transform pill = tab.lockedLook.transform.Find(DemoBadgeName);
        TMP_Text text = pill != null ? pill.GetComponentInChildren<TMP_Text>(true) : null;
        if (text != null) text.text = DemoMode.T("demo.tab.full_game");
    }

    // ------------------------------------------------------------------
    // Locked look
    // ------------------------------------------------------------------

    static GameObject BuildLockedLook(HudTab tab, GameObject template)
    {
        Transform parent = tab.swing != null ? tab.swing : tab.transform;
        GameObject look;
        if (template != null)
        {
            look = Object.Instantiate(template, parent, false);
            look.name = OurLockedName;
            StripLocalization(look.transform.Find("Label"));
            CopyIcon(tab, look.transform);
            CopyLabel(tab, look.transform);
        }
        else
        {
            look = BuildDrawnLockedLook(tab, parent);
        }

        if (tab.openLook != null && tab.openLook.transform.parent == parent)
            look.transform.SetSiblingIndex(tab.openLook.transform.GetSiblingIndex() + 1);
        look.SetActive(tab.IsLocked);   // HudTab switches it from the next SetLocked on
        return look;
    }

    static void CopyIcon(HudTab tab, Transform look)
    {
        Image target = ChildComponent<Image>(look, "Icon");
        Image source = tab.openLook != null ? ChildComponent<Image>(tab.openLook.transform, "Icon") : null;
        if (target == null || source == null) return;
        target.sprite = source.sprite;
        target.preserveAspect = source.preserveAspect;
    }

    static void CopyLabel(HudTab tab, Transform look)
    {
        TMP_Text target = ChildComponent<TMP_Text>(look, "Label");
        TMP_Text source = tab.openLook != null ? ChildComponent<TMP_Text>(tab.openLook.transform, "Label") : null;
        if (target != null && source != null) target.text = source.text;
    }

    /// <summary>A copied label must not keep writing the template tab's word (if the HUD gets localized later).</summary>
    static void StripLocalization(Transform label)
    {
        if (label == null) return;
        foreach (MonoBehaviour behaviour in label.GetComponents<MonoBehaviour>())
        {
            if (behaviour == null) continue;
            string typeName = behaviour.GetType().Name;
            if (behaviour is L10nText || typeName == "LocalizeStringEvent")
                Object.Destroy(behaviour);
        }
    }

    /// <summary>The design's locked tab drawn in code: dashed paper-shade tab, pencil icon and label, lock badge.</summary>
    static GameObject BuildDrawnLockedLook(HudTab tab, Transform parent)
    {
        RectTransform look = StatusUI.NewRect(OurLockedName, parent);
        StatusUI.Stretch(look, 0f, 0f, 0f, 4f);   // 4 px shorter than the open tab
        StatusBox paper = look.gameObject.AddComponent<StatusBox>();
        paper.SetStyle(UIColors.PaperShade, UIColors.Pencil, 3f);
        paper.SetDash(true, 9f, 6f);
        paper.Radii = new Vector4(0f, 0f, 6f, 6f);
        paper.SetShadow(UIColors.Dark(0.22f), new Vector2(0f, 8f), 12f);
        paper.raycastTarget = false;

        if (tab.openLook != null)
        {
            foreach (string part in new[] { "Icon", "Label" })
            {
                Transform source = tab.openLook.transform.Find(part);
                if (source == null) continue;
                GameObject copy = Object.Instantiate(source.gameObject, look, false);
                copy.name = part;
                Graphic graphic = copy.GetComponent<Graphic>();
                if (graphic != null) graphic.color = UIColors.Pencil;
                if (part == "Label") StripLocalization(copy.transform);
            }
        }

        StatusBox badge = StatusUI.Box(look, "LockBadge", UIColors.PaperHi, UIColors.Ink, 2f, Vector4.one * 16f);
        StatusUI.Place(badge.rectTransform, 118f, 16f, 32f, 32f);
        RectTransform glyph = StatusUI.Place(StatusUI.NewRect("Lock", badge.rectTransform), 7f, 7f, 18f, 18f);
        glyph.pivot = new Vector2(0f, 1f);
        UIShape shackle = glyph.gameObject.AddComponent<UIShape>();
        shackle.color = UIColors.Ink;
        shackle.strokeWidth = 2.6f * 0.75f;
        shackle.raycastTarget = false;
        float s = 18f / 24f;
        // The icon's "lock": a 15 x 10 body with a shackle over it
        shackle.AddStroke(Pts(s, new Vector2(4.5f, 10.5f), new Vector2(19.5f, 10.5f), new Vector2(19.5f, 20.5f), new Vector2(4.5f, 20.5f), new Vector2(4.5f, 10.5f)));
        var arc = new System.Collections.Generic.List<Vector2> { new Vector2(8f, 10.5f) };
        StatusUI.AddArc(arc, new Vector2(12f, 7.5f), 4f, 180f, 360f, 10);
        arc.Add(new Vector2(16f, 10.5f));
        shackle.AddStroke(Pts(s, arc.ToArray()));
        return look.gameObject;
    }

    // ------------------------------------------------------------------
    // NEW badge
    // ------------------------------------------------------------------

    /// <summary>The red NEW pill at a top corner of the open tab, 16 px past its edge (Skills keeps its right corner for the point count).</summary>
    static GameObject BuildNewBadge(HudTab tab, Feature feature)
    {
        if (tab.openLook == null) return null;
        RectTransform pill = StatusUI.NewRect(OurBadgeName, tab.openLook.transform);
        bool left = feature == Feature.Skills;
        pill.anchorMin = pill.anchorMax = new Vector2(left ? 0f : 1f, 1f);
        pill.pivot = new Vector2(left ? 0f : 1f, 1f);
        pill.anchoredPosition = new Vector2(left ? -16f : 16f, -14f);
        pill.sizeDelta = new Vector2(58f, 30f);

        StatusBox box = pill.gameObject.AddComponent<StatusBox>();
        box.SetStyle(UIColors.Loss, UIColors.PaperHi, 2f);
        box.Radii = Vector4.one * 15f;
        box.SetOutline(UIColors.Ink, 2f, 0f);
        box.raycastTarget = false;

        TMP_Text openLabel = ChildComponent<TMP_Text>(tab.openLook.transform, "Label");
        TMP_FontAsset font = openLabel != null && openLabel.font != null ? openLabel.font : BoardUI.ResolveFont(null);
        TextMeshProUGUI text = StatusUI.Text(pill, "Label", "", 17f, UIColors.PaperHi, font, TextAlignmentOptions.Center, true);
        StatusUI.Stretch(text.rectTransform);
        L10nFont.Attach(text);

        StatusFitWidth fit = pill.gameObject.AddComponent<StatusFitWidth>();   // as wide as the word (it differs per language)
        fit.text = text;
        fit.padding = 22f;

        pill.gameObject.SetActive(false);
        return pill.gameObject;
    }

    static void WriteBadge(HudTab tab)
    {
        if (tab.newBadge == null || tab.newBadge.name != OurBadgeName) return;   // a designed badge keeps its own word
        TMP_Text text = tab.newBadge.GetComponentInChildren<TMP_Text>(true);
        if (text != null) text.text = L10n.Get(FeatureGate.Table, "tutorial.tab.new");
    }

    // ------------------------------------------------------------------

    static T ChildComponent<T>(Transform parent, string name) where T : Component
    {
        if (parent == null) return null;
        Transform child = parent.Find(name);
        return child != null ? child.GetComponent<T>() : null;
    }

    /// <summary>24-unit icon points (y down) scaled and turned into local points (y up) for a top-left-pivoted UIShape.</summary>
    static Vector2[] Pts(float scale, params Vector2[] drawing)
    {
        var local = new Vector2[drawing.Length];
        for (int i = 0; i < drawing.Length; i++)
            local[i] = new Vector2(drawing[i].x * scale, -drawing[i].y * scale);
        return local;
    }
}
