using System;
using System.Collections.Generic;
using UnityEngine;

// The design canvas's extras (2026-11-24): poker hands for bare hands, golden and marked bills, the hopper's reels and the
// PAYDAY / JACKPOT / HAPPY HOUR boosts, the Written up stamp, and the knobs the Count's perks and hiring turn. Every bonus
// is money moved from the company account into Player Cash through the sink (CashLedger.PayBonus): nothing here makes
// money, so the floor's books still add up.
public sealed partial class CashFloorSim
{
    // ---- Knobs CashGrabSystem sets ----
    /// <summary>Money a second arriving on the floor now (bonuses with a "seconds of the Drop" floor use it).</summary>
    public double DropRate;
    /// <summary>Golden bills can come out of the tubes.</summary>
    public bool SpecialsEnabled = true;
    /// <summary>Marked bills can come out of the tubes (from CashRules.MarkedFromDay).</summary>
    public bool MarkedEnabled;
    /// <summary>The Lucky reels perk: the first reel always stops on the 7.</summary>
    public bool LuckyReels;
    /// <summary>Each department's speed factor (hires, the Long lunch perk, last Count's winner).</summary>
    public readonly float[] DeptSpeed = { 1f, 1f, 1f };
    /// <summary>The CEO's reach factor (interns).</summary>
    public float ReachBonus = 1f;
    public float SafeWiden { get; private set; } = 1f;

    // ---- Boost ----
    public CashBoost Boost { get; private set; }
    public float BoostLeft { get; private set; }
    public float BoostTotal { get; private set; }
    public double BoostMultiplier => Boost == CashBoost.None ? 1.0 : CashRules.BoostMultiplier(Boost);

    // ---- Reels (0 = the 7) ----
    public readonly int[] Reels = { 1, 3, 5 };
    /// <summary>Seconds since the reels last turned (the view spins them for a moment).</summary>
    public float ReelAge { get; private set; } = 99f;
    int reelCounter;

    // ---- Stamp ----
    public float StampCooldownLeft { get; private set; }

    // ---- Bonuses waiting to be paid ----
    double looseBonus;
    CashBonusSource looseSources;
    float looseTimer;

    /// <summary>The cards in the CEO's bare hands (the HUD fans them out).</summary>
    public void HeldCards(List<byte> into)
    {
        into.Clear();
        foreach (int i in held) into.Add(Bills[i].card);
    }

    readonly List<byte> cardScratch = new List<byte>(8);

    /// <summary>What the bare hand would score if banked now (Partial: pairs show before the hand is full).</summary>
    public CashPoker.Rank HeldRank()
    {
        if (tool != CashTool.Hands || held.Count == 0) return CashPoker.Rank.HighCard;
        HeldCards(cardScratch);
        return CashPoker.Partial(cardScratch);
    }

    // ------------------------------------------------------------------
    // Step
    // ------------------------------------------------------------------

    void StepBonus(float dt)
    {
        ReelAge += dt;
        StampCooldownLeft = Mathf.Max(0f, StampCooldownLeft - dt);

        if (Boost != CashBoost.None)
        {
            BoostLeft -= dt;
            if (BoostLeft <= 0f)
            {
                CashBoost ended = Boost;
                Boost = CashBoost.None;
                BoostLeft = 0f;
                Events.Add(new CashEvent(CashEventType.BoostEnded, new Vector2(Size.x * 0.5f, 60f), 0.0, 0, -1, (int)ended));
            }
        }

        if (looseBonus > 0.0)
        {
            looseTimer -= dt;
            if (looseTimer <= 0f) FlushLooseBonus();
        }
    }

    void FlushLooseBonus()
    {
        double want = looseBonus;
        CashBonusSource sources = looseSources;
        looseBonus = 0.0;
        looseSources = CashBonusSource.None;
        double paid = Sink != null ? Sink.PayBonus(want) : 0.0;
        if (paid > 0.0) Events.Add(new CashEvent(CashEventType.BonusPaid, SafeCenter, paid, 0, -1, (int)sources));
    }

    // ------------------------------------------------------------------
    // Boosts
    // ------------------------------------------------------------------

