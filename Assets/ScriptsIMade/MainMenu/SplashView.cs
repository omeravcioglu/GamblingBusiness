using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The start-up splash's picture, played by SplashScreen: the McHunter Studios logo assembles on the dark studio
/// backdrop (the trailer's opening, a little quicker), then the game logo slaps down piece by piece on the felt table,
/// under the lamp, with blurred cards and chips in the corners, a shake and a flash (the trailer's title shot).
/// Made in code from the Assets/UI/Splash sprites (Tools/Splash/make_splash_assets.py), placed by SplashLayout: every
/// logo piece is a crop of the user's original logo put back where it was, scaled evenly.
/// Pose(t) sets every piece for time t alone, so the editor preview (SplashPreview) can render any moment.
/// The canvas uses Expand: the 1920 x 1080 frame always fits, the backdrops reach the edges of any screen shape and the
/// props stay in its corners.
/// </summary>
public sealed class SplashView
{
    // ---- timeline, seconds from the first frame
    public const float StudioHit = 0.6f;               // the circle lands
    const float SheenStart = 1.95f, SheenEnd = 2.6f;   // light sweeps across "McHunter"
    const float StudioOutStart = 2.8f, StudioOutEnd = 3.3f;
    public const float GameStart = 3.35f;              // the felt cuts in under a flash with the first word
    const float Step = 0.11f;                          // the trailer's 16th note (0.15 s), a little quicker
    public const float LogoLanded = GameStart + 14f * Step + 0.4f;
    public const float End = GameStart + 2.75f;        // then SplashScreen fades to black and loads the menu

    const float StudioScale = 0.56f;   // the studio logo canvas (2000 x 706) on the frame, as in the trailer
    const float GameScale = 1f;        // the game logo canvas (1280 x 720)
    const float SheenAlpha = 0.55f;
    static readonly Color FlashColor = new Color32(0xFF, 0xF6, 0xD8, 0xFF);
    public static readonly Color HintColor = new Color32(0xF3, 0xE7, 0xC3, 0xFF);

    /// <summary>A sound at a moment of the timeline.</summary>
    public readonly struct Cue
    {
        public readonly float time;
        public readonly Sfx sound;
        public readonly float volume;

        public Cue(float time, Sfx sound, float volume = 1f)
        {
            this.time = time;
            this.sound = sound;
            this.volume = volume;
        }
    }

    public static readonly Cue[] Cues =
    {
        new Cue(StudioHit, Sfx.ChipBet),                  // the trailer's chip on the circle
        new Cue(SheenStart + 0.25f, Sfx.UiGlass, 0.5f),
        new Cue(GameStart, Sfx.CardPlace),                // IS
        new Cue(GameStart + Step, Sfx.UiScratch),         // GAMBLING
        new Cue(GameStart + 4f * Step, Sfx.CardPlace),    // BAD FOR
        new Cue(GameStart + 8f * Step, Sfx.UiScratch),    // BUSINESS
        new Cue(GameStart + 12f * Step, Sfx.CoinLand),    // ?
    };

    enum Kind { Word, Big, Deco }

