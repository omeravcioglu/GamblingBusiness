using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Building blocks of the casino screens built at runtime (design boards CasinoHub, Slots, CoinFlip, CasinoBlackjack):
/// felt, paper chips, casino buttons, poker chips, coins and cards, on top of the build mode kit (BlueprintUI,
/// BlueprintBox), the run-end kit (RunEndUI) and the UI kit (UIColors, UIFormat, MarkerButton, UIPolyline). Only used
/// when no designed casino UI is wired in. Positions are 1920 x 1080 reference pixels from the parent's top-left, y down
/// (like the boards' CSS). Build under an inactive parent: MarkerButton reads its face position when it wakes up.
/// Texts come from the "Casino" String Table (Assets/Localization/Source/Casino.csv). Pieces laid out by the width of a
/// text (the value chip, the links) are laid out again by FitValueChip / FitLink when the language changes.
/// </summary>
public static class CasinoUI
{
    /// <summary>The String Table collection of the casino screens' texts (Assets/Localization/Source/Casino.csv).</summary>
    public const string Table = "Casino";

    public static readonly Color FeltShadow = BlueprintUI.FeltShadow;
    public static readonly Color Wood = UIColors.ClipboardDark;
    public static readonly Color GoldLight = UIColors.Hex(0xF7DC87);
    public static readonly Color ChipRed = UIColors.Loss;
    public static readonly Color ChipBlue = UIColors.Info;
    public static readonly Color ChipBlack = UIColors.Hex(0x1B1B1F);
    public static readonly Color ChipGold = UIColors.Gold;
    /// <summary>rgba(29,36,51,0.85): the hard shadow under cards.</summary>
    public static readonly Color CardShadow = new Color32(29, 36, 51, 217);
    /// <summary>A refusal written on ink or felt (the boards' red is too dark there).</summary>
    public static readonly Color SoftRed = UIColors.Hex(0xFF8A7A);
    public static readonly Color SheetShadow = new Color(0f, 0f, 0f, 0.45f);

    public static readonly Vector4 RadiusSmall = BlueprintUI.RadiusSmall;
    public static readonly Vector4 RadiusLarge = BlueprintUI.RadiusLarge;
    public static readonly Vector4 RadiusCard = BlueprintUI.RadiusCard;
    /// <summary>"50% 46% 52% 48%" of a 60 px round button.</summary>
    public static readonly Vector4 RadiusRound = new Vector4(30f, 27.6f, 31.2f, 28.8f);

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>The font to build with: the preferred one, else the loaded Coming Soon font (HeatUI.ResolveFont).</summary>
    public static TMP_FontAsset ResolveFont(TMP_FontAsset preferred)
    {
        return HeatUI.ResolveFont(preferred);
    }

    // ------------------------------------------------------------------
    // Rects, boxes, text
    // ------------------------------------------------------------------

    public static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
    {
        return BlueprintUI.TopLeft(BlueprintUI.NewRect(name, parent), x, y, width, height);
    }

    public static RectTransform FullRect(Transform parent, string name)
    {
        return BlueprintUI.Stretch(BlueprintUI.NewRect(name, parent));
    }

    /// <summary>
    /// A 1920 x 1080 area centred in the parent, in the boards' coordinates: a screen's content goes in it, so on another
    /// aspect ratio it stays centred while the felt or scrim behind it fills the screen.
    /// </summary>
    public static RectTransform Stage(Transform parent)
    {
        RectTransform stage = BlueprintUI.NewRect("Stage", parent);
        stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(0.5f, 0.5f);
        stage.sizeDelta = new Vector2(1920f, 1080f);
        stage.anchoredPosition = Vector2.zero;
        return stage;
    }

    public static BlueprintBox Box(Transform parent, string name, Color fill, Color border, float borderWidth, Vector4 radii,
                                   float x, float y, float width, float height)
    {
        BlueprintBox box = BlueprintUI.Box(parent, name, fill, border, borderWidth, radii);
        BlueprintUI.TopLeft(box.rectTransform, x, y, width, height);
        return box;
    }

