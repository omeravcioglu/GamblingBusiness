using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The designed stock market screen (boards "Stock market" and its sell / locked / not-enough-money states), built by
/// StockMarketBuilder into Assets/UI/Prefabs/StockMarket.prefab. It only draws: Render(model) fills every text, row,
/// tab and button look from a StockScreenModel. StockMarketUI owns the state, builds the model from the live market
/// and hooks the buttons; the editor previews build models from the design canvas' sample numbers.
/// Fixed labels carry L10nText; the texts written here come from the "Stocks" table (StockFormat).
/// </summary>
public class StockMarketView : MonoBehaviour
{
    [Serializable]
    public class TicketLine
    {
        public GameObject row;
        public TMP_Text label;
        public TMP_Text value;
        public LayoutElement layout;
    }

    [Header("Screen")]
    [Tooltip("Scrim + newspaper sheet; shown while the market is open")]
    public GameObject root;
    public Button closeButton;
    public Button backButton;

    [Header("Edition line")]
    public TMP_Text dayText;
    public TMP_Text topMoverText;
    [Tooltip("The top mover text and the dot before it")]
    public GameObject[] topMoverParts = new GameObject[0];
    public TMP_Text feeText;

    [Header("List tabs")]
    public StockTabLook allTab;
    public TMP_Text allCountText;
    public StockTabLook ownedTab;
    public TMP_Text ownedCountText;
    public Graphic ownedCountFill;

    [Header("All stocks")]
    public GameObject allView;
    public RectTransform allRows;
    public StockRowView rowTemplate;

    [Header("Owned")]
    public GameObject ownedView;
    public RectTransform ownedRows;
    public StockOwnedRowView ownedRowTemplate;
    public TMP_Text ownedEmptyText;
    public GameObject totalsRow;
    public TMP_Text totalValueText;
    public TMP_Text totalProfitText;
    public TMP_Text totalPercentText;
    public GameObject splitBlock;
    public RectTransform splitBar;
    public StockBarSegmentView segmentTemplate;
    public TMP_Text bestText;
    public TMP_Text worstText;
    public RectTransform tradeRows;
    public StockTradeRowView tradeRowTemplate;
    public TMP_Text tradesEmptyText;
    public int tradesShown = 5;

    [Header("Selected stock")]
    public GameObject detailRoot;
    public TMP_Text symbolText;
    public RectTransform typeChip;
    public Image typeChipFill;
    public TMP_Text typeChipText;
    public TMP_Text companyText;
    public TMP_Text priceText;
    [Tooltip("Arrow + change, placed 20 px after the price")]
    public RectTransform changeGroup;
    public Image changeUp;
    public Image changeDown;
    public TMP_Text changeText;
    public StockChartGraphic chart;
    [Tooltip("Price labels of the 4 grid lines, top first")]
    public TMP_Text[] axisLabels = new TMP_Text[4];
    public RectTransform startLabel;
    public TMP_Text startLabelText;
    public TMP_Text rangeText;
    public TMP_Text hintText;
    public GameObject ownBox;
    public TMP_Text ownLine;
    public TMP_Text paidLine;
    public GameObject noneBox;
    public TMP_Text noneLine;

    [Header("Order ticket")]
    public GameObject ticketRoot;
    public TMP_Text ticketSubText;
    public StockTabLook buyTab;
    public StockTabLook sellTab;
    public GameObject openGroup;
    public Button quickButton;
    public TMP_Text quickText;
    public TMP_InputField amountInput;
    public Button lessButton;
    public Button moreButton;
    [Tooltip("Breakdown rows, top to bottom; the rule sits after the second")]
    public TicketLine[] lines = new TicketLine[5];
    public GameObject lineRule;
    public Button actionButton;
    public MarkerButton actionMarker;
    public Image actionFill;
    public GameObject actionSolidBorder;
    public GameObject actionDashedBorder;
    public GameObject actionShadow;
    public Image actionPlus;
    public Image actionMinus;
    public TMP_Text actionText;
    public GameObject lockedGroup;
    public GameObject lockedStamp;
    public Button openSkillsButton;
    public GameObject messageGroup;
    public Image messageOk;
    public Image messageError;
    public Image messageInfo;
    public TMP_Text messageText;