    // when each game logo piece lands (steps after GameStart, plus seconds) and how: the trailer's LOGO_AT
    static readonly Dictionary<string, (float step, float plus, Kind kind)> Landing = new Dictionary<string, (float step, float plus, Kind kind)>
    {
        { "game_is", (0f, 0f, Kind.Word) }, { "game_gambling", (1f, 0f, Kind.Big) }, { "game_badfor", (4f, 0f, Kind.Word) },
        { "game_business", (8f, 0f, Kind.Big) }, { "game_q", (12f, 0f, Kind.Big) },
        { "game_burst_367_60", (2f, 0f, Kind.Deco) }, { "game_heart", (2f, 0.02f, Kind.Deco) },
        { "game_burst_325_87", (2f, 0.05f, Kind.Deco) }, { "game_club", (2f, 0.07f, Kind.Deco) },
        { "game_burst_848_54", (2f, 0.1f, Kind.Deco) },
        { "game_burst_243_230", (5f, 0f, Kind.Deco) }, { "game_burst_233_257", (5f, 0.04f, Kind.Deco) },
        { "game_burst_232_321", (5f, 0.08f, Kind.Deco) }, { "game_burst_397_403", (6f, 0f, Kind.Deco) },
        { "game_burst_411_422", (6f, 0.04f, Kind.Deco) },
        { "game_burst_690_402", (9f, 0f, Kind.Deco) }, { "game_burst_883_333", (9f, 0.05f, Kind.Deco) },
        { "game_burst_647_614", (10f, 0f, Kind.Deco) },
        { "game_burst_1080_299", (13f, 0f, Kind.Deco) }, { "game_burst_1092_344", (13f, 0.04f, Kind.Deco) },
        { "game_burst_1085_490", (13f, 0.08f, Kind.Deco) }, { "game_burst_1073_541", (13f, 0.1f, Kind.Deco) },
        { "game_diamond", (14f, 0f, Kind.Deco) }, { "game_spade", (14f, 0.06f, Kind.Deco) },
    };

    // the bursts right of the question mark pulse when it wobbles
    static readonly HashSet<string> Pulsing = new HashSet<string>
    {
        "game_burst_1080_299", "game_burst_1092_344", "game_burst_1085_490", "game_burst_1073_541"
    };

    static readonly (float time, float strength)[] Hits =
    {
        (GameStart, 1.4f), (GameStart + Step, 0.6f), (GameStart + 4f * Step, 0.6f), (GameStart + 8f * Step, 0.6f),
        (GameStart + 12f * Step, 0.8f)
    };

    static readonly float[] WobbleBeats = { GameStart + 16f * Step, GameStart + 20f * Step };

    sealed class Piece
    {
        public RectTransform rect;
        public Image image;
        public Vector2 home;
        public float landAt;
        public Kind kind;
        public bool pulses, isQuestion;

        public void Set(float alpha, float scale, float angle = 0f, Vector2 offset = default)
        {
            image.canvasRenderer.SetAlpha(alpha);
            rect.localScale = new Vector3(scale, scale, 1f);
            rect.localEulerAngles = new Vector3(0f, 0f, angle);
            rect.anchoredPosition = home + offset;
        }
    }

    public GameObject Root { get; private set; }
    public Canvas Canvas { get; private set; }
    public TMP_Text Hint { get; private set; }
    public CanvasGroup Fade { get; private set; }

    GameObject studio, felt;
    Image studioGlow, flash;
    RectTransform studioLogo, gameLogo, props, sheenBand, sheenWord;
    CanvasGroup studioLogoGroup;
    Image sheenImage;
    Piece blob, circle, dot, mchunter, studios, ring;
    SplashLayout.Part mchunterPart;
    Vector2 gameHome;
    readonly List<Piece> gamePieces = new List<Piece>();

    /// <summary>The splash under <paramref name="parent"/> (its own overlay canvas), or null when the sprites are missing.</summary>
    public static SplashView Build(Transform parent, IList<Sprite> sprites, TMP_FontAsset hintFont)
    {
        var map = new Dictionary<string, Sprite>();
        if (sprites != null)
            foreach (var s in sprites)
                if (s != null) map[s.name] = s;
        foreach (string needed in new[] { "studio_glow", "felt_bg", "studio_mchunter", "game_gambling" })
        {
            if (!map.ContainsKey(needed))
            {
                Debug.LogWarning("[Splash] Sprite " + needed + " is missing: run Tools > Game > Splash > Install New Splash.");
                return null;
            }
        }
        var view = new SplashView();
        view.Make(parent, map, hintFont);
        view.Pose(0f);
        return view;
    }