    /// <summary>A circle <paramref name="size"/> wide centred on (x, y).</summary>
    public static BlueprintBox Circle(Transform parent, string name, Color fill, Color border, float borderWidth, float size, float x, float y)
    {
        BlueprintBox box = BlueprintUI.Box(parent, name, fill, border, borderWidth, BlueprintUI.Round(size * 0.5f));
        RunEndUI.PlaceCenter(box.rectTransform, x, y);
        box.rectTransform.sizeDelta = new Vector2(size, size);
        return box;
    }

    public static TextMeshProUGUI Text(Transform parent, string name, string text, float size, Color color, TMP_FontAsset font,
                                       float x, float y, float width, float height,
                                       TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, bool bold = false)
    {
        return RunEndUI.Text(parent, name, text, size, color, font, x, y, width, height, alignment, bold);
    }

    public static TextMeshProUGUI Shrink(TextMeshProUGUI text, float min)
    {
        return RunEndUI.Shrink(text, min);
    }

    public static float TextWidth(TMP_FontAsset font, string text, float size, bool bold = false, float letterSpacingEm = 0f)
    {
        return RunEndUI.TextWidth(font, text, size, bold, letterSpacingEm);
    }

    /// <summary>The felt of the boards over the parent (raycast = it takes the clicks meant for what is behind).</summary>
    public static CasinoFelt Felt(Transform parent, string name, bool raycast)
    {
        CasinoFelt felt = FullRect(parent, name).gameObject.AddComponent<CasinoFelt>();
        felt.raycastTarget = raycast;
        return felt;
    }

    /// <summary>The design's gold sparkle (line icon, 2.2 wide), <paramref name="size"/> px with its top-left at (x, y).</summary>
    public static CasinoIcon Sparkle(Transform parent, float x, float y, float size)
    {
        CasinoIcon icon = CasinoIcon.Create(parent, CasinoIconKind.Sparkle, size, UIColors.Gold, x + size * 0.5f, y + size * 0.5f, 2.2f);
        icon.name = "Sparkle";
        return icon;
    }

    // ------------------------------------------------------------------
    // Chips of paper
    // ------------------------------------------------------------------

    /// <summary>
    /// The boards' company value chip at (x, y), <paramref name="width"/> x <paramref name="height"/>: paper, 2 px ink
    /// border, a hard felt shadow, a banknote, "Company value" and the value in bold. Returns the value text.
    /// </summary>
    public static TextMeshProUGUI ValueChip(Transform parent, TMP_FontAsset font, float x, float y, float width, float height)
    {
        BlueprintBox chip = Box(parent, "CompanyValue", UIColors.PaperHi, UIColors.Ink, 2f, RadiusSmall, x, y, width, height);
        chip.SetShadow(FeltShadow, new Vector2(3f, 4f));
        CasinoIcon.Create(chip.transform, CasinoIconKind.Banknote, 28f, UIColors.Ink, 18f + 14f, height * 0.5f);
        string caption = L10n.Get(Table, "casino.value_chip.caption");   // "Company value"
        float captionWidth = Mathf.Ceil(TextWidth(font, caption, 19f)) + 2f;
        TextMeshProUGUI label = Text(chip.transform, "Label", caption, 19f, UIColors.InkSoft, font, 56f, 0f, captionWidth, height);
        L10nText.Attach(label, Table, "casino.value_chip.caption");
        float valueX = 56f + captionWidth + 10f;
        return Shrink(Text(chip.transform, "Value", "$0", 26f, UIColors.Ink, font, valueX, 0f, Mathf.Max(40f, width - valueX - 16f), height,
                           TextAlignmentOptions.MidlineLeft, true), 14f);
    }

    /// <summary>
    /// Lay a value chip (ValueChip) out again for its caption as it reads now, in its current font (after a language
    /// change): the value follows the caption. A chip that grows with its value is resized by its screen afterwards.
    /// </summary>
    public static void FitValueChip(TMP_Text value)
    {
        RectTransform chip = value != null ? value.rectTransform.parent as RectTransform : null;
        Transform labelTransform = chip != null ? chip.Find("Label") : null;
        TMP_Text caption = labelTransform != null ? labelTransform.GetComponent<TMP_Text>() : null;
        if (caption == null) return;
        float captionWidth = Mathf.Ceil(TextWidth(caption.font, caption.text, 19f)) + 2f;
        RectTransform captionRect = caption.rectTransform;
        captionRect.sizeDelta = new Vector2(captionWidth, captionRect.sizeDelta.y);
        float valueX = 56f + captionWidth + 10f;
        RectTransform valueRect = value.rectTransform;
        valueRect.anchoredPosition = new Vector2(valueX, valueRect.anchoredPosition.y);
        valueRect.sizeDelta = new Vector2(Mathf.Max(40f, chip.sizeDelta.x - valueX - 16f), valueRect.sizeDelta.y);
    }