    [Header("Footer")]
    public TMP_Text cashText;
    public TMP_Text holdingsText;
    public GameObject profitGroup;
    public TMP_Text profitText;
    public TMP_Text netText;

    [Header("Colours")]
    public Color paperHi = new Color32(0xFF, 0xFD, 0xF6, 0xFF);
    public Color disabledFill = new Color32(0xEA, 0xE2, 0xCF, 0xFF);

    /// <summary>A row of either list was clicked: the stock's symbol.</summary>
    public event Action<string> StockClicked;

    readonly List<StockRowView> rowPool = new List<StockRowView>();
    readonly List<StockOwnedRowView> ownedPool = new List<StockOwnedRowView>();
    readonly List<StockTradeRowView> tradePool = new List<StockTradeRowView>();
    readonly List<StockBarSegmentView> segmentPool = new List<StockBarSegmentView>();
    readonly List<StockRowModel> ownedList = new List<StockRowModel>();

    static string T(string key) => L10n.Get(StockFormat.Table, key);
    static string T(string key, params (string name, object value)[] args) => L10n.Get(StockFormat.Table, key, args);

    // ------------------------------------------------------------------

    public void Render(StockScreenModel m)
    {
        if (m == null) return;
        RenderEdition(m);
        RenderTabs(m);
        if (allView != null) allView.SetActive(!m.showOwned);
        if (ownedView != null) ownedView.SetActive(m.showOwned);
        if (m.showOwned) RenderOwned(m);
        else RenderAll(m);

        var sel = m.Selected;
        if (detailRoot != null) detailRoot.SetActive(sel != null);
        if (ticketRoot != null) ticketRoot.SetActive(sel != null);
        if (sel != null)
        {
            RenderDetail(m, sel);
            RenderTicket(m, sel);
        }
        RenderFooter(m);
    }

    // ------------------------------------------------------------------
    // Edition line and tabs

    void RenderEdition(StockScreenModel m)
    {
        if (dayText != null) dayText.text = T("stocks.day", ("day", m.day));
        StockRowModel top = null;
        foreach (var r in m.rows)
            if (top == null || Mathf.Abs(r.change) > Mathf.Abs(top.change)) top = r;
        foreach (var part in topMoverParts)
            if (part != null) part.SetActive(top != null);
        if (top != null && topMoverText != null)
            topMoverText.text = T("stocks.edition.top", ("symbol", StockFormat.Bold(top.symbol)),
                ("change", StockFormat.Bold(StockFormat.Colored(StockFormat.Percent(top.change), top.change >= 0 ? StockFormat.Gain : StockFormat.Loss))));
        if (feeText != null) feeText.text = T("stocks.edition.fee", ("fee", StockFormat.FeeNumber(m.feePercent)));
    }

    void RenderTabs(StockScreenModel m)
    {
        int owned = 0;
        foreach (var r in m.rows)
            if (r.owned > 0) owned++;
        if (allTab != null) allTab.Apply(m.showOwned ? StockTabLook.State.Inactive : StockTabLook.State.Active);
        if (ownedTab != null) ownedTab.Apply(m.showOwned ? StockTabLook.State.Active : StockTabLook.State.Inactive);
        if (allCountText != null) allCountText.text = m.rows.Count.ToString();
        if (ownedCountText != null) ownedCountText.text = owned.ToString();
        if (ownedCountFill != null)
            ownedCountFill.color = owned > 0 ? (Color)new Color32(0xFF, 0xE1, 0x4D, 0xFF) : StockFormat.ChipColor(StockType.Normal);
    }