    void Make(Transform parent, Dictionary<string, Sprite> map, TMP_FontAsset hintFont)
    {
        Root = new GameObject("SplashCanvas", typeof(RectTransform));
        Root.transform.SetParent(parent, false);
        Canvas = Root.AddComponent<Canvas>();
        Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        Canvas.sortingOrder = 100;
        var scaler = Root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        var root = (RectTransform)Root.transform;

        // ---- studio: a plain backdrop to the screen's edges, the glow on the frame, the logo
        studio = Stretch(root, "Studio").gameObject;
        Picture(studio.transform, "Backdrop", null, SplashLayout.StudioEdge, stretch: true);
        studioGlow = Picture(studio.transform, "Glow", map["studio_glow"], Color.white, size: new Vector2(1920f, 1080f));
        float lw = SplashLayout.StudioWidth * StudioScale, lh = SplashLayout.StudioHeight * StudioScale;
        studioLogo = Rect(studio.transform, "Logo", new Vector2(lw, lh));
        studioLogo.anchoredPosition = new Vector2(0f, 10f);
        studioLogoGroup = studioLogo.gameObject.AddComponent<CanvasGroup>();
        foreach (var p in SplashLayout.Studio)
        {
            if (!map.TryGetValue(p.sprite, out var sprite)) continue;
            if (p.sprite == "studio_sheen") continue;
            var piece = MakePiece(studioLogo, sprite, p, StudioScale);
            switch (p.sprite)
            {
                case "studio_blob": blob = piece; break;
                case "studio_circle": circle = piece; break;
                case "studio_dot": dot = piece; break;
                case "studio_studios": studios = piece; break;
                case "studio_mchunter":
                    mchunter = piece;
                    mchunterPart = p;
                    piece.image.type = Image.Type.Filled;
                    piece.image.fillMethod = Image.FillMethod.Horizontal;
                    piece.image.fillOrigin = (int)Image.OriginHorizontal.Left;
                    break;
            }
        }
        if (map.TryGetValue("studio_ring", out var ringSprite) && circle != null)
        {
            var c = circle.home; // the circle's middle on the logo
            var rt = Rect(studioLogo, "Ring", new Vector2(256f, 256f));
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = ringSprite;
            img.raycastTarget = false;
            ring = new Piece { rect = rt, image = img, home = c };
        }
        if (map.TryGetValue("studio_sheen", out var sheenSprite) && mchunter != null)
        {
            // a soft-edged band (RectMask2D) slides over the word's white shape, which stays put
            sheenBand = Rect(studioLogo, "SheenBand", new Vector2(0.24f * lw, lh + 20f));
            sheenBand.anchorMin = sheenBand.anchorMax = sheenBand.pivot = new Vector2(0f, 1f);
            var mask = sheenBand.gameObject.AddComponent<RectMask2D>();
            mask.softness = new Vector2Int(Mathf.RoundToInt(0.1f * lw), 0);
            sheenWord = Rect(sheenBand, "Word", new Vector2(mchunterPart.w * StudioScale, mchunterPart.h * StudioScale));
            sheenWord.anchorMin = sheenWord.anchorMax = sheenWord.pivot = new Vector2(0f, 1f);
            sheenImage = sheenWord.gameObject.AddComponent<Image>();
            sheenImage.sprite = sheenSprite;
            sheenImage.raycastTarget = false;
            sheenImage.color = new Color(1f, 1f, 1f, SheenAlpha);
        }

        // ---- felt: the table to the screen's edges (its texture covers 5:4 to 32:9), the props in the corners, the logo
        felt = Stretch(root, "Felt").gameObject;
        Picture(felt.transform, "Backdrop", null, SplashLayout.FeltEdge, stretch: true);
        Picture(felt.transform, "Table", map["felt_bg"], Color.white, size: new Vector2(SplashLayout.FeltWidth, SplashLayout.FeltHeight));
        props = Stretch(felt.transform, "Props");
        foreach (var p in SplashLayout.Props)
        {
            if (!map.TryGetValue(p.sprite, out var sprite)) continue;
            var corner = new Vector2(p.right ? 1f : 0f, p.bottom ? 0f : 1f);
            var img = Picture(props, p.sprite, sprite, Color.white, size: new Vector2(p.w, p.h));
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = rt.pivot = corner;
            rt.anchoredPosition = new Vector2(p.right ? p.x + p.w - 1920f : p.x, p.bottom ? 1080f - (p.y + p.h) : -p.y);
        }
        gameLogo = Rect(felt.transform, "Logo", new Vector2(SplashLayout.GameWidth * GameScale, SplashLayout.GameHeight * GameScale));
        gameHome = new Vector2((SplashLayout.GameWidth / 2f - SplashLayout.GameMidX) * GameScale,
            (SplashLayout.GameMidY - SplashLayout.GameHeight / 2f) * GameScale);
        foreach (var p in SplashLayout.Game)
        {
            if (!map.TryGetValue(p.sprite, out var sprite)) continue;
            var piece = MakePiece(gameLogo, sprite, p, GameScale);
            (float step, float plus, Kind kind) land = Landing.TryGetValue(p.sprite, out var l) ? l : (13f, 0f, Kind.Deco);
            piece.landAt = GameStart + land.step * Step + land.plus;
            piece.kind = land.kind;
            piece.pulses = Pulsing.Contains(p.sprite);
            piece.isQuestion = p.sprite == "game_q";
            gamePieces.Add(piece);
        }

        flash = Picture(root, "Flash", null, FlashColor, stretch: true);

        // ---- the skip hint (SplashScreen binds its text and shows it) and the fade to black
        var hintRect = Rect(root, "SkipHint", new Vector2(1000f, 44f));
        hintRect.anchorMin = hintRect.anchorMax = hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.anchoredPosition = new Vector2(0f, 44f);
        var hint = hintRect.gameObject.AddComponent<TextMeshProUGUI>();
        if (hintFont != null) hint.font = hintFont;
        hint.text = "Press Space to skip";
        hint.fontSize = 26f;
        hint.characterSpacing = 2f;
        hint.alignment = TextAlignmentOptions.Center;
        hint.textWrappingMode = TextWrappingModes.NoWrap;
        hint.color = HintColor;
        hint.alpha = 0f;
        hint.raycastTarget = false;
        Hint = hint;

        var fade = Picture(root, "Fade", null, Color.black, stretch: true);
        Fade = fade.gameObject.AddComponent<CanvasGroup>();
        Fade.alpha = 0f;
        Fade.blocksRaycasts = false;
        Fade.interactable = false;
    }

