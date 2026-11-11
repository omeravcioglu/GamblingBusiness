using TMPro;
using UnityEngine;

/// <summary>
/// Keeps this RectTransform as wide as a text's preferred width plus padding, growing around its pivot (the promotion
/// memo's Nice button grows leftwards from its right edge, the stamp from its centre). TMP can only measure text once
/// it's awake, so this sizes in LateUpdate, before the frame is drawn.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class StatusFitWidth : MonoBehaviour
{
    public TMP_Text text;
    [Tooltip("Width added to the text's width (both sides together)")]
    public float padding;

    void LateUpdate()
    {
        if (text == null) return;
        var rect = (RectTransform)transform;
        float width = Mathf.Ceil(text.preferredWidth) + padding;
        if (Mathf.Abs(rect.sizeDelta.x - width) > 0.5f)
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
    }
}
