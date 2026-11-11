using TMPro;
using UnityEngine;

/// <summary>
/// The casino's screens built at runtime from the design boards (CasinoHub, Slots, CoinFlip, CasinoBlackjack) on their own
/// overlay canvas. CasinoManager builds this the first time it opens with no designed casino UI wired in (Casino Panel
/// empty) and uses its panels: the root as Casino Panel, the hub as Game Selection Panel, the three game screens as the
/// game panels; so opening, closing and Escape go through CasinoManager as they would with a designed prefab.
/// Its texts come from the "Casino" String Table and follow the language's font (L10nFont on every text).
/// </summary>
public class CasinoView : MonoBehaviour
{
    [Tooltip("Everything of the casino (the Casino Panel)")]
    public RectTransform root;
    public CasinoHubView hub;
    public CasinoSlotsView slots;
    public CasinoCoinFlipView coinFlip;
    public CasinoBlackjackView blackjack;

    /// <summary>The screen on show is still animating an outcome (no leaving until it is done).</summary>
    public bool IsBusy => Busy(slots) || Busy(coinFlip) || Busy(blackjack);

    /// <summary>The hub's custom amount is being typed.</summary>
    public bool IsEditingBet => hub != null && hub.IsEditingBet;

    static bool Busy(CasinoScreen screen)
    {
        return screen != null && screen.isActiveAndEnabled && screen.IsBusy;
    }

    /// <summary>Give CasinoManager these screens as its panels.</summary>
    public void AssignPanels(CasinoManager manager)
    {
        if (manager == null) return;
        if (root != null) manager.casinoPanel = root.gameObject;
        if (hub != null) manager.gameSelectionPanel = hub.gameObject;
        if (slots != null) manager.slotMachinePanel = slots.gameObject;
        if (coinFlip != null) manager.coinFlipPanel = coinFlip.gameObject;
        if (blackjack != null) manager.blackjackPanel = blackjack.gameObject;
    }

    /// <summary>
    /// Build the four screens on an overlay canvas (sorting order <paramref name="sortingOrder"/>, 1920 x 1080 reference)
    /// under <paramref name="manager"/>. Everything starts hidden; CasinoManager shows what it needs.
    /// </summary>
    public static CasinoView Build(CasinoManager manager, TMP_FontAsset font, int sortingOrder)
    {
        font = CasinoUI.ResolveFont(font);
        Canvas canvas = HeatUI.CreateCanvas("CasinoCanvas (Runtime)", sortingOrder, manager != null ? manager.transform : null, true);
        CasinoView view = canvas.gameObject.AddComponent<CasinoView>();

        // Built inactive: the buttons wake up (MarkerButton) with their faces in place
        RectTransform root = CasinoUI.FullRect(canvas.transform, "Casino");
        root.gameObject.SetActive(false);
        view.root = root;
        view.hub = CasinoHubView.Build(root, font);
        view.slots = CasinoSlotsView.Build(root, font);
        view.coinFlip = CasinoCoinFlipView.Build(root, font);
        view.blackjack = CasinoBlackjackView.Build(root, font);
        // Every text of the casino takes the language's font (cards dealt later are made in the design font: see CasinoBlackjackView)
        L10nFont.AttachAll(root);

        foreach (CasinoScreen screen in new CasinoScreen[] { view.hub, view.slots, view.coinFlip, view.blackjack })
            if (screen != null) screen.manager = manager;
        if (manager != null)
        {
            view.slots.game = manager.slotMachine;
            view.coinFlip.game = manager.coinFlip;
            view.blackjack.game = manager.blackjack;
        }
        return view;
    }
}