    // ------------------------------------------------------------------ pose

    /// <summary>Every piece as it is at <paramref name="t"/> seconds.</summary>
    public void Pose(float t)
    {
        PoseStudio(t);
        PoseGame(t);
    }

    void PoseStudio(float t)
    {
        bool on = t < GameStart;
        if (studio.activeSelf != on) studio.SetActive(on);
        if (!on) return;

        const float hit = StudioHit;
        float outK = Seg(t, StudioOutStart, StudioOutEnd, InQ);
        studioGlow.canvasRenderer.SetAlpha((Seg(t, 0.1f, hit, InQ) * 0.6f + Seg(t, hit, hit + 0.4f) * 0.4f) *
            (1f - Seg(t, StudioOutStart - 0.05f, StudioOutEnd)));
        float s = L(0.985f, 1.03f, Seg(t, hit, StudioOutEnd)) + outK * 0.04f;
        studioLogo.localScale = new Vector3(s, s, 1f);
        studioLogoGroup.alpha = 1f - outK;

        circle?.Set(t >= hit ? 1f : 0f, Kf(t, hit, 0f, OutQ, hit + 0.17f, 1.14f, IoQ, hit + 0.36f, 1f));
        // CSS turns clockwise for positive angles, Unity counter-clockwise
        blob?.Set(Seg(t, hit + 0.05f, hit + 0.16f), Kf(t, hit + 0.05f, 0.45f, OutBack, hit + 0.5f, 1f),
            -Kf(t, hit + 0.05f, -50f, OutBack, hit + 0.55f, 0f));
        dot?.Set(Seg(t, hit + 0.22f, hit + 0.3f), Kf(t, hit + 0.22f, 0f, OutBack, hit + 0.5f, 1f), 0f,
            new Vector2(Kf(t, hit + 0.22f, 140f, OutBack, hit + 0.58f, 0f), 0f));
        if (mchunter != null)
        {
            // the trailer wiped the whole 2000 px canvas from the left: the word starts showing when the wipe reaches it
            float w = Seg(t, hit + 0.42f, hit + 0.95f, OutC);
            mchunter.image.fillAmount = Mathf.Clamp01((w * SplashLayout.StudioWidth - mchunterPart.x) / mchunterPart.w);
            mchunter.Set(t > hit + 0.42f ? 1f : 0f, 1f, 0f, new Vector2(0f, -L(12f, 0f, w)));
        }
        studios?.Set(Seg(t, hit + 0.85f, hit + 1.3f), L(1.12f, 1f, Seg(t, hit + 0.85f, hit + 1.45f, OutC)));
        ring?.Set((1f - Seg(t, hit, hit + 0.7f)) * (t >= hit ? 0.8f : 0f), L(0.4f, 3.2f, Seg(t, hit, hit + 0.7f, OutC)));

        if (sheenBand != null)
        {
            bool sheen = t > SheenStart && t < SheenEnd;
            if (sheenBand.gameObject.activeSelf != sheen) sheenBand.gameObject.SetActive(sheen);
            if (sheen)
            {
                float lw = studioLogo.sizeDelta.x;
                float left = (Seg(t, SheenStart, SheenEnd, IoQ) * 1.4f - 0.3f) * lw;
                sheenBand.anchoredPosition = new Vector2(left, 10f);
                sheenWord.anchoredPosition = new Vector2(mchunterPart.x * StudioScale - left, -mchunterPart.y * StudioScale - 10f);
            }
        }
    }