    /// <summary>A small "Game paused" chip (the paused pop-ups' one) with its top-left at (x, y).</summary>
    public static void PausedChip(Transform parent, TMP_FontAsset font, float x, float y)
    {
        BlueprintBox chip = Box(parent, "Paused", UIColors.PaperHi, UIColors.Ink, 2f, RadiusSmall, x, y, 224f, 48f);
        chip.SetShadow(BlueprintUI.InkShadow, new Vector2(3f, 4f));
        CasinoIcon icon = CasinoIcon.Create(chip.transform, CasinoIconKind.Pause, 24f, UIColors.Ink, 12f, 24f);
        TextMeshProUGUI text = Shrink(Text(chip.transform, "Text", "", 22f, UIColors.Ink, font, 34f, 0f, 170f, 48f), 15f);
        L10nText.Attach(text, Table, "casino.ui.paused");   // "Game paused"
        // Icon and words centred together, whatever the words are in the current language
        StaffButtonFit fit = chip.gameObject.AddComponent<StaffButtonFit>();
        fit.label = text;
        fit.icon = icon.rectTransform;
        fit.iconSize = 24f;
        fit.fontSize = 22f;
        fit.margin = 12f;
        fit.Layout();
    }

    // ------------------------------------------------------------------
    // Buttons
    // ------------------------------------------------------------------

    /// <summary>
    /// A casino button (CasinoButton looks) at (x, y), <paramref name="width"/> x <paramref name="height"/>: a face with an
    /// optional icon and a label over a hard shadow. Width 0 = fit the label (between <paramref name="minWidth"/> and
    /// <paramref name="maxWidth"/> when the label changes later).
    /// </summary>
    public static CasinoButton Button(Transform parent, string name, string label, CasinoIconKind icon, CasinoButton.Look look,
                                      TMP_FontAsset font, float fontSize, bool bold, float x, float y, float width, float height,
                                      Vector2 shadowOffset, Color shadowColor, float padding = 36f, float iconSize = 28f,
                                      float minWidth = 0f, float maxWidth = 0f)
    {
        float iconSpace = icon != CasinoIconKind.None ? iconSize + 12f : 0f;
        bool fit = width <= 0f;
        if (fit)
        {
            width = Mathf.Ceil(TextWidth(font, label, fontSize, bold)) + 4f + iconSpace + padding * 2f;
            if (maxWidth > 0f) width = Mathf.Clamp(width, Mathf.Min(minWidth, maxWidth), maxWidth);
        }

        RectTransform root = Rect(parent, name, x, y, width, height);
        Image hit = root.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        hit.raycastTarget = true;
        Button button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        BlueprintBox shadow = BlueprintUI.Box(root, "Shadow", shadowColor, Color.clear, 0f, RadiusLarge);
        BlueprintUI.Stretch(shadow.rectTransform);
        BlueprintBox face = BlueprintUI.Box(root, "Face", UIColors.Gold, UIColors.Ink, 3f, RadiusLarge);
        BlueprintUI.Stretch(face.rectTransform);
        BlueprintUI.HRow(face, 12f, new RectOffset(0, 0, 0, 0), TextAnchor.MiddleCenter);

        var content = new List<Graphic>(2);
        if (icon != CasinoIconKind.None)
        {
            CasinoIcon glyph = RowIcon(face.transform, icon, iconSize, UIColors.Ink);
            content.Add(glyph);
        }
        TextMeshProUGUI text = BlueprintUI.Text(face.transform, "Label", label, fontSize, UIColors.Ink, font, TextAlignmentOptions.Center, bold);
        text.enableAutoSizing = true;
        text.fontSizeMax = fontSize;
        text.fontSizeMin = Mathf.Round(fontSize * 0.6f);
        text.overflowMode = TextOverflowModes.Overflow;
        LayoutElement textLayout = BlueprintUI.Layout(text, -1f, height, 0f, -1f, -1f, -1f);
        content.Add(text);

        MarkerButton marker = root.gameObject.AddComponent<MarkerButton>();
        marker.face = face.rectTransform;
        marker.pressOffset = new Vector2(shadowOffset.x, -shadowOffset.y);

        CasinoButton casino = root.gameObject.AddComponent<CasinoButton>();
        casino.button = button;
        casino.face = face;
        casino.shadow = shadow;
        casino.content = content.ToArray();
        casino.look = look;
        casino.shadowColor = shadowColor;
        casino.shadowOffset = shadowOffset;
        casino.label = text;
        casino.labelLayout = textLayout;
        casino.padding = padding;
        casino.iconSpace = iconSpace;
        casino.minWidth = fit ? minWidth : 0f;
        casino.maxWidth = fit ? maxWidth : 0f;
        casino.FitLabel();
        casino.SetLook(look, false);
        return casino;
    }

