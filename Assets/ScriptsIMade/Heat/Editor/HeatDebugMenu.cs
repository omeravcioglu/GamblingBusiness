using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Game > Debug: try the heat system while MainGame is playing.
/// - Add 25 Heat / Set Heat 0: change the heat (with the usual level and freeze messages).
/// - Force Audit: a regulator audit right now, whatever the level (the fine follows the current heat).
/// - Show Tilt Prompt: the "Win it back?" offer right away, for a made-up loss (5% of company value), whatever the heat.
/// </summary>
public static class HeatDebugMenu
{
    const string Root = "Tools/Game/Debug/";

    [MenuItem(Root + "Add 25 Heat")]
    static void AddHeat()
    {
        HeatMeter meter = Meter();
        if (meter != null) meter.AddHeat(25f, "debug");
    }

    [MenuItem(Root + "Set Heat 0")]
    static void ClearHeat()
    {
        HeatMeter meter = Meter();
        if (meter != null) meter.SetHeat(0f, "debug");
    }

    [MenuItem(Root + "Force Audit")]
    static void ForceAudit()
    {
        HeatMeter meter = Meter();
        if (meter != null) meter.ForceAudit();
    }

    [MenuItem(Root + "Show Tilt Prompt")]
    static void ShowTiltPrompt()
    {
        HeatMeter meter = Meter();
        if (meter == null) return;
        TiltPromptView prompt = meter.TiltPrompt != null ? meter.TiltPrompt : Object.FindAnyObjectByType<TiltPromptView>();
        if (prompt != null) prompt.DebugShow();
        else Debug.LogWarning("[Heat] The tilt prompt isn't there yet (it is created a frame after the scene loads).");
    }

    [MenuItem(Root + "Add 25 Heat", true)]
    [MenuItem(Root + "Set Heat 0", true)]
    [MenuItem(Root + "Force Audit", true)]
    [MenuItem(Root + "Show Tilt Prompt", true)]
    static bool CanUse()
    {
        return EditorApplication.isPlaying;
    }

    static HeatMeter Meter()
    {
        if (!EditorApplication.isPlaying)
        {
            Debug.LogWarning("[Heat] Enter Play mode in MainGame first.");
            return null;
        }
        HeatMeter meter = HeatMeter.Instance != null ? HeatMeter.Instance : Object.FindAnyObjectByType<HeatMeter>();
        if (meter == null)
            Debug.LogWarning("[Heat] No HeatMeter in this scene (it is added to scenes with a CompanyManager, like MainGame).");
        return meter;
    }
}