    void PoseGame(float t)
    {
        bool on = t >= GameStart;
        if (felt.activeSelf != on) felt.SetActive(on);
        // the trailer's 0.75 flash: blending in linear colour makes a light veil much stronger, ^1.6 brings it back
        flash.canvasRenderer.SetAlpha(on ? Mathf.Pow(0.75f * Mathf.Exp(-(t - GameStart) * 9f), 1.6f) : 0f);
        if (!on) return;

        Vector3 k = Shake(t);
        float s = 1f + 0.04f * Seg(t, GameStart, End, IoS);
        gameLogo.anchoredPosition = gameHome + new Vector2(k.x, -k.y);
        gameLogo.localEulerAngles = new Vector3(0f, 0f, -k.z);
        gameLogo.localScale = new Vector3(s, s, 1f);
        // each prop grows from its corner (scaled round the screen's middle, they'd drift off a 32:9 screen)
        float ps = 1.02f + 0.03f * Seg(t, GameStart, End);
        props.anchoredPosition = new Vector2(-k.x * 0.3f, k.y * 0.3f);
        foreach (Transform prop in props)
            prop.localScale = new Vector3(ps, ps, 1f);

        // the question mark wobbles on the beats after the logo is in; the bursts beside it pulse
        float wob = 0f, pulse = 1f;
        for (int b = 0; b < WobbleBeats.Length; b++)
        {
            float tt = WobbleBeats[b];
            if (t < tt) continue;
            wob = 9f * Mathf.Exp(-(t - tt) * 5f) * Mathf.Sin((t - tt) * 18f) * (b % 2 == 1 ? -1f : 1f);
            pulse = 1f + 0.14f * Mathf.Exp(-(t - tt) * 9f);
        }

        foreach (var piece in gamePieces)
        {
            float u = t - piece.landAt;
            if (u < 0f)
            {
                piece.Set(0f, 1f);
                continue;
            }
            if (piece.kind == Kind.Deco)
            {
                // suits and bursts pop out of nothing
                piece.Set(Mathf.Clamp01(u / 0.04f), Seg(u, 0f, 0.3f, OutBack2) * (piece.pulses ? pulse : 1f));
            }
            else
            {
                // a word slapped down like a sticker
                bool big = piece.kind == Kind.Big;
                float scale = L(big ? 2f : 1.6f, 1f, Seg(u, 0f, big ? 0.3f : 0.24f, OutBack2));
                float drop = L(50f, 0f, Seg(u, 0f, 0.16f, OutC));
                piece.Set(Mathf.Clamp01(u / 0.05f), scale, piece.isQuestion ? -wob : 0f, new Vector2(0f, drop));
            }
        }
    }

