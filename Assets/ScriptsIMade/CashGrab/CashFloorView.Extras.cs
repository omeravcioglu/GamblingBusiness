using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The design canvas's HUD pieces on the cash floor (2026-11-24): the boost banner (PAYDAY / JACKPOT / HAPPY HOUR), the
// hopper's reels, bare hands' fanned cards and their poker hand, the Written up stamp's slot and hint, golden and marked
// bills, written-up workers, and the feedback for all of it. The Count's card is CashCountView.
public partial class CashFloorView
{
    RectTransform feltHud;

    BlueprintBox boostBox;
    TextMeshProUGUI boostText;
    RectTransform boostFill;
    float boostPunch;
    CashBoost shownBoost = (CashBoost)(-1);

    BlueprintBox reelBox;
    readonly BlueprintBox[] reelWindows = new BlueprintBox[3];
    readonly TextMeshProUGUI[] reelTexts = new TextMeshProUGUI[3];
    TextMeshProUGUI reelHint;
    float reelFlash;

    readonly TextMeshProUGUI[] cardTexts = new TextMeshProUGUI[CashPoker.HandSize];
    TextMeshProUGUI handRankText;
    readonly List<byte> heldCards = new List<byte>(CashPoker.HandSize);

    TextMeshProUGUI stampHint;
    BlueprintBox stampSlot;
    Image stampShade;

    CashCountView countView;
    public CashCountView Count => countView;

    // The reels' symbols: 0 is the 7
    static readonly string[] ReelGlyphs = { "7", "BAR", "$", "♥", "♣", "♦" };

