using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static UIBuild;

/// <summary>
/// The 20 mini-game screens on the CRT glass (boards Crt*), 1380 x 772 GameArea coordinates. Element names are the ones
/// the mini-game scripts look up (TargetZone, MovingBar, Hole0-8, ...); new visuals they drive are listed in their
/// CRT patches (HitLabel, Mole, Cells, Slots, ...). Also the preview states that mirror each board.
/// </summary>
public static partial class CrisisGamesBuilder
{
    static Sprite White => S("Kit/Special/white");
    static Sprite Circle => S("Kit/Circles/circle300_fill");

    // A bordered box with its outer halo and a soft inner glow (the CRT's lit frames)
    static void LitFrame(RectTransform parent, string name, float left, float top, float w, float h, Color c, float border, float radius,
        float halo = 18f, float haloAlpha = 0.45f, float inner = 18f, float innerAlpha = 0.2f, bool ellipse = false)
    {
        if (halo > 0f) Halo(parent, name + "Halo", left, top, w, h, Lit(c, haloAlpha), halo, radius, ellipse);
        if (inner > 0f)
        {
            var i = Shape(parent, name + "Inner", left + border, top + border, w - 2f * border, h - 2f * border, Lit(c, innerAlpha), Mathf.Max(0f, radius - border));
            if (ellipse) i.kind = UIShape.Kind.Ellipse;
            i.glow = -inner;
        }
        var s = Shape(parent, name, left, top, w, h, c, radius);
        if (ellipse) s.kind = UIShape.Kind.Ellipse;
        s.border = border;
    }

    /// <summary>CSS inner radii: the outer ones (tl, tr, br, bl) less the top / bottom border widths, never negative.</summary>
    static Vector4 Inner(Vector4 r, float top, float bottom) =>
        new Vector4(Mathf.Max(0f, r.x - top), Mathf.Max(0f, r.y - top), Mathf.Max(0f, r.z - bottom), Mathf.Max(0f, r.w - bottom));

    static Button MakeButton(GameObject go, Graphic target)
    {
        target.raycastTarget = true;
        var b = go.AddComponent<Button>();
        b.transition = Selectable.Transition.None;
        b.targetGraphic = target;
        return b;
    }

