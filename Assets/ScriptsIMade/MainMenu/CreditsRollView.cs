using UnityEngine;

/// <summary>
/// Credits (canvas board "Credits"): the receipt prints out of the office adding machine. It scrolls up from its
/// start until the barcode sits just above the machine's slot; holding Space prints faster. The receipt's lines
/// are texts in the prefab (fill the [placeholders] there).
/// </summary>
[DisallowMultipleComponent]
public class CreditsRollView : MonoBehaviour
{
    [Tooltip("The receipt (anchored at the top of its clip area)")]
    public RectTransform roll;
    [Tooltip("Top of the receipt when the page opens (y down, px from the clip area's top)")]
    public float startTop = 60f;
    [Tooltip("Top of the receipt when it's fully printed")]
    public float endTop = -546f;
    public float speed = 42f;
    public float fastMultiplier = 5f;
    [Tooltip("Pause before it starts printing")]
    public float delay = 0.6f;

    float top;
    float wait;

    void OnEnable()
    {
        top = startTop;
        wait = delay;
        Apply();
    }

    void Update()
    {
        if (roll == null) return;
        bool fast = Input.GetKey(KeyCode.Space);
        if (wait > 0f && !fast)
        {
            wait -= Time.unscaledDeltaTime;
            return;
        }
        if (top <= endTop) return;
        top = Mathf.Max(endTop, top - speed * (fast ? fastMultiplier : 1f) * Time.unscaledDeltaTime);
        Apply();
    }

    void Apply()
    {
        if (roll != null) roll.anchoredPosition = new Vector2(roll.anchoredPosition.x, -top);
    }

    /// <summary>Jump to a point of the roll, 0 = start, 1 = fully printed (previews).</summary>
    public void ShowAt(float t)
    {
        top = Mathf.Lerp(startTop, endTop, Mathf.Clamp01(t));
        Apply();
    }
}