    void BuildExtras()
    {
        // A layer over the felt area for HUD pieces that keep their size whatever the zoom
        feltHud = BlueprintUI.NewRect("FeltHud", root);
        feltHud.anchorMin = feltArea.anchorMin;
        feltHud.anchorMax = feltArea.anchorMax;
        feltHud.offsetMin = feltArea.offsetMin;
        feltHud.offsetMax = feltArea.offsetMax;

        // Boost banner, top centre
        boostBox = BlueprintUI.Box(feltHud, "Boost", UIColors.Highlight, UIColors.Ink, 3f, BlueprintUI.RadiusLarge);
        boostBox.rectTransform.anchorMin = boostBox.rectTransform.anchorMax = new Vector2(0.5f, 1f);
        boostBox.rectTransform.pivot = new Vector2(0.5f, 1f);
        boostBox.rectTransform.anchoredPosition = new Vector2(0f, -6f);
        boostBox.rectTransform.sizeDelta = new Vector2(380f, 62f);
        boostBox.SetShadow(UIColors.Dark(0.5f), new Vector2(3f, -5f));
        boostText = Label(boostBox.transform, "Text", "", 30f, UIColors.Ink, TextAlignmentOptions.Center, true);
        BlueprintUI.Stretch(boostText.rectTransform, 10f, 2f, 10f, 14f);
        BlueprintUI.AutoSize(boostText, 16f);
        BlueprintBox track = BlueprintUI.Box(boostBox.transform, "Track", UIColors.WithAlpha(UIColors.Ink, 0.15f), Color.clear, 0f, BlueprintUI.Round(3f));
        track.rectTransform.anchorMin = new Vector2(0f, 0f);
        track.rectTransform.anchorMax = new Vector2(1f, 0f);
        track.rectTransform.pivot = new Vector2(0.5f, 0f);
        track.rectTransform.offsetMin = new Vector2(16f, 7f);
        track.rectTransform.offsetMax = new Vector2(-16f, 13f);
        boostFill = BlueprintUI.NewRect("Fill", track.transform);
        boostFill.anchorMin = Vector2.zero;
        boostFill.anchorMax = Vector2.one;
        boostFill.offsetMin = boostFill.offsetMax = Vector2.zero;
        boostFill.gameObject.AddComponent<Image>().color = UIColors.Ink;
        boostBox.gameObject.SetActive(false);

        // The hopper's reels, top right of the felt
        reelBox = BlueprintUI.Box(feltHud, "Reels", UIColors.Ink, UIColors.Ink, 3f, BlueprintUI.RadiusLarge);
        BlueprintUI.TopLeft(reelBox.rectTransform, 10f, 8f, 236f, 98f);   // top left: the tips sit top right
        reelBox.SetShadow(UIColors.Dark(0.5f), new Vector2(3f, -5f));
        for (int r = 0; r < 3; r++)
        {
            BlueprintBox window = BlueprintUI.Box(reelBox.transform, "Reel" + r, UIColors.PaperHi, UIColors.Gold, 2.5f, BlueprintUI.Round(8f));
            BlueprintUI.TopLeft(window.rectTransform, 12f + r * 72f, 10f, 64f, 54f);
            reelWindows[r] = window;
            TextMeshProUGUI glyph = Label(window.transform, "Glyph", "7", 32f, UIColors.Loss, TextAlignmentOptions.Center, true);
            BlueprintUI.Stretch(glyph.rectTransform, 2f, 0f, 2f, 0f);
            BlueprintUI.AutoSize(glyph, 14f);
            reelTexts[r] = glyph;
        }
        reelHint = Label(reelBox.transform, "Hint", "", 14f, UIColors.Gold, TextAlignmentOptions.Center, true);
        reelHint.rectTransform.anchorMin = new Vector2(0f, 0f);
        reelHint.rectTransform.anchorMax = new Vector2(1f, 0f);
        reelHint.rectTransform.pivot = new Vector2(0.5f, 0f);
        reelHint.rectTransform.offsetMin = new Vector2(6f, 5f);
        reelHint.rectTransform.offsetMax = new Vector2(-6f, 27f);
        BlueprintUI.AutoSize(reelHint, 10f);

        // Bare hands: the cards of the bills in the hand, and what they make
        for (int k = 0; k < cardTexts.Length; k++)
        {
            TextMeshProUGUI t = Label(textLayer, "Card" + k, "", 17f, UIColors.Ink, TextAlignmentOptions.Center, true);
            t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0f, 1f);
            t.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            t.rectTransform.sizeDelta = new Vector2(40f, 26f);
            t.gameObject.SetActive(false);
            cardTexts[k] = t;
        }
        handRankText = Label(textLayer, "HandRank", "", 20f, UIColors.Highlight, TextAlignmentOptions.Center, true);
        handRankText.rectTransform.anchorMin = handRankText.rectTransform.anchorMax = new Vector2(0f, 1f);
        handRankText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        handRankText.rectTransform.sizeDelta = new Vector2(320f, 30f);
        handRankText.outlineWidth = 0.2f;
        handRankText.outlineColor = new Color32(29, 36, 51, 255);
        handRankText.gameObject.SetActive(false);

        // The stamp's hint over a worker caught mid-foul
        stampHint = Label(textLayer, "StampHint", "", 16f, UIColors.PaperHi, TextAlignmentOptions.Center, true);
        stampHint.rectTransform.anchorMin = stampHint.rectTransform.anchorMax = new Vector2(0f, 1f);
        stampHint.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        stampHint.rectTransform.sizeDelta = new Vector2(300f, 26f);
        stampHint.outlineWidth = 0.22f;
        stampHint.outlineColor = new Color32(192, 53, 43, 255);
        stampHint.gameObject.SetActive(false);

