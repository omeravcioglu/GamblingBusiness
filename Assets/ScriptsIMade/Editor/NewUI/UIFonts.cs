using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// The game font for the new UI: a dynamic TMP font asset made from Assets/UI/Fonts/ComingSoon-Regular.ttf
/// (Google Fonts, Apache 2.0). Coming Soon has no ğ Ğ ş Ş İ: those come from the first fallback, "PatrickHand SDF"
/// (Patrick Hand, SIL OFL 1.1: the Online City's font file), scaled to Coming Soon's letter heights. LiberationSans comes
/// after it for anything else. Without the .ttf the builders use LiberationSans SDF.
/// Also makes the "Pop" material (light outline + soft drop shadow) used for floating numbers.
/// </summary>
public static class UIFonts
{
    public const string FontFolder = "Assets/UI/Fonts";
    public const string SourceFontPath = FontFolder + "/ComingSoon-Regular.ttf";
    public const string FontAssetPath = FontFolder + "/ComingSoon SDF.asset";
    public const string PopMaterialPath = FontFolder + "/ComingSoon SDF - Pop.mat";
    const string LiberationPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";
    const string LiberationFallbackPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset";

    // Handwriting fallback for the Turkish letters Coming Soon lacks. The .ttf belongs to the Online City, which loads it
    // by this path: don't move or rename it.
    const string HandSourcePath = "Assets/Resources/OnlineCity/CityFontFallback.ttf";
    public const string HandAssetPath = FontFolder + "/PatrickHand SDF.asset";
    const string HandPrebake = "ğĞşŞİ";
    // Patrick Hand's H is 0.662 em tall and its x 0.468 em; Coming Soon's are 0.747 and 0.545. 1.15 sits between the ratios.
    const float HandScale = 1.15f;

    // Characters baked into the atlas up front (the rest are added when first used)
    const string Prebake = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~";

    static TMP_FontAsset cached;

