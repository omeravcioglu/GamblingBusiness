using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Draws a company logo: a preset (CityLogo code: the shape in its colour, an ink outline, the icon and/or the
/// letters) or a player's drawing ("p|hash": a pixel sticker, fetched through CityLogoCache).
/// </summary>
public class CityLogoView : VisualElement
{
    CityLogo logo = new CityLogo();
    readonly Label letters;
    readonly VisualElement pixelFace;
    string pixelHash;       // showing a player drawing
    string pixelCity;
    Texture2D ownTexture;   // the editor's live preview
    CityLogoCache.State pixelState;
    bool listening;

    public CityLogoView(string code = null, float size = 64f, string cityId = null)
    {
        style.width = size;
        style.height = size;
        style.flexShrink = 0;
        pickingMode = PickingMode.Ignore;

        pixelFace = new VisualElement();
        pixelFace.pickingMode = PickingMode.Ignore;
        pixelFace.style.position = Position.Absolute;
        pixelFace.style.display = DisplayStyle.None;
        Add(pixelFace);

        letters = new Label();
        letters.style.position = Position.Absolute;
        letters.style.left = 0;
        letters.style.right = 0;
        letters.style.unityTextAlign = TextAnchor.MiddleCenter;
        letters.style.unityFontStyleAndWeight = FontStyle.Bold;
        letters.style.marginTop = 0;
        letters.style.marginBottom = 0;
        letters.style.paddingTop = 0;
        letters.style.paddingBottom = 0;
        letters.style.whiteSpace = WhiteSpace.NoWrap;
        letters.pickingMode = PickingMode.Ignore;
        Add(letters);

        generateVisualContent += Draw;
        RegisterCallback<GeometryChangedEvent>(_ => LayoutParts());
        RegisterCallback<AttachToPanelEvent>(_ => Listen(pixelHash != null && pixelState != CityLogoCache.State.Ready));
        RegisterCallback<DetachFromPanelEvent>(_ => Listen(false));
        Set(code, cityId);
    }

    /// <summary>Show a logo code: a preset, or "p|hash" for a drawing of the given city (default: the city being visited).</summary>
    public void Set(string code, string cityId = null)
    {
        if (CityPixelLogo.IsRef(code))
        {
            logo = null;
            ownTexture = null;
            pixelHash = CityPixelLogo.HashOfRef(code);
            pixelCity = cityId ?? CityOnline.Instance?.CityId;
            RefreshPixels();
            return;
        }
        Set(CityLogo.Decode(code));
    }

    public void Set(CityLogo value)
    {
        logo = value != null ? value.Clone() : new CityLogo();
        pixelHash = null;
        ownTexture = null;
        Listen(false);
        Refresh();
    }

    /// <summary>Show a drawing straight from pixels (the drawing editor's preview).</summary>
    public void SetPixels(Texture2D texture)
    {
        logo = null;
        pixelHash = null;
        Listen(false);
        ownTexture = texture;
        pixelState = CityLogoCache.State.Ready;
        pixelFace.style.backgroundImage = new StyleBackground(texture);
        Refresh();
    }

    void RefreshPixels()
    {
        var texture = CityLogoCache.Texture(pixelCity, pixelHash, out pixelState);
        pixelFace.style.backgroundImage = texture != null ? new StyleBackground(texture) : new StyleBackground(StyleKeyword.None);
        Listen(pixelState != CityLogoCache.State.Ready && panel != null);
        Refresh();
    }

    void Listen(bool on)
    {
        if (on == listening) return;
        listening = on;
        if (on) CityLogoCache.Changed += OnLogoChanged;
        else CityLogoCache.Changed -= OnLogoChanged;
    }

    void OnLogoChanged(string hash)
    {
        if (hash == pixelHash) RefreshPixels();
    }

    bool ShowsPixels => ownTexture != null || pixelHash != null;

    void Refresh()
    {
        bool pixels = ShowsPixels;
        bool ready = ownTexture != null || pixelState == CityLogoCache.State.Ready;
        pixelFace.style.display = pixels && ready ? DisplayStyle.Flex : DisplayStyle.None;
        letters.text = pixels ? "" : logo.Letters;
        if (!pixels) letters.style.color = CityUi.Hex(logo.Foreground);
        LayoutParts();
        MarkDirtyRepaint();
    }

    void LayoutParts()
    {
        float s = Mathf.Min(resolvedStyle.width, resolvedStyle.height);
        if (float.IsNaN(s) || s <= 0) return;

        // A drawing sits inside a thin paper border, like a sticker
        float inset = Mathf.Max(2f, s * 0.07f);
        pixelFace.style.left = inset;
        pixelFace.style.top = inset;
        pixelFace.style.width = s - inset * 2;
        pixelFace.style.height = s - inset * 2;

        bool hasLetters = !ShowsPixels && !string.IsNullOrEmpty(logo.Letters);
        letters.style.display = hasLetters ? DisplayStyle.Flex : DisplayStyle.None;
        if (!hasLetters) return;

        if (logo.Icon == "none")
        {
            letters.style.fontSize = s * (logo.Letters.Length >= 3 ? 0.30f : 0.40f);
            letters.style.top = 0;
            letters.style.bottom = 0;
        }
        else
        {
            letters.style.fontSize = s * 0.16f;
            letters.style.top = s * 0.62f;
            letters.style.bottom = s * 0.14f;
        }
    }

    void Draw(MeshGenerationContext ctx)
    {
        var r = contentRect;
        float s = Mathf.Min(r.width, r.height);
        if (s < 2f) return;
        var box = new Rect(r.x + (r.width - s) * 0.5f, r.y + (r.height - s) * 0.5f, s, s);
        var p = ctx.painter2D;

        if (ShowsPixels)
        {
            // Sticker: paper square with an ink border; the drawing's texture sits on top (a child element)
            float radius = s * 0.12f;
            p.BeginPath();
            CityUi.RoundedRect(p, box, radius);
            p.fillColor = pixelState == CityLogoCache.State.Hidden ? CityUi.Hex("E6E1D6") : CityUi.Card;
            p.Fill();
            p.strokeColor = CityUi.Ink;
            p.lineWidth = s < 30 ? 2.5f : 3.5f;
            p.Stroke();

            if (pixelState == CityLogoCache.State.Hidden)
            {
                // Taken down: a crossed-out sticker
                p.strokeColor = CityUi.Muted;
                p.lineWidth = Mathf.Max(2f, s * 0.05f);
                p.BeginPath();
                p.MoveTo(new Vector2(box.x + s * 0.28f, box.y + s * 0.28f));
                p.LineTo(new Vector2(box.xMax - s * 0.28f, box.yMax - s * 0.28f));
                p.MoveTo(new Vector2(box.xMax - s * 0.28f, box.y + s * 0.28f));
                p.LineTo(new Vector2(box.x + s * 0.28f, box.yMax - s * 0.28f));
                p.Stroke();
            }
            else if (ownTexture == null && pixelState != CityLogoCache.State.Ready)
            {
                // Still loading: three soft dots
                p.fillColor = CityUi.Alpha(CityUi.Muted, 0.6f);
                for (int i = -1; i <= 1; i++)
                {
                    p.BeginPath();
                    p.Arc(new Vector2(box.center.x + i * s * 0.18f, box.center.y), s * 0.055f, Angle.Degrees(0), Angle.Degrees(360));
                    p.Fill();
                }
            }
            return;
        }

        var shape = CityIcons.LogoShape(logo.Shape);
        var fg = CityUi.Hex(logo.Foreground);
        CitySvg.Fill(p, shape, box, CityUi.Hex(logo.Background), 100f);
        if (logo.Ring)
        {
            float inset = s * 0.11f;
            var inner = new Rect(box.x + inset, box.y + inset, box.width - inset * 2, box.height - inset * 2);
            CitySvg.Stroke(p, shape, inner, CityUi.Alpha(fg, 0.55f), 3.2f, 100f);
        }
        CitySvg.Stroke(p, shape, box, CityUi.Ink, s < 30 ? 7f : 5f, 100f);

        if (logo.Icon != "none")
        {
            bool withLetters = !string.IsNullOrEmpty(logo.Letters);
            float size = s * (withLetters ? 0.42f : 0.54f);
            float cy = box.y + s * (withLetters ? 0.40f : 0.5f);
            var iconBox = new Rect(box.center.x - size * 0.5f, cy - size * 0.5f, size, size);
            CityIcons.DrawLogoIcon(p, logo.Icon, iconBox, fg, s < 30 ? 3.2f : 2.6f);
        }
    }

    /// <summary>The logo's main colour (plots are tinted with it on the map).</summary>
    public static Color BrandColour(string code)
    {
        if (CityPixelLogo.IsRef(code))
        {
            var pixels = CityLogoCache.Pixels(CityPixelLogo.HashOfRef(code));
            if (pixels == null) return CityUi.Card;
            // The colour painted most often
            var counts = new int[CityPixelLogo.Palette.Length];
            foreach (byte b in pixels.Pixels) if (b != 0) counts[b]++;
            int best = 0;
            for (int i = 1; i < counts.Length; i++) if (counts[i] > counts[best]) best = i;
            return best == 0 ? CityUi.Card : CityUi.Hex(CityPixelLogo.Palette[best]);
        }
        return CityUi.Hex(CityLogo.Decode(code).Background);
    }
}