        BuildStampSlot();
        countView = CashCountView.Create(this, system, feltHud, font);
    }

    /// <summary>The Written up stamp's slot in the toolbar, after the gloves: free from day 1, right mouse button.</summary>
    void BuildStampSlot()
    {
        if (glovesBadge == null) return;
        Transform strip = glovesBadge.transform.parent;
        stampSlot = BlueprintUI.Box(strip, "Stamp", UIColors.PaperHi, UIColors.Ink, 2.5f, BlueprintUI.RadiusSmall, true);
        BlueprintUI.Layout(stampSlot, 96f, 88f);
        stampSlot.rectTransform.SetSiblingIndex(glovesBadge.transform.GetSiblingIndex() + 1);
        Button button = BlueprintUI.MakeButton(stampSlot);
        button.onClick.AddListener(() =>
        {
            Vector2 at = new Vector2(system.Sim.Size.x * 0.5f, system.Sim.Size.y - 220f);
            Float(CashText.Get("cash.stamp.how"), at, UIColors.PaperHi, 22f, 2.2f);
        });

        BlueprintBox ink = BlueprintUI.Box(stampSlot.transform, "Ink", Color.clear, UIColors.Loss, 2.5f, BlueprintUI.Round(5f));
        BlueprintUI.Centered(ink.rectTransform, 78f, 44f, 0f, 8f);
        ink.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 7f);
        TextMeshProUGUI word = Label(ink.transform, "Label:cash.stamp.word", "", 14f, UIColors.Loss, TextAlignmentOptions.Center, true);
        BlueprintUI.Stretch(word.rectTransform, 3f, 2f, 3f, 2f);
        BlueprintUI.Wrap(word);
        BlueprintUI.AutoSize(word, 9f);
        word.lineSpacing = -28f;
        TextMeshProUGUI key = Label(stampSlot.transform, "Label:cash.stamp.key", "", 12f, UIColors.Pencil, TextAlignmentOptions.Center, true);
        key.rectTransform.anchorMin = new Vector2(0f, 0f);
        key.rectTransform.anchorMax = new Vector2(1f, 0f);
        key.rectTransform.pivot = new Vector2(0.5f, 0f);
        key.rectTransform.offsetMin = new Vector2(2f, 3f);
        key.rectTransform.offsetMax = new Vector2(-2f, 21f);
        BlueprintUI.AutoSize(key, 9f);

        RectTransform shade = BlueprintUI.NewRect("Cooldown", stampSlot.transform);
        shade.anchorMin = new Vector2(0f, 1f);
        shade.anchorMax = new Vector2(1f, 1f);
        shade.offsetMin = new Vector2(3f, 0f);
        shade.offsetMax = new Vector2(-3f, -3f);
        stampShade = shade.gameObject.AddComponent<Image>();
        stampShade.color = UIColors.Dark(0.55f);
        stampShade.raycastTarget = false;
        stampShade.enabled = false;
    }

    // ------------------------------------------------------------------
    // Per-frame HUD
    // ------------------------------------------------------------------

    void RefreshExtras(float dt)
    {
        CashFloorSim sim = system.Sim;

        // Boost banner
        CashBoost boost = sim.Boost;
        bool on = boost != CashBoost.None;
        if (boostBox.gameObject.activeSelf != on) boostBox.gameObject.SetActive(on);
        if (on)
        {
            if (boost != shownBoost)
            {
                shownBoost = boost;
                boostText.text = CashText.Get("cash.boost." + BoostId(boost), ("mult", MultText(CashRules.BoostMultiplier(boost))));
                boostBox.Fill = boost == CashBoost.Jackpot ? UIColors.Gold : boost == CashBoost.HappyHour ? UIColors.StickyPink : UIColors.Highlight;
            }
            float left = sim.BoostTotal > 0f ? Mathf.Clamp01(sim.BoostLeft / sim.BoostTotal) : 0f;
            Vector2 max = boostFill.anchorMax;
            if (Mathf.Abs(max.x - left) > 0.002f) boostFill.anchorMax = new Vector2(left, 1f);
            boostPunch = Mathf.Max(0f, boostPunch - dt * 3f);
            float wobble = 1f + boostPunch * 0.22f + Mathf.Sin(time * 9f) * 0.015f;
            BlueprintUI.SetLocalScale(boostBox.transform, new Vector3(wobble, wobble, 1f));
        }
        else
        {
            shownBoost = CashBoost.None;
        }

        // Reels: they spin for half a second after turning, then show where they stopped
        bool spinning = sim.ReelAge < 0.5f;
        reelFlash = Mathf.Max(0f, reelFlash - dt);
        for (int r = 0; r < 3; r++)
        {
            int symbol = spinning && sim.ReelAge < 0.18f + r * 0.12f ? (int)(time * 30f + r * 2) % CashRules.ReelSymbols : sim.Reels[r];
            string glyph = ReelGlyphs[Mathf.Clamp(symbol, 0, ReelGlyphs.Length - 1)];
            if (reelTexts[r].text != glyph) reelTexts[r].text = glyph;
            reelTexts[r].color = symbol == 0 ? UIColors.Loss : UIColors.Ink;
            bool seven = !spinning && sim.Reels[r] == 0;
            reelWindows[r].Fill = seven ? UIColors.Highlight : UIColors.PaperHi;
        }
        if (reelFlash > 0f) BlueprintUI.SetLocalScale(reelBox.transform, Vector3.one * (1f + reelFlash * 0.25f));
        else BlueprintUI.SetLocalScale(reelBox.transform, Vector3.one);
        string hint = sim.LuckyReels ? CashText.Get("cash.reels.lucky") : CashText.Get("cash.reels.hint");
        if (reelHint.text != hint) reelHint.text = hint;

        // Bare hands: the cards fanned over the hand, and the hand they make
        bool cards = sim.tool == CashTool.Hands && sim.Held.Count > 0;
        if (cards) sim.HeldCards(heldCards);
        else heldCards.Clear();
        for (int k = 0; k < cardTexts.Length; k++)
        {
            bool show = k < heldCards.Count;
            if (cardTexts[k].gameObject.activeSelf != show) cardTexts[k].gameObject.SetActive(show);
            if (!show) continue;
            byte card = heldCards[k];
            string label = CashPoker.CardLabel(card);
            if (cardTexts[k].text != label) cardTexts[k].text = label;
            cardTexts[k].color = CashPoker.IsRed(card) ? UIColors.Loss : UIColors.Ink;
            cardTexts[k].rectTransform.anchoredPosition = TextPos(CardSpot(sim, k) + new Vector2(0f, -12f));
            cardTexts[k].fontSize = 17f * zoomText;
        }
        bool rankOn = cards;
        if (handRankText.gameObject.activeSelf != rankOn) handRankText.gameObject.SetActive(rankOn);
        if (rankOn)
        {
            CashPoker.Rank rank = sim.HeldRank();
            string text = sim.Held.Count < CashPoker.HandSize
                ? (rank > CashPoker.Rank.HighCard
                    ? CashText.Get("cash.poker.holding", ("hand", CashText.Get("cash.poker." + CashPoker.Id(rank))), ("count", sim.Held.Count), ("max", CashPoker.HandSize))
                    : CashText.Get("cash.poker.filling", ("count", sim.Held.Count), ("max", CashPoker.HandSize)))
                : CashText.Get("cash.poker.ready", ("hand", CashText.Get("cash.poker." + CashPoker.Id(rank))), ("mult", MultText(CashPoker.Multiplier(rank))));
            if (handRankText.text != text) handRankText.text = text;
            handRankText.color = rank > CashPoker.Rank.HighCard ? UIColors.Highlight : UIColors.PaperHi;
            handRankText.rectTransform.anchoredPosition = TextPos(sim.HandPos + new Vector2(0f, -132f));
            handRankText.fontSize = 20f * zoomText;
        }

        // The stamp's hint: over the worker under the cursor when they're fouling
        CashFloorSim.Agent target = sim.Input.onFloor ? HoveredAgent(sim) : null;
        bool stampable = target != null && sim.IsFouling(target);
        if (stampHint.gameObject.activeSelf != stampable) stampHint.gameObject.SetActive(stampable);
        if (stampable)
        {
            string s = sim.StampCooldownLeft > 0f ? CashText.Get("cash.stamp.inking") : CashText.Get("cash.stamp.hint");
            if (stampHint.text != s) stampHint.text = s;
            stampHint.rectTransform.anchoredPosition = TextPos(target.pos + new Vector2(0f, -132f));
            stampHint.fontSize = 16f * zoomText;
        }

        // Stamp slot cooldown
        if (stampShade != null)
        {
            float shade = Mathf.Clamp01(sim.StampCooldownLeft / CashRules.StampCooldown);
            if (stampShade.enabled != shade > 0.001f) stampShade.enabled = shade > 0.001f;
            RectTransform cd = stampShade.rectTransform;
            float wanted = 1f - shade;
            if (Mathf.Abs(cd.anchorMin.y - wanted) > 0.002f) cd.anchorMin = new Vector2(cd.anchorMin.x, wanted);
        }
    }

    CashFloorSim.Agent HoveredAgent(CashFloorSim sim)
    {
        CashFloorSim.Agent best = null;
        float bestD = 56f * 56f;
        foreach (CashFloorSim.Agent a in sim.agents)
        {
            if (!a.active) continue;
            float d = (a.pos + new Vector2(0f, -34f) - sim.Cursor).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = a;
            }
        }
        return best;
    }

    /// <summary>Where the k-th card of the bare hand is drawn (world units: a fan above the hand).</summary>
    static Vector2 CardSpot(CashFloorSim sim, int k)
    {
        int n = Mathf.Max(1, sim.Held.Count);
        float spread = 36f;
        float x = (k - (n - 1) * 0.5f) * spread;
        return sim.HandPos + new Vector2(x, -64f + Mathf.Abs(k - (n - 1) * 0.5f) * 4f);
    }

    static string BoostId(CashBoost boost)
    {
        switch (boost)
        {
            case CashBoost.Jackpot: return "jackpot";
            case CashBoost.HappyHour: return "happy_hour";
            default: return "payday";
        }
    }

    /// <summary>"×1.5", "×2" (the multiplication sign is in Coming Soon).</summary>
    public static string MultText(double mult)
    {
        return "×" + mult.ToString(mult % 1.0 == 0.0 ? "0" : "0.#", System.Globalization.CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------
    // Drawing (inside the batch)
    // ------------------------------------------------------------------

    /// <summary>Under the bills and workers: golden bills' glow, the ring under a worker caught mid-foul.</summary>
    void DrawExtrasUnder(CashFloorSim sim, CashAtlas atlas)
    {
        CashBill[] bills = sim.Bills;
        CashAtlas.Sprite dot = atlas.Get("soft_dot");
        for (int i = 0; i < bills.Length; i++)
        {
            ref CashBill b = ref bills[i];
            if (b.state != BillState.Free || b.special != CashSpecial.Golden) continue;
            float pulse = 0.55f + Mathf.Sin(time * 6f + b.phase) * 0.2f;
            Vector2 at = W2L(b.pos + new Vector2(0f, -Mathf.Max(0f, b.z) * 0.55f));
            batch.Sprite(dot, at, new Vector2(150f, 110f) * zoom, 0f, new Color(1f, 0.85f, 0.3f, 0.45f * pulse));
            batch.Ring(at, (58f + Mathf.Sin(time * 4f + b.phase) * 6f) * zoom, 3f * zoom, new Color(1f, 0.88f, 0.4f, 0.8f * pulse));
        }

        foreach (CashFloorSim.Agent a in sim.agents)
        {
            if (!a.active || !sim.IsFouling(a)) continue;
            float pulse = 0.7f + Mathf.Sin(time * 12f) * 0.3f;
            Color red = UIColors.Loss;
            batch.Ring(W2L(a.pos), 34f * zoom, 4f * zoom, new Color(red.r, red.g, red.b, pulse));
        }
    }

    /// <summary>Over everything: sparkles on golden bills, the marked bills' dye, written-up slips, the bare hand's cards.</summary>
    void DrawExtrasOver(CashFloorSim sim, CashAtlas atlas)
    {
        CashBill[] bills = sim.Bills;
        CashAtlas.Sprite sparkle = atlas.Get("sparkle");
        for (int i = 0; i < bills.Length; i++)
        {
            ref CashBill b = ref bills[i];
            if (b.state == BillState.Inactive || b.special == CashSpecial.None) continue;
            Vector2 at = W2L(b.pos + new Vector2(0f, -Mathf.Max(0f, b.z) * 0.55f));
            if (b.special == CashSpecial.Golden)
            {
                float t = Mathf.Repeat(time * 1.3f + b.phase, 1f);
                float s = Mathf.Sin(t * Mathf.PI) * 22f * zoom;
                Vector2 off = new Vector2(Mathf.Cos(b.phase * 3f) * 30f, Mathf.Sin(b.phase * 5f) * 14f) * zoom;
                batch.Sprite(sparkle, at + off, new Vector2(s, s), time * 90f, new Color(1f, 0.97f, 0.75f, 1f));
            }
            else
            {
                // A red dye splotch on the corner
                Vector2 corner = at + new Vector2(Mathf.Cos(-b.angle * Mathf.Deg2Rad) * 34f, Mathf.Sin(-b.angle * Mathf.Deg2Rad) * 34f) * zoom;
                batch.Circle(corner, 9f * zoom, UIColors.Ink, 10);
                batch.Circle(corner, 7f * zoom, UIColors.Loss, 10);
            }
        }

        // Written-up workers wear the slip over their heads while they sit out
        foreach (CashFloorSim.Agent a in sim.agents)
        {
            if (!a.active || a.writtenUp <= 0f) continue;
            Vector2 head = W2L(a.pos) + new Vector2(0f, 120f * zoom);
            float tilt = -8f + Mathf.Sin(time * 3f + a.id) * 3f;
            batch.Box(head, new Vector2(84f, 30f) * zoom, tilt, UIColors.Loss);
            batch.Box(head, new Vector2(78f, 24f) * zoom, tilt, UIColors.PaperHi);
            float left = Mathf.Clamp01(a.writtenUp / CashRules.StampSitOut);
            batch.Box(head + new Vector2(-39f * (1f - left), -10f) * zoom, new Vector2(78f * left, 4f) * zoom, tilt, UIColors.Loss);
        }

        // Bare hands: the cards, fanned over the hand
        if (sim.tool == CashTool.Hands && sim.Held.Count > 0)
        {
            for (int k = 0; k < sim.Held.Count && k < CashPoker.HandSize; k++)
            {
                Vector2 c = W2L(CardSpot(sim, k));
                float tilt = (k - (sim.Held.Count - 1) * 0.5f) * -7f;
                batch.Box(c + new Vector2(3f, -4f) * zoom, new Vector2(40f, 56f) * zoom, tilt, new Color(0f, 0f, 0f, 0.3f));
                batch.Box(c, new Vector2(40f, 56f) * zoom, tilt, UIColors.Ink);
                batch.Box(c, new Vector2(35f, 51f) * zoom, tilt, UIColors.PaperHi);
            }
        }
    }

    // ------------------------------------------------------------------
    // Events → feedback (the new event types)
    // ------------------------------------------------------------------

    /// <summary>Feedback for the design canvas's events; false for the older ones (Handle deals with those).</summary>
    bool HandleExtra(CashEvent e)
    {
        CashFloorSim sim = system.Sim;
        switch (e.type)
        {
            case CashEventType.PokerHand:
            {
                var rank = (CashPoker.Rank)e.tier;
                string name = CashText.Get("cash.poker." + CashPoker.Id(rank));
                Float(CashText.Get("cash.fx.poker", ("hand", name), ("mult", MultText(e.count / 10.0))), sim.SafeCenter + new Vector2(0f, -250f),
                      UIColors.Highlight, 34f + e.tier * 2f, 1.9f);
                if (e.value > 0.0) Float("+" + CashText.Compact(e.value), sim.SafeCenter + new Vector2(0f, -205f), UIColors.Gold, 30f, 1.6f);
                if (rank >= CashPoker.Rank.Straight)
                {
                    Confetti(sim.SafeCenter + new Vector2(0f, -80f), 40 + e.tier * 8, 560f);
                    shake = Mathf.Max(shake, 6f + e.tier);
                }
                else
                {
                    Emit(sim.SafeCenter + new Vector2(0f, -60f), 10 + e.tier * 4, 2, UIColors.Gold, 380f, 0.9f, 18f, 800f);
                }
                coach?.OnPokerHand();
                return true;
            }
            case CashEventType.BonusPaid:
            {
                var sources = (CashBonusSource)e.tier;
                string why = (sources & CashBonusSource.Golden) != 0 ? CashText.Get("cash.fx.bonus.golden")
                           : (sources & CashBonusSource.Marked) != 0 ? CashText.Get("cash.fx.bonus.marked")
                           : CashText.Get("cash.fx.bonus.boost", ("mult", MultText(sim.BoostMultiplier)));
                Float("", sim.SafeCenter + new Vector2(70f, -140f), UIColors.Gold, 26f, 1.4f, 2, e.value, v => "+" + CashText.Compact(v) + " " + why);
                if ((sources & CashBonusSource.Golden) != 0)
                {
                    Emit(sim.SafeCenter + new Vector2(0f, -50f), 18, 0, UIColors.Highlight, 360f, 0.7f, 20f);
                    shake = Mathf.Max(shake, 5f);
                }
                return true;
            }
            case CashEventType.BoostStarted:
            {
                var boost = (CashBoost)e.tier;
                boostPunch = 1f;
                shownBoost = (CashBoost)(-1);
                Float(CashText.Get("cash.boost." + BoostId(boost), ("mult", MultText(e.value))) + "!", new Vector2(sim.Size.x * 0.5f, sim.Size.y * 0.36f),
                      boost == CashBoost.HappyHour ? UIColors.StickyPink : UIColors.Highlight, 50f, 1.8f);
                if (boost == CashBoost.Jackpot)
                {
                    reelFlash = 0.6f;
                    foreach (Vector2 m in sim.TubeMouths) Emit(m + new Vector2(0f, 20f), 18, 2, UIColors.Gold, 420f, 1.4f, 20f, 800f);
                    Confetti(new Vector2(sim.Size.x * 0.5f, sim.Size.y * 0.3f), 80, 640f);
                    shake = Mathf.Max(shake, 12f);
                }
                else
                {
                    Confetti(new Vector2(sim.Size.x * 0.5f, sim.Size.y * 0.3f), 30, 420f);
                    shake = Mathf.Max(shake, 5f);
                }
                coach?.OnBoost(boost);
                return true;
            }
            case CashEventType.BoostEnded:
            case CashEventType.ReelSpin:
                return true;
            case CashEventType.SpecialSpawned:
                if ((CashSpecial)e.tier == CashSpecial.Golden)
                {
                    Emit(e.pos, 10, 0, UIColors.Highlight, 220f, 0.5f, 16f);
                    Float(CashText.Get("cash.fx.golden"), e.pos + new Vector2(0f, 60f), UIColors.Highlight, 24f, 1.1f);
                    coach?.OnGolden();
                }
                return true;
            case CashEventType.MarkedBanked:
                Float("", sim.SafeCenter + new Vector2(-90f, -150f), UIColors.Loss, 22f, 1.2f, 3, CashRules.MarkedHeat * e.count,
                      v => CashText.Get("cash.fx.heat", ("points", v.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))));
                coach?.OnMarked();
                return true;
            case CashEventType.WrittenUp:
            {
                int d = Mathf.Clamp(e.dept, 0, 2);
                Float(CashText.Get("cash.fx.written_up"), e.pos + new Vector2(0f, -150f), UIColors.Loss, 34f, 1.6f);
                if (e.value > 0.0)
                    Float(CashText.Get("cash.fx.fine", ("amount", CashText.Compact(e.value)), ("dept", CashText.DeptName(d))), e.pos + new Vector2(0f, -112f), UIColors.Gold, 24f, 1.6f);
                Emit(e.pos + new Vector2(0f, -60f), 16, 0, UIColors.Loss, 280f, 0.5f, 18f);
                shake = Mathf.Max(shake, 7f);
                return true;
            }
            case CashEventType.StampMiss:
                Float(CashText.Get(e.tier == 1 ? "cash.fx.stamp_fair" : "cash.fx.stamp_wait"), e.pos + new Vector2(0f, -130f), UIColors.PaperHi, 20f, 1f);
                return true;
        }
        return false;
    }
}
