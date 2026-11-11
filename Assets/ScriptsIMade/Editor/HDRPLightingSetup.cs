#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Editor tool to set up proper HDRP lighting for indoor office scenes.
/// Fixes common issues like flat/too bright lighting.
/// </summary>
public class HDRPLightingSetup : EditorWindow
{
    private enum LightingPreset
    {
        BrightOffice,       // Well-lit modern office
        MoodyOffice,        // Darker, more dramatic
        NaturalDaylight,    // Realistic window lighting
        NightMode           // Evening/night office
    }
    
    private LightingPreset preset = LightingPreset.BrightOffice;
    private bool addAmbientOcclusion = true;
    private bool addBloom = true;
    private bool fixExposure = true;
    private bool addColorGrading = true;
    private bool addVignette = false;
    
    [MenuItem("Tools/HDRP Lighting Setup")]
    public static void ShowWindow()
    {
        GetWindow<HDRPLightingSetup>("HDRP Lighting Setup");
    }
    
    void OnGUI()
    {
        GUILayout.Label("HDRP Office Lighting Setup", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        
        EditorGUILayout.HelpBox(
            "This tool creates a Global Volume with proper settings to fix:\n" +
            "• Too bright / washed out lighting\n" +
            "• Flat look (no depth)\n" +
            "• Unrealistic colors",
            MessageType.Info);
        
        EditorGUILayout.Space();
        
        preset = (LightingPreset)EditorGUILayout.EnumPopup("Lighting Preset", preset);
        
        EditorGUILayout.Space();
        GUILayout.Label("Post-Processing Effects:", EditorStyles.boldLabel);
        
        fixExposure = EditorGUILayout.Toggle("Fix Exposure (brightness)", fixExposure);
        addAmbientOcclusion = EditorGUILayout.Toggle("Ambient Occlusion (depth)", addAmbientOcclusion);
        addColorGrading = EditorGUILayout.Toggle("Color Grading", addColorGrading);
        addBloom = EditorGUILayout.Toggle("Subtle Bloom", addBloom);
        addVignette = EditorGUILayout.Toggle("Vignette (edge darkening)", addVignette);
        
        EditorGUILayout.Space();
        
        if (GUILayout.Button("Apply Lighting Settings", GUILayout.Height(40)))
        {
            ApplyLightingSettings();
        }
        
        EditorGUILayout.Space();
        
        // === LIGHT CREATION SECTION ===
        GUILayout.Label("Create Office Lights:", EditorStyles.boldLabel);
        
        if (GUILayout.Button("Add Complete Office Lighting", GUILayout.Height(35)))
        {
            CreateCompleteOfficeLighting();
        }
        
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Directional (Sun)", GUILayout.Height(25)))
        {
            CreateDirectionalLight();
        }
        if (GUILayout.Button("Add Ceiling Lights", GUILayout.Height(25)))
        {
            CreateCeilingLights();
        }
        EditorGUILayout.EndHorizontal();
        
        EditorGUILayout.Space();
        
        // === FIX SECTION ===
        GUILayout.Label("Fix Existing Lights:", EditorStyles.boldLabel);
        
        if (GUILayout.Button("Fix Main Directional Light", GUILayout.Height(30)))
        {
            FixDirectionalLight();
        }
        
        if (GUILayout.Button("Reduce Environment Ambient", GUILayout.Height(30)))
        {
            ReduceAmbient();
        }
        
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "After applying:\n" +
            "1. Adjust the Volume's Exposure if still too bright/dark\n" +
            "2. Tweak Ambient Occlusion intensity for more/less depth\n" +
            "3. Position lights as needed in your scene",
            MessageType.Info);
    }
    
