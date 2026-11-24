using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The cash floor's art: one atlas (Assets/Resources/CashGrab/cash_atlas.png.bytes + cash_atlas.json, made by
/// Tools/CashGrab/make_cash_art.py) built into a texture at runtime, like the cursor (no texture import settings are
/// involved). Without the files every sprite falls back to a plain white square, so the floor still works (as colored
/// shapes) and nothing throws.
/// </summary>
public sealed class CashAtlas
{
    public struct Sprite
    {
        public Rect uv;          // in the texture (0..1, y up)
        public Vector2 size;     // nominal size in world units at 1x
        public Vector2 pivot;    // 0..1 from the bottom-left (Unity's convention)
        public bool valid;
    }

    [Serializable]
    class Manifest
    {
        public int width;
        public int height;
        public float scale = 2f;
        public Entry[] sprites;
    }

    [Serializable]
    class Entry
    {
        public string name;
        public int x, y, w, h;
        public float px = 0.5f, py = 0.5f;
    }

    static CashAtlas shared;

    public static CashAtlas Shared
    {
        get
        {
            if (shared == null || shared.Texture == null) shared = Load();
            return shared;
        }
    }

    public Texture2D Texture { get; private set; }
    public Texture2D Felt { get; private set; }
    public bool HasArt { get; private set; }
    public Sprite White { get; private set; }

    readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);

    public bool TryGet(string name, out Sprite sprite)
    {
        if (name != null && sprites.TryGetValue(name, out sprite)) return true;
        sprite = White;
        return false;
    }

    public Sprite Get(string name)
    {
        TryGet(name, out Sprite s);
        return s;
    }

    static CashAtlas Load()
    {
        var atlas = new CashAtlas();
        try
        {
            var png = Resources.Load<TextAsset>("CashGrab/cash_atlas.png");
            var json = Resources.Load<TextAsset>("CashGrab/cash_atlas");
            Manifest manifest = json != null ? JsonUtility.FromJson<Manifest>(json.text) : null;
            if (png != null && manifest != null && manifest.sprites != null)
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, false) { name = "CashAtlas" };
                if (texture.LoadImage(png.bytes, false))
                {
                    texture.filterMode = FilterMode.Trilinear;
                    texture.anisoLevel = 2;
                    texture.wrapMode = TextureWrapMode.Clamp;
                    texture.Apply(true, true);
                    atlas.Texture = texture;
                    float w = texture.width, h = texture.height, scale = manifest.scale > 0f ? manifest.scale : 2f;
                    foreach (Entry e in manifest.sprites)
                    {
                        if (e == null || string.IsNullOrEmpty(e.name) || e.w <= 0 || e.h <= 0) continue;
                        // A half-texel inset keeps bilinear sampling inside the sprite's own (bled) border
                        var uv = new Rect((e.x + 0.5f) / w, 1f - (e.y + e.h - 0.5f) / h, (e.w - 1f) / w, (e.h - 1f) / h);
                        atlas.sprites[e.name] = new Sprite
                        {
                            uv = uv,
                            size = new Vector2(e.w / scale, e.h / scale),
                            pivot = new Vector2(e.px, 1f - e.py),
                            valid = true
                        };
                    }
                    atlas.HasArt = atlas.sprites.Count > 0;
                }
                else
                {
                    UnityEngine.Object.Destroy(texture);
                }
                Resources.UnloadAsset(png);
            }
            else
            {
                Debug.LogWarning("[CashGrab] No cash floor art (Resources/CashGrab/cash_atlas.png.bytes + cash_atlas.json): drawing plain shapes. " +
                                 "Run python Tools/CashGrab/make_cash_art.py.");
            }

            var felt = Resources.Load<TextAsset>("CashGrab/felt_tile.png");
            if (felt != null)
            {
                var tile = new Texture2D(2, 2, TextureFormat.RGBA32, true, false) { name = "CashFelt" };
                if (tile.LoadImage(felt.bytes, false))
                {
                    tile.wrapMode = TextureWrapMode.Repeat;
                    tile.filterMode = FilterMode.Trilinear;
                    tile.Apply(true, true);
                    atlas.Felt = tile;
                }
                else
                {
                    UnityEngine.Object.Destroy(tile);
                }
                Resources.UnloadAsset(felt);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CashGrab] Couldn't load the cash floor art: " + e.Message);
        }

        if (atlas.Texture == null)
        {
            var white = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "CashAtlas (white)" };
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            white.SetPixels32(pixels);
            white.Apply(false, true);
            atlas.Texture = white;
            atlas.White = new Sprite { uv = new Rect(0.25f, 0.25f, 0.5f, 0.5f), size = new Vector2(16f, 16f), pivot = new Vector2(0.5f, 0.5f), valid = false };
        }
        else if (atlas.sprites.TryGetValue("white", out Sprite w))
        {
            // Sample the middle of the white square only
            Rect uv = w.uv;
            atlas.White = new Sprite { uv = new Rect(uv.center.x - uv.width * 0.25f, uv.center.y - uv.height * 0.25f, uv.width * 0.5f, uv.height * 0.5f), size = w.size, pivot = w.pivot, valid = false };
        }
        else
        {
            atlas.White = new Sprite { uv = new Rect(0f, 0f, 1f / atlas.Texture.width, 1f / atlas.Texture.height), size = new Vector2(16f, 16f), pivot = new Vector2(0.5f, 0.5f), valid = false };
        }
        return atlas;
    }
}