    /// <summary>A square casino button with only an icon (the boards' - and + next to a bet).</summary>
    public static CasinoButton IconButton(Transform parent, string name, CasinoIconKind icon, float x, float y, float size,
                                          Vector2 shadowOffset, Color shadowColor, float iconSize = 28f)
    {
        RectTransform root = Rect(parent, name, x, y, size, size);
        Image hit = root.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        hit.raycastTarget = true;
        Button button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        BlueprintBox shadow = BlueprintUI.Box(root, "Shadow", shadowColor, Color.clear, 0f, RadiusSmall);
        BlueprintUI.Stretch(shadow.rectTransform);
        BlueprintBox face = BlueprintUI.Box(root, "Face", UIColors.PaperHi, UIColors.Ink, 3f, RadiusSmall);
        BlueprintUI.Stretch(face.rectTransform);
        CasinoIcon glyph = CasinoIcon.Create(face.transform, icon, iconSize, UIColors.Ink, 0f, 0f);
        glyph.rectTransform.anchorMin = glyph.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        glyph.rectTransform.anchoredPosition = Vector2.zero;

        MarkerButton marker = root.gameObject.AddComponent<MarkerButton>();
        marker.face = face.rectTransform;
        marker.pressOffset = new Vector2(shadowOffset.x, -shadowOffset.y);

        CasinoButton casino = root.gameObject.AddComponent<CasinoButton>();
        casino.button = button;
        casino.face = face;
        casino.shadow = shadow;
        casino.content = new Graphic[] { glyph };
        casino.look = CasinoButton.Look.Paper;
        casino.shadowColor = shadowColor;
        casino.shadowOffset = shadowOffset;
        casino.SetLook(CasinoButton.Look.Paper, false);
        return casino;
    }

    /// <summary>The boards' round close button (60 px, paper, ink shadow) with its top-left at (x, y).</summary>
    public static Button CloseButton(Transform parent, string name, float x, float y, float size = 60f)
    {
        RectTransform root = Rect(parent, name, x, y, size, size);
        Image hit = root.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        hit.raycastTarget = true;
        Button button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        button.navigation = new Navigation { mode = Navigation.Mode.None };

        Vector4 radii = RadiusRound * (size / 60f);
        BlueprintBox shadow = BlueprintUI.Box(root, "Shadow", UIColors.Ink, Color.clear, 0f, radii);
        BlueprintUI.Stretch(shadow.rectTransform, 3f, 4f, -3f, -4f);
        BlueprintBox face = BlueprintUI.Box(root, "Face", UIColors.PaperHi, UIColors.Ink, 3f, radii);
        BlueprintUI.Stretch(face.rectTransform);
        CasinoIcon glyph = CasinoIcon.Create(face.transform, CasinoIconKind.Close, 28f, UIColors.Ink, 0f, 0f);
        glyph.rectTransform.anchorMin = glyph.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        glyph.rectTransform.anchoredPosition = Vector2.zero;

        MarkerButton marker = root.gameObject.AddComponent<MarkerButton>();
        marker.face = face.rectTransform;
        marker.shadow = shadow.gameObject;
        marker.pressOffset = new Vector2(3f, -4f);
        root.gameObject.AddComponent<CanvasGroup>();
        return button;
    }