    /// <summary>Start a boost. A weaker one doesn't cut a stronger one short. Returns true when it started.</summary>
    public bool StartBoost(CashBoost kind)
    {
        if (kind == CashBoost.None) return false;
        if (Boost != CashBoost.None && BoostLeft > 1f && CashRules.BoostMultiplier(Boost) > CashRules.BoostMultiplier(kind)) return false;
        Boost = kind;
        BoostTotal = CashRules.BoostSeconds(kind);
        BoostLeft = BoostTotal;
        Events.Add(new CashEvent(CashEventType.BoostStarted, new Vector2(Size.x * 0.5f, Size.y * 0.3f), CashRules.BoostMultiplier(kind), 0, -1, (int)kind));
        return true;
    }

    /// <summary>A loaded save: no boost carries over.</summary>
    public void ClearBoost()
    {
        Boost = CashBoost.None;
        BoostLeft = 0f;
    }

    // ------------------------------------------------------------------
    // Tubes: special bills and the reels
    // ------------------------------------------------------------------

    void OnTubeBill(int i, bool allowSpecial)
    {
        ref CashBill b = ref Bills[i];
        if (allowSpecial && SpecialsEnabled)
        {
            if (Chance(CashRules.GoldenChance)) b.special = CashSpecial.Golden;
            else if (MarkedEnabled && Chance(CashRules.MarkedChance)) b.special = CashSpecial.Marked;
            if (b.special != CashSpecial.None)
                Events.Add(new CashEvent(CashEventType.SpecialSpawned, b.pos, b.value, 0, -1, (int)b.special));
        }

        // The reels turn every few bills, only while the CEO watches (a jackpot nobody sees is no fun)
        if (!Visible) return;
        if (++reelCounter < CashRules.ReelEvery) return;
        reelCounter = 0;
        SpinReels();
    }

    void SpinReels()
    {
        for (int r = 0; r < Reels.Length; r++)
            Reels[r] = Chance(CashRules.SevenChance) ? 0 : 1 + rng.Next(CashRules.ReelSymbols - 1);
        if (LuckyReels) Reels[0] = 0;
        ReelAge = 0f;
        Events.Add(new CashEvent(CashEventType.ReelSpin, new Vector2(Size.x * 0.5f, 40f), 0.0, Reels[0] * 100 + Reels[1] * 10 + Reels[2]));
        if (Reels[0] == 0 && Reels[1] == 0 && Reels[2] == 0) StartBoost(CashBoost.Jackpot);
    }

    // ------------------------------------------------------------------
    // Banking: what a bill that reached the safe earns on top
    // ------------------------------------------------------------------

    /// <summary>A bill landed in the safe: its bonus (boost, golden, marked) goes on its batch or the loose pile.</summary>
    void AccrueBonus(double value, CashSpecial special, int batch)
    {
        double extra = 0.0;
        CashBonusSource sources = CashBonusSource.None;
        double boost = BoostMultiplier;
        if (boost > 1.0)
        {
            extra += value * (boost - 1.0);
            sources |= CashBonusSource.Boost;
        }
        if (special == CashSpecial.Golden)
        {
            extra += Math.Max(value * (CashRules.GoldenMultiplier - 1.0), DropRate * CashRules.GoldenBaseSeconds);
            sources |= CashBonusSource.Golden;
        }
        else if (special == CashSpecial.Marked)
        {
            extra += value * (CashRules.MarkedMultiplier - 1.0);
            sources |= CashBonusSource.Marked;
            Events.Add(new CashEvent(CashEventType.MarkedBanked, SafeCenter, value, 1));
        }
        if (extra <= 0.0) return;

        if (batch >= 0 && batches.TryGetValue(batch, out Batch bt))
        {
            bt.bonus += extra;
            bt.sources |= sources;
            batches[batch] = bt;
            return;
        }
        looseBonus += extra;
        looseSources |= sources;
        if (looseTimer <= 0f) looseTimer = 0.35f;
    }

    /// <summary>A deposit batch finished landing: pay its poker hand and its bonuses.</summary>
    void SettleBatchBonus(Batch b)
    {
        if (b.poker > (int)CashPoker.Rank.HighCard)
        {
            var rank = (CashPoker.Rank)b.poker;
            double mult = CashPoker.Multiplier(rank);
            double want = (mult - 1.0) * Math.Max(b.handValue, DropRate * CashRules.PokerBaseSeconds);
            double paid = Sink != null ? Sink.PayBonus(want) : 0.0;
            Events.Add(new CashEvent(CashEventType.PokerHand, SafeCenter, paid, (int)Math.Round(mult * 10.0), -1, b.poker));
        }
        if (b.bonus > 0.0)
        {
            double paid = Sink != null ? Sink.PayBonus(b.bonus) : 0.0;
            if (paid > 0.0) Events.Add(new CashEvent(CashEventType.BonusPaid, SafeCenter, paid, b.count, -1, (int)b.sources));
        }
    }

