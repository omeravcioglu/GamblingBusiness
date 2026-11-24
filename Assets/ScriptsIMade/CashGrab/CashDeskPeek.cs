using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The desk's window onto the cash floor: a paper chip at the bottom right of the office view showing the money waiting on
/// the floor and which departments are grabbing it, with a "Tab" keycap; click it (or press Tab) to go and grab it. It
/// pulses when money piles up or a payday lands. Hidden while the floor is open, in build mode and under any panel.
/// Own overlay canvas, sorting order 71 (over the HUD, like its other chips).
/// </summary>
public class CashDeskPeek : MonoBehaviour
{
    static CashDeskPeek instance;

    CashGrabSystem system;
    Canvas canvas;
    RectTransform chip;
    TextMeshProUGUI title, amount, hint, cash;
    Image[] dots;
    float pulse;
    readonly float[] grabFlash = new float[CashLedger.DeptCount];
    float refresh;
    bool remindedOnce;
    double lastFloor;

    public static void Create(CashGrabSystem owner)
    {
        if (instance != null) return;
        Canvas c = BlueprintUI.CreateOverlayCanvas("CashDeskPeek (Runtime)", 71);
        if (owner != null && owner.gameObject.scene.IsValid() && c.gameObject.scene != owner.gameObject.scene)
        {
            try { UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(c.gameObject, owner.gameObject.scene); }
            catch (System.Exception) { }
        }
        instance = c.gameObject.AddComponent<CashDeskPeek>();
        instance.system = owner;
        instance.canvas = c;
        instance.Build();
    }

    public static void Pulse()
    {
        if (instance != null) instance.pulse = 1f;
    }

