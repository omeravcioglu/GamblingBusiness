using UnityEngine;

/// <summary>
/// Colour tokens of the "Stationery vs. Felt" design (design canvas "IsGamblingBadForBusiness UI &amp; HUD").
/// Use these instead of hand-typed colours so every screen matches.
/// </summary>
public static class UIColors
{
    // Paper & ink
    public static readonly Color Paper = Hex(0xF7F2E6);
    public static readonly Color PaperHi = Hex(0xFFFDF6);
    public static readonly Color PaperShade = Hex(0xEAE2CF);
    public static readonly Color Ink = Hex(0x1D2433);
    public static readonly Color InkSoft = Hex(0x474F60);
    public static readonly Color Pencil = Hex(0x676B74);
    public static readonly Color Rule = Hex(0xC6D6E6);
    public static readonly Color Margin = Hex(0xE4A199);
    public static readonly Color LegalPad = Hex(0xFFF3A8);

    // Signals
    public static readonly Color Highlight = Hex(0xFFE14D);
    public static readonly Color Money = Hex(0x1E7B45);
    public static readonly Color MoneySoft = Hex(0xD5EDD9);
    public static readonly Color Loss = Hex(0xC0352B);
    public static readonly Color LossSoft = Hex(0xF6D8D3);
    public static readonly Color Warn = Hex(0xB85C0F);
    public static readonly Color Info = Hex(0x2B5BA8);

    // Sticky notes
    public static readonly Color StickyYellow = Hex(0xFFE680);
    public static readonly Color StickyBlue = Hex(0xBFE3F5);
    public static readonly Color StickyGreen = Hex(0xCDEBA6);
    public static readonly Color StickyPink = Hex(0xFFC7D1);

    // Ledger (company stats)
    public static readonly Color Ledger = Hex(0xEEF5E9);
    public static readonly Color LedgerBand = Hex(0xDCEBD3);
    public static readonly Color LedgerLine = Hex(0xA9C9A0);

    // Manila & wood
    public static readonly Color Manila = Hex(0xE7C98C);
    public static readonly Color ManilaEdge = Hex(0xC9A45F);
    public static readonly Color Clipboard = Hex(0x8B5E3C);
    public static readonly Color ClipboardDark = Hex(0x6E4829);

    // Felt & casino
    public static readonly Color Felt = Hex(0x1D5A3E);
    public static readonly Color FeltHi = Hex(0x2A7652);
    public static readonly Color FeltDark = Hex(0x123D29);
    public static readonly Color Gold = Hex(0xE2B13C);
    public static readonly Color GoldDark = Hex(0x9C7418);

    // Blueprint
    public static readonly Color Blueprint = Hex(0x1F4E8C);
    public static readonly Color BlueprintDark = Hex(0x163B6B);
    public static readonly Color BlueprintInk = Hex(0xEAF2FF);

    // Scene (dark overlays use SrgbAlpha so they darken like the design's CSS rgba values)
    public static readonly Color Scrim = new Color(16f / 255f, 13f / 255f, 8f / 255f, SrgbAlpha(0.66f));       // paused pop-ups
    public static readonly Color ScrimLight = new Color(16f / 255f, 13f / 255f, 8f / 255f, SrgbAlpha(0.45f));  // overlays that don't pause
    public static readonly Color Tape = new Color32(238, 226, 186, 217);     // masking tape, 85%

    /// <summary>
    /// The design's CSS blends translucent colours in sRGB; this project (HDRP, linear colour space) blends in linear
    /// space, where a dark overlay darkens much less. SrgbAlpha(a) is the alpha that darkens like CSS rgba(0,0,0,a).
    /// </summary>
    public static float SrgbAlpha(float cssAlpha)
    {
        return 1f - Mathf.Pow(1f - Mathf.Clamp01(cssAlpha), 2.2f);
    }

    /// <summary>
    /// Alpha that makes fg blended over bg in linear space look like CSS rgba(fg, cssAlpha) over bg (CSS blends in
    /// sRGB). Light colours over dark ones (the CRT's glows) need much less alpha than the CSS value.
    /// </summary>
    public static float BlendAlpha(float cssAlpha, Color fg, Color bg)
    {
        cssAlpha = Mathf.Clamp01(cssAlpha);
        float fs = Luma(fg), bs = Luma(bg);
        float fl = Mathf.GammaToLinearSpace(fs), bl = Mathf.GammaToLinearSpace(bs);
        if (Mathf.Abs(fl - bl) < 1e-4f) return cssAlpha;
        float want = Mathf.GammaToLinearSpace(bs + cssAlpha * (fs - bs));
        return Mathf.Clamp01((want - bl) / (fl - bl));
    }

    static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

    /// <summary>fg at the alpha that looks like CSS rgba(fg, cssAlpha) over bg.</summary>
    public static Color Over(Color fg, float cssAlpha, Color bg)
    {
        fg.a = BlendAlpha(cssAlpha, fg, bg);
        return fg;
    }

    /// <summary>Black at the alpha that looks like CSS rgba(0,0,0,cssAlpha).</summary>
    public static Color Dark(float cssAlpha)
    {
        return new Color(0f, 0f, 0f, SrgbAlpha(cssAlpha));
    }

    public static Color Hex(uint rgb)
    {
        return new Color32((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, 255);
    }

    public static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }
}
