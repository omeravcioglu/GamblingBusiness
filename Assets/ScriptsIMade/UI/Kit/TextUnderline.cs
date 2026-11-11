using System.Collections.Generic;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The design's thick underlines (CSS text-decoration: underline solid &lt;colour&gt; &lt;thickness&gt;, text-underline-offset)
/// under the parts of a text wrapped in &lt;u&gt; or &lt;u color=#RRGGBB&gt;. TMP's own underline is a hairline at the
/// font's position, so the tags are turned into links ("ul" or "ul#RRGGBB") and a band is drawn under each run, just
/// behind the text like TextHighlighter. Scripts can keep writing plain &lt;u&gt; tags.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
[DisallowMultipleComponent]
public class TextUnderline : MonoBehaviour
{
    public const string LinkPrefix = "ul";

    [Tooltip("Colour of a <u> without its own colour")]
    public Color color = UIColors.Ink;
    [Tooltip("Line thickness in UI units")]
    public float thickness = 3f;
    [Tooltip("Gap between the baseline and the top of the line (text-underline-offset)")]
    public float offset = 6f;
    [Tooltip("Keep each underlined phrase on one line (CSS white-space: nowrap)")]
    public bool keepTogether;

    static readonly Regex OpenTag = new Regex("<u(?:\\s+color\\s*=\\s*\"?(#[0-9A-Fa-f]{6,8})\"?)?\\s*>", RegexOptions.Compiled);

    TMP_Text text;
    RectTransform container;
    readonly List<Image> lines = new List<Image>();
    string lastText;
    Vector2 lastSize;

    void Awake()
    {
        text = GetComponent<TMP_Text>();
    }

    void OnEnable()
    {
        if (text == null) text = GetComponent<TMP_Text>();
        lastText = null;
        Convert();
    }

    void OnDisable()
    {
        if (container != null) container.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (container == null) return;
        if (Application.isPlaying) Destroy(container.gameObject);
        else DestroyImmediate(container.gameObject);
    }

    void LateUpdate()
    {
        Convert();
        var rt = text.rectTransform;
        if (lastText != text.text || lastSize != rt.rect.size || container == null)
        {
            lastText = text.text;
            lastSize = rt.rect.size;
            Rebuild();
        }
        else
        {
            SyncContainer();
        }
    }

    /// <summary>&lt;u&gt; tags become "ul" links (TMP then draws no underline of its own).</summary>
    void Convert()
    {
        string s = text.text;
        if (string.IsNullOrEmpty(s) || s.IndexOf("<u", System.StringComparison.Ordinal) < 0) return;
        string nobr = keepTogether ? "<nobr>" : "";
        string converted = OpenTag.Replace(s, m => (m.Groups[1].Success
            ? "<link=\"" + LinkPrefix + m.Groups[1].Value + "\">"
            : "<link=\"" + LinkPrefix + "\">") + nobr);
        converted = converted.Replace("</u>", (keepTogether ? "</nobr>" : "") + "</link>");
        if (converted != s) text.text = converted;
    }

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
        var go = new GameObject("Underlines", typeof(RectTransform));
        go.layer = gameObject.layer;
        container = (RectTransform)go.transform;
        container.SetParent(transform.parent, false);
        go.AddComponent<LayoutElement>().ignoreLayout = true;
    }

    public void Rebuild()
    {
        if (text == null) text = GetComponent<TMP_Text>();
        Convert();
        text.ForceMeshUpdate();
        EnsureContainer();
        container.gameObject.SetActive(true);
        SyncContainer();

        int used = 0;
        TMP_TextInfo info = text.textInfo;
        for (int l = 0; l < info.linkCount; l++)
        {
            TMP_LinkInfo link = info.linkInfo[l];
            string id = link.GetLinkID();
            if (id == null || !id.StartsWith(LinkPrefix, System.StringComparison.Ordinal)) continue;
            Color c = color;
            if (id.Length > LinkPrefix.Length && ColorUtility.TryParseHtmlString(id.Substring(LinkPrefix.Length), out Color parsed)) c = parsed;

            int first = link.linkTextfirstCharacterIndex;
            int last = first + link.linkTextLength - 1;
            int start = first;
            while (start <= last && start < info.characterCount)
            {
                int line = info.characterInfo[start].lineNumber;
                int end = start;
                while (end + 1 <= last && end + 1 < info.characterCount && info.characterInfo[end + 1].lineNumber == line)
                    end++;
                AddLine(ref used, info, start, end, c);
                start = end + 1;
            }
        }
        for (int i = used; i < lines.Count; i++)
            lines[i].enabled = false;
    }

    void AddLine(ref int used, TMP_TextInfo info, int start, int end, Color c)
    {
        while (start < end && !info.characterInfo[start].isVisible) start++;
        while (end > start && !info.characterInfo[end].isVisible) end--;
        var a = info.characterInfo[start];
        var b = info.characterInfo[end];
        if (!a.isVisible) return;
        float x1 = a.origin, x2 = b.xAdvance;
        float y2 = a.baseLine - offset;   // top of the line
        float y1 = y2 - thickness;

        Image line;
        if (used < lines.Count)
        {
            line = lines[used];
        }
        else
        {
            var go = new GameObject("Line", typeof(RectTransform));
            go.layer = gameObject.layer;
            go.transform.SetParent(container, false);
            line = go.AddComponent<Image>();
            line.raycastTarget = false;
            lines.Add(line);
        }
        used++;
        line.enabled = true;
        line.color = c;
        var r = line.rectTransform;
        Vector2 pivotOffset = new Vector2(text.rectTransform.pivot.x * text.rectTransform.rect.width,
                                          text.rectTransform.pivot.y * text.rectTransform.rect.height);
        r.anchorMin = r.anchorMax = Vector2.zero;
        r.pivot = Vector2.zero;
        r.anchoredPosition = new Vector2(x1, y1) + pivotOffset;
        r.sizeDelta = new Vector2(Mathf.Max(0f, x2 - x1), thickness);
    }
}