    /// <summary>
    /// The boards' "Back to casino" link: back arrow and a label with a wavy underline (2 px, 7 px under the text), no box.
    /// Top-left at (x, y).
    /// </summary>
    public static Button LinkButton(Transform parent, string name, string label, TMP_FontAsset font, float x, float y, float height,
                                    Color color, out TextMeshProUGUI text)
    {
        float textWidth = Mathf.Ceil(TextWidth(font, label, 26f)) + 2f;
        RectTransform root = Rect(parent, name, x, y, 40f + textWidth, height);
        Image hit = root.gameObject.AddComponent<Image>();
        hit.color = Color.clear;
        hit.raycastTarget = true;
        Button button = root.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = hit;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        root.gameObject.AddComponent<CanvasGroup>();

        CasinoIcon.Create(root, CasinoIconKind.Back, 28f, color, 14f, height * 0.5f);
        text = Text(root, "Label", label, 26f, color, font, 40f, 0f, textWidth, height);
        WavyLine(root, color, 40f, height * 0.5f + 15f, textWidth, 2f);
        return button;   // no face to move: the link only dims while disabled (SetEnabled)
    }

    /// <summary>
    /// Lay a link (LinkButton) out again for its label as it reads now, in its current font (after a language change):
    /// its width, the label's and the wavy underline's. Anything else is left alone.
    /// </summary>
    public static void FitLink(Selectable link)
    {
        RectTransform root = link != null ? link.transform as RectTransform : null;
        Transform labelTransform = root != null ? root.Find("Label") : null;
        Transform waveTransform = root != null ? root.Find("Wave") : null;
        TMP_Text label = labelTransform != null ? labelTransform.GetComponent<TMP_Text>() : null;
        UIPolyline wave = waveTransform != null ? waveTransform.GetComponent<UIPolyline>() : null;
        if (label == null || wave == null) return;
        float textWidth = Mathf.Ceil(TextWidth(label.font, label.text, 26f)) + 2f;
        root.sizeDelta = new Vector2(40f + textWidth, root.sizeDelta.y);
        label.rectTransform.sizeDelta = new Vector2(textWidth, label.rectTransform.sizeDelta.y);
        RectTransform waveRect = wave.rectTransform;
        waveRect.sizeDelta = new Vector2(textWidth, waveRect.sizeDelta.y);
        wave.ClearPaths();
        wave.AddPath(WavePoints(textWidth));
    }

