using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-time tips on the cash floor, as a sticky note at the top right of the felt: what the floor is, the first deposit,
/// each department's arrival, each kind of interference (and its counter), the shop. Once per player profile (PlayerPrefs
/// "CashGrab.Tip.*"; the demo keeps its own). One note at a time, queued; "Got it" or 12 seconds dismisses it.
/// </summary>
public class CashCoach : MonoBehaviour
{
    const float ShowSeconds = 12f;

    CashFloorView view;
    TMP_FontAsset font;
    RectTransform note;
    TextMeshProUGUI title, body, button;
    readonly Queue<string> queue = new Queue<string>();
    readonly HashSet<string> queued = new HashSet<string>();
    string showing;
    float timer;
    float appear;

    static string Prefix => (DemoMode.Active ? "Demo." : "") + "CashGrab.Tip.";

    public static CashCoach Create(CashFloorView owner, RectTransform parent, TMP_FontAsset font)
    {
        RectTransform rect = BlueprintUI.NewRect("Coach", parent);
        var coach = rect.gameObject.AddComponent<CashCoach>();
        coach.view = owner;
        coach.font = font;
        coach.note = rect;
        coach.Build();
        rect.gameObject.SetActive(false);
        return coach;
    }

    void Build()
    {
        note.anchorMin = note.anchorMax = new Vector2(1f, 1f);
        note.pivot = new Vector2(1f, 1f);
        note.anchoredPosition = new Vector2(-48f, -130f);
        note.sizeDelta = new Vector2(400f, 230f);
        BlueprintBox paper = BlueprintUI.AddBox(note, UIColors.StickyYellow, UIColors.Ink, 2.5f, BlueprintUI.RadiusLarge, true);
        paper.SetShadow(UIColors.Dark(0.45f), new Vector2(6f, -8f));

        title = BlueprintUI.Text(note, "Title", "", 24f, UIColors.Ink, font, TextAlignmentOptions.Left, true);
        BlueprintUI.TopLeft(title.rectTransform, 22f, 16f, 356f, 32f);
        BlueprintUI.AutoSize(title, 15f);
        body = BlueprintUI.Wrap(BlueprintUI.Text(note, "Body", "", 18f, UIColors.InkSoft, font));
        BlueprintUI.TopLeft(body.rectTransform, 22f, 52f, 356f, 120f);
        BlueprintUI.AutoSize(body, 12f);

        BlueprintBox ok = BlueprintUI.Box(note, "Ok", UIColors.PaperHi, UIColors.Ink, 2f, BlueprintUI.RadiusSmall, true);
        ok.rectTransform.anchorMin = ok.rectTransform.anchorMax = new Vector2(1f, 0f);
        ok.rectTransform.pivot = new Vector2(1f, 0f);
        ok.rectTransform.anchoredPosition = new Vector2(-18f, 16f);
        ok.rectTransform.sizeDelta = new Vector2(130f, 42f);
        ok.SetShadow(UIColors.Ink, new Vector2(0f, -3f));
        BlueprintUI.MakeButton(ok).onClick.AddListener(Dismiss);
        button = BlueprintUI.Text(ok.transform, "Label", "", 18f, UIColors.Ink, font, TextAlignmentOptions.Center, true);
        BlueprintUI.Stretch(button.rectTransform);
    }

    static bool Seen(string id) => PlayerPrefs.GetInt(Prefix + id, 0) == 1;

    static void MarkSeen(string id)
    {
        PlayerPrefs.SetInt(Prefix + id, 1);
        PlayerPrefs.Save();
    }

    /// <summary>Queue a tip (ignored when this profile has seen it, or it's already waiting).</summary>
    public void Tip(string id)
    {
        if (string.IsNullOrEmpty(id) || Seen(id) || queued.Contains(id) || showing == id) return;
        queue.Enqueue(id);
        queued.Add(id);
    }

    void Update()
    {
        if (showing == null)
        {
            if (queue.Count == 0 || view == null || !view.IsOpen) return;
            Show(queue.Dequeue());
            return;
        }
        timer -= Time.unscaledDeltaTime;
        appear = Mathf.MoveTowards(appear, 1f, Time.unscaledDeltaTime * 6f);
        float s = 0.85f + 0.15f * Mathf.SmoothStep(0f, 1f, appear);
        note.localScale = new Vector3(s, s, 1f);
        note.localRotation = Quaternion.Euler(0f, 0f, 1.5f * (1f - appear) + 1f);
        if (timer <= 0f) Dismiss();
    }

    void Show(string id)
    {
        queued.Remove(id);
        showing = id;
        timer = ShowSeconds;
        appear = 0f;
        title.text = CashText.Get("cash.tip." + id + ".title");
        body.text = CashText.Get("cash.tip." + id + ".body");
        button.text = CashText.Get("cash.tip.ok");
        note.gameObject.SetActive(true);
        note.SetAsLastSibling();
        CashGrabSystem.Instance?.Audio?.Play("stack_tick", 0.6f, 0.8f, 0.1f, 0f);
    }

    public void Dismiss()
    {
        if (showing == null) return;
        MarkSeen(showing);
        showing = null;
        note.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Moments
    // ------------------------------------------------------------------

    public void OnFloorOpened()
    {
        Tip("welcome");
        CashGrabSystem system = CashGrabSystem.Instance;
        if (system != null)
            for (int t = 2; t < CashRules.ToolCount; t++)
                if (system.ToolLevel((CashTool)t) == 0 && system.ToolUnlocked((CashTool)t)) Tip("tool_unlocked_" + t);
    }

    public void OnDeposit(double value)
    {
        Tip("first_deposit");
    }

    public void OnShopOpened()
    {
        Tip("shop");
    }

    public void OnDepartment(int dept, bool joined, int level, bool away = false)
    {
        if (joined) Tip("dept_" + CashRules.DeptIds[Mathf.Clamp(dept, 0, 2)]);
        else if (level >= 2) Tip("dept_upgrades");
    }

    public void OnInterference(CashInterference kind, int dept)
    {
        Tip("interference_" + CashText.InterferenceId(kind));
        Tip("stamp");   // the free answer to any foul
    }

    // ---- The design canvas's extras (2026-11-24) ----

    /// <summary>The CEO picked up their first bill with bare hands: explain the hand of five.</summary>
    public void OnHandsPicked() => Tip("hands");

    public void OnPokerHand() => Tip("hands");

    public void OnBoost(CashBoost boost)
    {
        Tip(boost == CashBoost.Jackpot ? "jackpot" : boost == CashBoost.HappyHour ? "happy_hour" : "payday");
    }

    public void OnGolden() => Tip("golden");

    public void OnMarked() => Tip("marked");

    public void OnCount(bool weekly) => Tip(weekly ? "count" : "tally");
}
