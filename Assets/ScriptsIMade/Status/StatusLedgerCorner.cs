using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The before/after picture on the promotion memo: the top-right corner of the office HUD's company ledger at real
/// scale, in one status tier's materials (design board "Status upgrades: every part by tier", ledger corners), drawn
/// with simple shapes: the binding behind the ledger (ring binder, oxblood leather, walnut, black), its trim line, the
/// letterhead rule, the mark (logo, embossed seal, CEO nameplate, crown), the brass clip and the corner protectors.
/// Builds its parts inside its own RectTransform (400 x 164 in the design) the first time it is shown. Its labels
/// (COMPANY VALUE, THE CEO) come from the "Status" String Table and every text follows the language's font.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class StatusLedgerCorner : MonoBehaviour
{
    /// <summary>Tiers the design draws (0-6); higher tiers of a longer custom ladder use the last look.</summary>
    public const int LookCount = 7;

    const float LedgerWidth = 440f;
    const float LedgerHeight = 330f;
    const float CropX = -53f;         // where the ledger sits in the frame, so its top-right corner shows
    const float CropY = 6f;
    const float FrameBorder = 2f;
    const float NameLeft = 56f;       // the ledger's text column
    const float NameRight = 408f;

    static readonly Vector4 FrameRadii = new Vector4(12f, 11f, 12f, 13f);
    static readonly Color FrameColor = UIColors.Hex(0x313C37);
    static readonly Color Navy = UIColors.Hex(0x22385C);
    static readonly Color Brass = UIColors.Hex(0xC2A04A);
    static readonly Color BrassShine = UIColors.Hex(0xEBD28E);
    static readonly Color GoldShine = UIColors.Hex(0xF7DC87);
    static readonly Color Gilt = UIColors.Hex(0xC99A2E);
    static readonly Color Black = UIColors.Hex(0x1B1B1F);
    static readonly Color Stitch = new Color32(236, 220, 188, 217);
    static readonly Color ClipBrass = UIColors.Hex(0x7D6424);
    static readonly Color ClipShine = UIColors.Hex(0xE3C981);
    static readonly Color Emboss = UIColors.Hex(0xD9E6D0);
    static readonly Color EmbossShadow = new Color(29f / 255f, 36f / 255f, 51f / 255f, 0.3f);
    static readonly Color PlateInk = UIColors.Hex(0x2A1D06);
    static readonly Color Screw = UIColors.Hex(0x8A6B25);

    // The binding behind the ledger, top and bottom of its sheen: none, ring binder (x2), oxblood leather, walnut, black (x2)
    static readonly Color Binder = UIColors.Hex(0x34506F);
    static readonly Color[] BindingTop =
    {
        Color.clear, Color.Lerp(Binder, Color.white, 0.16f), Color.Lerp(Binder, Color.white, 0.16f),
        UIColors.Hex(0x74262D), UIColors.Hex(0x6B4630), UIColors.Hex(0x2A2A30), UIColors.Hex(0x2A2A30)
    };
    static readonly Color[] BindingBottom =
    {
        Color.clear, Color.Lerp(Binder, Color.black, 0.14f), Color.Lerp(Binder, Color.black, 0.14f),
        UIColors.Hex(0x4E1519), UIColors.Hex(0x4A2F1C), UIColors.Hex(0x141417), UIColors.Hex(0x141417)
    };

    [Tooltip("Font of the ledger's texts (empty = TextMesh Pro's default)")]
    public TMP_FontAsset font;
    [Tooltip("Colour around the frame: it paints over the picture in the frame's rounded corners")]
    public Color surroundColor = UIColors.PaperHi;

    RectTransform ledger;
    StatusBox binding, bindingLine, page;
    Image ruleTop, ruleBottom;
    TextMeshProUGUI nameText, valueText;
    GameObject logo, seal, plate, crown, clip, cornerGuard;
    UIPolyline guardFill, guardShine;
    bool built;

    /// <summary>Draw the corner in <paramref name="tier"/>'s materials, with this company name and value.</summary>
    public void Show(int tier, string companyName, double value)
    {
        EnsureBuilt();
        int t = Mathf.Clamp(tier, 0, LookCount - 1);
        ApplyLook(t);

        nameText.text = companyName;
        valueText.text = UIFormat.Money(value);

        // Keep the name clear of the mark on the right
        float right = t == 2 ? 377f : (t == 5 ? 279f : (t >= 3 ? 365f : NameRight));
        nameText.rectTransform.sizeDelta = new Vector2(right - NameLeft, nameText.rectTransform.sizeDelta.y);
    }

    void ApplyLook(int t)
    {
        binding.gameObject.SetActive(t >= 1);
        if (t >= 1)
        {
            binding.SetGradient(BindingTop[t], BindingBottom[t]);
            binding.Border = t == 6 ? UIColors.Gold : UIColors.Ink;
        }

        bindingLine.gameObject.SetActive(t >= 3);
        if (t >= 3)
        {
            bindingLine.Border = t == 3 ? Stitch : (t == 4 ? Brass : UIColors.Gold);
            bindingLine.SetDash(t == 3, 6f, 4f);   // stitched leather
        }

        page.Border = t == 6 ? Gilt : UIColors.Ink;
        if (t == 0)
            page.SetShadow(UIColors.Dark(0.45f), new Vector2(0f, 22f), 48f);   // a loose ledger lying on the desk
        else
            page.SetShadow(UIColors.Dark(0.3f), new Vector2(0f, 2f));

        bool rule = t >= 2;
        ruleTop.gameObject.SetActive(rule);
        ruleBottom.gameObject.SetActive(rule);
        ruleTop.color = t == 2 ? Navy : (t <= 4 ? UIColors.Info : Black);
        ruleBottom.color = t <= 4 ? Brass : UIColors.Gold;

        logo.SetActive(t == 2);
        seal.SetActive(t == 3 || t == 4);
        plate.SetActive(t == 5);
        crown.SetActive(t == 6);
        clip.SetActive(t >= 2 && t <= 4);

        cornerGuard.SetActive(t >= 4);
        guardFill.color = t == 4 ? Brass : UIColors.Gold;
        guardShine.color = t == 4 ? BrassShine : GoldShine;
    }

    // ------------------------------------------------------------------
    // Building (positions in the ledger's drawing space: from its top-left corner, y down)
    // ------------------------------------------------------------------

    void EnsureBuilt()
    {
        if (built) return;
        built = true;
        var frame = (RectTransform)transform;

        StatusBox back = StatusUI.Box(frame, "Frame", FrameColor, Color.clear, 0f, FrameRadii);
        StatusUI.Stretch(back.rectTransform);

        RectTransform view = StatusUI.NewRect("View", frame);
        StatusUI.Stretch(view, FrameBorder, FrameBorder, FrameBorder, FrameBorder);
        view.gameObject.AddComponent<RectMask2D>();

        ledger = StatusUI.Place(StatusUI.NewRect("Ledger", view), CropX - FrameBorder, CropY - FrameBorder, LedgerWidth, LedgerHeight);

        binding = StatusUI.Box(ledger, "Binding", Color.white, UIColors.Ink, 3f, new Vector4(33f, 32f, 33f, 35f));
        StatusUI.Place(binding.rectTransform, 0f, 0f, LedgerWidth, LedgerHeight);
        bindingLine = StatusUI.Box(ledger, "BindingLine", Color.clear, Brass, 2f, new Vector4(31f, 30f, 31f, 33f));
        StatusUI.Place(bindingLine.rectTransform, 5f, 5f, 430f, 320f);

        page = StatusUI.Box(ledger, "Page", UIColors.Ledger, UIColors.Ink, 3f, new Vector4(23f, 22f, 23f, 25f));
        StatusUI.Place(page.rectTransform, 10f, 10f, 420f, 310f);
        StatusUI.Place(StatusUI.Block(ledger, "Band", UIColors.LedgerBand).rectTransform, 13f, 47f, 414f, 34f);
        StatusUI.Place(StatusUI.Block(ledger, "Band", UIColors.LedgerBand).rectTransform, 13f, 115f, 414f, 34f);

        // Letterhead rule under the company name
        ruleTop = StatusUI.Block(ledger, "RuleTop", Navy);
        StatusUI.Place(ruleTop.rectTransform, 59f, 63f, 346f, 2f);
        ruleBottom = StatusUI.Block(ledger, "RuleBottom", Brass);
        StatusUI.Place(ruleBottom.rectTransform, 59f, 66f, 346f, 1f);

        logo = BuildLogo();
        seal = BuildSeal();
        plate = BuildPlate();
        crown = BuildCrown();

        nameText = StatusUI.Text(ledger, "Company", "", 26f, UIColors.Ink, font, TextAlignmentOptions.Left, true);
        nameText.overflowMode = TextOverflowModes.Ellipsis;
        StatusUI.Place(nameText.rectTransform, NameLeft, 24f, NameRight - NameLeft, 33.8f);
        TextMeshProUGUI label = StatusUI.Text(ledger, "Label", "", 17f, UIColors.Pencil, font);
        label.characterSpacing = 8f;
        StatusUI.Place(label.rectTransform, NameLeft, 63.8f, NameRight - NameLeft, 25.8f);
        L10nText.Attach(label, StatusLadder.Table, "status.ledger.value_label");   // "COMPANY VALUE"
        valueText = StatusUI.Text(ledger, "Value", "", 50f, UIColors.Ink, font, TextAlignmentOptions.Left, true);
        StatusUI.Place(valueText.rectTransform, NameLeft, 95.6f, 384f, 52.5f);

        cornerGuard = BuildCornerGuard();
        clip = BuildClip();

        // The frame's edge on top; it also paints its rounded corners in the colour around the frame
        StatusBox edge = StatusUI.Box(frame, "FrameEdge", Color.clear, UIColors.Ink, FrameBorder, FrameRadii);
        StatusUI.Stretch(edge.rectTransform);
        edge.CornerColor = surroundColor;

        // Every text follows the language's font
        L10nFont.AttachAll(frame);
    }

    GameObject BuildLogo()
    {
        StatusBox disc = StatusUI.Box(ledger, "Logo", Navy, UIColors.Ink, 2f, Vector4.one * 15f);
        StatusUI.Place(disc.rectTransform, 381f, 23f, 30f, 30f);
        StatusUI.Star(disc.rectTransform, "Sparkle", UIColors.PaperHi, 11f);
        return disc.gameObject;
    }

    GameObject BuildSeal()
    {
        RectTransform seal = StatusUI.Place(StatusUI.NewRect("Seal", ledger), 369f, 16f, 44f, 44f);
        // Embossed into the page: a dark copy down-right, a white copy up-left, the pale seal on top
        SealLayer(seal, new Vector2(1f, 1.4f), EmbossShadow);
        SealLayer(seal, new Vector2(-0.8f, -0.8f), new Color(1f, 1f, 1f, 0.95f));
        SealLayer(seal, Vector2.zero, Emboss);
        return seal.gameObject;
    }

    static void SealLayer(RectTransform seal, Vector2 offset, Color color)
    {
        // Serrated outer ring (radius 19, 4 wide) and a thin inner ring (radius 13.5)
        StatusBox outer = StatusUI.Box(seal, "Ring", Color.clear, color, 4f, Vector4.one * 21f);
        StatusUI.Place(outer.rectTransform, 1f + offset.x, 1f + offset.y, 42f, 42f);
        outer.SetDash(true, 2.3f, 2f);
        StatusBox inner = StatusUI.Box(seal, "InnerRing", Color.clear, color, 1.6f, Vector4.one * 14.3f);
        StatusUI.Place(inner.rectTransform, 7.7f + offset.x, 7.7f + offset.y, 28.6f, 28.6f);
    }

    GameObject BuildPlate()
    {
        StatusBox plateBox = StatusUI.Box(ledger, "Nameplate", Color.white, UIColors.Ink, 2f, Vector4.one * 4f);
        plateBox.SetGradient(UIColors.Hex(0xEFD999), UIColors.Hex(0xB89543));
        plateBox.SetShadow(UIColors.Dark(0.3f), new Vector2(0f, 2f));
        StatusUI.Place(plateBox.rectTransform, 283f, 23f, 128f, 30f);

        TextMeshProUGUI text = StatusUI.Text(plateBox.rectTransform, "Text", "", 17f, PlateInk, font, TextAlignmentOptions.Center, true);
        text.characterSpacing = 12f;
        StatusUI.Stretch(text.rectTransform);
        L10nText.Attach(text, StatusLadder.Table, "status.ledger.nameplate");   // "THE CEO"
        foreach (float x in new[] { 7f, 115f })
        {
            StatusBox screw = StatusUI.Box(plateBox.rectTransform, "Screw", Screw, Color.clear, 0f, Vector4.one * 3f);
            StatusUI.Place(screw.rectTransform, x, 12f, 6f, 6f);
        }
        return plateBox.gameObject;
    }

    GameObject BuildCrown()
    {
        StatusBox disc = StatusUI.Box(ledger, "Crown", Color.white, UIColors.Ink, 2f, Vector4.one * 22f);
        disc.SetGradient(GoldShine, UIColors.GoldDark);
        disc.SetShadow(UIColors.Dark(0.3f), new Vector2(0f, 2f));
        StatusUI.Place(disc.rectTransform, 369f, 16f, 44f, 44f);

        // Gold rings inside the edge: dark gold under a 3 px gold ring
        StatusBox darkRing = StatusUI.Box(disc.rectTransform, "RingDark", Color.clear, UIColors.GoldDark, 4f, Vector4.one * 20f);
        StatusUI.Stretch(darkRing.rectTransform, 2f, 2f, 2f, 2f);
        StatusBox goldRing = StatusUI.Box(disc.rectTransform, "RingGold", Color.clear, UIColors.Gold, 3f, Vector4.one * 20f);
        StatusUI.Stretch(goldRing.rectTransform, 2f, 2f, 2f, 2f);

        // The crown: a 24-unit icon drawn 26 px wide
        RectTransform icon = StatusUI.Place(StatusUI.NewRect("Icon", disc.rectTransform), 9f, 9f, 26f, 26f);
        float s = 26f / 24f;
        StatusUI.Stroke(icon, "Crown", UIColors.Ink, 1.8f * s, StatusUI.Scaled(s,
            new Vector2(4.5f, 17.5f), new Vector2(3f, 8f), new Vector2(8.2f, 12.2f), new Vector2(12f, 5.5f),
            new Vector2(15.8f, 12.2f), new Vector2(21f, 8f), new Vector2(19.5f, 17.5f), new Vector2(4.5f, 17.5f)));
        StatusUI.Stroke(icon, "Base", UIColors.Ink, 1.8f * s, StatusUI.Scaled(s, new Vector2(4.5f, 20.5f), new Vector2(19.5f, 20.5f)));
        return disc.gameObject;
    }

    GameObject BuildCornerGuard()
    {
        // The design's corner protector ("M2 14Q2 2 14 2H34V12H12V34H2Z" in a 36 x 36 box), mirrored onto the top-right corner
        RectTransform guard = StatusUI.PlaceRotated(StatusUI.NewRect("CornerGuard", ledger), 406f, -2f, 36f, 36f, 0f);
        guard.localScale = new Vector3(-1f, 1f, 1f);

        // Filled as a thick L along the middle of its two 10 px arms
        var middle = new List<Vector2> { new Vector2(29f, 7f), new Vector2(14f, 7f) };
        StatusUI.AddArc(middle, new Vector2(14f, 14f), 7f, -90f, -180f, 6);
        middle.Add(new Vector2(7f, 29f));
        guardFill = StatusUI.Stroke(guard, "Fill", Brass, 10f, middle.ToArray());

        var edge = new List<Vector2> { new Vector2(2f, 14f) };
        for (int i = 1; i <= 6; i++)
        {
            // Quadratic curve (2,14) -> (14,2) around the control point (2,2)
            float t = i / 6f, u = 1f - t;
            edge.Add(u * u * new Vector2(2f, 14f) + 2f * u * t * new Vector2(2f, 2f) + t * t * new Vector2(14f, 2f));
        }
        edge.Add(new Vector2(34f, 2f));
        edge.Add(new Vector2(34f, 12f));
        edge.Add(new Vector2(12f, 12f));
        edge.Add(new Vector2(12f, 34f));
        edge.Add(new Vector2(2f, 34f));
        edge.Add(new Vector2(2f, 14f));
        StatusUI.Stroke(guard, "Edge", UIColors.Ink, 2f, edge.ToArray());

        guardShine = StatusUI.Stroke(guard, "Shine", BrassShine, 1.6f, new Vector2(15f, 5f), new Vector2(31f, 5f));
        guardShine.AddPath(StatusUI.Points(new Vector2(5f, 15f), new Vector2(5f, 31f)));

        StatusBox rivet = StatusUI.Box(guard, "Rivet", UIColors.Ink, Color.clear, 0f, Vector4.one * 1.8f);
        StatusUI.Place(rivet.rectTransform, 6.7f, 6.7f, 3.6f, 3.6f);
        return guard.gameObject;
    }

    GameObject BuildClip()
    {
        // Brass paper clip over the ledger's top edge, tilted 6 degrees clockwise
        RectTransform rect = StatusUI.PlaceRotated(StatusUI.NewRect("Clip", ledger), 264f, -8f, 24f, 60f, -6f);
        Vector2[] path = StatusUI.PaperClip(1f);
        StatusUI.Stroke(rect, "Brass", ClipBrass, 4.2f, path);
        StatusUI.Stroke(rect, "Shine", ClipShine, 2f, path);
        return rect.gameObject;
    }
}