    /// <summary>The Coming Soon font asset (created on first use), or LiberationSans SDF if the .ttf is missing.</summary>
    public static TMP_FontAsset Main
    {
        get
        {
            if (cached != null) return cached;
            cached = EnsureComingSoon();
            if (cached == null)
            {
                cached = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LiberationPath);
                Debug.LogWarning("[UIFonts] " + SourceFontPath + " is missing: using LiberationSans SDF until the Coming Soon font is added.");
            }
            return cached;
        }
    }

    public static bool HasComingSoon => File.Exists(SourceFontPath);

    public static TMP_FontAsset EnsureComingSoon()
    {
        var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
        if (existing != null)
        {
            EnsureFallback(existing);
            EnsureKerning(existing);
            EnsureWeights(existing);
            return existing;
        }

        var font = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (font == null) return null;

        var asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
        if (asset == null)
        {
            Debug.LogError("[UIFonts] TMP could not create a font asset from " + SourceFontPath);
            return null;
        }
        asset.name = "ComingSoon SDF";
        AssetDatabase.CreateAsset(asset, FontAssetPath);

        var atlas = asset.atlasTextures != null && asset.atlasTextures.Length > 0 ? asset.atlasTextures[0] : null;
        if (atlas != null)
        {
            atlas.name = "ComingSoon SDF Atlas";
            AssetDatabase.AddObjectToAsset(atlas, asset);
        }
        if (asset.material != null)
        {
            asset.material.name = "ComingSoon SDF Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
        }

        asset.TryAddCharacters(Prebake, out string missing);
        if (!string.IsNullOrEmpty(missing))
            Debug.Log("[UIFonts] Coming Soon has no glyphs for: " + missing);

        EnsureFallback(asset);
        EnsureKerning(asset);
        EnsureWeights(asset);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(FontAssetPath);
        Debug.Log("[UIFonts] Created " + FontAssetPath);
        return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
    }

    // Browsers draw font-weight 700 on a one-weight font as a slightly thicker outline with the same advances (measured
    // on the boards rendered by Edge: 2/100 em per letter left bold text 2-4 % wider); TMP's default synthetic-bold
    // spacing is 7/100 em per letter.
    const float BoldSpacing = 0f;

    // Coming Soon's strokes are thin, and TMP's anti-aliased edges blend in linear space here, so text came out with
    // ~70 % of the boards' ink (regular) and ~89 % (bold). Measured on the ending boards (19-60 px): regular weight 0.25
    // with sharpness 0.3 gives 96-100 %, bold weight 0.9 about 97 %.
    const float WeightNormal = 0.25f, WeightBold = 0.9f, Sharpness = 0.3f;

    // Patrick Hand's strokes are about 1.5 times as thick as Coming Soon's once scaled, and a fallback's material keeps its
    // own weights (TMP copies everything else from the text's material). Measured in linear light on the ending previews,
    // these bring its stems to Coming Soon's: 0.098 em bold at 72 px, 0.065 em regular at 26 px.
    const float HandWeightNormal = -0.25f, HandWeightBold = 0.35f;

    static void EnsureWeights(TMP_FontAsset asset) => EnsureWeights(asset, WeightNormal, WeightBold);

    /// <summary>Set the font material's weights and sharpness; true when something changed.</summary>
    static bool EnsureWeights(TMP_FontAsset asset, float normal, float bold)
    {
        var mat = asset.material;
        if (mat == null) return false;
        if (Mathf.Approximately(mat.GetFloat(ShaderUtilities.ID_WeightNormal), normal)
            && Mathf.Approximately(mat.GetFloat(ShaderUtilities.ID_WeightBold), bold)
            && Mathf.Approximately(mat.GetFloat(ShaderUtilities.ID_Sharpness), Sharpness))
            return false;
        mat.SetFloat(ShaderUtilities.ID_WeightNormal, normal);
        mat.SetFloat(ShaderUtilities.ID_WeightBold, bold);
        mat.SetFloat(ShaderUtilities.ID_Sharpness, Sharpness);
        ShaderUtilities.UpdateShaderRatios(mat);
        EditorUtility.SetDirty(mat);
        EditorUtility.SetDirty(asset);
        return true;
    }

    /// <summary>
    /// Bring the materials made from the font (glows, Pop, Heavy) up to its current weights: they copy the font material
    /// when they are made. Rewrites them in place, so prefabs keep their references.
    /// </summary>
    public static void RefreshDerivedMaterials()
    {
        var font = Main;
        if (font == null || font.material == null) return;
        foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { FontFolder }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.Contains("ComingSoon SDF - ")) continue;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null || mat == font.material) continue;
            mat.SetFloat(ShaderUtilities.ID_WeightNormal, WeightNormal);
            mat.SetFloat(ShaderUtilities.ID_WeightBold, WeightBold);
            mat.SetFloat(ShaderUtilities.ID_Sharpness, Sharpness);
            ShaderUtilities.UpdateShaderRatios(mat);
            EditorUtility.SetDirty(mat);
        }
    }

    static void EnsureFallback(TMP_FontAsset asset)
    {
        if (!Mathf.Approximately(asset.boldSpacing, BoldSpacing))
        {
            asset.boldSpacing = BoldSpacing;
            EditorUtility.SetDirty(asset);
        }
        if (asset.fallbackFontAssetTable == null)
            asset.fallbackFontAssetTable = new List<TMP_FontAsset>();
        var table = asset.fallbackFontAssetTable;
        bool changed = false;

        // Patrick Hand first, so ğ Ğ ş Ş İ stay handwritten
        var hand = EnsureHandFallback(asset);
        if (hand != null && (table.Count == 0 || table[0] != hand))
        {
            table.Remove(hand);
            table.Insert(0, hand);
            changed = true;
        }

        var fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LiberationFallbackPath);
        if (fallback == null)
            fallback = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LiberationPath);
        if (fallback != null && !table.Contains(fallback))
        {
            table.Add(fallback);
            changed = true;
        }

        if (!changed) return;
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        Debug.Log("[UIFonts] " + asset.name + " falls back to: " + string.Join(", ", table.ConvertAll(f => f != null ? f.name : "(missing)")));
    }

    /// <summary>
    /// The Patrick Hand fallback, made on first use (null when its .ttf isn't in the project). Its face lines are Coming
    /// Soon's, divided by its scale, so a line with ğ or Ş in it keeps Coming Soon's spacing.
    /// </summary>
    static TMP_FontAsset EnsureHandFallback(TMP_FontAsset main)
    {
        var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HandAssetPath);
        bool changed = false;
        if (asset == null)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(HandSourcePath);
            if (font == null) return null;

            asset = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            if (asset == null)
            {
                Debug.LogError("[UIFonts] TMP could not create a font asset from " + HandSourcePath);
                return null;
            }
            asset.name = "PatrickHand SDF";
            AssetDatabase.CreateAsset(asset, HandAssetPath);

            var atlas = asset.atlasTextures != null && asset.atlasTextures.Length > 0 ? asset.atlasTextures[0] : null;
            if (atlas != null)
            {
                atlas.name = "PatrickHand SDF Atlas";
                AssetDatabase.AddObjectToAsset(atlas, asset);
            }
            if (asset.material != null)
            {
                asset.material.name = "PatrickHand SDF Material";
                AssetDatabase.AddObjectToAsset(asset.material, asset);
            }

            asset.TryAddCharacters(HandPrebake, out string missing);
            if (!string.IsNullOrEmpty(missing))
                Debug.LogWarning("[UIFonts] Patrick Hand has no glyphs for: " + missing);
            // A handful of letters needs no kerning, and TMP can read pairs from the wrong face for new glyphs (see EnsureKerning)
            asset.getFontFeatures = false;
            changed = true;
            Debug.Log("[UIFonts] Created " + HandAssetPath);
        }

        if (!Mathf.Approximately(asset.boldSpacing, BoldSpacing))
        {
            asset.boldSpacing = BoldSpacing;
            changed = true;
        }

        var face = asset.faceInfo;
        var mainFace = main.faceInfo;
        float k = face.pointSize / mainFace.pointSize * mainFace.scale / HandScale;
        if (!Mathf.Approximately(face.scale, HandScale)
            || !Mathf.Approximately(face.ascentLine, mainFace.ascentLine * k)
            || !Mathf.Approximately(face.descentLine, mainFace.descentLine * k)
            || !Mathf.Approximately(face.lineHeight, mainFace.lineHeight * k)
            || !Mathf.Approximately(face.capLine, mainFace.capLine * k)
            || !Mathf.Approximately(face.meanLine, mainFace.meanLine * k))
        {
            face.scale = HandScale;
            face.ascentLine = mainFace.ascentLine * k;
            face.descentLine = mainFace.descentLine * k;
            face.lineHeight = mainFace.lineHeight * k;
            face.capLine = mainFace.capLine * k;
            face.meanLine = mainFace.meanLine * k;
            asset.faceInfo = face;
            changed = true;
        }

        if (EnsureWeights(asset, HandWeightNormal, HandWeightBold)) changed = true;
        if (changed)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }
        return asset;
    }

    /// <summary>
    /// Keeps the asset's kerning exactly the font's own 'kern' pairs. TMP reads pair adjustments for newly added glyphs
    /// from whatever face the font engine loaded last, so glyphs added right after the LiberationSans fallback's once
    /// stored Liberation's pairs here (in the wrong units): "00" overlapped by a quarter em. The table is rebuilt from
    /// the .ttf and TMP stops reading features at runtime (the table already covers every glyph of the font).
    /// </summary>
    static void EnsureKerning(TMP_FontAsset asset)
    {
        if (asset.sourceFontFile == null) return;
        string path = AssetDatabase.GetAssetPath(asset.sourceFontFile);
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var pairs = ReadKernPairs(File.ReadAllBytes(path), out int unitsPerEm);
        if (pairs == null || unitsPerEm <= 0) return;

        float scale = asset.faceInfo.pointSize / unitsPerEm;
        var want = new List<GlyphPairAdjustmentRecord>(pairs.Count);
        foreach (var p in pairs)
        {
            var first = new GlyphAdjustmentRecord(p.left, new GlyphValueRecord(0f, 0f, p.value * scale, 0f));
            var second = new GlyphAdjustmentRecord(p.right, new GlyphValueRecord(0f, 0f, 0f, 0f));
            want.Add(new GlyphPairAdjustmentRecord(first, second));
        }

        var have = asset.fontFeatureTable.glyphPairAdjustmentRecords;
        bool same = !asset.getFontFeatures && have != null && have.Count == want.Count;
        if (same)
        {
            var lookup = new Dictionary<ulong, float>(have.Count);
            foreach (var r in have)
                lookup[((ulong)r.firstAdjustmentRecord.glyphIndex << 32) | r.secondAdjustmentRecord.glyphIndex] = r.firstAdjustmentRecord.glyphValueRecord.xAdvance;
            foreach (var r in want)
            {
                ulong key = ((ulong)r.firstAdjustmentRecord.glyphIndex << 32) | r.secondAdjustmentRecord.glyphIndex;
                if (!lookup.TryGetValue(key, out float x) || Mathf.Abs(x - r.firstAdjustmentRecord.glyphValueRecord.xAdvance) > 0.001f)
                {
                    same = false;
                    break;
                }
            }
        }
        if (same) return;

        int before = have != null ? have.Count : 0;
        asset.fontFeatureTable.glyphPairAdjustmentRecords = want;
        asset.getFontFeatures = false;
        asset.ReadFontAssetDefinition();
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();
        Debug.Log("[UIFonts] " + asset.name + ": kerning rebuilt from " + path + " (" + before + " -> " + want.Count + " pairs); runtime font-feature reading off.");
    }

    struct KernPair
    {
        public uint left, right;
        public short value;
    }

    /// <summary>Horizontal format-0 pairs of a TrueType 'kern' table (null if the font has none) and the font's units per em.</summary>
    static List<KernPair> ReadKernPairs(byte[] ttf, out int unitsPerEm)
    {
        unitsPerEm = 0;
        if (ttf == null || ttf.Length < 12) return null;
        int U16(int o) => (ttf[o] << 8) | ttf[o + 1];
        int U32(int o) => (ttf[o] << 24) | (ttf[o + 1] << 16) | (ttf[o + 2] << 8) | ttf[o + 3];

        int kern = -1;
        int numTables = U16(4);
        for (int i = 0; i < numTables; i++)
        {
            int rec = 12 + 16 * i;
            if (rec + 16 > ttf.Length) return null;
            string tag = System.Text.Encoding.ASCII.GetString(ttf, rec, 4);
            int offset = U32(rec + 8);
            if (tag == "head" && offset + 20 <= ttf.Length) unitsPerEm = U16(offset + 18);
            else if (tag == "kern") kern = offset;
        }
        if (kern < 0 || kern + 4 > ttf.Length) return null;

        var pairs = new List<KernPair>();
        int nSub = U16(kern + 2);
        int sub = kern + 4;
        for (int s = 0; s < nSub && sub + 14 <= ttf.Length; s++)
        {
            int length = U16(sub + 2);
            int coverage = U16(sub + 4);
            bool horizontal = (coverage & 1) != 0, minimum = (coverage & 2) != 0, cross = (coverage & 4) != 0;
            if ((coverage >> 8) == 0 && horizontal && !minimum && !cross)
            {
                int n = U16(sub + 6);
                for (int i = 0; i < n; i++)
                {
                    int o = sub + 14 + 6 * i;
                    if (o + 6 > ttf.Length) break;
                    pairs.Add(new KernPair { left = (uint)U16(o), right = (uint)U16(o + 2), value = (short)U16(o + 4) });
                }
            }
            if (length <= 0) break;
            sub += length;
        }
        return pairs;
    }

    /// <summary>Material for floating numbers: the font with a paper-coloured outline and a soft shadow.</summary>
    public static Material PopMaterial
    {
        get
        {
            var font = Main;
            if (font == null || font.material == null) return null;
            string path = HasComingSoon ? PopMaterialPath : FontFolder + "/LiberationSans SDF - Pop.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null && mat.GetTexture(ShaderUtilities.ID_MainTex) == font.material.GetTexture(ShaderUtilities.ID_MainTex))
                return mat;

            Directory.CreateDirectory(FontFolder);
            bool create = mat == null;
            if (create)
                mat = new Material(font.material);
            else
                mat.CopyPropertiesFromMaterial(font.material);
            mat.name = Path.GetFileNameWithoutExtension(path);
            Shader sdf = Shader.Find("TextMeshPro/Distance Field");
            mat.shader = sdf != null ? sdf : font.material.shader;
            mat.SetTexture(ShaderUtilities.ID_MainTex, font.material.GetTexture(ShaderUtilities.ID_MainTex));
            mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
            mat.SetFloat(ShaderUtilities.ID_OutlineWidth, 0.22f);
            mat.SetColor(ShaderUtilities.ID_OutlineColor, UIColors.PaperHi);
            mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.35f));
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.35f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.45f);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.1f);
            if (create)
                AssetDatabase.CreateAsset(mat, path);
            else
                EditorUtility.SetDirty(mat);
            return mat;
        }
    }

    /// <summary>
    /// The font with a soft coloured glow (CSS text-shadow 0 0 Npx colour) for the office CRT's lit texts, one material
    /// per colour name (Assets/UI/Fonts/ComingSoon SDF - Glow &lt;name&gt;.mat). glow.a is the glow's strength.
    /// </summary>
    public static Material GlowMaterial(string name, Color glow, float outer = 0.55f)
    {
        var font = Main;
        if (font == null || font.material == null) return null;
        string path = FontFolder + "/ComingSoon SDF - Glow " + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool create = mat == null;
        if (create)
            mat = new Material(font.material);
        else
            mat.CopyPropertiesFromMaterial(font.material);
        mat.name = Path.GetFileNameWithoutExtension(path);
        Shader sdf = Shader.Find("TextMeshPro/Distance Field");
        mat.shader = sdf != null ? sdf : font.material.shader;
        mat.SetTexture(ShaderUtilities.ID_MainTex, font.material.GetTexture(ShaderUtilities.ID_MainTex));
        mat.EnableKeyword(ShaderUtilities.Keyword_Glow);
        mat.SetColor(ShaderUtilities.ID_GlowColor, glow);
        mat.SetFloat(ShaderUtilities.ID_GlowOffset, 0f);
        mat.SetFloat(ShaderUtilities.ID_GlowInner, 0.05f);
        mat.SetFloat(ShaderUtilities.ID_GlowOuter, outer);
        mat.SetFloat(ShaderUtilities.ID_GlowPower, 1f);
        ShaderUtilities.UpdateShaderRatios(mat);
        if (create)
            AssetDatabase.CreateAsset(mat, path);
        else
            EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>
    /// The font grown outward (TMP face dilate), for text the design strokes as well as fills (SVG text with a stroke,
    /// e.g. the HIGH ROLLER masthead): Assets/UI/Fonts/ComingSoon SDF - Heavy &lt;name&gt;.mat.
    /// </summary>
    public static Material DilateMaterial(string name, float dilate)
    {
        var font = Main;
        if (font == null || font.material == null) return null;
        string path = FontFolder + "/ComingSoon SDF - Heavy " + name + ".mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool create = mat == null;
        if (create)
            mat = new Material(font.material);
        else
            mat.CopyPropertiesFromMaterial(font.material);
        mat.name = Path.GetFileNameWithoutExtension(path);
        Shader sdf = Shader.Find("TextMeshPro/Distance Field");
        mat.shader = sdf != null ? sdf : font.material.shader;
        mat.SetTexture(ShaderUtilities.ID_MainTex, font.material.GetTexture(ShaderUtilities.ID_MainTex));
        mat.SetFloat(ShaderUtilities.ID_FaceDilate, dilate);
        ShaderUtilities.UpdateShaderRatios(mat);
        if (create)
            AssetDatabase.CreateAsset(mat, path);
        else
            EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>Natural line height of the font in em (TMP's default line spacing).</summary>
    public static float NaturalLineHeight(TMP_FontAsset font)
    {
        if (font == null) return 1.2f;
        var face = font.faceInfo;
        if (face.pointSize <= 0) return 1.2f;
        return face.lineHeight / face.pointSize;
    }

    /// <summary>Ascent + descent of the font in em (the CSS "content area" of an inline box).</summary>
    public static float ContentHeight(TMP_FontAsset font)
    {
        if (font == null) return 1.1f;
        var face = font.faceInfo;
        if (face.pointSize <= 0) return 1.1f;
        return (face.ascentLine - face.descentLine) / face.pointSize;
    }

    [MenuItem("Tools/Game/New UI/Create Font Asset")]
    public static void CreateFromMenu()
    {
        cached = null;
        var asset = EnsureComingSoon();
        if (asset == null)
            EditorUtility.DisplayDialog("Coming Soon font", "Put ComingSoon-Regular.ttf in " + FontFolder + " first.", "OK");
        else
            Selection.activeObject = asset;
    }
}
