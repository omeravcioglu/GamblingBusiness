using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The demo's per-run state (saved under "demo"): what the demo already did in this company (the skill gift, the golden
/// CV, the house flip, the end card...) and how long it has been played. Exists only in demo mode, in the scene of the
/// CompanyManager (DemoRunBootstrap adds it): fresh on a new company, restored on Continue. Use it through DemoRun.
/// </summary>
[DisallowMultipleComponent]
public class DemoRunState : MonoBehaviour, ISaveParticipant
{
    public static DemoRunState Instance { get; private set; }

    [Serializable]
    internal class Data
    {
        // A field missing from an older save keeps its default here
        public float playSeconds;
        public bool skillGiftGiven;
        public bool goldenDealt;
        public bool firstCrisisDone;
        public bool houseFlipUsed;
        public bool coachDone;
        public bool sofaHintShown;
        public bool verdictKnown;
        public bool verdictHit;
        public int verdictTier;
        public bool endCardShown;
        public bool keepPlaying;
        public bool finalCardShown;
        public string endReason = "";
    }

    internal Data data = new Data();

    public string SaveKey => "demo";

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public string CaptureState()
    {
        return JsonUtility.ToJson(data);
    }

    public void RestoreState(string json)
    {
        var restored = new Data();
        if (!string.IsNullOrEmpty(json))
        {
            try { JsonUtility.FromJsonOverwrite(json, restored); }
            catch (Exception e) { Debug.LogWarning("[Demo] Couldn't read the saved demo state (" + e.Message + "): starting it fresh."); }
        }
        if (restored.endReason == null) restored.endReason = "";
        if (float.IsNaN(restored.playSeconds) || restored.playSeconds < 0f) restored.playSeconds = 0f;
        data = restored;
    }
}

/// <summary>
/// The demo's run state for every system. Getters return false/0/"" and setters do nothing while there is no
/// DemoRunState (outside demo mode, or in a scene without a company).
/// </summary>
public static class DemoRun
{
    static DemoRunState.Data D => DemoRunState.Instance != null ? DemoRunState.Instance.data : null;

    /// <summary>A demo run is loaded (demo mode, company scene).</summary>
    public static bool Exists => DemoRunState.Instance != null;

    /// <summary>Unscaled MainGame seconds played (focused, pause menu closed).</summary>
    public static float PlaySeconds { get => D?.playSeconds ?? 0f; set { var d = D; if (d != null) d.playSeconds = Mathf.Max(0f, value); } }
    /// <summary>The Board signing bonus (skill points on the first hire) was given.</summary>
    public static bool SkillGiftGiven { get => D?.skillGiftGiven ?? false; set { var d = D; if (d != null) d.skillGiftGiven = value; } }
    /// <summary>The first batch's guaranteed golden CV was dealt.</summary>
    public static bool GoldenDealt { get => D?.goldenDealt ?? false; set { var d = D; if (d != null) d.goldenDealt = value; } }
    /// <summary>The first (forgiving) crisis happened.</summary>
    public static bool FirstCrisisDone { get => D?.firstCrisisDone ?? false; set { var d = D; if (d != null) d.firstCrisisDone = value; } }
    /// <summary>The first build coin flip (on the house) was used.</summary>
    public static bool HouseFlipUsed { get => D?.houseFlipUsed ?? false; set { var d = D; if (d != null) d.houseFlipUsed = value; } }
    /// <summary>The FIRST BUILD checklist is done.</summary>
    public static bool CoachDone { get => D?.coachDone ?? false; set { var d = D; if (d != null) d.coachDone = value; } }
    /// <summary>The sofa tip toast was shown.</summary>
    public static bool SofaHintShown { get => D?.sofaHintShown ?? false; set { var d = D; if (d != null) d.sofaHintShown = value; } }
    /// <summary>The finale's Board verdict is known.</summary>
    public static bool VerdictKnown { get => D?.verdictKnown ?? false; set { var d = D; if (d != null) d.verdictKnown = value; } }
    /// <summary>The finale's Board target was hit.</summary>
    public static bool VerdictHit { get => D?.verdictHit ?? false; set { var d = D; if (d != null) d.verdictHit = value; } }
    /// <summary>The finale target's tier (BoardMeetings.TierSafe / TierStretch / TierMoonshot).</summary>
    public static int VerdictTier { get => D?.verdictTier ?? 0; set { var d = D; if (d != null) d.verdictTier = value; } }
    /// <summary>The DEMO COMPLETE card was shown.</summary>
    public static bool EndCardShown { get => D?.endCardShown ?? false; set { var d = D; if (d != null) d.endCardShown = value; } }
    /// <summary>The player chose Keep playing on the end card.</summary>
    public static bool KeepPlaying { get => D?.keepPlaying ?? false; set { var d = D; if (d != null) d.keepPlaying = value; } }
    /// <summary>The final card (no Keep playing) was shown.</summary>
    public static bool FinalCardShown { get => D?.finalCardShown ?? false; set { var d = D; if (d != null) d.finalCardShown = value; } }
    /// <summary>Why the demo ended: "verdict", "timecap", "hardstop" or "jackpot" ("" while it hasn't).</summary>
    public static string EndReason { get => D?.endReason ?? ""; set { var d = D; if (d != null) d.endReason = value ?? ""; } }
}

/// <summary>Adds DemoRunState to every scene with a CompanyManager while demo mode is on (before SaveSystem loads the run).</summary>
static class DemoRunBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (!DemoMode.Active) return;
        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureState();   // the first scene loaded before this ran
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // SaveSystem collects the participants two frames after sceneLoaded, so this one is there in time
        EnsureState();
    }

    static void EnsureState()
    {
        if (!Application.isPlaying || !DemoMode.Active) return;
        CompanyManager company = CompanyManager.Instance != null ? CompanyManager.Instance : UnityEngine.Object.FindAnyObjectByType<CompanyManager>();
        if (company == null) return;
        if (DemoRunState.Instance != null || UnityEngine.Object.FindAnyObjectByType<DemoRunState>(FindObjectsInactive.Include) != null) return;

        GameObject go = new GameObject("Demo (Runtime)");
        Scene scene = company.gameObject.scene;
        if (scene.IsValid() && scene.isLoaded && go.scene != scene)
        {
            // Live and die with the company's scene: a new company starts with a fresh state
            try { SceneManager.MoveGameObjectToScene(go, scene); }
            catch (Exception e) { Debug.LogWarning("[Demo] Couldn't move the demo state next to the CompanyManager: " + e.Message); }
        }
        go.AddComponent<DemoRunState>();
    }
}
