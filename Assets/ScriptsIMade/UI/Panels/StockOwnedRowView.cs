using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One row of the designed stock list's Owned tab: shares, value and profit.</summary>
public class StockOwnedRowView : MonoBehaviour
{
    public Button button;
    public Graphic band;
    public TMP_Text symbolText;
    public TMP_Text companyText;
    public RectTransform chip;
    public Image chipFill;
    public TMP_Text chipText;
    public TMP_Text sharesText;
    public TMP_Text valueText;
    public TMP_Text profitText;
    public TMP_Text percentText;

    [HideInInspector] public string symbol;
}
