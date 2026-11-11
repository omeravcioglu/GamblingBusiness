using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One pinned index card on the skill board (204 x 94): name, icon, effect line and its state.
/// Owned = ruled white card with a green check; available = yellow sticky with a glow and its SP price;
/// locked = grey card with a dashed edge and a lock. The selected card gets a thicker ring; a card just unlocked
/// gets the APPROVED stamp. SkillBoardView passes the texts in the current language and calls Show again on a
/// language change.
/// </summary>
[DisallowMultipleComponent]
public class SkillCardView : MonoBehaviour
{
    // Demo: FullGame = a skill the demo keeps for the full game (grey like Locked, no lock badge, a FULL GAME chip)
    public enum State { Owned, Available, Locked, FullGame }

    public Button button;

    [Header("Face")]
    public Image fill;
    public GameObject rules;
    public GameObject solidBorder;
    public GameObject dashedBorder;
    public GameObject hardShadow;
    public TMP_Text nameText;
    public TMP_Text effectText;
    public Image icon;

    [Header("Rings and glow")]
    public Image glow;
    public Image outerRing;
    public GameObject inkRing;
    public Sprite glowSmall;
    public Sprite glowLarge;
    [Tooltip("Shadow sprite padding (3 x sigma) of glowSmall and glowLarge")]
    public float glowSmallPad = 36f;
    public float glowLargePad = 48f;

    [Header("Badges")]
    public GameObject ownedBadge;
    public GameObject costChip;
    public TMP_Text costText;
    public GameObject lockBadge;
    public GameObject approvedStamp;

    static readonly Color GreenRing = new Color32(0x7E, 0xD6, 0x8E, 0xFF);

    public void Show(string skillName, string effect, Sprite iconSprite, int cost, State state, bool selected, bool stamped)
    {
        bool owned = state == State.Owned, avail = state == State.Available, lockBadgeOn = state == State.Locked;
        bool fullGame = state == State.FullGame;   // Demo
        bool locked = lockBadgeOn || fullGame;     // Demo: both wear the grey, dashed face

        if (nameText != null)
        {
            nameText.text = skillName;
            nameText.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
            nameText.color = locked ? (selected ? UIColors.InkSoft : UIColors.Pencil) : UIColors.Ink;
        }
        if (effectText != null) effectText.text = effect;
        if (icon != null)
        {
            if (iconSprite != null) icon.sprite = iconSprite;
            icon.color = locked ? UIColors.Pencil : owned ? UIColors.Money : UIColors.Ink;
        }

        if (fill != null) fill.color = locked ? UIColors.PaperShade : avail ? UIColors.StickyYellow : UIColors.PaperHi;
        SetActive(rules, owned);
        SetActive(solidBorder, !locked);
        SetActive(dashedBorder, locked);
        SetActive(hardShadow, owned && !selected);

        // rings: available 4 px yellow + soft glow; selected: 2 px ink, 7 px ring (green once owned), bigger glow
        bool ring = avail || selected;
        SetActive(outerRing != null ? outerRing.gameObject : null, ring);
        SetActive(glow != null ? glow.gameObject : null, ring);
        SetActive(inkRing, selected && !locked);
        if (ring)
        {
            float grow = selected ? 7f : 4f;
            Color ringColor = selected && owned ? GreenRing : UIColors.Highlight;
            SetOutset(outerRing != null ? outerRing.rectTransform : null, grow, Vector2.zero);
            if (outerRing != null) outerRing.color = ringColor;

            if (glow != null)
            {
                float spread = selected ? 10f : 6f;
                glow.sprite = selected ? glowLarge : glowSmall;
                SetOutset(glow.rectTransform, spread + (selected ? glowLargePad : glowSmallPad), Vector2.zero);
                float a = selected ? (owned ? 0.55f : locked ? 0.45f : 0.6f) : 0.5f;
                glow.color = UIColors.WithAlpha(ringColor, a);
            }
        }

        SetActive(ownedBadge, owned);
        if (fullGame && costChip == null) BuildFullGameChip();   // Demo: a card face without a price chip gets one
        SetActive(costChip, avail || fullGame);
        if (costText != null)
            costText.text = fullGame ? DemoMode.T("demo.tab.full_game") : L10n.Get(SkillText.Table, "skillboard.cost", ("cost", cost));
        if (costChip != null)
        {
            Transform sparkle = costChip.transform.Find("Icon");   // Demo: no price, so no SP sparkle
            if (sparkle != null) SetActive(sparkle.gameObject, !fullGame);
        }
        SetActive(lockBadge, lockBadgeOn);
        SetActive(approvedStamp, stamped && owned);
    }

