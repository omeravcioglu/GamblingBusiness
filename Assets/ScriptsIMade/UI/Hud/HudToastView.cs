using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One memo-slip toast: icon circle and kind chip in the kind's sticky-note colour, the message on the right. Messages
/// arrive in the player's language; the kind tag comes from the Hud String Table.
/// </summary>
[DisallowMultipleComponent]
public class HudToastView : MonoBehaviour
{
    const string T = "Hud";   // Assets/Localization/Source/Hud.csv

    public TMP_Text message;
    public Image iconCircle;
    public Image icon;
    public Image kindChip;
    public TMP_Text kindLabel;

    void Awake()
    {
        L10nFont.Attach(message);
        L10nFont.Attach(kindLabel);
    }

    public static Color ColorFor(NotificationKind kind)
    {
        switch (kind)
        {
            case NotificationKind.Good: return UIColors.StickyGreen;
            case NotificationKind.Bad: return UIColors.StickyPink;
            case NotificationKind.Special: return UIColors.StickyYellow;
            default: return UIColors.StickyBlue;
        }
    }

    public static string LabelFor(NotificationKind kind)
    {
        switch (kind)
        {
            case NotificationKind.Good: return L10n.Get(T, "hud.toast.kind.good");
            case NotificationKind.Bad: return L10n.Get(T, "hud.toast.kind.bad");
            case NotificationKind.Special: return L10n.Get(T, "hud.toast.kind.special");
            default: return L10n.Get(T, "hud.toast.kind.info");
        }
    }

    public void Set(string text, NotificationKind kind, Sprite iconSprite)
    {
        Color c = ColorFor(kind);
        if (message != null) message.text = text;
        if (iconCircle != null) iconCircle.color = c;
        if (kindChip != null) kindChip.color = c;
        if (kindLabel != null) kindLabel.text = LabelFor(kind);
        if (icon != null && iconSprite != null) icon.sprite = iconSprite;
    }
}
