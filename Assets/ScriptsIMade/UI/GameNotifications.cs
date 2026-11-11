using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>What a notification is about. The fallback view colours its accent bar by this.</summary>
public enum NotificationKind
{
    Info,     // neutral information
    Good,     // money gained, lucky effects
    Bad,      // money lost, refusals
    Special   // rare or big moments: jackpots, skill unlocks
}

/// <summary>
/// Short game messages ("Lottery Ticket: +$5,000"), so random effects and rewards don't change money silently.
/// - Post from anywhere: GameNotifications.Show("Skill point earned!", NotificationKind.Good).
/// - A designed UI subscribes to Posted and sets UseFallbackView = false.
/// - Until then this component is the fallback view: stacked toasts at the bottom-center of the screen, newest at
///   the bottom, at most MaxVisible at once (more wait their turn), each fading out after a few seconds of real time.
///   It creates itself on the first message while playing, has its own overlay canvas (no raycasts, so it never
///   blocks clicks), survives scene loads and clears its toasts on a scene change.
/// </summary>
public class GameNotifications : MonoBehaviour
{
    /// <summary>Fires for every message, whether or not the fallback view shows it.</summary>
    public static event Action<string, NotificationKind> Posted;

    /// <summary>Show the built-in toasts. Set false once a designed UI handles Posted.</summary>
    public static bool UseFallbackView = true;

    const int MaxVisible = 4;
    const int MaxWaiting = 8;             // older waiting messages are dropped beyond this
    const float VisibleSeconds = 3.5f;
    const float BusyVisibleSeconds = 1.75f; // shorter while messages are waiting for a slot
    const float FadeSeconds = 0.5f;
    const float ToastWidth = 720f;
    const float FontSize = 24f;
    const float BottomMargin = 48f;       // reference pixels (1920x1080) above the bottom edge
    const int CanvasSortingOrder = 900;   // above the HUD and its panels

    static readonly Color BackgroundColor = new Color(0.08f, 0.09f, 0.11f, 0.9f);

    static GameNotifications instance;
    static bool quitting;

    class Toast
    {
        public GameObject root;
        public CanvasGroup group;
        public string message;
        public float age;
    }

    struct Waiting
    {
        public string message;
        public NotificationKind kind;
    }

    readonly List<Toast> toasts = new List<Toast>();
    readonly Queue<Waiting> waiting = new Queue<Waiting>();
    RectTransform stack;

    // ------------------------------------------------------------------
    // Static API
    // ------------------------------------------------------------------

    /// <summary>Post a message (plain text: the game font has no emoji or symbols).</summary>
    public static void Show(string message, NotificationKind kind = NotificationKind.Info)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        try { Posted?.Invoke(message, kind); }
        catch (Exception e) { Debug.LogException(e); }
        GameSfx.PlayNotification(kind);   // soft and Low priority: gives way to the sound of what caused the message

        if (!UseFallbackView || quitting || !Application.isPlaying) return;

