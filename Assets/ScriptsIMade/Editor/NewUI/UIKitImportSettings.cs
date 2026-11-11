using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Import settings for the generated UI kit (Assets/UI/Kit, Icons, Shadows), read from Assets/UI/Kit/kit.json,
/// which Tools/UIKit/gen_kit.py writes next to the sprites: pixels per unit, 9-slice borders, wrap mode, mipmaps.
/// The splash sprites and the menu logo (Assets/UI/Splash, Assets/UI/Logo, from Tools/Splash/make_splash_assets.py) all get
/// one setup instead.
/// Runs automatically whenever one of those PNGs is imported.
/// </summary>
public class UIKitImportSettings : AssetPostprocessor
{
    const string Root = "Assets/UI/";
    const string SplashRoot = "Assets/UI/Splash/";
    const string LogoRoot = "Assets/UI/Logo/";
    const string ManifestPath = "Assets/UI/Kit/kit.json";

    [System.Serializable]
    class Entry
    {
        public float ppu = 100f;
        public int[] border;
        public string wrap = "clamp";
        public bool mip;
    }

    static Dictionary<string, Entry> manifest;
    static System.DateTime manifestTime;

    static Dictionary<string, Entry> Manifest
    {
        get
        {
            string full = Path.GetFullPath(ManifestPath);
            if (!File.Exists(full)) return null;
            System.DateTime time = File.GetLastWriteTimeUtc(full);
            if (manifest == null || time != manifestTime)
            {
                manifest = Parse(File.ReadAllText(full));
                manifestTime = time;
            }
            return manifest;
        }
    }

    // kit.json is a flat object: { "Kit/card_fill.png": { "border": [..], "mip": false, "ppu": 200, "wrap": "clamp" }, ... }
    static Dictionary<string, Entry> Parse(string json)
    {
        var result = new Dictionary<string, Entry>();
        int i = 0;
        while (true)
        {
            int keyStart = json.IndexOf('"', i);
            if (keyStart < 0) break;
            int keyEnd = json.IndexOf('"', keyStart + 1);
            if (keyEnd < 0) break;
            string key = json.Substring(keyStart + 1, keyEnd - keyStart - 1);
            int objStart = json.IndexOf('{', keyEnd);
            int objEnd = json.IndexOf('}', objStart + 1);
            if (objStart < 0 || objEnd < 0) break;
            if (!key.StartsWith("__"))
            {
                try
                {
                    Entry e = JsonUtility.FromJson<Entry>(json.Substring(objStart, objEnd - objStart + 1));
                    result[key] = e;
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning("[UIKit] Can't read kit.json entry " + key + ": " + ex.Message);
                }
            }
            i = objEnd + 1;
        }
        return result;
    }

    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Root) || !assetPath.EndsWith(".png")) return;
        if (assetPath.StartsWith(SplashRoot) || assetPath.StartsWith(LogoRoot))
        {
            SetupSplash((TextureImporter)assetImporter);
            return;
        }
        var map = Manifest;
        if (map == null) return;
        string rel = assetPath.Substring(Root.Length);
        if (!map.TryGetValue(rel, out Entry e)) return;

        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = e.ppu;
        ti.alphaIsTransparency = true;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.sRGBTexture = true;
        ti.mipmapEnabled = e.mip;
        ti.filterMode = FilterMode.Bilinear;
        ti.wrapMode = e.wrap == "repeat" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.isReadable = false;

        var settings = new TextureImporterSettings();
        ti.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteGenerateFallbackPhysicsShape = false;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        ti.SetTextureSettings(settings);

        if (e.border != null && e.border.Length == 4)
            ti.spriteBorder = new Vector4(e.border[0], e.border[1], e.border[2], e.border[3]);
        else
            ti.spriteBorder = Vector4.zero;
    }

    // The logo crops show smaller than their pixels on a 1080p screen and scale while they land: trilinear mipmaps keep
    // them clean. Uncompressed, so the outlines and the dark gradients stay exact.
    static void SetupSplash(TextureImporter ti)
    {
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spritePixelsPerUnit = 100f;
        ti.alphaIsTransparency = true;
        ti.alphaSource = TextureImporterAlphaSource.FromInput;
        ti.sRGBTexture = true;
        ti.mipmapEnabled = true;
        ti.filterMode = FilterMode.Trilinear;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.maxTextureSize = 4096;
        ti.isReadable = false;

        var settings = new TextureImporterSettings();
        ti.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteGenerateFallbackPhysicsShape = false;
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        ti.SetTextureSettings(settings);
        ti.spriteBorder = Vector4.zero;
    }

    [MenuItem("Tools/Game/New UI/Reimport UI Kit Sprites")]
    public static void ReimportAll()
    {
        manifest = null;
        AssetDatabase.ImportAsset("Assets/UI", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
        Debug.Log("[UIKit] Reimported Assets/UI.");
    }
}
