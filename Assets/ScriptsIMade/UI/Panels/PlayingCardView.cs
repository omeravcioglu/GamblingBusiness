using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One playing card of the design (150 x 210, paper, rank + suit in two corners, big suit in the middle) or its
/// back (red hatched). Cards are codes 0-51 as in BlackjackGame: rank = code % 13 + 1, suit = code / 13
/// (0 spades, 1 hearts, 2 diamonds, 3 clubs).
/// </summary>
[DisallowMultipleComponent]
public class PlayingCardView : MonoBehaviour
{
    public GameObject face;
    public GameObject back;

    [Header("Corners (rank over suit; the bottom one is turned 180 degrees)")]
    public RectTransform cornerTop;
    public RectTransform cornerBottom;
    public TMP_Text rankTop;
    public TMP_Text rankBottom;
    public Image suitTop;
    public Image suitBottom;
    [Tooltip("From the card's edges to a corner group")]
    public Vector2 cornerInset = new Vector2(12f, 10f);
    public float cornerHeight = 56f;
    public float cornerSuitSize = 24f;

    [Header("Middle")]
    public Image suitCenter;
    [Tooltip("Filled suit shapes: spades, hearts, diamonds, clubs")]
    public Sprite[] suitSprites = new Sprite[4];

    public static string RankLabel(int rank)
    {
        switch (rank)
        {
            case 1: return "A";
            case 11: return "J";
            case 12: return "Q";
            case 13: return "K";
            default: return rank.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public static bool IsRed(int suit) => suit == 1 || suit == 2;

    public void Set(int code, bool faceUp)
    {
        if (face != null) face.SetActive(faceUp);
        if (back != null) back.SetActive(!faceUp);
        if (!faceUp) return;

        int rank = code % 13 + 1;
        int suit = Mathf.Clamp(code / 13, 0, 3);
        Color c = IsRed(suit) ? UIColors.Loss : UIColors.Ink;
        string label = RankLabel(rank);
        Sprite sprite = suitSprites != null && suit < suitSprites.Length ? suitSprites[suit] : null;

        if (rankTop != null) { rankTop.text = label; rankTop.color = c; }
        if (rankBottom != null) { rankBottom.text = label; rankBottom.color = c; }
        foreach (var img in new[] { suitTop, suitBottom, suitCenter })
        {
            if (img == null) continue;
            if (sprite != null) img.sprite = sprite;
            img.color = c;
        }

        // a corner group is as wide as its widest line ("10" is wider than the suit), its left edge stays put
        float w = cornerSuitSize;
        if (rankTop != null) w = Mathf.Max(w, rankTop.GetPreferredValues(label).x);
        PlaceCorner(cornerTop, w, false);
        PlaceCorner(cornerBottom, w, true);
    }

    void PlaceCorner(RectTransform corner, float width, bool bottom)
    {
        if (corner == null) return;
        corner.sizeDelta = new Vector2(width, cornerHeight);
        if (bottom)
        {
            corner.anchorMin = corner.anchorMax = new Vector2(1f, 0f);
            corner.anchoredPosition = new Vector2(-(cornerInset.x + width * 0.5f), cornerInset.y + cornerHeight * 0.5f);
        }
        else
        {
            corner.anchorMin = corner.anchorMax = new Vector2(0f, 1f);
            corner.anchoredPosition = new Vector2(cornerInset.x + width * 0.5f, -(cornerInset.y + cornerHeight * 0.5f));
        }
    }
}
