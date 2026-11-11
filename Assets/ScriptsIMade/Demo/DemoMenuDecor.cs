using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// The main menu in demo mode (only MainMenuView's public references; the designed menu is not edited):
/// - the heads slip shows the demo's starting value, and the coin's first-flip odds are the demo's (so the confirm sheet's
///   "1 in 2" matches what MainMenuUI tosses);
/// - a tilted red DEMO stamp on the logo and "v0.1.0 · DEMO" on the version chip;
/// - a yellow sticky "Like it? Wishlist the full game." with a Wishlist on Steam button, under the menu's fading group,
///   so it fades with the logo and the pad while the coin decides and during the credits.
/// DemoMenuDecorBootstrap adds it when the menu scene loads.
/// </summary>
[DisallowMultipleComponent]
public class DemoMenuDecor : MonoBehaviour
{
    // Logo-local positions (the logo's pivot is its top-left; y down is negative): the stamp right of "GAMBLING", the
    // sticky between the logo and the card table (the comic logo is 500 x 379 at 112, 28 of the menu)
    static readonly Vector2 StampAtLogo = new Vector2(622f, -54f);
    static readonly Vector2 StickyAtLogo = new Vector2(678f, -294f);
    const float StickyWidth = 330f;

    MainMenuView view;
    RectTransform stamp, sticky;
    bool started;

    /// <summary>Apply the demo's values before MainMenuView.Start writes its texts.</summary>
    public static void Apply(MainMenuView menuView)
    {
        if (menuView == null || !DemoMode.Active) return;
        menuView.startingValue = DemoMode.StartingValue;
        if (menuView.coin != null) menuView.coin.successChance = DemoMode.Config.fundingFirstFlipChance;
        if (menuView.GetComponent<DemoMenuDecor>() == null) menuView.gameObject.AddComponent<DemoMenuDecor>().view = menuView;
    }

    void OnEnable()
    {
        L10n.LanguageChanged += OnLanguageChanged;
    }

    void OnDisable()
    {
        L10n.LanguageChanged -= OnLanguageChanged;
    }

    System.Collections.IEnumerator Start()
    {
        if (view == null) view = GetComponent<MainMenuView>();
        BuildDecor();
        // MainMenuView.Start writes "v0.1.0"; this one goes over it
        yield return null;
        started = true;
        WriteVersion();
    }

    void OnLanguageChanged()
    {
        // The stamp and the sticky are sized by their words: made again in the new language
        BuildDecor();
        if (started) WriteVersion();
    }

    void WriteVersion()
    {
        if (view == null || view.versionText == null) return;
        string version = Application.version ?? "";
        if (version.EndsWith("-demo")) version = version.Substring(0, version.Length - 5);   // the demo build's bundle version
        view.versionText.text = DemoMode.T("demo.menu.version", ("version", version));
    }

    void BuildDecor()
    {
        if (view == null) return;
        if (stamp != null) Destroy(stamp.gameObject);
        if (sticky != null) Destroy(sticky.gameObject);

        Transform parent = view.menuGroup != null ? view.menuGroup.transform : view.transform;
        TMP_FontAsset font = RunEndUI.ResolveFont(null);

        stamp = RunEndUI.Stamp(parent, "DemoStamp", DemoMode.T("demo.menu.stamp"), 64f, UIColors.Loss, font, 6f, 3f, 5f, 12f, 0.12f, 22f, 0f, -12f);
        L10nFont.AttachAll(stamp);
        TextMeshProUGUI word = stamp.GetComponentInChildren<TextMeshProUGUI>(true);
        if (word != null) RunEndUI.Shrink(word, 32f);   // measured in the design font: the language's font may run wider
        Place(stamp, StampAtLogo, new Vector2(734f, 82f), -12f);

        sticky = BuildSticky(parent, font);
        Place(sticky, StickyAtLogo, new Vector2(790f, 322f), -3f);
    }

    RectTransform BuildSticky(Transform parent, TMP_FontAsset font)
    {
        // Built inactive, so MarkerButton starts with its face set
        BlueprintBox note = BlueprintUI.Box(parent, "DemoWishlistNote", UIColors.StickyYellow, Color.clear, 0f, BlueprintUI.Round(2f));
        note.SetShadow(new Color(0f, 0f, 0f, 0.3f), new Vector2(4f, 8f), 10f);
        RectTransform rect = note.rectTransform;
        rect.gameObject.SetActive(false);
        rect.sizeDelta = new Vector2(StickyWidth, 160f);
        BlueprintUI.VColumn(note, 12f, new RectOffset(20, 26, 22, 22), TextAnchor.UpperCenter);
        BlueprintUI.Fit(note, false, true);

        BlueprintBox band = BlueprintUI.Box(rect, "Band", UIColors.Dark(0.07f), Color.clear, 0f, BlueprintUI.Round(0f));
        BlueprintUI.TopLeft(band.rectTransform, 0f, 0f, StickyWidth, 16f);
        band.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        TextMeshProUGUI text = BlueprintUI.Text(rect, "Note", "", 23f, UIColors.Ink, font, TextAlignmentOptions.Center);
        RunEndUI.Wrapped(text, 1.3f);
        L10nText.Attach(text, DemoMode.Table, "demo.menu.wishlist_note");

        Button button = RunEndUI.PaperButton(rect, "Wishlist", DemoMode.T("demo.wishlist"), RunEndIconKind.None, font, UIColors.Highlight,
                                             StickyWidth - 46f, 54f, 24f, true, new Vector2(4f, 5f), out TextMeshProUGUI label, 16f);
        L10nText.Attach(label, DemoMode.Table, "demo.wishlist");
        BlueprintUI.Layout(button, -1f, 54f);
        button.onClick.AddListener(DemoMode.OpenStorePage);

        L10nFont.AttachAll(rect);
        rect.gameObject.SetActive(true);
        return rect;
    }

    /// <summary>
    /// Put a decor piece at a point of the logo (it follows the logo wherever the menu puts it), else at a point of the
    /// 1920 x 1080 menu.
    /// </summary>
    void Place(RectTransform rect, Vector2 logoLocal, Vector2 menuPoint, float cssRotation)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        if (view.logo != null)
        {
            rect.position = view.logo.TransformPoint(new Vector3(logoLocal.x, logoLocal.y, 0f));
            rect.rotation = view.logo.rotation * RunEndUI.Rotation(cssRotation);
        }
        else
        {
            rect.anchoredPosition = new Vector2(menuPoint.x - 960f, 540f - menuPoint.y);
            rect.localRotation = RunEndUI.Rotation(cssRotation);
        }
    }
}

/// <summary>Adds DemoMenuDecor to the main menu while demo mode is on (before the menu's Start writes its texts).</summary>
static class DemoMenuDecorBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (!DemoMode.Active) return;
        SceneManager.sceneLoaded += OnSceneLoaded;
        Decorate();   // the first scene loaded before this ran
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Decorate();
    }

    static void Decorate()
    {
        if (!Application.isPlaying || !DemoMode.Active) return;
        MainMenuView view = Object.FindAnyObjectByType<MainMenuView>(FindObjectsInactive.Include);
        if (view != null) DemoMenuDecor.Apply(view);
    }
}
