using TMPro;
using UnityEngine;

/// <summary>
/// One playing card of the casino blackjack board (150 x 210): paper, rank over suit in two corners (the bottom one
/// turned 180 degrees), a big suit in the middle, or its back (red with pale 45 degree stripes). Suits are drawn
/// (CasinoIcon). Cards are codes 0-51 like CasinoBlackjack: rank = code % 13 + 1, suit = code / 13.
/// Build one with CasinoUI.Card; a designed card can fill the references instead.
/// </summary>
[DisallowMultipleComponent]
public class CasinoCard : MonoBehaviour
{
    public GameObject face;
    public GameObject back;
    public RectTransform cornerTop;
    public RectTransform cornerBottom;
    public TMP_Text rankTop;
    public TMP_Text rankBottom;
    public CasinoIcon suitTop;
    public CasinoIcon suitBottom;
    public CasinoIcon suitCenter;
    [Tooltip("From the card's top-left (and bottom-right) corner to a corner group")]
    public Vector2 cornerInset = new Vector2(12f, 10f);
    public float cornerSuitSize = 24f;

    int shownCode = -1;
    bool shownFaceUp;

    public int Code => shownCode;
    public bool FaceUp => shownFaceUp;

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
        shownCode = code;
        shownFaceUp = faceUp;
        if (face != null && face.activeSelf != faceUp) face.SetActive(faceUp);
        if (back != null && back.activeSelf == faceUp) back.SetActive(!faceUp);
        if (!faceUp || code < 0) return;

        int rank = code % 13 + 1;
        int suit = Mathf.Clamp(code / 13, 0, 3);
        Color c = IsRed(suit) ? UIColors.Loss : UIColors.Ink;
        string label = RankLabel(rank);
        CasinoIconKind kind = CasinoIcon.Suit(suit);

        if (rankTop != null) { rankTop.text = label; rankTop.color = c; }
        if (rankBottom != null) { rankBottom.text = label; rankBottom.color = c; }
        foreach (CasinoIcon icon in new[] { suitTop, suitBottom, suitCenter })
        {
            if (icon == null) continue;
            icon.Kind = kind;
            icon.color = c;
        }

        // A corner group is as wide as its widest line ("10" is wider than the suit); its outer edge stays put
        float width = cornerSuitSize;
        if (rankTop != null) width = Mathf.Max(width, rankTop.GetPreferredValues(label).x);
        PlaceCorner(cornerTop, width, false);
        PlaceCorner(cornerBottom, width, true);
    }

    void PlaceCorner(RectTransform corner, float width, bool bottom)
    {
        if (corner == null) return;
        corner.sizeDelta = new Vector2(width, corner.sizeDelta.y);
        float half = corner.sizeDelta.y * 0.5f;
        if (bottom)
        {
            corner.anchorMin = corner.anchorMax = new Vector2(1f, 0f);
            corner.anchoredPosition = new Vector2(-(cornerInset.x + width * 0.5f), cornerInset.y + half);
        }
        else
        {
            corner.anchorMin = corner.anchorMax = new Vector2(0f, 1f);
            corner.anchoredPosition = new Vector2(cornerInset.x + width * 0.5f, -(cornerInset.y + half));
        }
    }
}