    void CreateCompleteOfficeLighting()
    {
        // Create parent object
        GameObject lightingRoot = new GameObject("Office Lighting");
        
        // Create directional light (ambient fill)
        GameObject dirLight = CreateDirectionalLight();
        if (dirLight != null)
            dirLight.transform.SetParent(lightingRoot.transform);
        
        // Create ceiling lights
        GameObject ceilingLights = CreateCeilingLights();
        if (ceilingLights != null)
            ceilingLights.transform.SetParent(lightingRoot.transform);
        
        Selection.activeGameObject = lightingRoot;
        
        Debug.Log("[HDRPLightingSetup] Created complete office lighting setup!\n" +
                  "- 1 Directional light for ambient fill\n" +
                  "- 4 Ceiling area lights\n" +
                  "Move and adjust lights as needed for your office layout.");
    }
    
    GameObject CreateDirectionalLight()
    {
        // Check if one already exists
        Light[] existingLights = FindObjectsOfType<Light>();
        foreach (var l in existingLights)
        {
            if (l.type == LightType.Directional)
            {
                if (!EditorUtility.DisplayDialog("Directional Light Exists",
                    "A directional light already exists. Create another one?", "Yes", "No"))
                {
                    Selection.activeGameObject = l.gameObject;
                    return l.gameObject;
                }
                break;
            }
        }
        
        // Create new directional light
        GameObject lightObj = new GameObject("Directional Light - Ambient Fill");
        Light light = lightObj.AddComponent<Light>();
        light.type = LightType.Directional;
        
        // Set rotation (angled down like sun through window)
        lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        
        // HDRP settings
        HDAdditionalLightData hdLight = lightObj.AddComponent<HDAdditionalLightData>();
        
        // Low intensity for indoor ambient fill
        light.intensity = 1f;
        light.color = new Color(1f, 0.96f, 0.9f); // Warm white
        light.shadows = LightShadows.Soft;
        
        // Adjust based on preset
        switch (preset)
        {
            case LightingPreset.BrightOffice:
                light.intensity = 1.5f;
                break;
            case LightingPreset.MoodyOffice:
                light.intensity = 0.5f;
                break;
            case LightingPreset.NaturalDaylight:
                light.intensity = 2f;
                light.color = new Color(1f, 0.98f, 0.95f);
                break;
            case LightingPreset.NightMode:
                light.intensity = 0.2f;
                light.color = new Color(0.7f, 0.8f, 1f); // Cool moonlight
                break;
        }
        
        Debug.Log($"[HDRPLightingSetup] Created directional light with intensity {light.intensity}");
        
        return lightObj;
    }
    