        GameNotifications view = GetOrCreateView();
        if (view != null)
            view.Enqueue(message, kind);
    }

    /// <summary>Remove every toast (and waiting message) from the fallback view.</summary>
    public static void ClearAll()
    {
        if (instance != null)
            instance.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics: start every session clean
        instance = null;
        quitting = false;
        Posted = null;
        UseFallbackView = true;
        Application.quitting -= OnQuitting;
        Application.quitting += OnQuitting;
    }

    static void OnQuitting()
    {
        quitting = true;
    }

    static GameNotifications GetOrCreateView()
    {
        if (instance != null) return instance;

        GameObject go = new GameObject("GameNotifications", typeof(RectTransform));
        DontDestroyOnLoad(go);
        return go.AddComponent<GameNotifications>(); // Awake registers it as the instance
    }

    // ------------------------------------------------------------------
    // Fallback view
    // ------------------------------------------------------------------

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }
        instance = this;
        BuildCanvas();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (instance == this)
            instance = null;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (mode == LoadSceneMode.Single)
            Clear();
    }

    void BuildCanvas()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CanvasSortingOrder;

        CanvasScaler scaler = GetComponent<CanvasScaler>();
        if (scaler == null) scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        // No GraphicRaycaster: toasts never take clicks

        GameObject stackObject = new GameObject("Toasts", typeof(RectTransform));
        stack = (RectTransform)stackObject.transform;
        stack.SetParent(transform, false);
        stack.anchorMin = new Vector2(0.5f, 0f);
        stack.anchorMax = new Vector2(0.5f, 0f);
        stack.pivot = new Vector2(0.5f, 0f);          // grows upward from the bottom
        stack.anchoredPosition = new Vector2(0f, BottomMargin);
        stack.sizeDelta = new Vector2(ToastWidth, 0f);

        VerticalLayoutGroup layout = stackObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.LowerCenter;
        layout.spacing = 8f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        ContentSizeFitter fitter = stackObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    void Enqueue(string message, NotificationKind kind)
    {
        // The same message while it is still showing: restart its timer instead of stacking a copy
        for (int i = 0; i < toasts.Count; i++)
        {
            Toast shown = toasts[i];
            if (shown.root == null || shown.message != message) continue;

            shown.age = 0f;
            shown.group.alpha = 1f;
            shown.root.transform.SetAsLastSibling();
            toasts.RemoveAt(i);
            toasts.Add(shown);
            return;
        }

        if (toasts.Count < MaxVisible && waiting.Count == 0)
        {
            AddToast(message, kind);
            return;
        }

        waiting.Enqueue(new Waiting { message = message, kind = kind });
        while (waiting.Count > MaxWaiting)
            waiting.Dequeue();
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        float visibleFor = waiting.Count > 0 ? BusyVisibleSeconds : VisibleSeconds;

        for (int i = toasts.Count - 1; i >= 0; i--)
        {
            Toast toast = toasts[i];
            if (toast.root == null)
            {
                toasts.RemoveAt(i);
                continue;
            }

            toast.age += dt;
            if (toast.age >= visibleFor + FadeSeconds)
            {
                RemoveToast(i);
                continue;
            }
            toast.group.alpha = toast.age <= visibleFor ? 1f : 1f - (toast.age - visibleFor) / FadeSeconds;
        }

        while (waiting.Count > 0 && toasts.Count < MaxVisible)
        {
            Waiting next = waiting.Dequeue();
            AddToast(next.message, next.kind);
        }
    }

    void AddToast(string message, NotificationKind kind)
    {
        if (stack == null) return;

        GameObject root = new GameObject("Toast", typeof(RectTransform));
        root.transform.SetParent(stack, false);
        root.transform.SetAsLastSibling(); // newest at the bottom

        Image background = root.AddComponent<Image>();
        background.color = BackgroundColor;
        background.raycastTarget = false;

        CanvasGroup group = root.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;

        HorizontalLayoutGroup row = root.AddComponent<HorizontalLayoutGroup>();
        row.padding = new RectOffset(0, 20, 10, 10);
        row.spacing = 16f;
        row.childAlignment = TextAnchor.MiddleLeft;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = false;
        row.childForceExpandHeight = true;

        // Coloured bar on the left: what kind of message this is
        GameObject accent = new GameObject("Accent", typeof(RectTransform));
        accent.transform.SetParent(root.transform, false);
        Image accentImage = accent.AddComponent<Image>();
        accentImage.color = AccentColor(kind);
        accentImage.raycastTarget = false;
        LayoutElement accentLayout = accent.AddComponent<LayoutElement>();
        accentLayout.minWidth = 8f;
        accentLayout.preferredWidth = 8f;

        GameObject textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(root.transform, false);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = message;
        text.fontSize = FontSize;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        LayoutElement textLayout = textObject.AddComponent<LayoutElement>();
        textLayout.flexibleWidth = 1f;
        textLayout.minHeight = 32f;

        toasts.Add(new Toast { root = root, group = group, message = message, age = 0f });
    }

    void RemoveToast(int index)
    {
        Toast toast = toasts[index];
        toasts.RemoveAt(index);
        if (toast.root != null)
            Destroy(toast.root);
    }

    void Clear()
    {
        waiting.Clear();
        for (int i = toasts.Count - 1; i >= 0; i--)
            RemoveToast(i);
    }

    static Color AccentColor(NotificationKind kind)
    {
        switch (kind)
        {
            case NotificationKind.Good: return new Color(0.3f, 0.85f, 0.4f);
            case NotificationKind.Bad: return new Color(0.95f, 0.3f, 0.3f);
            case NotificationKind.Special: return new Color(1f, 0.78f, 0.2f);
            default: return new Color(0.4f, 0.65f, 1f);
        }
    }
}
