using System;
using System.Collections.Generic;

/// <summary>
/// Bare hands' poker (design canvas, 2026-11-24): every bill carries a playing card in its serial number (CashBill.card,
/// 0..51: rank = card / 4 with 0 = the 2 and 12 = the ace, suit = card % 4). Bare hands hold five bills; banking all
/// five at once scores them as a poker hand, and the hand's multiplier pays a bonus from the company account.
/// </summary>
public static class CashPoker
{
    public enum Rank
    {
        HighCard = 0,
        Pair,
        TwoPair,
        ThreeOfAKind,
        Straight,
        Flush,
        FullHouse,
        FourOfAKind,
        StraightFlush,
        RoyalFlush
    }

    public const int HandSize = 5;

    static readonly double[] Multipliers = { 1.0, 1.5, 2.0, 3.0, 4.0, 5.0, 7.0, 10.0, 20.0, 40.0 };
    static readonly string[] Ids = { "high_card", "pair", "two_pair", "three", "straight", "flush", "full_house", "four", "straight_flush", "royal" };
    static readonly string[] RankText = { "2", "3", "4", "5", "6", "7", "8", "9", "10", "J", "Q", "K", "A" };
    static readonly string[] SuitText = { "♠", "♥", "♦", "♣" };   // spades, hearts, diamonds, clubs

    /// <summary>What a hand pays in total (1 = no bonus).</summary>
    public static double Multiplier(Rank rank) => Multipliers[(int)rank];

    /// <summary>The hand's id for texts ("cash.poker." + id).</summary>
    public static string Id(Rank rank) => Ids[(int)rank];

    public static int CardRank(byte card) => (card % 52) / 4;
    public static int CardSuit(byte card) => (card % 52) % 4;
    public static bool IsRed(byte card) => CardSuit(card) == 1 || CardSuit(card) == 2;
    public static string CardLabel(byte card) => RankText[CardRank(card)] + SuitText[CardSuit(card)];

    static readonly int[] counts = new int[13];
    static readonly List<int> ranks = new List<int>(5);

    /// <summary>Score five cards (fewer score as high card at best: only a full hand counts).</summary>
    public static Rank Evaluate(IReadOnlyList<byte> cards)
    {
        if (cards == null || cards.Count < HandSize) return Rank.HighCard;
        Array.Clear(counts, 0, counts.Length);
        bool flush = true;
        int suit = CardSuit(cards[0]);
        for (int i = 0; i < HandSize; i++)
        {
            counts[CardRank(cards[i])]++;
            if (CardSuit(cards[i]) != suit) flush = false;
        }

        int pairs = 0, trips = 0, quads = 0, distinct = 0;
        ranks.Clear();
        for (int r = 0; r < 13; r++)
        {
            int c = counts[r];
            if (c == 0) continue;
            distinct++;
            ranks.Add(r);
            if (c == 2) pairs++;
            else if (c == 3) trips++;
            else if (c >= 4) quads++;
        }

        bool straight = false;
        bool aceHigh = false;
        if (distinct == HandSize)
        {
            if (ranks[4] - ranks[0] == 4)
            {
                straight = true;
                aceHigh = ranks[4] == 12;
            }
            else if (ranks[0] == 0 && ranks[1] == 1 && ranks[2] == 2 && ranks[3] == 3 && ranks[4] == 12)
            {
                straight = true;   // the wheel: A-2-3-4-5
            }
        }

        if (straight && flush) return aceHigh ? Rank.RoyalFlush : Rank.StraightFlush;
        if (quads > 0) return Rank.FourOfAKind;
        if (trips > 0 && pairs > 0) return Rank.FullHouse;
        if (flush) return Rank.Flush;
        if (straight) return Rank.Straight;
        if (trips > 0) return Rank.ThreeOfAKind;
        if (pairs >= 2) return Rank.TwoPair;
        if (pairs == 1) return Rank.Pair;
        return Rank.HighCard;
    }

    /// <summary>
    /// The best rank the cards held so far already make (for the HUD while the hand fills up): pairs, two pair, trips
    /// and quads count before the fifth card; straights and flushes only with all five.
    /// </summary>
    public static Rank Partial(IReadOnlyList<byte> cards)
    {
        if (cards == null || cards.Count == 0) return Rank.HighCard;
        if (cards.Count >= HandSize) return Evaluate(cards);
        Array.Clear(counts, 0, counts.Length);
        for (int i = 0; i < cards.Count; i++) counts[CardRank(cards[i])]++;
        int pairs = 0, trips = 0, quads = 0;
        for (int r = 0; r < 13; r++)
        {
            if (counts[r] == 2) pairs++;
            else if (counts[r] == 3) trips++;
            else if (counts[r] >= 4) quads++;
        }
        if (quads > 0) return Rank.FourOfAKind;
        if (trips > 0 && pairs > 0) return Rank.FullHouse;
        if (trips > 0) return Rank.ThreeOfAKind;
        if (pairs >= 2) return Rank.TwoPair;
        if (pairs == 1) return Rank.Pair;
        return Rank.HighCard;
    }
}