    /// <summary>A wavy line (CSS text-decoration: underline wavy) <paramref name="width"/> long, centred on y.</summary>
    public static UIPolyline WavyLine(Transform parent, Color color, float x, float y, float width, float thickness)
    {
        RectTransform rect = Rect(parent, "Wave", x, y - 4f, width, 8f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(x, -y);
        UIPolyline line = rect.gameObject.AddComponent<UIPolyline>();
        line.color = color;
        line.width = thickness;
        line.capSteps = 6;
        line.AddPath(WavePoints(width));
        return line;
    }

    static Vector2[] WavePoints(float width)
    {
        var points = new List<Vector2>();
        const float wave = 10f, amplitude = 2f;
        for (float px = 0f; px <= width + 0.01f; px += 1.5f)
            points.Add(new Vector2(px, Mathf.Sin(px / wave * Mathf.PI * 2f) * amplitude));
        return points.ToArray();
    }

    /// <summary>An icon inside a horizontal layout row.</summary>
    public static CasinoIcon RowIcon(Transform parent, CasinoIconKind kind, float size, Color color, float stroke = -1f)
    {
        RectTransform rect = BlueprintUI.NewRect(kind.ToString(), parent);
        rect.sizeDelta = new Vector2(size, size);
        CasinoIcon icon = rect.gameObject.AddComponent<CasinoIcon>();
        icon.Kind = kind;
        icon.StrokeWidth = stroke;
        icon.color = color;
        icon.raycastTarget = false;
        BlueprintUI.Layout(icon, size, size, 0f, 0f, size, size);
        return icon;
    }

    /// <summary>Enable or disable a link or round button (it dims while disabled); casino buttons show their Off look instead.</summary>
    public static void SetEnabled(Selectable control, bool on)
    {
        BlueprintUI.SetInteractable(control, on);
    }

    // ------------------------------------------------------------------
    // Poker chips, coins, cards
    // ------------------------------------------------------------------

    /// <summary>A poker chip <paramref name="size"/> wide centred on (x, y), with an optional value label.</summary>
    public static CasinoChip Chip(Transform parent, string name, float size, Color chip, Color spots, bool inner, float x, float y,
                                  string label, Color labelColor, TMP_FontAsset font, out TextMeshProUGUI text)
    {
        RectTransform rect = BlueprintUI.NewRect(name, parent);
        RunEndUI.PlaceCenter(rect, x, y);
        rect.sizeDelta = new Vector2(size, size);
        CasinoChip graphic = rect.gameObject.AddComponent<CasinoChip>();
        graphic.SetStyle(chip, spots, size >= 50f ? 3f : 2f, inner);
        graphic.raycastTarget = false;
        text = null;
        if (label != null)
        {
            text = BlueprintUI.Text(rect, "Label", label, 17f, labelColor, font, TextAlignmentOptions.Center, true);
            BlueprintUI.Stretch(text.rectTransform);
            text.enableAutoSizing = true;
            text.fontSizeMax = 17f;
            text.fontSizeMin = 10f;
            text.rectTransform.sizeDelta = new Vector2(-size * 0.42f, 0f);   // inside the inner disc
        }
        return graphic;
    }

    /// <summary>A chip's value label: "$100", "$1K", "$2.5K", "$25K", "$1.2M", "$3B".</summary>
    public static string ChipLabel(double value)
    {
        double abs = System.Math.Abs(System.Math.Round(value));
        if (abs >= 1e12) return "$" + (abs / 1e12).ToString("0.#", Inv) + "T";
        if (abs >= 1e9) return "$" + (abs / 1e9).ToString("0.#", Inv) + "B";
        if (abs >= 1e6) return "$" + (abs / 1e6).ToString("0.#", Inv) + "M";
        if (abs >= 1e3) return "$" + (abs / 1e3).ToString(abs >= 1e5 ? "0" : "0.#", Inv) + "K";
        return "$" + abs.ToString("0", Inv);
    }

    /// <summary>The chip colours for a bet: red below $500, blue below $1,000, black below $5,000, gold from there.</summary>
    public static void ChipColors(double value, out Color chip, out Color spots, out Color label)
    {
        if (value >= 5000) { chip = ChipGold; spots = UIColors.PaperHi; label = UIColors.Ink; }
        else if (value >= 1000) { chip = ChipBlack; spots = UIColors.PaperHi; label = UIColors.PaperHi; }
        else if (value >= 500) { chip = ChipBlue; spots = UIColors.PaperHi; label = UIColors.PaperHi; }
        else { chip = ChipRed; spots = UIColors.PaperHi; label = UIColors.PaperHi; }
    }

    /// <summary>A gold coin of the boards (see Coin): its parts, so a flip can squash it and swap the letter.</summary>
    public struct CoinParts
    {
        public RectTransform root;
        public RectTransform body;
        public BlueprintBox shadow;
        public TextMeshProUGUI letter;
        public TextMeshProUGUI letterLight;
        public TextMeshProUGUI letterDark;
    }

    /// <summary>
    /// The boards' gold coin, <paramref name="size"/> wide, centred on (x, y): a face with a light-to-gold shine, a 4 px ink
    /// border, a solid inner ring (and a dashed one), a big letter with a light and a dark copy (the CSS text-shadows), over
    /// a gold-dark edge and an ink edge below it (thickness) and, with <paramref name="softShadow"/>, a soft shadow on the
    /// table. The body (face and edges) is what a flip squashes.
    /// </summary>
    public static CoinParts Coin(Transform parent, string name, float size, TMP_FontAsset font, float x, float y, string face,
                                 float goldEdge, float inkEdge, float ringInset, float ringWidth, float dashedInset, bool softShadow,
                                 float lightOffset, float darkOffset)
    {
        var parts = new CoinParts();
        RectTransform root = BlueprintUI.NewRect(name, parent);
        RunEndUI.PlaceCenter(root, x, y);
        root.sizeDelta = new Vector2(size, size);
        parts.root = root;

        Vector4 round = BlueprintUI.Round(size * 0.5f);
        if (softShadow)
        {
            BlueprintBox soft = BlueprintUI.Box(root, "Shadow", Color.clear, Color.clear, 0f, round);
            BlueprintUI.Stretch(soft.rectTransform);
            soft.SetShadow(new Color(0f, 0f, 0f, 0.4f), new Vector2(0f, 36f * size / 320f), 48f * size / 320f);
            parts.shadow = soft;
        }

        RectTransform body = BlueprintUI.Stretch(BlueprintUI.NewRect("Body", root));
        parts.body = body;
        BlueprintBox ink = BlueprintUI.Box(body, "InkEdge", UIColors.Ink, Color.clear, 0f, round);
        BlueprintUI.Stretch(ink.rectTransform, 0f, inkEdge, 0f, -inkEdge);
        BlueprintBox edge = BlueprintUI.Box(body, "GoldEdge", UIColors.GoldDark, Color.clear, 0f, round);
        BlueprintUI.Stretch(edge.rectTransform, 0f, goldEdge, 0f, -goldEdge);

        BlueprintBox faceBox = BlueprintUI.Box(body, "Face", UIColors.Gold, UIColors.Ink, 4f, round);
        faceBox.SetGradient(GoldLight, new Vector2(0.38f, 0.32f));
        BlueprintUI.Stretch(faceBox.rectTransform);

        BlueprintBox ring = BlueprintUI.Box(faceBox.transform, "Ring", Color.clear, UIColors.GoldDark, ringWidth,
                                            BlueprintUI.Round(size * 0.5f - ringInset));
        BlueprintUI.Stretch(ring.rectTransform, ringInset, ringInset, ringInset, ringInset);
        if (dashedInset > 0f)
        {
            BlueprintBox dashed = BlueprintUI.Box(faceBox.transform, "Dashes", Color.clear, UIColors.WithAlpha(UIColors.GoldDark, 0.7f), 2f,
                                                  BlueprintUI.Round(size * 0.5f - dashedInset));
            dashed.SetDash(true, 7f, 5f);
            BlueprintUI.Stretch(dashed.rectTransform, dashedInset, dashedInset, dashedInset, dashedInset);
        }

        float letterSize = size * 0.5625f;
        parts.letterDark = BlueprintUI.Text(faceBox.transform, "LetterDark", face, letterSize, UIColors.ClipboardDark, font, TextAlignmentOptions.Center, true);
        BlueprintUI.Stretch(parts.letterDark.rectTransform, 0f, -darkOffset, 0f, darkOffset);
        parts.letterLight = BlueprintUI.Text(faceBox.transform, "LetterLight", face, letterSize, GoldLight, font, TextAlignmentOptions.Center, true);
        BlueprintUI.Stretch(parts.letterLight.rectTransform, 0f, lightOffset, 0f, -lightOffset);
        parts.letter = BlueprintUI.Text(faceBox.transform, "Letter", face, letterSize, UIColors.GoldDark, font, TextAlignmentOptions.Center, true);
        BlueprintUI.Stretch(parts.letter.rectTransform);
        return parts;
    }

    /// <summary>Set a coin's letter (and its two shadow copies).</summary>
    public static void SetCoinLetter(CoinParts coin, string value)
    {
        if (coin.letter != null && coin.letter.text != value) coin.letter.text = value;
        if (coin.letterLight != null && coin.letterLight.text != value) coin.letterLight.text = value;
        if (coin.letterDark != null && coin.letterDark.text != value) coin.letterDark.text = value;
    }

    /// <summary>
    /// A playing card of the blackjack board (150 x 210) with its top-left at (x, y): the face (paper, 2 px ink border,
    /// corners, big suit) and the back (red, pale stripes), over a hard shadow.
    /// </summary>
    public static CasinoCard Card(Transform parent, string name, TMP_FontAsset font, float x, float y)
    {
        RectTransform root = Rect(parent, name, x, y, 150f, 210f);
        CasinoCard card = root.gameObject.AddComponent<CasinoCard>();

        BlueprintBox face = BlueprintUI.Box(root, "Face", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.Round(12f));
        face.SetShadow(CardShadow, new Vector2(3f, 4f));
        BlueprintUI.Stretch(face.rectTransform);
        card.face = face.gameObject;
        card.cornerTop = Corner(face.transform, "CornerTop", font, out TextMeshProUGUI rankTop, out CasinoIcon suitTop);
        card.cornerBottom = Corner(face.transform, "CornerBottom", font, out TextMeshProUGUI rankBottom, out CasinoIcon suitBottom);
        card.cornerBottom.localEulerAngles = new Vector3(0f, 0f, 180f);
        card.rankTop = rankTop;
        card.rankBottom = rankBottom;
        card.suitTop = suitTop;
        card.suitBottom = suitBottom;
        card.suitCenter = CasinoIcon.Create(face.transform, CasinoIconKind.Spade, 72f, UIColors.Ink, 75f, 105f);

        BlueprintBox back = BlueprintUI.Box(root, "Back", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.Round(12f));
        back.SetShadow(CardShadow, new Vector2(3f, 4f));
        BlueprintUI.Stretch(back.rectTransform);
        CardBack(back.transform, 10f, 6f);
        card.back = back.gameObject;
        back.gameObject.SetActive(false);
        return card;
    }

    /// <summary>The red striped back inside a card, <paramref name="inset"/> px from its edge.</summary>
    public static void CardBack(Transform card, float inset, float radius)
    {
        BlueprintBox red = BlueprintUI.Box(card, "Pattern", UIColors.Loss, UIColors.Ink, 1f, BlueprintUI.Round(radius));
        BlueprintUI.Stretch(red.rectTransform, inset, inset, inset, inset);
        RectTransform hatchRect = BlueprintUI.Stretch(BlueprintUI.NewRect("Stripes", red.transform), 1f, 1f, 1f, 1f);
        CasinoIcon hatch = hatchRect.gameObject.AddComponent<CasinoIcon>();
        hatch.Kind = CasinoIconKind.Hatch;
        hatch.Amount = 14f;
        hatch.StrokeWidth = 6f;
        hatch.color = UIColors.WithAlpha(UIColors.PaperHi, 0.18f);
        hatch.raycastTarget = false;
    }

    static RectTransform Corner(Transform face, string name, TMP_FontAsset font, out TextMeshProUGUI rank, out CasinoIcon suit)
    {
        RectTransform corner = BlueprintUI.NewRect(name, face);
        corner.pivot = new Vector2(0.5f, 0.5f);
        corner.sizeDelta = new Vector2(24f, 56f);
        rank = BlueprintUI.Text(corner, "Rank", "A", 30f, UIColors.Ink, font, TextAlignmentOptions.Center, true);
        RectTransform rankRect = rank.rectTransform;
        rankRect.anchorMin = new Vector2(0f, 1f);
        rankRect.anchorMax = new Vector2(1f, 1f);
        rankRect.pivot = new Vector2(0.5f, 1f);
        rankRect.sizeDelta = new Vector2(12f, 32f);
        rankRect.anchoredPosition = new Vector2(0f, 3f);
        suit = CasinoIcon.Create(corner, CasinoIconKind.Spade, 24f, UIColors.Ink, 0f, 0f);
        RectTransform suitRect = suit.rectTransform;
        suitRect.anchorMin = suitRect.anchorMax = new Vector2(0.5f, 0f);
        suitRect.pivot = new Vector2(0.5f, 0f);
        suitRect.anchoredPosition = Vector2.zero;
        return corner;
    }

    // ------------------------------------------------------------------
    // Motion
    // ------------------------------------------------------------------

    /// <summary>1 - (1 - t)^3</summary>
    public static float EaseOut(float t)
    {
        t = Mathf.Clamp01(t);
        float u = 1f - t;
        return 1f - u * u * u;
    }

    /// <summary>Ease out with a small overshoot (CSS "back" easing).</summary>
    public static float EaseOutBack(float t, float overshoot = 1.4f)
    {
        t = Mathf.Clamp01(t);
        float u = t - 1f;
        return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
    }
}