    /// <summary>Bare hands bank a full hand: score it now (the cards are known) and keep it on the batch.</summary>
    void ScoreHand(ref Batch batch)
    {
        if (tool != CashTool.Hands || held.Count < CashPoker.HandSize) return;
        cardScratch.Clear();
        double value = 0.0;
        for (int k = 0; k < CashPoker.HandSize; k++)
        {
            cardScratch.Add(Bills[held[k]].card);
            value += Bills[held[k]].value;
        }
        batch.poker = (int)CashPoker.Evaluate(cardScratch);
        batch.handValue = value;
    }

    // ------------------------------------------------------------------
    // The Written up stamp (right mouse button on a worker caught mid-foul)
    // ------------------------------------------------------------------

    /// <summary>This worker is up to something right now: about to interfere, dashing at the hand, or their department's trap is out.</summary>
    public bool IsFouling(Agent a)
    {
        if (a == null || !a.active || a.stun > 0f) return false;
        if (a.state == AgentState.Telegraph || a.state == AgentState.Dash) return true;
        foreach (Hazard h in hazards)
            if (h.dept == a.dept && h.kind != HazardKind.Paperweight) return true;
        return false;
    }

    /// <summary>
    /// The right mouse button over a worker: a foul gets stamped (sit-out and a fine), anything else is a miss when no
    /// gadget is selected. Returns true when the click was used (the gadget doesn't fire).
    /// </summary>
    bool TryStamp()
    {
        Agent a = AgentAt(Cursor, 56f);
        if (a == null) return false;
        bool fouling = IsFouling(a);
        bool hasGadget = gadget != CashGadget.None && gadget != CashGadget.Gloves && gadgetLevel[(int)gadget] > 0;
        if (!fouling)
        {
            if (hasGadget) return false;
            Events.Add(new CashEvent(CashEventType.StampMiss, a.pos, 0.0, 0, a.dept, 1));
            StampCooldownLeft = Mathf.Max(StampCooldownLeft, CashRules.StampMissCooldown);
            return true;
        }
        if (StampCooldownLeft > 0f)
        {
            if (hasGadget) return false;
            Events.Add(new CashEvent(CashEventType.StampMiss, a.pos, 0.0, 0, a.dept, 0));
            return true;
        }

        // Their trap goes too
        for (int k = hazards.Count - 1; k >= 0; k--)
        {
            Hazard h = hazards[k];
            if (h.dept != a.dept || h.kind == HazardKind.Paperweight) continue;
            hazards.RemoveAt(k);
            Vector2 at = h.kind == HazardKind.Firewall || h.kind == HazardKind.Popup ? h.rect.center : h.center;
            Events.Add(new CashEvent(CashEventType.InterferenceEnd, at, 0.0, 0, h.dept, (int)h.kind));
        }
        double fine = Sink != null ? Sink.FineDept(a.dept, CashRules.StampFineShare) : 0.0;
        Stun(a, CashRules.StampSitOut);
        a.writtenUp = CashRules.StampSitOut;
        StampCooldownLeft = CashRules.StampCooldown;
        Events.Add(new CashEvent(CashEventType.WrittenUp, a.pos, fine, 0, a.dept));
        return true;
    }

    // ------------------------------------------------------------------
    // Perks
    // ------------------------------------------------------------------

    /// <summary>The Wide safe perk: deposit reach and the safe's mouth grow (1 = normal).</summary>
    public void SetSafeWiden(float factor)
    {
        factor = Mathf.Clamp(factor, 1f, 2f);
        if (Mathf.Approximately(factor, SafeWiden)) return;
        SafeWiden = factor;
        PlaceSafe();
    }

    void PlaceSafe()
    {
        float w = 208f * SafeWiden, h = 160f;
        SafeRect = new Rect(SafeCenter.x - w * 0.5f, SafeCenter.y - 78f, w, h);
        DepositRadius = 165f * SafeWiden;
    }
}