    // ------------------------------------------------------------------
    // All stocks

    void RenderAll(StockScreenModel m)
    {
        EnsurePool(rowPool, rowTemplate, allRows, m.rows.Count, r => r.button, r => r.symbol);
        for (int i = 0; i < m.rows.Count; i++)
        {
            var r = m.rows[i];
            var v = rowPool[i];
            v.symbol = r.symbol;
            bool selected = r.symbol == m.selected;
            if (v.band != null) v.band.enabled = selected;
            Set(v.symbolText, r.symbol);
            Set(v.companyText, r.company);
            SetChip(v.companyText, v.chip, v.chipFill, v.chipText, r.type, 8f, 16f);
            Set(v.priceText, StockFormat.Money(r.price));
            bool up = r.change >= 0f;
            Color c = up ? StockFormat.Gain : StockFormat.Loss;
            Set(v.changeText, StockFormat.Percent(r.change), c);
            PlaceArrow(v.changeText, up ? v.upIcon : v.downIcon, up ? v.downIcon : v.upIcon, c);
            bool owns = r.owned > 0;
            Set(v.ownedText, owns ? StockFormat.Count(r.owned) : "—", owns ? StockFormat.Ink : StockFormat.Pencil);
            if (v.ownedText != null) v.ownedText.fontStyle = owns ? FontStyles.Bold : FontStyles.Normal;
        }
    }

    // ------------------------------------------------------------------
    // Owned: holdings with profit, the split bar, recent trades

    void RenderOwned(StockScreenModel m)
    {
        ownedList.Clear();
        foreach (var r in m.rows)
            if (r.owned > 0) ownedList.Add(r);

        EnsurePool(ownedPool, ownedRowTemplate, ownedRows, ownedList.Count, r => r.button, r => r.symbol);
        double value = 0.0, paid = 0.0;
        StockRowModel best = null, worst = null;
        for (int i = 0; i < ownedList.Count; i++)
        {
            var r = ownedList[i];
            var v = ownedPool[i];
            v.symbol = r.symbol;
            if (v.band != null) v.band.enabled = r.symbol == m.selected;
            Set(v.symbolText, r.symbol);
            Set(v.companyText, r.company);
            SetChip(v.companyText, v.chip, v.chipFill, v.chipText, r.type, 8f, 16f);
            Set(v.sharesText, StockFormat.Count(r.owned));
            Set(v.valueText, StockFormat.Money(r.Value));
            Color c = StockFormat.ProfitColor(r.Profit);
            Set(v.profitText, StockFormat.SignedMoney(r.Profit), c);
            Set(v.percentText, StockFormat.Percent(r.ProfitPercent), c);
            value += r.Value;
            paid += r.Paid;
            if (best == null || r.ProfitPercent > best.ProfitPercent) best = r;
            if (worst == null || r.ProfitPercent < worst.ProfitPercent) worst = r;
        }

        bool any = ownedList.Count > 0;
        if (ownedEmptyText != null) ownedEmptyText.gameObject.SetActive(!any);
        if (totalsRow != null) totalsRow.SetActive(any);
        if (splitBlock != null) splitBlock.SetActive(any);
        if (any)
        {
            double profit = value - paid;
            Color c = StockFormat.ProfitColor(profit);
            Set(totalValueText, StockFormat.Money(value));
            Set(totalProfitText, StockFormat.SignedMoney(profit), c);
            Set(totalPercentText, paid > 0 ? StockFormat.Percent(profit / paid * 100.0) : "", c);
            RenderSplit(value);
            Set(bestText, T("stocks.owned.best", ("stock", StockFormat.Bold(StockFormat.Colored(best.symbol + " " + StockFormat.Percent(best.ProfitPercent), StockFormat.Gain)))));
            Set(worstText, T("stocks.owned.worst", ("stock", StockFormat.Bold(StockFormat.Colored(worst.symbol + " " + StockFormat.Percent(worst.ProfitPercent), StockFormat.ProfitColor(worst.Profit))))));
        }

        int shown = Mathf.Min(tradesShown, m.trades.Count);
        EnsurePool<StockTradeRowView>(tradePool, tradeRowTemplate, tradeRows, shown, null, null);
        for (int i = 0; i < shown; i++)
        {
            var t = m.trades[i];
            var v = tradePool[i];
            Set(v.dayText, T("stocks.day", ("day", t.day)));
            Set(v.whatText, T(t.buy ? "stocks.trade.bought" : "stocks.trade.sold",
                ("count", StockFormat.Count(t.shares)), ("symbol", t.symbol), ("price", StockFormat.Money(t.price))));
            Set(v.cashText, StockFormat.SignedMoney(t.buy ? -t.cash : t.cash));
            if (t.buy) Set(v.profitText, "—", StockFormat.Pencil);
            else Set(v.profitText, StockFormat.SignedMoney(t.profit), StockFormat.ProfitColor(t.profit));
            if (v.profitText != null) v.profitText.fontStyle = t.buy ? FontStyles.Normal : FontStyles.Bold;
        }
        if (tradesEmptyText != null) tradesEmptyText.gameObject.SetActive(shown == 0);
    }

