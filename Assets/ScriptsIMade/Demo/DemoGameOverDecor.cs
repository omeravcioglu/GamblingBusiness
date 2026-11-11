using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Game over in demo mode (bankruptcy isn't the demo's end, so the hearing and the ledger sheet stay as they are): adds a
/// "Wishlist the full game" link under the sheet's buttons, and makes the sheet's Start a new company skip the bank's
/// coin toss (the menu starts the next company at once). Hooks GameOverView.Shown, so it decorates every sheet that
/// shows, a rebuilt one included.
/// </summary>
static class DemoGameOverDecor
{
    const string LinkName = "DemoWishlist";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        GameOverView.Shown -= OnShown;
        if (DemoMode.Active) GameOverView.Shown += OnShown;
    }

    static void OnShown(GameOverView view)
    {
        if (view == null || view.root == null || !DemoMode.Active) return;
        Transform parent = view.newCompanyButton != null ? view.newCompanyButton.transform.parent : view.root;
        if (parent == null || parent.Find(LinkName) != null) return;   // this sheet is decorated already

        TMP_FontAsset font = RunEndUI.ResolveFont(view.font);
        Button link = RunEndUI.LinkButton(parent, LinkName, DemoMode.T("demo.gameover.wishlist"), RunEndIconKind.None, font, 24f, 44f,
                                          out TextMeshProUGUI label);
        L10nText.Attach(label, DemoMode.Table, "demo.gameover.wishlist");
        HorizontalLayoutGroup row = link.GetComponent<HorizontalLayoutGroup>();
        if (row != null) row.childAlignment = TextAnchor.MiddleCenter;   // centred, whatever the language makes its width
        link.onClick.AddListener(DemoMode.OpenStorePage);
        Place((RectTransform)link.transform, parent as RectTransform, view.newCompanyButton);

        // The sheet's own Start a new company goes to the menu: from there the next company starts without the toss
        if (view.newCompanyButton != null)
            view.newCompanyButton.onClick.AddListener(() =>
            {
                PlayerPrefs.SetInt(DemoDirector.SkipFundingTossKey, 1);
                PlayerPrefs.Save();
            });
    }

    // Just under the buttons, across the sheet (the runtime sheet places them from its top-left); else at the bottom
    static void Place(RectTransform link, RectTransform parent, Button below)
    {
        RectTransform button = below != null ? below.transform as RectTransform : null;
        if (parent != null && button != null && button.anchorMin == new Vector2(0f, 1f) && button.anchorMax == new Vector2(0f, 1f))
        {
            float y = -button.anchoredPosition.y + button.sizeDelta.y * button.pivot.y + 10f;   // the button's bottom edge
            float width = Mathf.Max(200f, parent.rect.width - 120f);
            BlueprintUI.TopLeft(link, (parent.rect.width - width) * 0.5f, y, width, 44f);
            return;
        }
        link.anchorMin = link.anchorMax = new Vector2(0.5f, 0f);
        link.pivot = new Vector2(0.5f, 0f);
        link.sizeDelta = new Vector2(600f, 44f);
        link.anchoredPosition = new Vector2(0f, 16f);
    }
}
