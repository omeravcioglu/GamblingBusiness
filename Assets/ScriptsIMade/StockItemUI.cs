using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>One row of the stock list. Clicking it selects the stock in StockMarketUI.</summary>
public class StockItemUI : MonoBehaviour
{
    [Header("UI Elements")]
    public TextMeshProUGUI symbolText;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI priceText;
    public TextMeshProUGUI changeText;
    public TextMeshProUGUI ownedText;
    public TextMeshProUGUI typeText;
    public Button selectButton;
    public Image backgroundImage;

    [Header("Colors")]
    public Color positiveColor = new Color(0.2f, 0.8f, 0.2f);
    public Color negativeColor = new Color(0.9f, 0.2f, 0.2f);
    public Color unchangedColor = new Color(0.75f, 0.75f, 0.75f);
    public Color ownedHighlightColor = new Color(0.3f, 0.3f, 0.5f);
    public Color normalColor = new Color(0.2f, 0.2f, 0.2f);
    public Color selectedColor = new Color(0.25f, 0.4f, 0.65f);

    // Stock type colors
    public Color memeColor = new Color(1f, 0.5f, 0f);      // Orange
    public Color volatileColor = new Color(1f, 0.3f, 0.3f); // Red
    public Color stableColor = new Color(0.3f, 0.7f, 1f);   // Blue
    public Color dividendColor = new Color(0.3f, 0.9f, 0.3f); // Green

    private Stock stock;
    private StockMarketUI marketUI;
    private bool isSelected;

    /// <summary>The stock shown in this row.</summary>
    public Stock Stock => stock;

    public void Setup(Stock stock, StockMarketUI ui)
    {
        this.stock = stock;
        this.marketUI = ui;

        if (selectButton != null)
        {
            selectButton.onClick.RemoveListener(OnSelect); // never add twice
            selectButton.onClick.AddListener(OnSelect);
        }

        Refresh();
    }

    /// <summary>Highlight this row as the selected stock.</summary>
    public void SetSelected(bool selected)
    {
        isSelected = selected;
        UpdateBackground();
    }

    public void Refresh()
    {
        if (stock == null) return;

        if (symbolText != null)
            symbolText.text = stock.symbol;

        if (nameText != null)
            nameText.text = stock.companyName;

        if (priceText != null)
            priceText.text = "$" + stock.currentPrice.ToString("N2");

        if (changeText != null)
        {
            float change = stock.GetWindowChangePercent();   // against 30 ticks ago, like the chart
            changeText.text = StockFormat.Percent(change);
            changeText.color = change > 0 ? positiveColor : (change < 0 ? negativeColor : unchangedColor);
        }

        if (ownedText != null)
        {
            // Share count only; the value is shown in the detail panel
            ownedText.text = stock.ownedShares > 0 ? stock.ownedShares.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) : "—";
        }

        if (typeText != null)
        {
            typeText.text = StockFormat.TypeName(stock.stockType);

            // Color based on type
            switch (stock.stockType)
            {
                case StockType.Meme:
                    typeText.color = memeColor;
                    break;
                case StockType.Volatile:
                    typeText.color = volatileColor;
                    break;
                case StockType.Stable:
                    typeText.color = stableColor;
                    break;
                case StockType.Dividend:
                    typeText.color = dividendColor;
                    break;
                default:
                    typeText.color = Color.white;
                    break;
            }
        }

        UpdateBackground();
    }

    void UpdateBackground()
    {
        // Selected > owned > normal
        if (backgroundImage == null || stock == null) return;
        if (isSelected)
            backgroundImage.color = selectedColor;
        else
            backgroundImage.color = stock.ownedShares > 0 ? ownedHighlightColor : normalColor;
    }

    void OnSelect()
    {
        if (marketUI != null && stock != null)
        {
            marketUI.SelectStock(stock);
        }
    }
}
