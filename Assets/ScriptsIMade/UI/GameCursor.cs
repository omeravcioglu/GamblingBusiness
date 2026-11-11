using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The game's own mouse cursor, drawn in the "Stationery vs. Felt" look by Tools/Cursors/make_cursors.py:
/// - Arrow: a folded-paper arrow, the default (also over text fields);
/// - Hover: the arrow turned gold over anything clickable (buttons, toggles, dropdowns, sliders, the office items you
///   can upgrade);
/// - Press: the gold arrow pushing in, a shade darker, while the left button is held on something clickable.
/// (make_cursors.py --style hand draws a pointing hand for Hover and Press instead.)
/// A hardware cursor (CursorMode.Auto), so it never lags behind a slow frame. Its size follows the monitor's height
/// (32/40/48/64 px). The images are PNG data in Assets/Resources/Cursors kept as TextAssets (.bytes), so the textures are
/// made here and no texture import settings are involved; cursors.json holds each image's hotspot.
/// Starts itself at boot. GameCursor.Enabled = false brings back the system cursor.
/// </summary>
public class GameCursor : MonoBehaviour
{
    public enum Look { Arrow, Hover, Press }

    const string Folder = "Cursors/";
    const float RecheckSeconds = 0.1f;          // what's under a mouse that doesn't move is checked this often
    const float SizePerScreenHeight = 0.037f;   // 40 px at 1080p, 48 at 1440p, 64 at 2160p (nearest size made)

    static GameCursor instance;
    static bool isOn = true;

    /// <summary>False shows the system cursor (true by default).</summary>
    public static bool Enabled
    {
        get => isOn;
        set
        {
            if (isOn == value) return;
            isOn = value;
            if (instance != null) instance.Invalidate();
        }
    }

    /// <summary>The look shown now.</summary>
    public static Look Current => instance != null ? instance.shown : Look.Arrow;

    [Serializable]
    class Entry
    {
        public string look;
        public int size;
        public int x;
        public int y;
    }

    [Serializable]
    class Manifest
    {
        public int[] sizes;
        public Entry[] cursors;
    }

    readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>();
    readonly List<RaycastResult> hits = new List<RaycastResult>();
    Manifest manifest;
    Look shown;
    int shownSize = -1;   // -1: nothing applied yet (or it must be applied again)
    bool systemShown;
    Vector3 lastMouse = new Vector3(float.NaN, 0f, 0f);
    float nextCheck;
    bool overClickable;
    EventSystem pointerEvents;
    PointerEventData pointer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics
        instance = null;
        isOn = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (Application.isBatchMode || instance != null) return;
        var go = new GameObject("GameCursor");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<GameCursor>();
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        var json = Resources.Load<TextAsset>(Folder + "cursors");
        if (json != null) manifest = JsonUtility.FromJson<Manifest>(json.text);
        if (manifest == null || manifest.sizes == null || manifest.sizes.Length == 0 || manifest.cursors == null)
        {
            Debug.LogWarning("[GameCursor] Resources/Cursors/cursors.json is missing or empty: keeping the system cursor.");
            manifest = null;
        }
    }

    void OnDestroy()
    {
        if (instance != this) return;
        instance = null;
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        foreach (Texture2D texture in textures.Values)
            if (texture != null) Destroy(texture);
        textures.Clear();
    }

    void OnApplicationFocus(bool focused)
    {
        // Windows can put its own cursor back while the game is in the background
        if (focused) Invalidate();
    }

    void Invalidate()
    {
        shownSize = -1;
        systemShown = false;
    }

    void Update()
    {
        if (!isOn || manifest == null)
        {
            if (!systemShown)
            {
                Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                systemShown = true;
                shownSize = -1;
            }
            return;
        }

        Look look = OverClickable() ? (Input.GetMouseButton(0) ? Look.Press : Look.Hover) : Look.Arrow;
        int size = SizeFor(Screen.currentResolution.height);
        if (look == shown && size == shownSize && !systemShown) return;

        Texture2D texture = TextureFor(look, size, out Vector2 hotspot);
        if (texture == null) return;   // an image is missing: keep what's shown
        Cursor.SetCursor(texture, hotspot, CursorMode.Auto);
        shown = look;
        shownSize = size;
        systemShown = false;
    }

    // ------------------------------------------------------------------ what's under the mouse

    bool OverClickable()
    {
        Vector3 mouse = Input.mousePosition;
        if (mouse == lastMouse && Time.unscaledTime < nextCheck) return overClickable;
        lastMouse = mouse;
        nextCheck = Time.unscaledTime + RecheckSeconds;
        overClickable = OverClickableUI(mouse) || OfficeItemInteractable.Hovered != null;
        return overClickable;
    }

    /// <summary>The topmost UI element under the mouse takes clicks (the same test the EventSystem uses) and isn't a text field.</summary>
    bool OverClickableUI(Vector3 mouse)
    {
        EventSystem events = EventSystem.current;
        if (events == null) return false;
        if (pointer == null || pointerEvents != events)
        {
            pointer = new PointerEventData(events);
            pointerEvents = events;
        }
        pointer.position = mouse;
        hits.Clear();
        events.RaycastAll(pointer, hits);
        if (hits.Count == 0) return false;

        GameObject top = hits[0].gameObject;
        GameObject handler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(top);
        if (handler == null) handler = ExecuteEvents.GetEventHandler<IPointerDownHandler>(top);
        if (handler == null) return false;
        if (handler.GetComponent<TMP_InputField>() != null || handler.GetComponent<InputField>() != null) return false;
        Selectable selectable = handler.GetComponent<Selectable>();
        return selectable == null || selectable.IsInteractable();
    }

    // ------------------------------------------------------------------ images

    int SizeFor(int screenHeight)
    {
        float want = screenHeight * SizePerScreenHeight;
        int best = manifest.sizes[0];
        foreach (int size in manifest.sizes)
            if (Mathf.Abs(size - want) < Mathf.Abs(best - want)) best = size;
        return best;
    }

    Texture2D TextureFor(Look look, int size, out Vector2 hotspot)
    {
        string name = look.ToString().ToLowerInvariant();
        hotspot = Vector2.zero;
        foreach (Entry entry in manifest.cursors)
        {
            if (entry.look == name && entry.size == size)
            {
                hotspot = new Vector2(entry.x, entry.y);
                break;
            }
        }

        string key = name + "_" + size;
        if (textures.TryGetValue(key, out Texture2D cached)) return cached;

        Texture2D texture = null;
        var bytes = Resources.Load<TextAsset>(Folder + key);
        if (bytes != null)
        {
            var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Cursor " + key };
            if (loaded.LoadImage(bytes.bytes, false))
                texture = AsCursorTexture(loaded);
            else
                Destroy(loaded);
            Resources.UnloadAsset(bytes);
        }
        if (texture == null) Debug.LogWarning("[GameCursor] No cursor image Resources/" + Folder + key + ".bytes.");
        textures[key] = texture;   // a missing one is remembered too, so it isn't looked up every frame
        return texture;
    }

    /// <summary>Cursor.SetCursor wants RGBA32 without mipmaps (LoadImage may pick another 32-bit layout).</summary>
    static Texture2D AsCursorTexture(Texture2D loaded)
    {
        Texture2D texture = loaded;
        if (loaded.format != TextureFormat.RGBA32)
        {
            texture = new Texture2D(loaded.width, loaded.height, TextureFormat.RGBA32, false) { name = loaded.name };
            texture.SetPixels32(loaded.GetPixels32());
            texture.Apply(false, false);
            Destroy(loaded);
        }
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
#if UNITY_EDITOR
        texture.alphaIsTransparency = true;
#endif
        return texture;
    }
}