    /// <summary>Points on a cubic Bezier (SVG C command), in the same frame as the control points.</summary>
    static Vector2[] Bezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, int steps = 20)
    {
        var pts = new Vector2[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps, u = 1f - t;
            pts[i] = u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
        }
        return pts;
    }

    /// <summary>Moves the pivot without moving the rect.</summary>
    static void KeepPivot(RectTransform rt, Vector2 pivot)
    {
        Vector2 d = pivot - rt.pivot;
        rt.pivot = pivot;
        rt.anchoredPosition += new Vector2(d.x * rt.rect.width, d.y * rt.rect.height);
    }

    static RectTransform AtLeftMiddle(RectTransform rt, float x)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0f);
        return rt;
    }

    // ------------------------------------------------------------------ Arrow Timing

    static void ArrowTiming(RectTransform area, MiniGameBase game)
    {
        var dial = Node(area, "Dial", 520f, 306f, 340f, 340f);
        Lines(dial, "Ticks", 0f, 0f, 340f, 340f, Lit(Blue, 0.55f), 5f,
            new[] { V(170, 16), V(170, 36) }, new[] { V(324, 170), V(304, 170) }, new[] { V(170, 324), V(170, 304) }, new[] { V(16, 170), V(36, 170) },
            new[] { V(242, 45.3f), V(247, 36.6f) }, new[] { V(294.7f, 98), V(303.4f, 93) }, new[] { V(294.7f, 242), V(303.4f, 247) },
            new[] { V(242, 294.7f), V(247, 303.4f) }, new[] { V(98, 294.7f), V(93, 303.4f) }, new[] { V(45.3f, 242), V(36.6f, 247) },
            new[] { V(45.3f, 98), V(36.6f, 93) }, new[] { V(98, 45.3f), V(93, 36.6f) });
        var zone = Box(dial, "TargetZone", 6f, 6f, 328f, 328f, Circle, Green);
        zone.type = Image.Type.Filled;
        zone.fillMethod = Image.FillMethod.Radial360;
        zone.fillOrigin = (int)Image.Origin360.Top;
        zone.fillClockwise = true;
        zone.fillAmount = 60f / 360f;
        LitFrame(dial, "Ring", 0f, 0f, 340f, 340f, Blue, 6f, 0f, ellipse: true);

        // the arrow on its pivot (0 = 12 o'clock, clockwise), with a trail behind it
        var pivot = Node(dial, "ArrowPivot", 0f, 0f, 340f, 340f, Anchor.Center);
        float[] trail = { -36f, -24f, -12f };
        float[] alpha = { 0.12f, 0.22f, 0.38f };
        for (int i = 0; i < 3; i++)
        {
            float a = trail[i] * Mathf.Deg2Rad;
            Lines(pivot, "Trail" + i, 0f, 0f, 340f, 340f, Lit(Yellow, alpha[i]), 16f, new[] { V(170, 170), V(170 + Mathf.Sin(a) * 136f, 170 - Mathf.Cos(a) * 136f) });
        }
        // soft glow under the arrow (CSS drop-shadow 0 0 14px)
        Lines(pivot, "GlowWide", 0f, 0f, 340f, 340f, Lit(Yellow, 0.12f), 46f, new[] { V(170, 170), V(170, 30) });
        Lines(pivot, "GlowNear", 0f, 0f, 340f, 340f, Lit(Yellow, 0.22f), 30f, new[] { V(170, 170), V(170, 26) });
        Halo(pivot, "HubGlow", 146f, 146f, 48f, 48f, Lit(Yellow, 0.6f), 16f, 0f, true);
        Lines(pivot, "Shaft", 0f, 0f, 340f, 340f, Yellow, 18f, new[] { V(170, 170), V(170, 64) });
        Poly(pivot, "Head", 0f, 0f, 340f, 340f, Yellow, V(170, 16), V(199, 64), V(141, 64));
        Lines(pivot, "HeadEdge", 0f, 0f, 340f, 340f, Yellow, 6f, new[] { V(170, 16), V(199, 64), V(141, 64), V(170, 16) });
        Oval(pivot, "Hub", 146f, 146f, 48f, 48f, Yellow);
        Oval(pivot, "HubDot", 162f, 162f, 16f, 16f, Ink);

        // "HIT HERE", placed beside the zone by the script
        var label = Node(dial, "HitLabel", 40f, 148f, 260f, 44f, Anchor.Center);
        AtLeftMiddle(Lines(label, "Chevron", 0f, 0f, 30f, 44f, Green, 6f, new[] { V(24, 6), V(8, 22), V(24, 38) }).rectTransform, 15f);
        var text = Localize(Glowing(Text(label, "Text", 44f, 0f, 216f, 36.8f, "HIT HERE", 32f, Green, bold: true), glowGreen),
            CrisisText.Table, "crisis.game.arrow_timing.hit_here");
        AtLeftMiddle(text.rectTransform, 44f + 108f);
    }

    // ------------------------------------------------------------------ Stop the Bar

    static void StopTheBar(RectTransform area, MiniGameBase game)
    {
        LitFrame(area, "TrackFrame", 110f, 400f, 1160f, 180f, Blue, 6f, 18f);
        var track = Node(area, "Track", 116f, 406f, 1148f, 168f);
        var zone = Node(track, "TargetZone", 474f, 0f, 200f, 168f, Anchor.Center);
        Halo(zone, "Glow", 0f, 0f, 200f, 168f, Lit(Green, 0.7f), 28f, 0f);
        Shape(zone, "Fill", 0f, 0f, 200f, 168f, Green);
        Localize(Glowing(Text(zone, "StopHere", -50f, -88f, 300f, 36.8f, "STOP HERE", 32f, Green, bold: true, align: TextAlignmentOptions.Center), glowGreen),
            CrisisText.Table, "crisis.game.stop_the_bar.stop_here");
        Lines(zone, "Chevron", 78f, -44f, 44f, 30f, Green, 6f, new[] { V(6, 6), V(22, 22), V(38, 6) });

        var bar = Node(area, "MovingBar", 574f, 350f, 36f, 280f, Anchor.Center);
        Shape(bar, "Trail3", -104f, 16f, 18f, 248f, Lit(Yellow, 0.12f), 6f);
        Shape(bar, "Trail2", -74f, 10f, 24f, 260f, Lit(Yellow, 0.22f), 6f);
        Shape(bar, "Trail1", -40f, 4f, 30f, 272f, Lit(Yellow, 0.38f), 7f);
        Halo(bar, "Glow", 0f, 0f, 36f, 280f, Lit(Yellow, 0.8f), 26f, 8f);
        Shape(bar, "Fill", 0f, 0f, 36f, 280f, Yellow, 8f);
    }

    // ------------------------------------------------------------------ Color Match

    static void ColorMatch(RectTransform area, MiniGameBase game)
    {
        var glow = Halo(area, "ColorGlow", 390f, 312f, 600f, 168f, Lit(Red, 0.65f), 44f, 22f);
        glow.enabled = false;
        Box(area, "ColorDisplay", 390f, 312f, 600f, 168f, S("Kit/r20_fill"), Lit(Blue, 0.12f));
        var frame = Shape(area, "WindowFrame", 390f, 500f, 600f, 22f, Lit(Blue, 0.55f), 11f);
        frame.border = 3f;
        var fill = Box(area, "WindowFill", 393f, 503f, 594f, 16f, White, Yellow);
        fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 1f;
        Glowing(Text(area, "WindowText", 1008f, 493f, 110f, 36.4f, "", 26f, Yellow, bold: true), glowYellow);

        Color[] cols = { Red, Green, Blue, Yellow };
        string[] letters = { "R", "G", "B", "Y" };
        Material[] glows = { glowRed, glowGreen, glowBlue, glowYellow };
        for (int i = 0; i < 4; i++)
        {
            var ring = Box(area, "KeyIndicator" + i, 390f + 160f * i, 548f, 120f, 84f, S("Kit/small_line3"), cols[i]);
            var k = ring.rectTransform;
            Halo(k, "Glow", 0f, 0f, 120f, 84f, Lit(cols[i], 0.35f), 14f, 12f);
            Shape(k, "Fill", 3f, 3f, 114f, 78f, Lit(cols[i], 0.12f)).Round(SmallX - Vector4.one * 3f, SmallY - Vector4.one * 3f);
            Glowing(Text(k, "Label", 0f, 0f, 120f, 84f, letters[i], 44f, cols[i], bold: true, align: TextAlignmentOptions.Center), glows[i]);
        }
    }

    // ------------------------------------------------------------------ Whack-A-Mole

    static void WhackAMole(RectTransform area, MiniGameBase game)
    {
        var w = (WhackAMoleMiniGame)game;
        w.holeColor = Black;
        w.moleColor = Yellow;
        float[][] holes =
        {
            new[] { 318f, 356f, 204f, 48f, 0.7f }, new[] { 588f, 356f, 204f, 48f, 0.7f }, new[] { 858f, 356f, 204f, 48f, 0.7f },
            new[] { 289f, 468f, 222f, 52f, 0.85f }, new[] { 579f, 468f, 222f, 52f, 0.85f }, new[] { 869f, 468f, 222f, 52f, 0.85f },
            new[] { 260f, 584f, 240f, 56f, 1f }, new[] { 570f, 584f, 240f, 56f, 1f }, new[] { 880f, 584f, 240f, 56f, 1f },
        };
        for (int i = 0; i < 9; i++)
        {
            float x = holes[i][0], y = holes[i][1], hw = holes[i][2], hh = holes[i][3], a = holes[i][4];
            Halo(area, "HoleGlow" + i, x, y, hw, hh, Lit(Blue, 0.25f + 0.15f * (a - 0.7f) / 0.3f), 16f, 0f, true);
            var hole = Box(area, "Hole" + i, x, y, hw, hh, Circle, Black);
            hole.type = Image.Type.Simple;
            MakeButton(hole.gameObject, hole);
            var h = hole.rectTransform;
            var depth = Oval(h, "Depth", 0f, 0f, hw, hh, UIColors.Dark(0.9f));
            depth.glow = -14f;
            var rim = Oval(h, "Rim", 0f, 0f, hw, hh, Lit(Blue, a));
            rim.border = 5f;
            BuildMole(h, hw, hh);
        }
    }

    // an office mole in a shirt and tie, clipped at the hole's centre line so it rises out of it
    static void BuildMole(RectTransform hole, float hw, float hh)
    {
        var mole = Node(hole, "Mole", (hw - 180f) * 0.5f, hh * 0.5f - 150f, 180f, 150f);
        mole.gameObject.AddComponent<RectMask2D>();
        var body = NewRect(mole, "Body");
        body.anchorMin = body.anchorMax = new Vector2(0.5f, 0f);
        body.pivot = new Vector2(0.5f, 0f);
        body.sizeDelta = new Vector2(180f, 150f);
        body.anchoredPosition = Vector2.zero;
        Halo(body, "ShirtGlow", 34f, 110f, 112f, 64f, Lit(Paper, 0.4f), 16f, 0f);
        Shape(body, "Shirt", 34f, 110f, 112f, 64f, Paper).Round(new Vector4(44, 44, 0, 0), new Vector4(32, 32, 0, 0));
        Poly(body, "CollarL", 62f, 104f, 56f, 28f, Paper, V(4, 2), V(28, 14), V(16, 26));
        Poly(body, "CollarR", 62f, 104f, 56f, 28f, Paper, V(52, 2), V(28, 14), V(40, 26));
        Lines(body, "CollarEdge", 62f, 104f, 56f, 28f, Ink, 2.5f, new[] { V(4, 2), V(28, 14), V(16, 26), V(4, 2) }, new[] { V(52, 2), V(28, 14), V(40, 26), V(52, 2) });
        Poly(body, "Knot", 78f, 114f, 24f, 44f, Red, V(7, 1), V(17, 1), V(15, 9), V(9, 9));
        Poly(body, "Tie", 78f, 114f, 24f, 44f, Red, V(9, 9), V(15, 9), V(20, 32), V(12, 42), V(4, 32));
        Lines(body, "TieEdge", 78f, 114f, 24f, 44f, Ink, 2f, new[] { V(7, 1), V(17, 1), V(15, 9), V(9, 9), V(7, 1) }, new[] { V(9, 9), V(15, 9), V(20, 32), V(12, 42), V(4, 32), V(9, 9) });
        Halo(body, "HeadGlow", 40f, 24f, 100f, 92f, Lit(Yellow, 0.6f), 26f, 46f);
        Shape(body, "Head", 40f, 24f, 100f, 92f, Yellow).Round(new Vector4(50, 50, 46, 46), new Vector4(51.5f, 51.5f, 40.5f, 40.5f));
        Oval(body, "EyeL", 64f, 54f, 13f, 15f, Ink);
        Oval(body, "EyeR", 103f, 54f, 13f, 15f, Ink);
        Oval(body, "ShineL", 67f, 57f, 5f, 5f, Paper);
        Oval(body, "ShineR", 106f, 57f, 5f, 5f, Paper);
        Oval(body, "Nose", 77f, 74f, 26f, 17f, Red);
        Oval(body, "NoseEdge", 77f, 74f, 26f, 17f, Ink).border = 2f;
        Shape(body, "Teeth", 83f, 92f, 14f, 12f, Paper).Round(0f, 0f, 5f, 5f);
        var teethEdge = Shape(body, "TeethEdge", 83f, 92f, 14f, 12f, Ink);
        teethEdge.Round(0f, 0f, 5f, 5f);
        teethEdge.border = 2f;
        Box(body, "Gap", 89f, 92f, 2f, 10f, null, Ink);
        // clicks on the mole count for its hole
        foreach (var g in body.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = true;
        mole.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ Simon Says

    static void SimonSays(RectTransform area, MiniGameBase game)
    {
        var sg = (SimonSaysMiniGame)game;
        Color[] cols = { Green, Red, Yellow, Blue };
        sg.buttonColors = cols.Select(c => Lit(c, 0.3f)).ToArray();
        sg.flashColor = Paper;
        float[][] pos = { new[] { 521f, 306f }, new[] { 697f, 306f }, new[] { 521f, 482f }, new[] { 697f, 482f } };
        float[] turn = { 0f, 90f, -90f, 180f };   // CSS degrees: the big corner top-left, top-right, bottom-left, bottom-right
        for (int i = 0; i < 4; i++)
        {
            var pad = Box(area, "ColorButton" + i, pos[i][0], pos[i][1], 162f, 162f, S("Kit/Special/crt_pad"), sg.buttonColors[i], rotation: turn[i]);
            pad.type = Image.Type.Simple;
            MakeButton(pad.gameObject, pad);
            var p = pad.rectTransform;
            var big = new Vector4(144, 18, 18, 18);
            var glowGo = Node(p, "Glow", 0f, 0f, 162f, 162f);
            var g1 = Shape(glowGo, "Halo", 0f, 0f, 162f, 162f, Lit(cols[i], 0.75f)).Round(big, new Vector4(-1, -1, -1, -1));
            g1.glow = 48f;
            var g2 = Shape(glowGo, "Band", -6f, -6f, 174f, 174f, Lit(cols[i], 0.55f)).Round(big + Vector4.one * 6f, new Vector4(-1, -1, -1, -1));
            g2.border = 6f;
            glowGo.gameObject.SetActive(false);
            var soft = Shape(p, "Soft", 0f, 0f, 162f, 162f, Lit(cols[i], 0.35f)).Round(big, new Vector4(-1, -1, -1, -1));
            soft.glow = 16f;
            var ring = Shape(p, "Ring", 0f, 0f, 162f, 162f, cols[i]).Round(big, new Vector4(-1, -1, -1, -1));
            ring.border = 5f;
        }
        Halo(area, "HubHalo", 620f, 405f, 140f, 140f, Lit(Blue, 0.35f), 22f, 0f, true);
        Oval(area, "HubRing", 616f, 401f, 148f, 148f, Lit(Blue, 0.55f)).border = 4f;
        Oval(area, "HubEdge", 620f, 405f, 140f, 140f, Black);
        Oval(area, "Hub", 626f, 411f, 128f, 128f, Ink);
        // SimonSaysMiniGame writes the status (get ready, watch, your turn)
        var status = Glowing(Text(area, "StatusText", 632f, 425f, 116f, 100f, "Your turn!", 28f, Yellow, bold: true, align: TextAlignmentOptions.Center, wrap: true), glowYellow);
        status.enableAutoSizing = true;
        status.fontSizeMin = 18f;
        status.fontSizeMax = 28f;
    }

    // ------------------------------------------------------------------ Number Sequence

    static void NumberSequence(RectTransform area, MiniGameBase game)
    {
        // memorize -> type it: fixed-width pills (sized for the longest time, "4.5 s") with their content centred
        float memW = StepWidth("Memorize", "4.5 s", 26f), typeW = StepWidth("Type it!", null, 28f);
        float stepsW = memW + 20f + 34f + 20f + typeW, x0 = (GW - stepsW) * 0.5f;
        var steps = Node(area, "Steps", x0, 344f, stepsW, 60f);
        const string memorize = "crisis.game.number_sequence.memorize", typeIt = "crisis.game.number_sequence.type_it";
        var slot1 = Node(steps, "Slot1", 0f, 0f, memW, 60f);
        StepPill(slot1, "StepMemorize", memW, "eye", "Memorize", memorize, "4 s", StepLook.Active);
        StepPill(slot1, "StepMemorizeDone", memW, "check", "Memorize", memorize, "4 s", StepLook.Done).SetActive(false);
        Icon(steps, "Arrow", memW + 20f, 13f, 34f, "arrow-right", Blue, "b");
        var slot2 = Node(steps, "Slot2", memW + 74f, 0f, typeW, 60f);
        StepPill(slot2, "StepTypeIdle", typeW, "keyboard", "Type it!", typeIt, null, StepLook.Idle);
        StepPill(slot2, "StepType", typeW, "keyboard", "Type it!", typeIt, null, StepLook.Active).SetActive(false);

        var cells = Node(area, "Cells", 278f, 446f, 824f, 132f);
        var hl = HLayout(cells.gameObject, 16f, null, TextAnchor.MiddleCenter, false, false, false, false);
        for (int i = 0; i < 7; i++)
        {
            var cell = NewRect(cells, "Cell" + i);
            cell.sizeDelta = new Vector2(104f, 132f);
            Layout(cell, 104f, 132f, 104f, 132f);
            var frame = Stretch(cell, "Frame");
            LitFrame(frame, "Box", 0f, 0f, 104f, 132f, Blue, 4f, 14f, 14f, 0.35f, 14f, 0.15f);
            var caret = Stretch(cell, "FrameCaret");
            LitFrame(caret, "Box", 0f, 0f, 104f, 132f, Yellow, 4f, 14f, 20f, 0.55f, 14f, 0.18f);
            Halo(caret, "CaretGlow", 48f, 28f, 8f, 76f, Lit(Yellow, 0.8f), 12f, 4f);
            Shape(caret, "Caret", 48f, 28f, 8f, 76f, Yellow, 4f);
            caret.gameObject.SetActive(false);
            var empty = Stretch(cell, "FrameEmpty");
            var dashed = Shape(empty, "Box", 0f, 0f, 104f, 132f, Lit(Blue, 0.4f), 14f);
            dashed.border = 4f;
            dashed.dash = 10f;
            dashed.gap = 7f;
            empty.gameObject.SetActive(false);
            Glowing(Text(cell, "Digit", 0f, 0f, 104f, 132f, (i + 3).ToString(), 72f, Paper, bold: true, align: TextAlignmentOptions.Center), glowPaper);
        }
        var input = InputBox(area, "InputField", 278f, 446f, 824f, 132f, 72f, new Color(1f, 1f, 1f, 0f), true, 0f, TMP_InputField.ContentType.IntegerNumber, 7);
        input.caretWidth = 0;
        input.caretColor = new Color(0f, 0f, 0f, 0f);
        input.selectionColor = new Color(0f, 0f, 0f, 0f);
    }

    enum StepLook { Active, Done, Idle }

    static float StepWidth(string label, string time, float pad)
    {
        float w = pad + 28f + 10f + TextWidth(label, 26f, true) + pad;
        if (time != null) w += 10f + TextWidth(time, 26f);
        return w;
    }

    /// <summary>A step pill: icon, label (bound to its Crisis.csv key) and the seconds the game writes (time; null = none).</summary>
    static GameObject StepPill(RectTransform slot, string name, float w, string icon, string label, string key, string time, StepLook look)
    {
        var pill = Stretch(slot, name);
        Color ink = look == StepLook.Active ? Ink : (look == StepLook.Done ? Green : Lit(Blue, 0.6f));
        if (look == StepLook.Active)
        {
            Halo(pill, "Glow", 0f, 0f, w, 60f, Lit(Yellow, 0.55f), 22f, 30f);
            Shape(pill, "Fill", 0f, 0f, w, 60f, Yellow, 30f);
        }
        else
        {
            Shape(pill, "Ring", 0f, 0f, w, 60f, look == StepLook.Done ? Lit(Green, 0.6f) : Lit(Blue, 0.4f), 30f).border = 3f;
        }
        var row = Stretch(pill, "Content");
        HLayout(row.gameObject, 10f, null, TextAnchor.MiddleCenter, true, true, false, false);
        LayoutIcon(row, "Icon", 28f, icon, ink, look == StepLook.Done ? "b" : null);
        Localize(LayoutText(row, "Label", label, 26f, ink, look == StepLook.Active), CrisisText.Table, key);
        if (time != null)
            LayoutText(row, "Time", time, 26f, look == StepLook.Done ? Blue : Ink);
        return pill.gameObject;
    }

    // ------------------------------------------------------------------ Keypad Code

    static void KeypadCode(RectTransform area, MiniGameBase game)
    {
        Localize(Text(area, "CodeLabel", 110f, 296f, 300f, 30.8f, "CODE", 22f, Blue, letterSpacingEm: 0.1f), CrisisText.Table, "crisis.game.keypad_code.code");
        LitFrame(area, "CodeFrame", 110f, 332f, 568f, 136f, Blue, 6f, 18f);
        var shown = Node(area, "CodeShown", 116f, 338f, 556f, 124f);
        for (int i = 0; i < 4; i++)
            Glowing(Text(shown, "Digit" + i, i * 145f, 6f, 121f, 96f, "7391"[i].ToString(), 96f, Yellow, bold: true, align: TextAlignmentOptions.Center), glowYellow);
        var track = Shape(shown, "HideTrack", 24f, 106f, 508f, 8f, Lit(Yellow, 0.2f), 4f);
        var bar = Box(track.rectTransform, "HideBar", 0f, 0f, 508f, 8f, White, Yellow);
        bar.type = Image.Type.Filled;
        bar.fillMethod = Image.FillMethod.Horizontal;
        bar.fillOrigin = (int)Image.OriginHorizontal.Left;
        var hidden = Node(area, "CodeHidden", 116f, 338f, 556f, 124f);
        for (int i = 0; i < 4; i++)
            Shape(hidden, "Dash" + i, i * 145f + 28.5f, 57f, 64f, 10f, Lit(Paper, 0.5f), 5f);
        hidden.gameObject.SetActive(false);

        Localize(Text(area, "TypedLabel", 110f, 480f, 300f, 30.8f, "YOU TYPED", 22f, Blue, letterSpacingEm: 0.1f), CrisisText.Table, "crisis.game.keypad_code.you_typed");
        var slots = Node(area, "Slots", 110f, 516f, 568f, 118f);
        for (int i = 0; i < 4; i++)
        {
            var slot = Node(slots, "Slot" + i, i * 148f, 0f, 124f, 118f);
            Shape(slot, "Fill", 0f, 0f, 124f, 118f, Lit(Paper, 0.05f), 14f);
            Shape(slot, "Edge", 0f, 0f, 124f, 118f, Lit(Paper, 0.3f), 14f).border = 4f;
            Glowing(Text(slot, "Char", 0f, 0f, 124f, 110f, "", 72f, Paper, bold: true, align: TextAlignmentOptions.Center), glowPaper);
            Shape(slot, "Bar", 34f, 97f, 56f, 7f, Lit(Paper, 0.3f), 4f);
            var cursor = Node(slot, "Cursor", 34f, 97f, 56f, 7f);
            Halo(cursor, "Glow", 0f, 0f, 56f, 7f, Lit(Yellow, 0.8f), 12f, 4f);
            Shape(cursor, "Bar", 0f, 0f, 56f, 7f, Yellow, 4f);
            cursor.gameObject.SetActive(false);
        }

        var pad = Node(area, "Keypad", 780f, 304f, 490f, 326f);
        pad.gameObject.AddComponent<CanvasGroup>();
        for (int d = 1; d <= 9; d++)
            PadKey(pad, "NumButton" + d, ((d - 1) % 3) * 170f, ((d - 1) / 3) * 84f, 150f, d.ToString(), Blue, 44f, d % 2 == 0);
        PadKey(pad, "ClearButton", 0f, 252f, 130f, "Clear", Red, 28f, false, "crisis.game.keypad_code.clear");
        PadKey(pad, "NumButton0", 170f, 252f, 150f, "0", Blue, 44f, true);

        float lw = 22f + 28f + 10f + TextWidth("Unlocks when the code hides", 26f) + 22f;
        var pill = Node(area, "LockPill", 780f + (490f - lw) * 0.5f, 439f, lw, 56f);
        Halo(pill, "Glow", 0f, 0f, lw, 56f, Lit(Yellow, 0.45f), 18f, 28f);
        Shape(pill, "Fill", 0f, 0f, lw, 56f, Ink, 28f);
        Shape(pill, "Edge", 0f, 0f, lw, 56f, Yellow, 28f).border = 3f;
        Icon(pill, "Lock", 22f, 14f, 28f, "lock", Yellow);
        Localize(Text(pill, "Label", 60f, 0f, lw - 70f, 56f, "Unlocks when the code hides", 26f, Yellow), CrisisText.Table, "crisis.game.keypad_code.unlocks");
        pill.gameObject.SetActive(false);
    }

    /// <summary>A keypad key; key: the label's Crisis.csv entry (null for the digits, which stay as they are).</summary>
    static void PadKey(RectTransform pad, string name, float x, float y, float w, string label, Color c, float size, bool flip, string key = null)
    {
        var keyRt = Node(pad, name, x, y, w, 74f);
        Halo(keyRt, "Glow", 0f, 0f, w, 74f, Lit(c, c == Red ? 0.4f : 0.35f), 14f, 14f);
        var face = Shape(keyRt, "Face", 0f, 0f, w, 74f, Lit(c, 0.08f));
        face.Round(flip ? CardX2 : CardX, flip ? CardY2 : CardY);
        var btn = MakeButton(keyRt.gameObject, face);
        // hover and press thicken the faint face (its alpha is tiny, so the tint scales alpha)
        btn.transition = Selectable.Transition.ColorTint;
        var colors = ColorBlock.defaultColorBlock;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 3f);
        colors.pressedColor = new Color(1f, 1f, 1f, 6f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(1f, 1f, 1f, 0.5f);
        colors.fadeDuration = 0.05f;
        btn.colors = colors;
        var edge = Shape(keyRt, "Edge", 0f, 0f, w, 74f, c);
        edge.Round(flip ? CardX2 : CardX, flip ? CardY2 : CardY);
        edge.border = 4f;
        var text = Text(keyRt, "Label", 0f, 0f, w, 70f, label, size, c == Red ? Red : Paper, bold: true, align: TextAlignmentOptions.Center);
        if (key != null) Localize(text, CrisisText.Table, key);
        if (c == Red) return;

        // the digit just entered: yellow key pushed down 3 px (KeypadCodeMiniGame flashes it)
        var lit = Node(keyRt, "Lit", 0f, 3f, w, 74f);
        Halo(lit, "Glow", 0f, 0f, w, 74f, Lit(Yellow, 0.75f), 26f, 16f);
        Shape(lit, "Fill", 0f, 0f, w, 74f, Yellow).Round(flip ? CardX2 : CardX, flip ? CardY2 : CardY);
        var litText = Text(lit, "Label", 0f, 0f, w, 70f, label, size, Ink, bold: true, align: TextAlignmentOptions.Center);
        if (key != null) Localize(litText, CrisisText.Table, key);
        lit.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ Button Mash

    static void ButtonMash(RectTransform area, MiniGameBase game)
    {
        LitFrame(area, "MeterFrame", 110f, 304f, 1040f, 84f, Blue, 6f, 18f);
        var bar = Box(area, "ProgressBar", 116f, 310f, 1028f, 72f, White, Yellow);
        bar.type = Image.Type.Filled;
        bar.fillMethod = Image.FillMethod.Horizontal;
        bar.fillOrigin = (int)Image.OriginHorizontal.Left;
        bar.fillAmount = 0f;
        foreach (float t in new[] { 255f, 512f, 769f })
            Box(area, "Tick" + t, 116f + t, 310f, 4f, 72f, null, UIColors.Dark(0.35f));
        LitFrame(area, "GoalFrame", 1170f, 304f, 100f, 84f, Green, 6f, 18f, 18f, 0.5f, 14f, 0.2f);
        Glowing(Text(area, "Goal", 1170f, 304f, 100f, 84f, "40", 44f, Green, bold: true, align: TextAlignmentOptions.Center), glowGreen);

        Lines(area, "LinesLeft", 128f, 440f, 96f, 160f, Yellow, 9f, new[] { V(84, 30), V(22, 10) }, new[] { V(84, 80), V(14, 80) }, new[] { V(84, 130), V(22, 150) });
        Lines(area, "LinesRight", 1156f, 440f, 96f, 160f, Yellow, 9f, new[] { V(12, 30), V(74, 10) }, new[] { V(12, 80), V(82, 80) }, new[] { V(12, 130), V(74, 150) });
        var key = Node(area, "MashButton", 240f, 428f, 900f, 184f);
        var kx = new Vector4(22, 30, 20, 32);
        var ky = new Vector4(30, 20, 32, 22);
        var halo = Shape(key, "Halo", 0f, 0f, 900f, 184f, Lit(Paper, 0.35f)).Round(kx, ky);
        halo.glow = 44f;
        var ring = Shape(key, "Ring", -4f, -4f, 908f, 192f, Lit(Paper, 0.2f)).Round(kx + Vector4.one * 4f, ky + Vector4.one * 4f);
        ring.border = 4f;
        // 5 px black edge, 22 px at the bottom (the key's depth), and a faint shade along the face's bottom
        Shape(key, "Edge", 0f, 0f, 900f, 184f, Black).Round(kx, ky);
        var faceRx = Inner(kx, 5f, 5f);
        var faceRy = Inner(ky, 5f, 22f);
        Shape(key, "Face", 5f, 5f, 890f, 157f, Paper).Round(faceRx, faceRy);
        Shape(key, "Shade", 5f, 154f, 890f, 8f, UIColors.Over(Ink, 0.08f, Paper)).Round(new Vector4(0f, 0f, faceRx.z, faceRx.w), new Vector4(0f, 0f, 8f, 8f));
        Localize(Text(key, "Label", 5f, 5f, 890f, 157f, "SPACE", 88f, Ink, bold: true, align: TextAlignmentOptions.Center, letterSpacingEm: 0.24f),
            CrisisText.Table, "crisis.key.space.caps");
    }

    // ------------------------------------------------------------------ Math Blitz

    static void MathBlitz(RectTransform area, MiniGameBase game)
    {
        Glowing(Text(area, "ProblemText", 0f, 296f, GW, 165f, "7 × 8 = <color=#FFE14D>?</color>", 150f, Paper, bold: true, align: TextAlignmentOptions.Center), glowPaper);
        Color[] cols = { Yellow, Blue, Pink, Paper };
        for (int i = 0; i < 4; i++)
        {
            bool flip = i % 2 == 1;
            var rx = flip ? CardX2 : CardX;
            var ry = flip ? CardY2 : CardY;
            var btn = Node(area, "AnswerButton" + i, 100f + i * 304f, 500f, 268f, 140f);
            Shape(btn, "Shadow", 6f, 8f, 268f, 140f, Black).Round(rx, ry);
            Halo(btn, "Glow", 0f, 0f, 268f, 140f, Lit(cols[i], i == 3 ? 0.3f : 0.35f), 26f, 16f);
            var face = Shape(btn, "Face", 0f, 0f, 268f, 140f, cols[i]).Round(rx, ry);
            var edge = Shape(btn, "Edge", 0f, 0f, 268f, 140f, Black).Round(rx, ry);
            edge.border = 4f;
            MakeButton(btn.gameObject, face);
            Text(btn, "AnswerText", 0f, 0f, 268f, 136f, new[] { "54", "56", "58", "64" }[i], 72f, Ink, bold: true, align: TextAlignmentOptions.Center);
        }
    }

    // ------------------------------------------------------------------ Quick Tap

    static void QuickTap(RectTransform area, MiniGameBase game)
    {
        var field = Shape(area, "Field", 110f, 300f, 1160f, 350f, Lit(Blue, 0.28f), 24f);
        field.border = 3f;
        field.dash = 12f;
        field.gap = 9f;
        var spawn = Node(area, "SpawnArea", 110f, 300f, 1160f, 350f);

        var last = Node(spawn, "LastHit", 505f, 100f, 150f, 150f, Anchor.Center);
        last.gameObject.AddComponent<CanvasGroup>();
        var dash = Oval(last, "Ring", 0f, 0f, 150f, 150f, Lit(Green, 0.55f));
        dash.border = 4f;
        dash.dash = 12f;
        dash.gap = 8f;
        Icon(last, "Check", 43f, 43f, 64f, "check", Green, "b");
        last.gameObject.SetActive(false);

        var target = Node(spawn, "TargetButton", 505f, 100f, 150f, 150f, Anchor.Center);
        var disc = AddImage(target.gameObject, Circle, Red);
        disc.type = Image.Type.Simple;
        MakeButton(target.gameObject, disc);
        Halo(target, "Glow", 0f, 0f, 150f, 150f, Lit(Red, 0.65f), 34f, 0f, true);
        // radial bands of the 142 px padding box: red to 24 %, paper to 46 %, red to 68 %, paper to 86 %, red
        foreach (var (band, radius, col) in new[] { ("Band1", 61f, Paper), ("Band2", 48.3f, Red), ("Band3", 32.7f, Paper), ("Bull", 17f, Red) })
            Oval(target, band, 75f - radius, 75f - radius, 2f * radius, 2f * radius, col);
        Oval(target, "Edge", 0f, 0f, 150f, 150f, Black).border = 4f;
        Oval(target, "Outer", -4f, -4f, 158f, 158f, Lit(Red, 0.35f)).border = 4f;
        // where the shrinking ring started, and a step in between
        Oval(target, "Start", -95f, -95f, 340f, 340f, Lit(Yellow, 0.14f)).border = 4f;
        Oval(target, "Mid", -63f, -63f, 276f, 276f, Lit(Yellow, 0.3f)).border = 5f;
        var ring = Node(target, "Ring", -32f, -32f, 214f, 214f, Anchor.Center);
        var halo = StretchShape(ring, "Glow", Lit(Yellow, 0.7f));
        halo.kind = UIShape.Kind.Ellipse;
        halo.glow = 22f;
        var inset = StretchShape(ring, "Inset", Lit(Yellow, 0.35f));
        inset.kind = UIShape.Kind.Ellipse;
        inset.glow = -16f;
        inset.spread = -7f;   // inside the 7 px ring
        var r = ring.gameObject.AddComponent<UIShape>();
        r.raycastTarget = false;
        r.kind = UIShape.Kind.Ellipse;
        r.color = Yellow;
        r.border = 7f;
        foreach (var g in target.GetComponentsInChildren<UIShape>(true)) g.raycastTarget = false;
    }

    // ------------------------------------------------------------------ Rapid Keys

    static void RapidKeys(RectTransform area, MiniGameBase game)
    {
        Lines(area, "LinesLeft", 482f, 338f, 80f, 140f, Yellow, 8f, new[] { V(72, 34), V(14, 12) }, new[] { V(72, 70), V(6, 70) }, new[] { V(72, 106), V(14, 128) });
        Lines(area, "LinesRight", 818f, 338f, 80f, 140f, Yellow, 8f, new[] { V(8, 34), V(66, 12) }, new[] { V(8, 70), V(74, 70) }, new[] { V(8, 106), V(66, 128) });
        // CSS box-shadow order: the glow under the solid 16 px lip, the lip under the key
        Halo(area, "KeyGlow", 586f, 304f, 208f, 208f, Lit(Yellow, 0.6f), 48f, 30f);
        Shape(area, "KeyShade", 586f, 320f, 208f, 208f, Lit(Yellow, 0.38f), 30f);
        Shape(area, "Key", 586f, 304f, 208f, 208f, Yellow, 30f);
        Shape(area, "KeyBump", 670f, 476f, 40f, 7f, UIColors.Over(Ink, 0.35f, Yellow), 4f);
        Text(area, "KeyDisplay", 586f, 304f, 208f, 196f, "J", 132f, Ink, bold: true, align: TextAlignmentOptions.Center);

        var row = Node(area, "KeyRow", 339f, 552f, 702f, 84f);
        string[] letters = { "A", "S", "D", "F", "J", "K", "L" };
        float[] xs = { 0f, 98f, 196f, 294f, 422f, 520f, 618f };
        for (int i = 0; i < 7; i++)
        {
            // keycaps: 4 px edges, 12 px at the bottom, the letter centred above it
            var key = Node(row, "Key_" + letters[i], xs[i], 0f, 84f, 84f);
            var idle = Stretch(key, "Idle");
            var ring = Shape(idle, "Ring", 0f, 0f, 84f, 84f, Lit(Blue, 0.55f), 14f);
            ring.border = 4f;
            ring.borderBottom = 12f;
            Text(idle, "Label", 0f, 0f, 84f, 72f, letters[i], 44f, Blue, bold: true, align: TextAlignmentOptions.Center);
            var lit = Stretch(key, "Lit");
            Halo(lit, "Glow", 0f, 0f, 84f, 84f, Lit(Yellow, 0.65f), 26f, 14f);
            Shape(lit, "Depth", 0f, 0f, 84f, 84f, Lit(Yellow, 0.45f), 14f);
            Shape(lit, "Fill", 0f, 0f, 84f, 72f, Yellow).Round(14f, 14f, 4f, 4f);
            Text(lit, "Label", 0f, 0f, 84f, 72f, letters[i], 44f, Ink, bold: true, align: TextAlignmentOptions.Center);
            if (letters[i] == "F" || letters[i] == "J")
            {
                Shape(idle, "Bump", 32f, 58f, 20f, 4f, Lit(Blue, 0.75f), 2f);
                Shape(lit, "Bump", 32f, 58f, 20f, 4f, UIColors.Over(Ink, 0.45f, Yellow), 2f);
            }
            lit.gameObject.SetActive(false);
        }
    }

    // ------------------------------------------------------------------ Sequence Memory

    static void SequenceMemory(RectTransform area, MiniGameBase game)
    {
        var sm = (SequenceMemoryMiniGame)game;
        sm.normalColor = Lit(Blue, 0.06f);
        sm.flashColor = Blue;
        for (int i = 0; i < 9; i++)
        {
            var tile = Box(area, "GridButton" + i, 522f + (i % 3) * 118f, 306f + (i / 3) * 118f, 100f, 100f, S("Kit/r16_fill"), sm.normalColor);
            MakeButton(tile.gameObject, tile);
            var t = tile.rectTransform;
            var glow = Node(t, "Glow", 0f, 0f, 100f, 100f);
            Halo(glow, "Halo", 0f, 0f, 100f, 100f, Lit(Blue, 0.85f), 36f, 16f);
            Shape(glow, "Band", -10f, -10f, 120f, 120f, Lit(Blue, 0.16f), 26f).border = 10f;
            glow.gameObject.SetActive(false);
            Shape(t, "Frame", 0f, 0f, 100f, 100f, Lit(Blue, 0.5f), 16f).border = 4f;
        }

        var watch = Node(area, "WatchPhase", 110f, 440f, 372f, 84f);
        watch.gameObject.AddComponent<CanvasGroup>();
        float ww = TextWidth("Watch...", 36f);
        float x0 = 372f - (44f + 12f + ww + 12f + 36f);
        Icon(watch, "Eye", x0, 0f, 44f, "eye", Lit(Blue, 0.55f));
        ShrinkToFit(Localize(Text(watch, "Label", x0 + 56f, 0f, ww + 4f, 44f, "Watch...", 36f, Lit(Blue, 0.6f)), CrisisText.Table, "crisis.game.sequence_memory.watch"), 28f);
        var done = Icon(watch, "Done", 372f - 36f, 4f, 36f, "check", Green, "b");
        done.gameObject.SetActive(false);
        Localize(Text(watch, "Note", 0f, 50f, 372f, 30.8f, "shown once, no replays", 22f, Lit(Blue, 0.6f), align: TextAlignmentOptions.Right),
            CrisisText.Table, "crisis.game.sequence_memory.no_replays");

        var turn = Node(area, "TurnPhase", 898f, 446f, 372f, 60f);
        Icon(turn, "Arrow", 0f, 10f, 40f, "arrow-left", Yellow, "b");
        Localize(Glowing(Text(turn, "Label", 52f, 0f, 320f, 60f, "Your turn!", 44f, Yellow, bold: true), glowYellow),
            CrisisText.Table, "crisis.game.sequence_memory.your_turn");
        turn.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ Pattern Match

    static void PatternMatch(RectTransform area, MiniGameBase game)
    {
        Localize(Text(area, "PatternLabel", 110f, 312f, 223f, 96f, "PATTERN", 22f, Blue, align: TextAlignmentOptions.Right, letterSpacingEm: 0.1f),
            CrisisText.Table, "crisis.game.pattern_match.pattern");
        Localize(Text(area, "InputLabel", 110f, 444f, 223f, 96f, "INPUT", 22f, Blue, align: TextAlignmentOptions.Right, letterSpacingEm: 0.1f),
            CrisisText.Table, "crisis.game.pattern_match.input");
        foreach (var rowName in new[] { "PatternSlots", "InputSlots" })
        {
            var row = Node(area, rowName, 357f, rowName == "PatternSlots" ? 312f : 444f, 666f, 96f);
            for (int i = 0; i < 6; i++)
            {
                var slot = Node(row, "Slot" + i, i * 114f, 0f, 96f, 96f);
                var fill = Shape(slot, "Fill", 0f, 0f, 96f, 96f, Green, 14f);
                fill.gameObject.SetActive(false);
                Shape(slot, "Frame", 0f, 0f, 96f, 96f, Lit(Blue, 0.3f), 14f).border = 4f;
                var mark = Node(slot, "Mark", 0f, 0f, 96f, 96f);
                var dashed = Shape(mark, "Ring", 0f, 0f, 96f, 96f, Lit(Blue, 0.45f), 14f);
                dashed.border = 4f;
                dashed.dash = 10f;
                dashed.gap = 7f;
                Text(mark, "Q", 0f, 0f, 96f, 92f, "?", 56f, Lit(Blue, 0.6f), bold: true, align: TextAlignmentOptions.Center);
                mark.gameObject.SetActive(false);
                var cursor = Node(slot, "Cursor", 26f, 62f, 44f, 8f);
                Halo(cursor, "Glow", 0f, 0f, 44f, 8f, Lit(Yellow, 0.8f), 10f, 3f);
                Shape(cursor, "Bar", 0f, 0f, 44f, 8f, Yellow, 3f);
                cursor.gameObject.SetActive(false);
                var arrow = Icon(slot, "Arrow", 19f, 19f, 58f, "arrow-right", Blue, "b");
                arrow.gameObject.SetActive(false);
            }
        }
        // PatternMatchMiniGame writes the status (get ready, memorize, enter the pattern)
        Glowing(Text(area, "StatusText", 0f, 572f, GW, 36.8f, "Enter pattern!", 32f, Yellow, bold: true, align: TextAlignmentOptions.Center), glowYellow);
    }

    // ------------------------------------------------------------------ Safe Cracker

    static void SafeCracker(RectTransform area, MiniGameBase game)
    {
        Oval(area, "DialFace", 250f, 318f, 300f, 300f, Lit(Blue, 0.06f));
        LitFrame(area, "DialRim", 250f, 318f, 300f, 300f, Blue, 6f, 0f, 24f, 0.45f, 24f, 0.18f, true);
        var dial = Node(area, "Dial", 256f, 324f, 288f, 288f);
        Lines(dial, "Ticks", 0f, 0f, 288f, 288f, Lit(Blue, 0.45f), 4f,
            new[] { V(182.3f, 26.1f), V(186.6f, 12.7f) }, new[] { V(244.3f, 71.1f), V(255.6f, 62.9f) }, new[] { V(268, 144), V(282, 144) },
            new[] { V(244.3f, 216.9f), V(255.6f, 225.1f) }, new[] { V(182.3f, 261.9f), V(186.6f, 275.3f) }, new[] { V(105.7f, 261.9f), V(101.4f, 275.3f) },
            new[] { V(43.7f, 216.9f), V(32.4f, 225.1f) }, new[] { V(20, 144), V(6, 144) }, new[] { V(43.7f, 71.1f), V(32.4f, 62.9f) },
            new[] { V(105.7f, 26.1f), V(101.4f, 12.7f) });
        var nums = Node(dial, "Numbers", 0f, 0f, 288f, 288f);
        for (int i = 0; i < 10; i++)
        {
            var holder = Node(nums, "N" + i, 0f, 0f, 288f, 288f, rotation: 36f * i);
            Text(holder, "Label", 119f, 10f, 50f, 44f, i.ToString(), 34f, Blue, bold: true, align: TextAlignmentOptions.Center);
        }
        LitFrame(area, "Hub", 338f, 406f, 124f, 124f, Blue, 5f, 0f, 16f, 0.4f, 0f, 0f, true);
        Oval(area, "HubFill", 343f, 411f, 114f, 114f, Ink);
        Glowing(Text(area, "CurrentText", 338f, 406f, 124f, 124f, "0", 72f, Paper, bold: true, align: TextAlignmentOptions.Center), glowPaper);
        Halo(area, "PointerGlow", 386f, 296f, 28f, 22f, Lit(Yellow, 0.6f), 8f, 4f);
        Poly(area, "Pointer", 380f, 292f, 40f, 38f, Yellow, V(3, 3), V(37, 3), V(20, 35));

        foreach (bool left in new[] { true, false })
        {
            var group = Node(area, left ? "TurnLeft" : "TurnRight", 0f, 0f, GW, GH);
            var arc = left ? Arc(200, 200, 178, -110f, -160f) : Arc(200, 200, 178, -70f, -20f);
            var head = left ? new[] { V(50.7f, 126.5f), V(32.7f, 139.1f), V(27, 117.8f) } : new[] { V(349.3f, 126.5f), V(367.3f, 139.1f), V(373, 117.8f) };
            float kx = left ? 186f : 562f;
            string icon = left ? "arrow-left" : "arrow-right";
            var lit = Stretch(group, "Lit");
            Lines(lit, "Arc", 200f, 268f, 400f, 160f, Yellow, 8f, arc, head);
            Halo(lit, "KeyGlow", kx, 424f, 52f, 52f, Lit(Yellow, 0.6f), 18f, 12f);
            // keycap: 3 px edges, 9 px at the bottom (a paler yellow when lit)
            Shape(lit, "Depth", kx, 424f, 52f, 52f, Lit(Yellow, 0.45f), 12f);
            Shape(lit, "Key", kx, 424f, 52f, 43f, Yellow).Round(12f, 12f, 3f, 3f);
            Icon(lit, "Icon", kx + 12f, 433f, 28f, icon, Ink, "b");
            lit.gameObject.SetActive(false);
            var idle = Stretch(group, "Idle");
            Lines(idle, "Arc", 200f, 268f, 400f, 160f, Lit(Blue, 0.45f), 8f, arc, head);
            var cap = Shape(idle, "Key", kx, 424f, 52f, 52f, Lit(Blue, 0.55f), 12f);
            cap.border = 3f;
            cap.borderBottom = 9f;
            Icon(idle, "Icon", kx + 12f, 433f, 28f, icon, Blue, "b");
        }

        Localize(Text(area, "FindLabel", 740f, 364f, 300f, 30.8f, "FIND", 22f, Blue, letterSpacingEm: 0.1f), CrisisText.Table, "crisis.game.safe_cracker.find");
        var cells = Node(area, "FindCells", 740f, 404f, 416f, 170f);
        for (int i = 0; i < 3; i++)
        {
            var cell = Node(cells, "Cell" + i, i * 152f, 0f, 112f, 140f);
            var fill = Node(cell, "Fill", 0f, 0f, 112f, 140f);
            Halo(fill, "Glow", 0f, 0f, 112f, 140f, Lit(Green, 0.6f), 22f, 16f);
            Shape(fill, "Box", 0f, 0f, 112f, 140f, Green, 16f);
            Shape(cell, "FrameIdle", 0f, 0f, 112f, 140f, Lit(Blue, 0.4f), 16f).border = 5f;
            var frame = Node(cell, "Frame", 0f, 0f, 112f, 140f);
            LitFrame(frame, "Box", 0f, 0f, 112f, 140f, Yellow, 5f, 16f, 26f, 0.55f, 18f, 0.18f);
            Text(cell, "Digit", 0f, 0f, 112f, 136f, "7", 88f, Yellow, bold: true, align: TextAlignmentOptions.Center);
            Icon(cell, "Check", 112f - 8f - 28f, 8f, 28f, "check", Ink, "b");
            var under = Node(cell, "Underline", 0f, 154f, 112f, 8f);
            Halo(under, "Glow", 0f, 0f, 112f, 8f, Lit(Yellow, 0.8f), 12f, 4f);
            Shape(under, "Bar", 0f, 0f, 112f, 8f, Yellow, 4f);
            if (i < 2) Shape(cells, "Sep" + i, i * 152f + 120f, 67f, 24f, 6f, Lit(Blue, 0.55f), 3f);
        }
    }

    // ------------------------------------------------------------------ Odd One Out

    static void OddOneOut(RectTransform area, MiniGameBase game)
    {
        var oo = (OddOneOutMiniGame)game;
        oo.possibleColors = new[] { Blue, Yellow, Green, Red, Pink };
        oo.idleColor = Lit(Blue, 0.15f);
        for (int i = 0; i < 9; i++)
        {
            var item = Box(area, "ItemButton" + i, 522f + (i % 3) * 118f, 306f + (i / 3) * 118f, 100f, 100f, S("Kit/r16_fill"), oo.idleColor);
            MakeButton(item.gameObject, item);
        }
    }

    // ------------------------------------------------------------------ Catch Falling

    static void CatchFalling(RectTransform area, MiniGameBase game)
    {
        foreach (var r in new[] { new[] { 364f, 300f, 6f, 346f }, new[] { 1010f, 300f, 6f, 346f }, new[] { 364f, 640f, 652f, 6f } })
        {
            Halo(area, "RailGlow", r[0], r[1], r[2], r[3], Lit(Blue, 0.6f), 14f, 3f);
            Shape(area, "Rail", r[0], r[1], r[2], r[3], Blue, 3f);
        }
        var shaft = Node(area, "Shaft", 370f, 300f, 640f, 340f);
        var falling = Node(shaft, "FallingObject", 284f, 20f, 72f, 72f, Anchor.Center);
        Shape(falling, "Trail3", 0f, -84f, 72f, 72f, Lit(Yellow, 0.10f), 8f);
        Shape(falling, "Trail2", 0f, -56f, 72f, 72f, Lit(Yellow, 0.18f), 8f);
        Shape(falling, "Trail1", 0f, -28f, 72f, 72f, Lit(Yellow, 0.30f), 8f);
        Halo(falling, "Glow", 0f, 0f, 72f, 72f, Lit(Yellow, 0.75f), 26f, 8f);
        Shape(falling, "Body", 0f, 0f, 72f, 72f, Yellow, 8f);
        Shape(falling, "Shutter", 16f, 0f, 40f, 26f, Ink).Round(0f, 0f, 4f, 4f);
        Shape(falling, "Slot", 40f, 5f, 9f, 15f, Yellow, 2f);
        Shape(falling, "Label", 10f, 36f, 52f, 28f, Paper, 3f);

        var catcher = Node(shaft, "Catcher", 232f, 278f, 176f, 52f, Anchor.Center);
        var u = new List<Vector2> { V(8, 6), V(8, 32) };
        u.AddRange(Arc(20, 32, 12, 180f, 90f, 8));
        u.Add(V(156, 44));
        u.AddRange(Arc(156, 32, 12, 90f, 0f, 8));
        u.Add(V(168, 6));
        // drop-shadow glow (wide faint strokes), the tinted inside, the tray
        Lines(catcher, "GlowWide", 0f, 0f, 176f, 52f, Lit(Paper, 0.1f), 34f, u.ToArray());
        Lines(catcher, "GlowNear", 0f, 0f, 176f, 52f, Lit(Paper, 0.2f), 20f, u.ToArray());
        Poly(catcher, "Inside", 0f, 0f, 176f, 52f, Lit(Paper, 0.2f), V(8, 6), V(168, 6), V(168, 32), V(156, 44), V(20, 44), V(8, 32));
        Lines(catcher, "Tray", 0f, 0f, 176f, 52f, Paper, 10f, u.ToArray());
        // speed lines trailing it while it moves (drawn for moving left; the script mirrors them)
        var lines = Stretch(catcher, "SpeedLines");
        Shape(lines, "Line1", 190f, 10f, 52f, 6f, Lit(Paper, 0.5f), 3f);
        Shape(lines, "Line2", 202f, 25f, 36f, 6f, Lit(Paper, 0.32f), 3f);
        Shape(lines, "Line3", 190f, 40f, 52f, 6f, Lit(Paper, 0.5f), 3f);
        lines.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ Higher Lower

    static void HigherLower(RectTransform area, MiniGameBase game)
    {
        Glowing(Text(area, "CurrentNumber", 150f, 300f, 560f, 250f, "52", 220f, Paper, bold: true, align: TextAlignmentOptions.Center), glowPaper);
        // HigherLowerMiniGame writes the hint
        Text(area, "HintText", 150f, 566f, 560f, 36.4f, "The next one is 1 to 100, never 52", 26f, Blue, align: TextAlignmentOptions.Center);
        BigCall(area, "HigherButton", 790f, 316f, Green, "HIGHER", "arrow-up", CardX, CardY, "crisis.game.higher_lower.higher");
        BigCall(area, "LowerButton", 790f, 488f, Red, "LOWER", "arrow-down", CardX2, CardY2, "crisis.game.higher_lower.lower");
    }

    /// <summary>A big call button: the label (bound to labelKey in Crisis.csv) and its arrow key cap.</summary>
    static void BigCall(RectTransform area, string name, float x, float y, Color c, string label, string icon, Vector4 rx, Vector4 ry, string labelKey)
    {
        var btn = Node(area, name, x, y, 440f, 140f);
        Shape(btn, "Shadow", 6f, 8f, 440f, 140f, Black).Round(rx, ry);
        Halo(btn, "Glow", 0f, 0f, 440f, 140f, Lit(c, 0.35f), 26f, 16f);
        var face = Shape(btn, "Face", 0f, 0f, 440f, 140f, c).Round(rx, ry);
        var edge = Shape(btn, "Edge", 0f, 0f, 440f, 140f, Black).Round(rx, ry);
        edge.border = 4f;
        MakeButton(btn.gameObject, face);
        Localize(Text(btn, "Label", 40f, 0f, 300f, 136f, label, 60f, Ink, bold: true), CrisisText.Table, labelKey);
        // keycap: 3 px black edges, 8 px at the bottom
        var key = Node(btn, "Key", 440f - 28f - 72f, 34f, 72f, 72f);
        Shape(key, "Edge", 0f, 0f, 72f, 72f, Black).Round(SmallX, SmallY);
        Shape(key, "Face", 3f, 3f, 66f, 61f, Paper).Round(Inner(SmallX, 3f, 3f), Inner(SmallY, 3f, 8f));
        Icon(key, "Icon", 16f, 13.5f, 40f, icon, Ink, "b");
    }

    // ------------------------------------------------------------------ Word Type

    static void WordType(RectTransform area, MiniGameBase game)
    {
        // WordTypeMiniGame writes the words (English in every language: the player types Latin letters)
        Glowing(Text(area, "WordDisplay", 0f, 300f, GW, 165f, "PROFIT", 150f, Yellow, bold: true, align: TextAlignmentOptions.Center, letterSpacingEm: 0.14f), glowYellow);
        Localize(Text(area, "TypeLabel", 340f, 482f, 700f, 30.8f, "TYPE IT HERE", 22f, Blue, align: TextAlignmentOptions.Center, letterSpacingEm: 0.1f),
            CrisisText.Table, "crisis.game.word_type.type_it_here");
        InputFrame(area, "InputFrame", 370f, 519f, 640f, 104f);
        InputBox(area, "InputField", 370f, 519f, 640f, 104f, 64f, Paper, false, 0.14f, TMP_InputField.ContentType.Standard, 12);
    }

    static void InputFrame(RectTransform area, string name, float x, float y, float w, float h)
    {
        Halo(area, name + "Glow", x, y, w, h, Lit(Blue, 0.45f), 22f, 12f);
        Shape(area, name + "Bg", x, y, w, h, UIColors.Over(Black, 0.55f, Ink)).Round(SmallX, SmallY);
        var inner = Shape(area, name + "Inner", x + 4f, y + 4f, w - 8f, h - 8f, Lit(Blue, 0.16f)).Round(SmallX - Vector4.one * 4f, SmallY - Vector4.one * 4f);
        inner.glow = -18f;
        var edge = Shape(area, name + "Edge", x, y, w, h, Blue).Round(SmallX, SmallY);
        edge.border = 4f;
    }

    // ------------------------------------------------------------------ Coin Flip

    static void CoinFlip(RectTransform area, MiniGameBase game)
    {
        var cf = (CoinFlipMiniGame)game;
        cf.headsColor = Yellow;
        cf.tailsColor = UIColors.Hex(0xD5DBE3);
        CallTile(area, "HeadsTile", 170f, 346f, "H", "HEADS", CardX, CardY, "crisis.game.coin_flip.heads");
        CallTile(area, "TailsTile", 930f, 346f, "T", "TAILS", CardX2, CardY2, "crisis.game.coin_flip.tails");
        // motion arcs while it spins (CoinFlipMiniGame turns them on)
        var spin = Node(area, "SpinLines", 0f, 0f, GW, GH);
        foreach (bool left in new[] { true, false })
        {
            float bx = left ? 528f : 786f;
            var outerArc = left ? Bezier(V(66, 12), V(28, 58), V(28, 162), V(66, 208)) : Bezier(V(14, 12), V(52, 58), V(52, 162), V(14, 208));
            var innerArc = left ? Bezier(V(44, 62), V(30, 90), V(30, 130), V(44, 158)) : Bezier(V(36, 62), V(50, 90), V(50, 130), V(36, 158));
            string side = left ? "Left" : "Right";
            Lines(spin, side + "Glow", bx, 326f, 80f, 220f, Lit(Yellow, 0.14f), 21f, outerArc, innerArc);
            Lines(spin, side, bx, 326f, 80f, 220f, Lit(Yellow, 0.55f), 7f, outerArc, innerArc);
        }
        spin.gameObject.SetActive(false);

        // the coin's edge (its thickness) behind it; scales with the spin around the coin's centre
        var coinEdge = Box(area, "CoinEdge", 640f, 306f, 124f, 260f, Circle, Lit(Yellow, 0.38f));
        coinEdge.type = Image.Type.Simple;
        KeepPivot(coinEdge.rectTransform, new Vector2((688f - 640f) / 124f, 0.5f));
        var coin = Box(area, "CoinImage", 626f, 306f, 124f, 260f, Circle, Yellow);
        coin.type = Image.Type.Simple;
        var c = coin.rectTransform;
        var coinGlow = StretchShape(c, "Glow", Lit(Yellow, 0.7f));
        coinGlow.kind = UIShape.Kind.Ellipse;
        coinGlow.glow = 34f;
        Oval(c, "Rim", 12f, 18f, 100f, 224f, UIColors.WithAlpha(Ink, UIColors.SrgbAlpha(0.3f))).border = 4f;
        var letter = Text(c, "Letter", 0f, 0f, 124f, 260f, "H", 120f, UIColors.WithAlpha(Ink, 0.78f), bold: true, align: TextAlignmentOptions.Center);
        letter.rectTransform.localScale = new Vector3(0.5f, 1f, 1f);
        // CoinFlipMiniGame writes the call lines (the coin's letter stays the key: H / T)
        Text(area, "ResultText", 0f, 588f, GW, 50.6f, "Make your call!", 44f, Paper, align: TextAlignmentOptions.Center);
    }

    /// <summary>A call tile: the key cap (a letter that stays), and the word under it (bound to wordKey in Crisis.csv).</summary>
    static void CallTile(RectTransform area, string name, float x, float y, string key, string word, Vector4 rx, Vector4 ry, string wordKey)
    {
        var tile = Node(area, name, x, y, 280f, 184f);
        var lit = Stretch(tile, "Lit");
        Halo(lit, "Glow", 0f, 0f, 280f, 184f, Lit(Yellow, 0.4f), 26f, 16f);
        Shape(lit, "Fill", 0f, 0f, 280f, 184f, Lit(Yellow, 0.12f)).Round(rx, ry);
        Shape(lit, "Edge", 0f, 0f, 280f, 184f, Yellow).Round(rx, ry).border = 4f;
        // keycap: 3 px edges, 9 px at the bottom
        Shape(lit, "KeyEdge", 98f, 23.3f, 84f, 84f, Black).Round(SmallX, SmallY);
        Shape(lit, "KeyFace", 101f, 26.3f, 78f, 72f, Yellow).Round(Inner(SmallX, 3f, 3f), Inner(SmallY, 3f, 9f));
        Text(lit, "Key", 98f, 26.3f, 84f, 72f, key, 48f, Ink, bold: true, align: TextAlignmentOptions.Center);
        Localize(Glowing(Text(lit, "Word", 0f, 119.3f, 280f, 41.4f, word, 36f, Yellow, bold: true, align: TextAlignmentOptions.Center, letterSpacingEm: 0.08f), glowYellow),
            CrisisText.Table, wordKey);
        lit.gameObject.SetActive(false);
        var idle = Stretch(tile, "Idle");
        Shape(idle, "Edge", 0f, 0f, 280f, 184f, Lit(Blue, 0.55f)).Round(rx, ry).border = 4f;
        var cap = Shape(idle, "KeyEdge", 98f, 23.3f, 84f, 84f, Blue).Round(SmallX, SmallY);
        cap.border = 3f;
        cap.borderBottom = 9f;
        Text(idle, "Key", 98f, 26.3f, 84f, 72f, key, 48f, Blue, bold: true, align: TextAlignmentOptions.Center);
        Localize(Text(idle, "Word", 0f, 119.3f, 280f, 41.4f, word, 36f, Blue, bold: true, align: TextAlignmentOptions.Center, letterSpacingEm: 0.08f),
            CrisisText.Table, wordKey);
    }

    // ------------------------------------------------------------------ Speed Count

    static void SpeedCount(RectTransform area, MiniGameBase game)
    {
        var sc = (SpeedCountMiniGame)game;
        LitFrame(area, "ItemFrame", 150f, 306f, 782f, 292f, Blue, 6f, 18f);
        Node(area, "ItemContainer", 156f, 312f, 770f, 280f);
        var template = Node(area, "ItemTemplate", 0f, 0f, 60f, 60f);
        Halo(template, "Glow", 0f, 0f, 60f, 60f, Lit(Yellow, 0.6f), 16f, 8f);
        Shape(template, "Box", 0f, 0f, 60f, 60f, Yellow, 8f);
        Box(template, "Tape", 24f, 0f, 12f, 60f, null, UIColors.Over(Ink, 0.22f, Yellow));   // a parcel: tape down the middle
        template.gameObject.SetActive(false);
        sc.itemPrefab = template.gameObject;
        sc.itemSize = new Vector2(60f, 60f);
        sc.itemColor = Yellow;

        Localize(Text(area, "HowMany", 990f, 343f, 240f, 30.8f, "HOW MANY?", 22f, Blue, align: TextAlignmentOptions.Center, letterSpacingEm: 0.1f),
            CrisisText.Table, "crisis.game.speed_count.how_many");
        InputFrame(area, "InputFrame", 1010f, 380f, 200f, 136f);
        InputBox(area, "InputField", 1010f, 380f, 200f, 136f, 96f, Paper, true, 0f, TMP_InputField.ContentType.IntegerNumber, 2);
        Text(area, "RangeText", 960f, 530f, 300f, 30.8f, "between 3 and 11", 22f, Blue, align: TextAlignmentOptions.Center);   // SpeedCountMiniGame writes it
    }

    // ==================================================================
    // Previews (the boards' moments)

    public static void RenderPreviews(GameObject hud, GameObject prefab, string dir, List<string> log)
    {
        const string crisis = "Server is crashing! Get systems back online!";
        UIPreview.Render(Path.Combine(dir, "crisis_warning.png"), log, new UIPreview.Layer(hud), new UIPreview.Layer(prefab, g =>
        {
            var v = Open(g, "StopTheBarPanel");
            v.ShowWarning(crisis, 2000f, -1000f);
            v.SetCountdown(2.6f, 3f);
        }));
        foreach (bool solved in new[] { true, false })
        {
            UIPreview.Render(Path.Combine(dir, solved ? "crisis_solved.png" : "crisis_failed.png"), log, new UIPreview.Layer(hud), new UIPreview.Layer(prefab, g =>
            {
                var v = Open(g, "StopTheBarPanel");
                v.ShowPlay(crisis, 2000f, -1000f);
                if (solved) v.ShowResult(true, "Servers are back online.", 2400f, "$2,000 × 1.2 · the bonus grows with every crisis", "SOLVED");
                else v.ShowResult(false, "The server is still on fire.", -1200f, "$1,000 × 1.2 · giving up costs the same", "TIME'S UP");
                v.SetClosesIn(3f);
            }));
        }

        var crises = new Dictionary<string, string>
        {
            { "ArrowTimingPanel", "Delivery truck broke down! Reroute the shipment!" }, { "StopTheBarPanel", crisis },
            { "ColorMatchPanel", "Tax deadline approaching! File the paperwork!" }, { "WhackAMolePanel", "Employee caught sleeping on the job! What do you do?" },
            { "SimonSaysPanel", "New hire is struggling with training! Help them out!" }, { "NumberSequencePanel", "Backup system failing! Save the data!" },
            { "KeypadCodePanel", "Security breach detected! Lock it down!" }, { "ButtonMashPanel", "Website traffic spike! Keep it running!" },
            { "MathBlitzPanel", "Invoice discrepancy found! Reconcile the accounts!" }, { "QuickTapPanel", "Critical bug found in production! Fix it fast!" },
            { "RapidKeysPanel", "Critical bug found in production! Fix it fast!" }, { "SequenceMemoryPanel", "Backup system failing! Save the data!" },
            { "PatternMatchPanel", "Delivery truck broke down! Reroute the shipment!" }, { "SafeCrackerPanel", "Security breach detected! Lock it down!" },
            { "OddOneOutPanel", "Expense report fraud suspected! Investigate!" }, { "CatchFallingPanel", "Backup system failing! Save the data!" },
            { "HigherLowerPanel", "Budget overrun detected! Cut costs now!" }, { "WordTypePanel", "Press asking for a statement! Craft a response!" },
            { "CoinFlipPanel", "An employee wants a raise or they're leaving! Decide now!" }, { "SpeedCountPanel", "Inventory system showing errors! Correct the data!" },
        };
        foreach (var spec in Specs)
        {
            var s = spec;
            UIPreview.Render(Path.Combine(dir, "crt_" + s.panel.Replace("Panel", "").ToLowerInvariant() + ".png"), log, new UIPreview.Layer(prefab, g =>
            {
                var v = Open(g, s.panel);
                v.ShowPlay(crises[s.panel], 2000f, -1000f);
                PreviewState(v.gameObject, v, s.panel);
            }));
        }
    }

    static CrtScreenView Open(GameObject root, string panel)
    {
        var p = root.transform.Find(panel).gameObject;
        p.SetActive(true);
        return p.GetComponent<CrtScreenView>();
    }

    static T Get<T>(GameObject panel, string path) where T : Component
    {
        var t = panel.transform.Find(path);
        return t != null ? t.GetComponent<T>() : null;
    }

    static void On(GameObject panel, string path, bool on)
    {
        var t = panel.transform.Find(path);
        if (t != null) t.gameObject.SetActive(on);
    }

    static void Typed(GameObject panel, string path, string text)
    {
        var input = Get<TMP_InputField>(panel, path);
        if (input == null) return;
        input.text = text;
        if (input.textComponent != null) input.textComponent.text = text;   // edit mode: the field doesn't redraw itself
    }

    static void PreviewState(GameObject p, CrtScreenView v, string panel)
    {
        switch (panel)
        {
            case "ArrowTimingPanel":
            {
                v.SetTime(7f, 30f); v.SetProgress(3, 5); v.SetStat("×1.3");
                var zone = Get<Image>(p, "GameArea/Dial/TargetZone");
                zone.fillAmount = 45f / 360f;
                zone.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -110f);
                Get<RectTransform>(p, "GameArea/Dial/ArrowPivot").localRotation = Quaternion.Euler(0f, 0f, -95f);
                var label = Get<RectTransform>(p, "GameArea/Dial/HitLabel");
                float mid = 132.5f * Mathf.Deg2Rad;
                label.pivot = new Vector2(0f, 0.5f);
                label.anchoredPosition = new Vector2(Mathf.Sin(mid), Mathf.Cos(mid)) * 200f;
                break;
            }
            case "StopTheBarPanel":
            {
                v.SetTime(8f, 60f); v.SetProgress(2, 3); v.SetStat("×1.5");
                var zone = Get<RectTransform>(p, "GameArea/Track/TargetZone");
                zone.anchoredPosition = new Vector2(190f, zone.anchoredPosition.y);
                break;
            }
            case "ColorMatchPanel":
            {
                v.SetTime(11f, 25f); v.SetProgress(9, 10); v.SetStat("1.0 s");
                Get<Image>(p, "GameArea/ColorDisplay").color = Red;
                var glow = Get<UIShape>(p, "GameArea/ColorGlow");
                glow.enabled = true;
                glow.color = Lit(Red, 0.65f);
                Get<Image>(p, "GameArea/WindowFill").fillAmount = 0.4f;
                Get<TMP_Text>(p, "GameArea/WindowText").text = "0.4 s";
                break;
            }
            case "WhackAMolePanel":
                v.SetTime(6f, 30f); v.SetProgress(11, 15); v.SetStat("×1.8");
                On(p, "GameArea/Hole5/Mole", true);
                break;
            case "SimonSaysPanel":
            {
                v.SetTime(10f, 60f); v.SetProgress(3, 5, 3); v.SetStat("3 / 4");
                Get<Image>(p, "GameArea/ColorButton1").color = Paper;
                On(p, "GameArea/ColorButton1/Glow", true);
                break;
            }
            case "NumberSequencePanel":
            {
                v.SetTime(8f, 60f); v.SetProgress(3, 5, 3); v.SetStat("6");
                On(p, "GameArea/Steps/Slot1/StepMemorize", false);
                On(p, "GameArea/Steps/Slot1/StepMemorizeDone", true);
                On(p, "GameArea/Steps/Slot2/StepTypeIdle", false);
                On(p, "GameArea/Steps/Slot2/StepType", true);
                string typed = "4917";
                for (int i = 0; i < 7; i++)
                {
                    string cell = "GameArea/Cells/Cell" + i;
                    On(p, cell, i < 6);
                    Get<TMP_Text>(p, cell + "/Digit").text = i < typed.Length ? typed[i].ToString() : "";
                    On(p, cell + "/Frame", i < typed.Length);
                    On(p, cell + "/FrameCaret", i == typed.Length);
                    On(p, cell + "/FrameEmpty", i > typed.Length);
                }
                break;
            }
            case "KeypadCodePanel":
            {
                v.SetTime(6f, 20f); v.SetProgress(2, 4, -1, true); v.SetStat("1", "TRIES LEFT");
                On(p, "GameArea/Keypad/NumButton3/Lit", true);
                On(p, "GameArea/CodeShown", false);
                On(p, "GameArea/CodeHidden", true);
                for (int i = 0; i < 4; i++)
                {
                    Get<TMP_Text>(p, "GameArea/Slots/Slot" + i + "/Char").text = i < 2 ? "73"[i].ToString() : "";
                    On(p, "GameArea/Slots/Slot" + i + "/Bar", i >= 2);
                    On(p, "GameArea/Slots/Slot" + i + "/Cursor", i == 2);
                }
                break;
            }
            case "ButtonMashPanel":
                v.SetTime(3f, 10f); v.SetProgress(31, 40); v.SetStat("9");
                Get<Image>(p, "GameArea/ProgressBar").fillAmount = 31f / 40f;
                break;
            case "MathBlitzPanel":
                v.SetTime(8f, 30f); v.SetProgress(5, 8);
                break;
            case "QuickTapPanel":
            {
                v.SetTime(8.6f, 20f); v.SetProgress(10, 12); v.SetStat("0.9 s");
                Get<RectTransform>(p, "GameArea/SpawnArea/TargetButton").anchoredPosition = new Vector2(160f, -3f);
                var last = Get<RectTransform>(p, "GameArea/SpawnArea/LastHit");
                last.gameObject.SetActive(true);
                last.anchoredPosition = new Vector2(-360f, 55f);
                break;
            }
            case "RapidKeysPanel":
                v.SetTime(6f, 20f); v.SetProgress(12, 15);
                On(p, "GameArea/KeyRow/Key_J/Lit", true);
                On(p, "GameArea/KeyRow/Key_J/Idle", false);
                break;
            case "SequenceMemoryPanel":
                v.SetTime(10f, 60f); v.SetProgress(3, 5);
                Get<Image>(p, "GameArea/GridButton4").color = Blue;
                On(p, "GameArea/GridButton4/Glow", true);
                Get<UIShape>(p, "GameArea/GridButton4/Frame").color = Blue;
                On(p, "GameArea/TurnPhase", true);
                On(p, "GameArea/WatchPhase/Done", true);
                p.transform.Find("GameArea/WatchPhase").GetComponent<CanvasGroup>().alpha = 0.6f;
                break;
            case "PatternMatchPanel":
            {
                v.SetTime(6f, 60f); v.SetProgress(2, 3); v.SetStat("6");
                float[] rot = { 90f, 0f, 0f, -90f };
                for (int i = 0; i < 6; i++)
                {
                    On(p, "GameArea/PatternSlots/Slot" + i + "/Mark", true);
                    On(p, "GameArea/PatternSlots/Slot" + i + "/Frame", false);
                    string s = "GameArea/InputSlots/Slot" + i;
                    bool done = i < 4;
                    On(p, s + "/Fill", done);
                    On(p, s + "/Frame", !done);
                    On(p, s + "/Arrow", done);
                    On(p, s + "/Cursor", i == 4);
                    if (done)
                    {
                        var a = Get<Image>(p, s + "/Arrow");
                        a.color = Ink;
                        a.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rot[i]);
                    }
                    if (i == 4) Get<UIShape>(p, s + "/Frame").color = Yellow;
                }
                break;
            }
            case "SafeCrackerPanel":
            {
                v.SetTime(7f, 30f); v.SetProgress(2, 3);
                Get<RectTransform>(p, "GameArea/Dial").localRotation = Quaternion.Euler(0f, 0f, 216f);
                Get<TMP_Text>(p, "GameArea/CurrentText").text = "6";
                Get<TMP_Text>(p, "GameArea/Dial/Numbers/N6/Label").color = Paper;
                On(p, "GameArea/TurnLeft/Lit", true);
                On(p, "GameArea/TurnLeft/Idle", false);
                string[] digits = { "2", "0", "7" };
                for (int i = 0; i < 3; i++)
                {
                    string c = "GameArea/FindCells/Cell" + i;
                    bool found = i < 2;
                    Get<TMP_Text>(p, c + "/Digit").text = digits[i];
                    Get<TMP_Text>(p, c + "/Digit").color = found ? Ink : Yellow;
                    On(p, c + "/Fill", found);
                    On(p, c + "/Check", found);
                    On(p, c + "/Frame", !found);
                    On(p, c + "/FrameIdle", false);
                    On(p, c + "/Underline", !found);
                }
                break;
            }
            case "OddOneOutPanel":
                v.SetTime(5f, 25f); v.SetProgress(4, 5);
                for (int i = 0; i < 9; i++) Get<Image>(p, "GameArea/ItemButton" + i).color = i == 2 ? Yellow : Blue;
                break;
            case "CatchFallingPanel":
                v.SetTime(5f, 25f); v.SetProgress(6, 10); v.SetLives(2, 3);
                Get<RectTransform>(p, "GameArea/Shaft/FallingObject").anchoredPosition = new Vector2(-100f, 30f);
                Get<RectTransform>(p, "GameArea/Shaft/Catcher").anchoredPosition = new Vector2(10f, -134f);
                On(p, "GameArea/Shaft/Catcher/SpeedLines", true);
                break;
            case "HigherLowerPanel":
                v.SetTime(9f, 30f); v.SetProgress(3, 5); v.SetLives(1, 2);
                break;
            case "WordTypePanel":
                v.SetTime(7f, 30f); v.SetProgress(4, 6);
                Typed(p, "GameArea/InputField", "PROF");
                break;
            case "CoinFlipPanel":
                v.SetTime(6f, 60f); v.SetProgress(2, 3); v.SetLives(1, 3);
                On(p, "GameArea/HeadsTile/Lit", true);
                On(p, "GameArea/HeadsTile/Idle", false);
                On(p, "GameArea/SpinLines", true);
                Get<TMP_Text>(p, "GameArea/ResultText").text = "You called <color=#FFE14D>HEADS</color>...";
                break;
            case "SpeedCountPanel":
            {
                v.SetTime(4f, 30f); v.SetProgress(3, 5); v.SetLives(2, 3);
                Typed(p, "GameArea/InputField", "9");
                var container = Get<RectTransform>(p, "GameArea/ItemContainer");
                var template = p.transform.Find("GameArea/ItemTemplate").gameObject;
                int[][] cells = { new[] { 1, 0, 3, -2 }, new[] { 4, 0, -4, 4 }, new[] { 8, 0, 2, 1 }, new[] { 3, 1, -1, 3 }, new[] { 10, 1, -3, -4 },
                    new[] { 0, 2, 4, 2 }, new[] { 6, 2, 1, -3 }, new[] { 2, 3, -2, 1 }, new[] { 7, 3, 4, -1 } };
                foreach (var c in cells)
                {
                    var item = UnityEngine.Object.Instantiate(template, container, false);
                    item.SetActive(true);
                    var rt = (RectTransform)item.transform;
                    rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                    rt.pivot = new Vector2(0f, 1f);
                    rt.anchoredPosition = new Vector2(c[0] * 70 + 5 + c[2], -(c[1] * 70 + 5 + c[3]));
                }
                break;
            }
        }
    }
}
