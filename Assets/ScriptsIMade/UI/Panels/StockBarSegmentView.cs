using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One stock's part of the Owned tab's "Where your money is" bar.</summary>
public class StockBarSegmentView : MonoBehaviour
{
    public Image fill;
    [Tooltip("2 px ink line on the right edge (hidden on the last segment)")]
    public Graphic edge;
    public TMP_Text label;
}
