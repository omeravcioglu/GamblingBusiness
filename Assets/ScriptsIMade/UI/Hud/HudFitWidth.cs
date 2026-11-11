using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Keeps a button or chip at least its designed width, and wider when its content row needs more room (a longer
/// language, a bigger number): width = max(minWidth, the content layout's preferred width), growing around the pivot.
/// Sizes in LateUpdate, from the layout drawn last frame. A parent layout group with childControlWidth off places it.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(RectTransform))]
public class HudFitWidth : MonoBehaviour
{
    [Tooltip("The layout group that sets the width (e.g. a marker button's Face/Content row)")]
    public RectTransform content;
    [Tooltip("The designed width: never narrower")]
    public float minWidth;

    void LateUpdate()
    {
        if (content == null) return;
        var rect = (RectTransform)transform;
        float width = Mathf.Max(minWidth, Mathf.Ceil(LayoutUtility.GetPreferredWidth(content)));
        if (Mathf.Abs(rect.sizeDelta.x - width) > 0.5f)
            rect.sizeDelta = new Vector2(width, rect.sizeDelta.y);
    }
}