    public static void NoteGrab(int dept)
    {
        if (instance != null && dept >= 0 && dept < instance.grabFlash.Length) instance.grabFlash[dept] = 0.8f;
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    void Build()
    {
        TMP_FontAsset font = L10n.DefaultFont;
        foreach (TMP_FontAsset candidate in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
            if (candidate != null && candidate.name == L10n.DefaultFontName) { font = candidate; break; }

        chip = BlueprintUI.NewRect("CashPeek", canvas.transform);
        chip.anchorMin = chip.anchorMax = new Vector2(1f, 0f);
        chip.pivot = new Vector2(1f, 0f);
        chip.anchoredPosition = new Vector2(-32f, 150f);
        chip.sizeDelta = new Vector2(330f, 138f);
        BlueprintBox box = BlueprintUI.AddBox(chip, UIColors.Felt, UIColors.Ink, 3f, BlueprintUI.RadiusLarge, true);
        box.SetShadow(UIColors.Ink, new Vector2(3f, -5f));
        BlueprintBox inner = BlueprintUI.AddBox(BlueprintUI.Stretch(BlueprintUI.NewRect("Stitch", chip), 6f, 6f, 6f, 6f), Color.clear, UIColors.WithAlpha(UIColors.Gold, 0.6f), 1.5f, BlueprintUI.Round(10f));
        inner.SetDash(true, 7f, 5f);
        Button button = BlueprintUI.MakeButton(box);
        button.onClick.AddListener(() => system?.View?.Open());

        title = BlueprintUI.Caps(BlueprintUI.Text(chip, "Title", "", 15f, UIColors.Gold, font));
        BlueprintUI.TopLeft(title.rectTransform, 18f, 12f, 220f, 22f);
        amount = BlueprintUI.Text(chip, "Amount", "$0", 30f, UIColors.PaperHi, font, TextAlignmentOptions.Left, true);
        BlueprintUI.TopLeft(amount.rectTransform, 16f, 34f, 230f, 40f);
        BlueprintUI.AutoSize(amount, 16f);
        hint = BlueprintUI.Text(chip, "Hint", "", 15f, UIColors.StickyGreen, font);
        BlueprintUI.TopLeft(hint.rectTransform, 18f, 76f, 290f, 24f);
        BlueprintUI.AutoSize(hint, 10f);
        cash = BlueprintUI.Text(chip, "Cash", "", 15f, UIColors.Gold, font, TextAlignmentOptions.Left, true);
        BlueprintUI.TopLeft(cash.rectTransform, 18f, 104f, 220f, 24f);
        BlueprintUI.AutoSize(cash, 10f);

        RectTransform key = BlueprintUI.Keycap(chip, "Tab", font, 17f);
        key.anchorMin = key.anchorMax = new Vector2(1f, 1f);
        key.pivot = new Vector2(1f, 1f);
        key.anchoredPosition = new Vector2(-16f, -14f);
        key.sizeDelta = new Vector2(58f, 36f);

        dots = new Image[CashLedger.DeptCount];
        for (int d = 0; d < dots.Length; d++)
        {
            RectTransform dot = BlueprintUI.NewRect("Dot" + d, chip);
            dot.anchorMin = dot.anchorMax = new Vector2(1f, 0f);
            dot.pivot = new Vector2(1f, 0f);
            dot.anchoredPosition = new Vector2(-18f - (dots.Length - 1 - d) * 24f, 18f);
            dot.sizeDelta = new Vector2(16f, 16f);
            dots[d] = dot.gameObject.AddComponent<Image>();
            dots[d].raycastTarget = false;
        }
        L10nFont.AttachAll(chip);
        canvas.enabled = false;
    }

    void Update()
    {
        if (system == null) system = CashGrabSystem.Instance;
        bool show = system != null && system.Ready && !system.IsFloorOpen && !TowerBuilder.IsBuildModeOpen
                    && (!UIManager.Exists || !UIManager.Instance.IsAnyPanelOpen())
                    && (system.Sim.FloorValue > 0.0 || system.Stream.RatePerSecond > 0.0 || system.DeptLevel(0) > 0);
        if (canvas.enabled != show) canvas.enabled = show;
        if (!show) return;

        pulse = Mathf.Max(0f, pulse - Time.unscaledDeltaTime * 1.2f);
        for (int d = 0; d < grabFlash.Length; d++) grabFlash[d] = Mathf.Max(0f, grabFlash[d] - Time.unscaledDeltaTime);

        refresh -= Time.unscaledDeltaTime;
        if (refresh <= 0f)
        {
            refresh = 0.2f;
            double floor = system.Sim.FloorValue;
            title.text = CashText.Get("cash.peek.title");
            amount.text = UIFormat.Money(floor);
            bool grabbing = false;
            for (int d = 0; d < dots.Length; d++)
            {
                bool on = system.DeptLevel(d) > 0;
                dots[d].enabled = on;
                if (on && grabFlash[d] > 0f) grabbing = true;
            }
            hint.text = grabbing ? CashText.Get("cash.peek.grabbing") : CashText.Get("cash.peek.hint");
            hint.color = grabbing ? UIColors.StickyPink : UIColors.StickyGreen;
            cash.text = CashText.Get("cash.peek.your_cash", ("amount", UIFormat.Money(system.Ledger.PlayerCash)));

            // Money piling up: a little nudge (and a one-time toast for a new player)
            if (floor > lastFloor * 1.5 + 50.0 && floor > 200.0) pulse = Mathf.Max(pulse, 0.6f);
            lastFloor = floor;
            if (!remindedOnce && floor > 300.0 && PlayerPrefs.GetInt((DemoMode.Active ? "Demo." : "") + "CashGrab.Tip.welcome", 0) == 0)
            {
                remindedOnce = true;
                GameNotifications.Show(CashText.Get("cash.toast.money_waiting"), NotificationKind.Special);
            }
        }

        for (int d = 0; d < dots.Length; d++)
        {
            float f = grabFlash[d];
            Color c = CashRules.DeptColors[d];
            dots[d].color = Color.Lerp(UIColors.WithAlpha(c, 0.55f), Color.white, f * 0.6f);
            dots[d].rectTransform.localScale = Vector3.one * (1f + f * 0.4f);
        }
        float s = 1f + Mathf.Sin(Time.unscaledTime * 10f) * 0.03f * pulse + pulse * 0.04f;
        chip.localScale = new Vector3(s, s, 1f);
    }
}
