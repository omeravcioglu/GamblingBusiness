using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Designed view for GameNotifications: memo-slip toasts stacked from the bottom-right corner
/// (newest at the bottom), coloured by NotificationKind, at most 4 on screen; more wait their turn.
/// Runs on real time, so toasts also leave while the game is paused. While this view is enabled the
/// built-in fallback toasts are switched off.
/// </summary>
[DisallowMultipleComponent]
public class HudToasts : MonoBehaviour
{
    [Tooltip("Inactive toast used as the template (anchored bottom-right)")]
    public RectTransform template;
    public float spacing = 12f;
    public float minHeight = 116f;
    public float textVerticalPadding = 28f;
    public int maxVisible = 4;
    public int maxWaiting = 8;
    public float visibleSeconds = 4f;
    public float busyVisibleSeconds = 2f;
    public float slideSeconds = 0.25f;
    public float fadeSeconds = 0.4f;

    [Header("Kinds (Info, Good, Bad, Special)")]
    public Sprite infoIcon;
    public Sprite goodIcon;
    public Sprite badIcon;
    public Sprite specialIcon;

    class Toast
    {
        public RectTransform rect;
        public CanvasGroup group;
        public float age;
        public float height;
        public float y;          // current animated bottom offset
    }

    struct Waiting
    {
        public string message;
        public NotificationKind kind;
    }

    readonly List<Toast> toasts = new List<Toast>();
    readonly Queue<Waiting> waiting = new Queue<Waiting>();
    bool subscribed;

    void OnEnable()
    {
        if (template != null)
            template.gameObject.SetActive(false);
        GameNotifications.Posted += OnPosted;
        GameNotifications.UseFallbackView = false;
        subscribed = true;
    }

    void OnDisable()
    {
        if (subscribed)
        {
            GameNotifications.Posted -= OnPosted;
            GameNotifications.UseFallbackView = true;
            subscribed = false;
        }
        foreach (var t in toasts)
            if (t.rect != null) Destroy(t.rect.gameObject);
        toasts.Clear();
        waiting.Clear();
    }

    void OnPosted(string message, NotificationKind kind)
    {
        if (!isActiveAndEnabled) return;
        if (toasts.Count >= maxVisible)
        {
            waiting.Enqueue(new Waiting { message = message, kind = kind });
            while (waiting.Count > maxWaiting)
                waiting.Dequeue();
            return;
        }
        Spawn(message, kind);
    }

    void Spawn(string message, NotificationKind kind)
    {
        if (template == null) return;
        RectTransform rt = Instantiate(template, template.parent);
        rt.name = "Toast";
        rt.gameObject.SetActive(true);
        var view = rt.GetComponent<HudToastView>();
        float height = minHeight;
        if (view != null)
        {
            view.Set(message, kind, IconFor(kind));
            if (view.message != null)
            {
                float textWidth = view.message.rectTransform.rect.width;
                var css = view.message.GetComponent<CssLineHeight>();
                float pref = css != null ? CssLineHeight.Height(view.message, css.lineHeight, textWidth)
                                         : view.message.GetPreferredValues(message, textWidth, 0f).y;
                height = Mathf.Max(minHeight, Mathf.Ceil(pref) + textVerticalPadding);
            }
        }
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
        var group = rt.GetComponent<CanvasGroup>();
        if (group == null) group = rt.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false;
        group.interactable = false;
        group.alpha = 0f;

        // The newest toast slides in at the bottom; the older ones move up in Update
        var toast = new Toast { rect = rt, group = group, height = height, age = 0f, y = template.anchoredPosition.y };
        rt.anchoredPosition = new Vector2(template.anchoredPosition.x + 60f, toast.y);
        toasts.Add(toast);
    }

    Sprite IconFor(NotificationKind kind)
    {
        switch (kind)
        {
            case NotificationKind.Good: return goodIcon;
            case NotificationKind.Bad: return badIcon;
            case NotificationKind.Special: return specialIcon;
            default: return infoIcon;
        }
    }

    void Update()
    {
        if (toasts.Count == 0)
        {
            if (waiting.Count > 0)
            {
                var w = waiting.Dequeue();
                Spawn(w.message, w.kind);
            }
            return;
        }

        float dt = Time.unscaledDeltaTime;
        float life = waiting.Count > 0 ? busyVisibleSeconds : visibleSeconds;
        float baseY = template != null ? template.anchoredPosition.y : 32f;
        float stackY = baseY;

        for (int i = toasts.Count - 1; i >= 0; i--)
        {
            var t = toasts[i];
            t.age += dt;
            // ease towards the stacked position
            t.y = Mathf.MoveTowards(t.y, stackY, 900f * dt);
            float slide = Mathf.Clamp01(t.age / Mathf.Max(0.01f, slideSeconds));
            float ease = 1f - (1f - slide) * (1f - slide);
            float fade = t.age > life ? Mathf.Clamp01(1f - (t.age - life) / Mathf.Max(0.01f, fadeSeconds)) : 1f;
            t.group.alpha = ease * fade;
            Vector2 home = template.anchoredPosition;
            t.rect.anchoredPosition = new Vector2(home.x + (1f - ease) * 60f, t.y);
            stackY += t.height + spacing;
        }

        for (int i = toasts.Count - 1; i >= 0; i--)
        {
            var t = toasts[i];
            if (t.age > life + fadeSeconds)
            {
                Destroy(t.rect.gameObject);
                toasts.RemoveAt(i);
            }
        }

        while (toasts.Count < maxVisible && waiting.Count > 0)
        {
            var w = waiting.Dequeue();
            Spawn(w.message, w.kind);
        }
    }
}