    // ------------------------------------------------------------------
    // Demo: FULL GAME chip and BOARD GIFT tape

    GameObject giftTape;
    TMP_Text giftText;

    /// <summary>Demo: the BOARD GIFT tape on a skill the Board granted (SkillBoardView calls it after Show).</summary>
    public void ShowGift(bool gift)
    {
        if (!gift && giftTape == null) return;
        if (gift && giftTape == null) BuildGiftTape();
        if (giftText != null && gift) giftText.text = DemoMode.T("demo.skill.gift");
        SetActive(giftTape, gift);
    }

    TMP_FontAsset CardFont() => nameText != null && nameText.font != null ? nameText.font : BoardUI.ResolveFont(null);

    void BuildGiftTape()
    {
        RectTransform tape = StatusUI.NewRect("DemoGiftTape", transform);
        tape.anchorMin = tape.anchorMax = new Vector2(0f, 1f);
        tape.pivot = new Vector2(0.5f, 0.5f);
        tape.anchoredPosition = new Vector2(38f, 4f);   // across the top-left corner, clear of the pin and the name
        tape.sizeDelta = new Vector2(104f, 22f);
        tape.localEulerAngles = new Vector3(0f, 0f, 5f);
        StatusBox box = tape.gameObject.AddComponent<StatusBox>();
        box.SetStyle(UIColors.Tape, UIColors.WithAlpha(UIColors.Ink, 0.25f), 1f);
        box.raycastTarget = false;
        TextMeshProUGUI text = StatusUI.Text(tape, "Label", "", 13f, UIColors.Ink, CardFont(), TextAlignmentOptions.Center, true);
        StatusUI.Stretch(text.rectTransform);
        L10nFont.Attach(text);
        StatusFitWidth fit = tape.gameObject.AddComponent<StatusFitWidth>();
        fit.text = text;
        fit.padding = 20f;
        giftTape = tape.gameObject;
        giftText = text;
    }

    void BuildFullGameChip()
    {
        // The designed price chip's place: the card's top-right corner, growing left with the word
        RectTransform chip = StatusUI.NewRect("DemoCost", transform);
        chip.anchorMin = chip.anchorMax = new Vector2(0f, 1f);
        chip.pivot = new Vector2(1f, 1f);
        chip.anchoredPosition = new Vector2(214f, 13f);
        chip.sizeDelta = new Vector2(96f, 28f);
        StatusBox box = chip.gameObject.AddComponent<StatusBox>();
        box.SetStyle(UIColors.PaperHi, UIColors.Ink, 2f);
        box.Radii = Vector4.one * 8f;
        box.raycastTarget = false;
        TextMeshProUGUI text = StatusUI.Text(chip, "Text", "", 15f, UIColors.Ink, CardFont(), TextAlignmentOptions.Center, true);
        StatusUI.Stretch(text.rectTransform);
        L10nFont.Attach(text);
        StatusFitWidth fit = chip.gameObject.AddComponent<StatusFitWidth>();
        fit.text = text;
        fit.padding = 22f;
        costChip = chip.gameObject;
        costText = text;
    }

    static void SetActive(GameObject go, bool on)
    {
        if (go != null && go.activeSelf != on) go.SetActive(on);
    }

    static void SetOutset(RectTransform rt, float by, Vector2 shift)
    {
        if (rt == null) return;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(-by + shift.x, -by - shift.y);
        rt.offsetMax = new Vector2(by + shift.x, by - shift.y);
    }
}