    void RenderSplit(double total)
    {
        EnsurePool<StockBarSegmentView>(segmentPool, segmentTemplate, splitBar, ownedList.Count, null, null);
        float inner = splitBar.rect.width;
        if (inner <= 1f) inner = splitBar.sizeDelta.x;
        float x = 0f;
        for (int i = 0; i < ownedList.Count; i++)
        {
            var r = ownedList[i];
            var seg = segmentPool[i];
            bool last = i == ownedList.Count - 1;
            float share = total > 0 ? (float)(r.Value / total) : 0f;
            float w = last ? inner - x : Mathf.Round(inner * share);
            var rt = (RectTransform)seg.transform;
            rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
            rt.sizeDelta = new Vector2(Mathf.Max(0f, w), rt.sizeDelta.y);
            x += w;
            if (seg.fill != null) seg.fill.color = StockFormat.ChipColor(r.type);
            if (seg.edge != null) seg.edge.enabled = !last;
            if (seg.label != null)
            {
                seg.label.text = r.symbol + " " + T("stocks.percent", ("sign", ""), ("value", Mathf.RoundToInt(share * 100f).ToString(L10n.Culture)));
                seg.label.enabled = seg.label.preferredWidth + 6f <= w;
            }
        }
    }

    // ------------------------------------------------------------------
    // Selected stock

    static readonly List<float> history = new List<float>();

