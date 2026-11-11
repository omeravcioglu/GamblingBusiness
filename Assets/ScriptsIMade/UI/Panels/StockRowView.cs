using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One row of the designed stock list (StockMarketView's All tab), built by StockMarketBuilder.</summary>
public class StockRowView : MonoBehaviour
{
    public Button button;
    [Tooltip("Highlighter band behind the selected row")]
    public Graphic band;
    public TMP_Text symbolText;
    public TMP_Text companyText;
    [Tooltip("Type chip, placed 8 px after the company name")]
    public RectTransform chip;
    public Image chipFill;
    public TMP_Text chipText;
    public TMP_Text priceText;
    [Tooltip("Right-aligned change; the arrow sits 4 px left of it")]
    public TMP_Text changeText;
    public Image upIcon;
    public Image downIcon;
    public TMP_Text ownedText;

    [HideInInspector] public string symbol;
}