    // ------------------------------------------------------------------ building helpers

    static RectTransform Rect(Transform parent, string name, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        return rt;
    }

    static RectTransform Stretch(Transform parent, string name)
    {
        var rt = Rect(parent, name, Vector2.zero);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        return rt;
    }

    static Image Picture(Transform parent, string name, Sprite sprite, Color color, bool stretch = false, Vector2 size = default)
    {
        var rt = stretch ? Stretch(parent, name) : Rect(parent, name, size);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>A logo crop at its place on the logo canvas (top-left origin), turning and scaling round its pivot.</summary>
    static Piece MakePiece(RectTransform parent, Sprite sprite, SplashLayout.Part p, float scale)
    {
        var rt = Rect(parent, p.sprite, new Vector2(p.w * scale, p.h * scale));
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2((p.pivotX - p.x) / p.w, 1f - (p.pivotY - p.y) / p.h);
        var home = new Vector2(p.pivotX * scale, -p.pivotY * scale);
        rt.anchoredPosition = home;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.raycastTarget = false;
        return new Piece { rect = rt, image = img, home = home };
    }

    // ------------------------------------------------------------------ the trailer's motion maths (engine.js)

    static float L(float a, float b, float t) => a + (b - a) * t;   // unclamped: the back easings overshoot

    static float Seg(float t, float t0, float t1, Func<float, float> ease = null)
    {
        float u = Mathf.Clamp01((t - t0) / (t1 - t0));
        return ease != null ? ease(u) : u;
    }

    static float Kf(float t, float t0, float v0, Func<float, float> e0, float t1, float v1)
    {
        if (t <= t0) return v0;
        if (t <= t1) return L(v0, v1, (e0 ?? IoC)(Mathf.Clamp01((t - t0) / (t1 - t0))));
        return v1;
    }

    static float Kf(float t, float t0, float v0, Func<float, float> e0, float t1, float v1, Func<float, float> e1, float t2, float v2)
    {
        if (t <= t1) return Kf(t, t0, v0, e0, t1, v1);
        return Kf(t, t1, v1, e1, t2, v2);
    }

    static readonly Func<float, float> InQ = t => t * t;
    static readonly Func<float, float> OutQ = t => 1f - (1f - t) * (1f - t);
    static readonly Func<float, float> IoQ = t => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
    static readonly Func<float, float> OutC = t => 1f - Mathf.Pow(1f - t, 3f);
    static readonly Func<float, float> IoC = t => t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    static readonly Func<float, float> IoS = t => -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f;
    static readonly Func<float, float> OutBack = t => Back(t, 1.70158f);
    static readonly Func<float, float> OutBack2 = t => Back(t, 2.4f);

    static float Back(float t, float c1)
    {
        float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    /// <summary>The trailer's camera shake: x, y (CSS: down is positive) and a turn in degrees (CSS: clockwise).</summary>
    static Vector3 Shake(float t)
    {
        const double amp = 18.0, dur = 0.42;
        double dx = 0, dy = 0, r = 0;
        foreach (var hit in Hits)
        {
            double u = t - hit.time;
            if (u < 0 || u > dur) continue;
            double k = Math.Exp(-u * 10) * hit.strength * amp;
            double seed = hit.time * 7.0;
            dx += k * Noise(u * 36, seed + 1);
            dy += k * Noise(u * 36, seed + 2);
            r += k * 0.025 * Noise(u * 30, seed + 3);
        }
        return new Vector3((float)dx, (float)dy, (float)r);
    }

    static double Hash(double i)
    {
        double x = Math.Sin(i * 127.1 + 311.7) * 43758.5453;
        return x - Math.Floor(x);
    }

    static double Noise(double x, double seed)
    {
        double i = Math.Floor(x), f = x - i, u = f * f * (3 - 2 * f);
        double a = Hash(i + seed * 97.3), b = Hash(i + 1 + seed * 97.3);
        return (a + (b - a) * u) * 2 - 1;
    }
}
