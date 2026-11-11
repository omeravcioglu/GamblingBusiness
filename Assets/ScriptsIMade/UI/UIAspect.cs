using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;

/// <summary>
/// Keeps the whole 1920 x 1080 UI on screen whatever the screen's shape. Every screen canvas is laid out on a 1920 x 1080
/// frame and scaled with the screen. "Match width or height 0.5" made the canvas of a 21:9 or 32:9 screen shorter than
/// 1080 (2226 x 932 on 3440 x 1440): the tops and bottoms of panels went off screen and their buttons couldn't be pressed,
/// and a 16:10 or 4:3 screen cut the sides. Expand scales by the tighter side instead, so the frame always fits, with
/// room to spare at the sides of a wide screen or above and below a taller one (16:9 is unchanged).
/// The UI builders and the code that makes canvases set Expand themselves. This catches the rest (scene canvases, prefabs
/// built before, other code, UI Toolkit panels made at run time): when a scene loads, when the screen size changes and
/// twice a second, any 1920 x 1080 screen-scaled canvas or panel that still matches width or height is switched.
/// </summary>
public class UIAspect : MonoBehaviour
{
    const float SweepSeconds = 0.5f;
    static UIAspect instance;

    Vector2Int lastScreen;
    float nextSweep;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics
        instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Create()
    {
        if (instance != null) return;
        var go = new GameObject("UIAspect");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<UIAspect>();
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Sweep();

    void Update()
    {
        var screen = new Vector2Int(Screen.width, Screen.height);
        if (screen == lastScreen && Time.unscaledTime < nextSweep) return;
        lastScreen = screen;
        nextSweep = Time.unscaledTime + SweepSeconds;
        Sweep();
    }

    /// <summary>Switch every loaded 1920 x 1080 canvas scaler and run-time panel that matches width or height to Expand.</summary>
    public static void Sweep()
    {
        foreach (var scaler in FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Apply(scaler);
        foreach (var panel in Resources.FindObjectsOfTypeAll<PanelSettings>())
            Apply(panel);
    }

    /// <summary>Expand for a 1920 x 1080 screen-scaled canvas that matches width or height; true when it changed it.</summary>
    public static bool Apply(CanvasScaler scaler)
    {
        if (scaler == null || scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return false;
        if (scaler.screenMatchMode != CanvasScaler.ScreenMatchMode.MatchWidthOrHeight) return false;
        if (!IsFrame(scaler.referenceResolution)) return false;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        return true;
    }

    static void Apply(PanelSettings panel)
    {
        if (panel == null || panel.scaleMode != PanelScaleMode.ScaleWithScreenSize) return;
        if (panel.screenMatchMode != PanelScreenMatchMode.MatchWidthOrHeight) return;
        if (!IsFrame(panel.referenceResolution)) return;
#if UNITY_EDITOR
        // An asset: changing it in Play mode would change the file. Only panels made at run time are switched here.
        if (UnityEditor.EditorUtility.IsPersistent(panel)) return;
#endif
        panel.screenMatchMode = PanelScreenMatchMode.Expand;
    }

    static bool IsFrame(Vector2 reference) => Mathf.Abs(reference.x - 1920f) < 1f && Mathf.Abs(reference.y - 1080f) < 1f;
}