    void RenderDetail(StockScreenModel m, StockRowModel r)
    {
        Set(symbolText, r.symbol);
        SetChip(symbolText, typeChip, typeChipFill, typeChipText, r.type, 16f, 24f);
        Set(companyText, r.company);
        Set(priceText, StockFormat.Money(r.price));

        history.Clear();
        history.AddRange(m.history);
        if (history.Count == 0) history.Add(r.price);
        float change = history[0] > 0f ? (history[history.Count - 1] / history[0] - 1f) * 100f : 0f;
        bool up = change >= 0f;
        Color c = up ? StockFormat.Gain : StockFormat.Loss;
        if (changeGroup != null && priceText != null)
            changeGroup.anchoredPosition = new Vector2(priceText.rectTransform.anchoredPosition.x + priceText.preferredWidth + 20f,
                changeGroup.anchoredPosition.y);
        if (changeUp != null) { changeUp.enabled = up; changeUp.color = c; }
        if (changeDown != null) { changeDown.enabled = !up; changeDown.color = c; }
        Set(changeText, T("stocks.detail.change", ("change", StockFormat.Percent(change))), c);

        StockChartGraphic.NiceGrid(history, out float top, out float step);
        if (chart != null) chart.SetPrices(history, top, step);
        for (int i = 0; i < axisLabels.Length; i++)
            Set(axisLabels[i], GridLabel(top - i * step, step));
        PlaceStartLabel(history, top, step);

        float lo = float.MaxValue, hi = float.MinValue;
        foreach (float p in history) { lo = Mathf.Min(lo, p); hi = Mathf.Max(hi, p); }
        Set(rangeText, T("stocks.detail.range", ("low", StockFormat.Money(lo)), ("high", StockFormat.Money(hi))));
        Set(hintText, T("stocks.detail.hint", ("type", StockFormat.Bold(StockFormat.TypeName(r.type))), ("hint", StockFormat.TypeHint(r.type))));

        bool owns = r.owned > 0;
        if (ownBox != null) ownBox.SetActive(owns);
        if (noneBox != null) noneBox.SetActive(!owns);
        if (owns)
        {
            string amount = T("stocks.detail.own.amount", ("shares", StockFormat.Count(r.owned)), ("count", r.owned));
            Set(ownLine, T("stocks.detail.own", ("amount", StockFormat.Bold(amount)), ("worth", StockFormat.Bold(StockFormat.Money(r.Value)))));
            string profit = StockFormat.SignedMoney(r.Profit) + " (" + StockFormat.Percent(r.ProfitPercent) + ")";
            Set(paidLine, T("stocks.detail.paid", ("avg", StockFormat.Money(r.averagePrice)),
                ("profit", StockFormat.Bold(StockFormat.Colored(profit, StockFormat.ProfitColor(r.Profit))))));
        }
        else
            Set(noneLine, T("stocks.detail.none", ("symbol", r.symbol)));
    }