    GameObject CreateCeilingLights()
    {
        GameObject ceilingParent = new GameObject("Ceiling Lights");
        
        // Create a grid of area lights (simulating ceiling panels)
        float spacing = 4f;
        float height = 3f; // Height above ground
        int rows = 2;
        int cols = 2;
        
        float intensity;
        Color lightColor;
        
        switch (preset)
        {
            case LightingPreset.BrightOffice:
                intensity = 800f;
                lightColor = new Color(1f, 0.98f, 0.95f);
                break;
            case LightingPreset.MoodyOffice:
                intensity = 400f;
                lightColor = new Color(1f, 0.9f, 0.8f);
                break;
            case LightingPreset.NaturalDaylight:
                intensity = 600f;
                lightColor = Color.white;
                break;
            case LightingPreset.NightMode:
                intensity = 300f;
                lightColor = new Color(1f, 0.95f, 0.8f);
                break;
            default:
                intensity = 600f;
                lightColor = Color.white;
                break;
        }
        
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                GameObject lightObj = new GameObject($"Ceiling Light ({row},{col})");
                lightObj.transform.SetParent(ceilingParent.transform);
                
                // Position in grid
                float x = (col - (cols - 1) * 0.5f) * spacing;
                float z = (row - (rows - 1) * 0.5f) * spacing;
                lightObj.transform.localPosition = new Vector3(x, height, z);
                
                // Point down
                lightObj.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                
                // Create area light
                Light light = lightObj.AddComponent<Light>();
                light.type = LightType.Rectangle;
                light.color = lightColor;
                light.intensity = intensity;
                light.shadows = LightShadows.Soft;
                
                // HDRP settings
                HDAdditionalLightData hdLight = lightObj.AddComponent<HDAdditionalLightData>();
                hdLight.SetAreaLightSize(new Vector2(1f, 1f));
            }
        }
        
        Debug.Log($"[HDRPLightingSetup] Created {rows * cols} ceiling area lights\n" +
                  "Position the 'Ceiling Lights' object in your office center.");
        
        return ceilingParent;
    }
    
    void ApplyLightingSettings()
    {
        // Find or create global volume
        Volume volume = FindGlobalVolume();
        if (volume == null)
        {
            volume = CreateGlobalVolume();
        }
        
        // Get or create profile
        VolumeProfile profile = volume.sharedProfile;
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            string path = "Assets/Settings/OfficeLightingProfile.asset";
            
            // Ensure directory exists
            if (!AssetDatabase.IsValidFolder("Assets/Settings"))
            {
                AssetDatabase.CreateFolder("Assets", "Settings");
            }
            
            AssetDatabase.CreateAsset(profile, path);
            volume.sharedProfile = profile;
        }
        
        // Get preset values
        float exposureValue;
        float aoIntensity;
        float saturation;
        float contrast;
        float bloomIntensity;
        
        switch (preset)
        {
            case LightingPreset.BrightOffice:
                exposureValue = 10f;
                aoIntensity = 0.6f;
                saturation = 5f;
                contrast = 10f;
                bloomIntensity = 0.1f;
                break;
            case LightingPreset.MoodyOffice:
                exposureValue = 8f;
                aoIntensity = 1.0f;
                saturation = -10f;
                contrast = 20f;
                bloomIntensity = 0.15f;
                break;
            case LightingPreset.NaturalDaylight:
                exposureValue = 11f;
                aoIntensity = 0.5f;
                saturation = 0f;
                contrast = 5f;
                bloomIntensity = 0.2f;
                break;
            case LightingPreset.NightMode:
                exposureValue = 7f;
                aoIntensity = 0.8f;
                saturation = -15f;
                contrast = 15f;
                bloomIntensity = 0.25f;
                break;
            default:
                exposureValue = 10f;
                aoIntensity = 0.6f;
                saturation = 0f;
                contrast = 10f;
                bloomIntensity = 0.1f;
                break;
        }
        
        // Apply Exposure
        if (fixExposure)
        {
            Exposure exposure;
            if (!profile.TryGet(out exposure))
            {
                exposure = profile.Add<Exposure>();
            }
            exposure.active = true;
            exposure.mode.Override(ExposureMode.Fixed);
            exposure.fixedExposure.Override(exposureValue);
        }
        
        // Apply Ambient Occlusion
        if (addAmbientOcclusion)
        {
            ScreenSpaceAmbientOcclusion ao;
            if (!profile.TryGet(out ao))
            {
                ao = profile.Add<ScreenSpaceAmbientOcclusion>();
            }
            ao.active = true;
            ao.intensity.Override(aoIntensity);
            ao.directLightingStrength.Override(0.25f);
            ao.radius.Override(2f);
        }
        
        // Apply Color Adjustments
        if (addColorGrading)
        {
            ColorAdjustments colorAdj;
            if (!profile.TryGet(out colorAdj))
            {
                colorAdj = profile.Add<ColorAdjustments>();
            }
            colorAdj.active = true;
            colorAdj.saturation.Override(saturation);
            colorAdj.contrast.Override(contrast);
        }
        
        // Apply Bloom
        if (addBloom)
        {
            Bloom bloom;
            if (!profile.TryGet(out bloom))
            {
                bloom = profile.Add<Bloom>();
            }
            bloom.active = true;
            bloom.intensity.Override(bloomIntensity);
            bloom.threshold.Override(0.9f);
            bloom.scatter.Override(0.6f);
        }
        
        // Apply Vignette
        if (addVignette)
        {
            Vignette vignette;
            if (!profile.TryGet(out vignette))
            {
                vignette = profile.Add<Vignette>();
            }
            vignette.active = true;
            vignette.intensity.Override(0.25f);
            vignette.smoothness.Override(0.4f);
        }
        
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        
        Selection.activeGameObject = volume.gameObject;
        
        Debug.Log($"[HDRPLightingSetup] Applied {preset} lighting preset!\n" +
                  $"- Exposure: {exposureValue}\n" +
                  $"- AO Intensity: {aoIntensity}\n" +
                  "Select the Volume to fine-tune settings.");
    }
    
    Volume FindGlobalVolume()
    {
        Volume[] volumes = FindObjectsOfType<Volume>();
        foreach (var vol in volumes)
        {
            if (vol.isGlobal)
                return vol;
        }
        return null;
    }
    
    Volume CreateGlobalVolume()
    {
        GameObject volumeObj = new GameObject("Global Lighting Volume");
        Volume volume = volumeObj.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1;
        
        Debug.Log("[HDRPLightingSetup] Created new Global Volume");
        return volume;
    }
    
    void FixDirectionalLight()
    {
        Light[] lights = FindObjectsOfType<Light>();
        Light directionalLight = null;
        
        foreach (var light in lights)
        {
            if (light.type == LightType.Directional)
            {
                directionalLight = light;
                break;
            }
        }
        
        if (directionalLight == null)
        {
            EditorUtility.DisplayDialog("No Directional Light", 
                "No directional light found in the scene.", "OK");
            return;
        }
        
        // Get HDRP additional data
        var hdLight = directionalLight.GetComponent<HDAdditionalLightData>();
        if (hdLight == null)
        {
            hdLight = directionalLight.gameObject.AddComponent<HDAdditionalLightData>();
        }
        
        // Apply good defaults
        directionalLight.intensity = 2f; // Lower intensity
        directionalLight.shadows = LightShadows.Soft;
        directionalLight.shadowStrength = 0.8f;
        
        // Enable shadows via HDRP component
        hdLight.EnableShadows(true);
        
        Selection.activeGameObject = directionalLight.gameObject;
        EditorUtility.SetDirty(directionalLight);
        EditorUtility.SetDirty(hdLight);
        
        Debug.Log("[HDRPLightingSetup] Fixed directional light:\n" +
                  "- Reduced intensity to 2\n" +
                  "- Enabled soft shadows\n" +
                  "- To enable Contact Shadows: select the light > HDRP section > check 'Contact Shadows'");
    }
    
    void ReduceAmbient()
    {
        // Find the HDRP visual environment
        Volume[] volumes = FindObjectsOfType<Volume>();
        
        foreach (var vol in volumes)
        {
            if (vol.sharedProfile == null) continue;
            
            VisualEnvironment ve;
            if (vol.sharedProfile.TryGet(out ve))
            {
                Debug.Log("[HDRPLightingSetup] Found Visual Environment in " + vol.name);
            }
            
            // Check for indirect lighting controller
            IndirectLightingController ilc;
            if (vol.sharedProfile.TryGet(out ilc))
            {
                ilc.indirectDiffuseLightingMultiplier.Override(0.5f);
                EditorUtility.SetDirty(vol.sharedProfile);
                Debug.Log("[HDRPLightingSetup] Reduced indirect lighting multiplier to 0.5");
            }
        }
        
        // Also check sky settings
        Debug.Log("[HDRPLightingSetup] To further reduce ambient:\n" +
                  "1. Go to Window > Rendering > Lighting\n" +
                  "2. Under Environment, reduce 'Ambient Intensity'\n" +
                  "3. Or in your Sky volume, reduce 'Exposure'");
    }
}
#endif
