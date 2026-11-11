using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The design's highlighter mark: a yellow band behind part of a text, over the lower half of the letters
/// (CSS: linear-gradient(transparent 52%, #FFE14D 52% 90%, transparent 90%), padding 0 6px).
/// Wrap the marked words in a TMP link with the id "hl": "Hire someone with &lt;link="hl"&gt;IQ 120+&lt;/link&gt;".
/// The bands are images in a container just behind the text (TMP's own &lt;mark&gt; draws over the ink).
/// </summary>
[RequireComponent(typeof(TMP_Text))]
[DisallowMultipleComponent]
public class TextHighlighter : MonoBehaviour
{
    public const string LinkId = "hl";

    public Color color = new Color32(0xFF, 0xE1, 0x4D, 0xF2);
    [Tooltip("Band from this fraction of the line height, measured from the top")]
    [Range(0f, 1f)] public float bandTop = 0.52f;
    [Range(0f, 1f)] public float bandBottom = 0.90f;
    public float paddingX = 6f;
    [Tooltip("Keep each marked phrase on one line (CSS white-space: nowrap on the mark)")]
    public bool keepTogether;

    TMP_Text text;
    RectTransform container;
    readonly List<Image> bands = new List<Image>();
    string lastText;
    Vector2 lastSize;

    void Awake()
    {
        text = GetComponent<TMP_Text>();
    }

    void OnEnable()
    {
        if (text == null) text = GetComponent<TMP_Text>();
        TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);
        lastText = null;
    }

    void OnDisable()
    {
        TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
        if (container != null) container.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (container == null) return;
        if (Application.isPlaying) Destroy(container.gameObject);
        else DestroyImmediate(container.gameObject);
    }

    void OnTextChanged(Object changed)
    {
        if (changed == text) lastText = null;
    }

    /// <summary>&lt;link="hl"&gt;words&lt;/link&gt; -&gt; &lt;link="hl"&gt;&lt;nobr&gt;words&lt;/nobr&gt;&lt;/link&gt; when keepTogether.</summary>
    void KeepTogether()
    {
        if (!keepTogether) return;
        string s = text.text;
        const string open = "<link=\"" + LinkId + "\">";
        if (string.IsNullOrEmpty(s) || s.IndexOf(open, System.StringComparison.Ordinal) < 0) return;
        var sb = new System.Text.StringBuilder(s.Length + 32);
        int i = 0;
        while (true)
        {
            int a = s.IndexOf(open, i, System.StringComparison.Ordinal);
            int b = a < 0 ? -1 : s.IndexOf("</link>", a + open.Length, System.StringComparison.Ordinal);
            if (a < 0 || b < 0)
            {
                sb.Append(s, i, s.Length - i);
                break;
            }
            string inner = s.Substring(a + open.Length, b - a - open.Length);
            sb.Append(s, i, a - i).Append(open);
            if (inner.StartsWith("<nobr>", System.StringComparison.Ordinal)) sb.Append(inner);
            else sb.Append("<nobr>").Append(inner).Append("</nobr>");
            sb.Append("</link>");
            i = b + "</link>".Length;
        }
        string result = sb.ToString();
        if (result != s) text.text = result;
    }

    void LateUpdate()
    {
        KeepTogether();
        var rt = text.rectTransform;
        if (lastText != text.text || lastSize != rt.rect.size || container == null)
        {
            lastText = text.text;
            lastSize = rt.rect.size;
            Rebuild();
        }
        else
        {
            SyncContainer();   // layout groups can move the text without resizing it
        }
    }

    // Sit exactly on the text, one sibling before it so the ink draws on top
    void SyncContainer()
    {
        var rt = text.rectTransform;
        int ti = rt.GetSiblingIndex();
        int ci = container.GetSiblingIndex();
        if (ci != ti - 1)
            container.SetSiblingIndex(ci < ti ? ti - 1 : ti);
        container.anchorMin = rt.anchorMin;
        container.anchorMax = rt.anchorMax;
        container.pivot = rt.pivot;
        container.anchoredPosition = rt.anchoredPosition;
        container.sizeDelta = rt.sizeDelta;
        container.localRotation = rt.localRotation;
        container.localScale = rt.localScale;
    }

    void EnsureContainer()
    {
        if (container != null) return;
        var go = new GameObject("Highlights", typeof(RectTransform));
        go.layer = gameObject.layer;
        container = (RectTransform)go.transform;
        container.SetParent(transform.parent, false);
        var le = go.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
    }

    public void Rebuild()
    {
        if (text == null) text = GetComponent<TMP_Text>();
        KeepTogether();
        text.ForceMeshUpdate();
        EnsureContainer();
        container.gameObject.SetActive(true);

        SyncContainer();

        int used = 0;
        TMP_TextInfo info = text.textInfo;
        for (int l = 0; l < info.linkCount; l++)
        {
            TMP_LinkInfo link = info.linkInfo[l];
            if (link.GetLinkID() != LinkId) continue;
            int first = link.linkTextfirstCharacterIndex;
            int last = first + link.linkTextLength - 1;

            // one band per line the marked words cover
            int start = first;
            while (start <= last && start < info.characterCount)
            {
                int line = info.characterInfo[start].lineNumber;
                int end = start;
                while (end + 1 <= last && end + 1 < info.characterCount && info.characterInfo[end + 1].lineNumber == line)
                    end++;
                AddBand(ref used, info, start, end, line);
                start = end + 1;
            }
        }
        for (int i = used; i < bands.Count; i++)
            bands[i].enabled = false;
    }

    void AddBand(ref int used, TMP_TextInfo info, int start, int end, int line)
    {
        // skip leading/trailing spaces
        while (start < end && !info.characterInfo[start].isVisible) start++;
        while (end > start && !info.characterInfo[end].isVisible) end--;
        var a = info.characterInfo[start];
        var b = info.characterInfo[end];
        // CSS: the band covers bandTop..bandBottom of the inline box's content area (the font's ascent + descent)
        float ascent, descent;
        var font = a.fontAsset != null ? a.fontAsset : text.font;
        if (font != null && font.faceInfo.pointSize > 0)
        {
            float scale = a.pointSize / font.faceInfo.pointSize;
            ascent = font.faceInfo.ascentLine * scale;
            descent = font.faceInfo.descentLine * scale;   // negative
        }
        else
        {
            ascent = a.pointSize * 0.9f;
            descent = -a.pointSize * 0.25f;
        }
        float top = a.baseLine + ascent;
        float h = ascent - descent;
        float y1 = top - h * bandTop;
        float y2 = top - h * bandBottom;
        float x1 = a.bottomLeft.x - paddingX;
        float x2 = b.topRight.x + paddingX;

        Image band;
        if (used < bands.Count)
        {
            band = bands[used];
        }
        else
        {
            var go = new GameObject("Band", typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(container, false);
            band = go.AddComponent<Image>();
            band.raycastTarget = false;
            bands.Add(band);
        }
        used++;
        band.enabled = true;
        band.color = color;
        var r = band.rectTransform;
        // text-local coordinates are relative to the text's pivot; the container matches the text rect
        Vector2 pivotOffset = new Vector2(text.rectTransform.pivot.x * text.rectTransform.rect.width,
                                          text.rectTransform.pivot.y * text.rectTransform.rect.height);
        r.anchorMin = r.anchorMax = Vector2.zero;
        r.pivot = Vector2.zero;
        r.anchoredPosition = new Vector2(x1, y2) + pivotOffset;
        r.sizeDelta = new Vector2(x2 - x1, y1 - y2);
    }
}