    static string GridLabel(float value, float step)
    {
        double v = Math.Round(value, 2);
        bool whole = step >= 1f && Math.Abs(v - Math.Round(v)) < 1e-6;
        string sign = v < 0 ? "−" : "";
        return sign + "$" + Math.Abs(v).ToString(whole ? "N0" : "N2", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The "start $…" label goes on the corner of the dashed start line that the price line crosses least.</summary>
    void PlaceStartLabel(List<float> prices, float top, float step)
    {
        if (startLabel == null || chart == null) return;
        Set(startLabelText, T("stocks.chart.start", ("price", StockFormat.Money(prices[0]))));
        int n = prices.Count;
        float Y(float p) => StockChartGraphic.GridTop + (top - p) * StockChartGraphic.GridSpacing / step;
        float startY = Y(prices[0]);
        float w = startLabel.sizeDelta.x;
        var spots = new (float x, float y, bool right)[]
        {
            (534f - w, startY - 29f, true), (534f - w, startY + 5f, true), (70f, startY - 29f, false), (70f, startY + 5f, false)
        };
        var best = spots[0];
        int fewest = int.MaxValue;
        foreach (var sp in spots)
        {
            if (sp.y < StockChartGraphic.PlotTop || sp.y + 24f > StockChartGraphic.PlotBottom) continue;
            int hits = 0;
            for (int i = 0; i + 1 < n; i++)
            {
                for (int k = 0; k < 4; k++)
                {
                    float t = k / 4f;
                    float x = Mathf.Lerp(StockChartGraphic.IndexToX(i, n), StockChartGraphic.IndexToX(i + 1, n), t);
                    float y = Mathf.Lerp(Y(prices[i]), Y(prices[i + 1]), t);
                    if (x >= sp.x - 4f && x <= sp.x + w + 4f && y >= sp.y - 4f && y <= sp.y + 28f) hits++;
                }
            }
            if (hits < fewest)
            {
                fewest = hits;
                best = sp;
            }
        }
        startLabel.anchoredPosition = new Vector2(best.x, -best.y);
        if (startLabelText != null)
            startLabelText.alignment = best.right ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft;
    }

    // ------------------------------------------------------------------
    // Order ticket

    void RenderTicket(StockScreenModel m, StockRowModel r)
    {
        bool owns = r.owned > 0;
        bool sell = m.sellMode && owns;
        bool lockedBody = m.locked && !sell;
        Set(ticketSubText, T("stocks.ticket.sub", ("symbol", r.symbol), ("price", StockFormat.Money(r.price))));

        if (buyTab != null) buyTab.Apply(m.locked ? StockTabLook.State.Locked : sell ? StockTabLook.State.Inactive : StockTabLook.State.Active);
        if (sellTab != null)
        {
            sellTab.Apply(!owns ? StockTabLook.State.Disabled : sell ? StockTabLook.State.Active : StockTabLook.State.Inactive);
            if (sellTab.button != null) sellTab.button.interactable = owns;
        }
        if (openGroup != null) openGroup.SetActive(!lockedBody);
        if (lockedGroup != null) lockedGroup.SetActive(lockedBody);
        if (lockedStamp != null) lockedStamp.SetActive(lockedBody);

        string message = m.message ?? "";
        int kind = message.Length > 0 ? (m.messageIsError ? 2 : 1) : 0;   // 0 none, 1 ok, 2 error, 3 info
        if (!lockedBody)
        {
            long n = m.amount;
            double fee = m.feePercent / 100.0;
            string feeLabel = T("stocks.ticket.line.fee", ("fee", StockFormat.FeeNumber(m.feePercent)));
            bool valid = n > 0;
            bool enabled = valid;
            SetQuick(sell ? T("stocks.ticket.all", ("count", StockFormat.Count(r.owned)))
                          : T("stocks.ticket.max", ("count", StockFormat.Count(m.maxBuy))), false);
            if (amountInput != null && !amountInput.isFocused && n > 0 && ParseAmount(amountInput.text) != n)
                amountInput.SetTextWithoutNotify(StockFormat.Count(n));

            for (int i = 0; i < lines.Length; i++)
                if (lines[i] != null && lines[i].row != null) lines[i].row.SetActive(false);
            if (lineRule != null) lineRule.SetActive(valid);

            if (valid && !sell)
            {
                double gross = (double)r.price * n, f = gross * fee, total = gross + f;
                bool broke = total > m.cash;
                SetLine(0, T("stocks.ticket.line.shares", ("count", StockFormat.Count(n)), ("price", StockFormat.Money(r.price))), StockFormat.Money(gross), 22f, false, StockFormat.Ink, false);
                SetLine(1, feeLabel, StockFormat.Money(f), 22f, false, StockFormat.Ink, false);
                SetLine(2, T("stocks.ticket.line.total"), StockFormat.Money(total), 26f, true, broke ? StockFormat.Loss : StockFormat.Ink, false);
                if (broke)
                {
                    SetLine(3, T("stocks.ticket.line.your_cash"), StockFormat.Money(m.cash), 19f, false, StockFormat.InkSoft, false);
                    enabled = false;
                    message = T("stocks.msg.no_money", ("need", StockFormat.Money(total)), ("have", StockFormat.Money(m.cash)));
                    kind = 2;
                    SetQuick(T("stocks.ticket.max", ("count", StockFormat.Count(m.maxBuy))), true);
                }
                else
                    SetLine(3, T("stocks.ticket.line.cash_left"), StockFormat.Money(m.cash - total), 19f, false, StockFormat.InkSoft, false);
            }
            else if (valid)
            {
                double gross = (double)r.price * n, f = gross * fee, get = gross - f, cost = (double)r.averagePrice * n;
                SetLine(0, T("stocks.ticket.line.shares", ("count", StockFormat.Count(n)), ("price", StockFormat.Money(r.price))), StockFormat.Money(gross), 22f, false, StockFormat.Ink, false);
                SetLine(1, feeLabel, StockFormat.Money(-f), 22f, false, StockFormat.Ink, false);
                SetLine(2, T("stocks.ticket.line.you_get"), StockFormat.Money(get), 26f, true, StockFormat.Ink, false);
                SetLine(3, T("stocks.ticket.line.you_paid"), StockFormat.Money(cost), 19f, false, StockFormat.InkSoft, false);
                SetLine(4, T("stocks.ticket.line.profit"), StockFormat.SignedMoney(get - cost), 22f, true, StockFormat.ProfitColor(get - cost), true);
                if (n > r.owned)
                {
                    enabled = false;
                    message = T("stocks.msg.no_shares", ("count", StockFormat.Count(r.owned)), ("symbol", r.symbol));
                    kind = 2;
                }
                else if (kind == 0)
                {
                    message = T("stocks.msg.sell_hint");
                    kind = 3;
                }
            }
            else
            {
                message = T("stocks.msg.amount");
                kind = 2;
            }

            SetAction(enabled, sell, T(sell ? "stocks.ticket.action.sell" : "stocks.ticket.action.buy",
                ("count", StockFormat.Count(Math.Max(0, n))), ("symbol", r.symbol)));
        }
        else
            kind = 0;

        if (messageGroup != null) messageGroup.SetActive(kind != 0);
        if (kind != 0)
        {
            Color c = kind == 1 ? StockFormat.Gain : kind == 2 ? StockFormat.Loss : StockFormat.InkSoft;
            if (messageOk != null) { messageOk.enabled = kind == 1; messageOk.color = c; }
            if (messageError != null) { messageError.enabled = kind == 2; messageError.color = c; }
            if (messageInfo != null) { messageInfo.enabled = kind == 3; messageInfo.color = c; }
            Set(messageText, message, c);
            if (messageText != null) messageText.fontStyle = kind == 3 ? FontStyles.Normal : FontStyles.Bold;
        }
    }

    static long ParseAmount(string text)
    {
        long.TryParse((text ?? "").Trim(), System.Globalization.NumberStyles.Integer | System.Globalization.NumberStyles.AllowThousands,
            System.Globalization.CultureInfo.InvariantCulture, out long v);
        return v;
    }

    void SetQuick(string text, bool highlight)
    {
        if (quickText == null) return;
        quickText.text = highlight ? "<link=\"hl\"><space=2>" + text + "<space=2></link>" : text;
        if (quickButton != null)
        {
            var rt = (RectTransform)quickButton.transform;
            rt.sizeDelta = new Vector2(quickText.GetPreferredValues(text).x + 24f + 4f, rt.sizeDelta.y);
        }
    }

    void SetLine(int i, string label, string value, float size, bool bold, Color color, bool mark)
    {
        if (i < 0 || i >= lines.Length || lines[i] == null || lines[i].row == null) return;
        var l = lines[i];
        l.row.SetActive(true);
        var style = bold ? FontStyles.Bold : FontStyles.Normal;
        if (l.label != null)
        {
            l.label.text = label;
            l.label.fontSize = size;
            l.label.fontStyle = style;
            l.label.color = color;
        }
        if (l.value != null)
        {
            l.value.text = mark ? "<link=\"hl\"><space=4>" + value + "<space=4></link>" : value;
            l.value.fontSize = size;
            l.value.fontStyle = style;
            l.value.color = color;
        }
        if (l.layout != null)
            l.layout.minHeight = l.layout.preferredHeight = size >= 26f ? 36f : size >= 22f ? 30f : 26f;
    }

    void SetAction(bool enabled, bool sell, string text)
    {
        Color fillColor = sell ? StockFormat.Loss : StockFormat.Gain;
        if (actionButton != null) actionButton.interactable = enabled;
        if (actionMarker != null)
        {
            actionMarker.SetFillColor(fillColor);
            for (int i = 0; i < 3; i++) actionMarker.SetContentColor(i, paperHi);
        }
        if (actionFill != null) actionFill.color = enabled ? fillColor : disabledFill;
        if (actionSolidBorder != null) actionSolidBorder.SetActive(enabled);
        if (actionDashedBorder != null) actionDashedBorder.SetActive(!enabled);
        if (actionShadow != null) actionShadow.SetActive(enabled);
        Color content = enabled ? paperHi : StockFormat.Pencil;
        if (actionPlus != null) { actionPlus.gameObject.SetActive(!sell); actionPlus.color = content; }
        if (actionMinus != null) { actionMinus.gameObject.SetActive(sell); actionMinus.color = content; }
        Set(actionText, text, content);
    }

    // ------------------------------------------------------------------
    // Footer

    void RenderFooter(StockScreenModel m)
    {
        double value = 0.0, paid = 0.0;
        bool any = false;
        foreach (var r in m.rows)
        {
            if (r.owned <= 0) continue;
            any = true;
            value += r.Value;
            paid += r.Paid;
        }
        Set(cashText, StockFormat.Money(m.cash));
        Set(holdingsText, StockFormat.Money(value));
        if (profitGroup != null) profitGroup.SetActive(any);
        if (any) Set(profitText, StockFormat.SignedMoney(value - paid), StockFormat.ProfitColor(value - paid));
        Set(netText, "<link=\"hl\"><space=6>" + StockFormat.Money(m.cash + value) + "<space=6></link>");
    }

    // ------------------------------------------------------------------
    // Helpers

    static void Set(TMP_Text t, string text)
    {
        if (t != null) t.text = text;
    }

    static void Set(TMP_Text t, string text, Color color)
    {
        if (t == null) return;
        t.text = text;
        t.color = color;
    }

    /// <summary>Type chip: its text and colour, sized to the text (+ padding + 2 px borders), <paramref name="gap"/> after the text before it.</summary>
    static void SetChip(TMP_Text before, RectTransform chip, Image fill, TMP_Text label, StockType type, float gap, float padding)
    {
        if (chip == null) return;
        string name = StockFormat.TypeName(type);
        if (label != null) label.text = name;
        if (fill != null) fill.color = StockFormat.ChipColor(type);
        float w = (label != null ? label.GetPreferredValues(name).x : 60f) + padding + 4f;
        float x = before != null ? before.rectTransform.anchoredPosition.x + before.GetPreferredValues(before.text).x + gap : chip.anchoredPosition.x;
        chip.anchoredPosition = new Vector2(x, chip.anchoredPosition.y);
        chip.sizeDelta = new Vector2(w, chip.sizeDelta.y);
    }

    /// <summary>Arrow 4 px left of a right-aligned change text.</summary>
    static void PlaceArrow(TMP_Text text, Image show, Image hide, Color color)
    {
        if (hide != null) hide.enabled = false;
        if (show == null || text == null) return;
        show.enabled = true;
        show.color = color;
        var tr = text.rectTransform;
        float right = tr.anchoredPosition.x + tr.sizeDelta.x;
        var rt = show.rectTransform;
        rt.anchoredPosition = new Vector2(right - text.GetPreferredValues(text.text).x - 4f - rt.sizeDelta.x, rt.anchoredPosition.y);
    }

    void EnsurePool<TView>(List<TView> pool, TView template, RectTransform parent, int count,
        Func<TView, Button> button, Func<TView, string> symbol) where TView : Component
    {
        if (template == null || parent == null) return;
        if (template.gameObject.activeSelf) template.gameObject.SetActive(false);
        while (pool.Count < count)
        {
            var v = Instantiate(template, parent, false);
            v.name = template.name.Replace("Template", "") + pool.Count;
            if (button != null && symbol != null)
            {
                var b = button(v);
                if (b != null)
                {
                    var captured = v;
                    b.onClick.AddListener(() => StockClicked?.Invoke(symbol(captured)));
                }
            }
            pool.Add(v);
        }
        for (int i = 0; i < pool.Count; i++)
        {
            bool on = i < count;
            if (pool[i].gameObject.activeSelf != on) pool[i].gameObject.SetActive(on);
        }
    }
}
