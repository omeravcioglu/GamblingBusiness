using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The Steam demo's numbers ("The First 12 Days"). Read from Resources/DemoMode.json with JsonUtility: a field missing
/// from the file keeps the default here. Outside demo mode DemoMode.Config is a default instance that nothing should act on.
/// </summary>
[Serializable]
public class DemoConfig
{
    // money and pacing
    public double startingValue = 100000;
    public float dayMin = 15f, dayMax = 25f;               // CompanyManager day length (full game: 15-45)
    public float taskStartDelay = 20f;
    public int salaryMin = 30000, salaryMax = 150000;
    public float goldenChance = 0.03f;
    public bool guaranteedFirstGolden = true;
    // skills
    public int skillGiftPoints = 2;                         // granted on the run's first hire
    public int hiresPerSkillPoint = 2;
    public int maxSkillTier = 3;
    public string[] lockedSkills = { "growth_3_stocks", "secret_ultimate" };
    // casino
    public int casinoAfterHires = 4, casinoFromDay = 4;     // whichever comes first
    public bool grantCasinoChain = false;                   // false = only chaos_5_casino
    public long casinoDefaultBet = 1000;
    public float casinoMaxBetShare = 0.5f;
    public int maxDoubleStreak = 3;
    // heat
    public float heatBetBase = 1.5f, heatBetMin = 1.5f;
    public float auditChanceHot = 0.03f, auditChanceOnFire = 0.10f;
    public float buildGambleHeatScale = 0.5f;
    // unlock schedule (FeatureGate properties read these)
    public int riskCardsAfterHires = 3, staffAtEmployees = 5, buildFromDay = 5, crisesFromDay = 6;
    public int boardFirstDay = 8, boardInterval = 4, managerFromDay = 10;
    public float managerDecisionRate = 0.6f;
    public string[] lockedFeatures = { "Stocks", "Online" };   // Feature enum names; add "Manager" to lock it
    public float minMemoGapSeconds = 25f;
    // risk cards
    public float firstRiskCardMin = 30f, firstRiskCardMax = 60f, riskCardMin = 50f, riskCardMax = 130f;
    public float riskLossCap = 0.40f;
    // crises
    public float firstCrisisMin = 20f, firstCrisisMax = 40f, crisisMin = 35f, crisisMax = 80f;
    public double crisisRewardShare = 0.02, crisisPenaltyShare = 0.01;
    // board
    public double boardExpectedMinimum = 4000;
    // building
    public double startingBuildBudget = 6000, buildWinMultiplier = 2.0;
    public bool houseFlip = true;
    public double houseFlipMaxBet = 10000;
    // shell and demo end
    public float splashSeconds = 6f, fundingFirstFlipChance = 0.5f;
    public float endCapSeconds = 810f, endGraceSeconds = 45f;    // MainGame play seconds (focused, pause menu closed)
    public int hardStopDay = 25;
    public float hardStopSeconds = 1500f;
    public double valueCap = 10000000;
    public bool alwaysShowTour = false;                       // kiosk: reset the tutorial profile on New Game
    // download size (Build Demo parks the other portraits for the build; PortraitLibrary keeps every pick inside these)
    public int demoPortraitsPerGender = 500;                  // the first N files of Portraits/Female and /Male ship; 0 = all 6,712
    public int demoPortraitMaxSize = 512;                     // Standalone max size of the shipped portraits; 0 = as imported (2048)
    // steam
    public long steamAppId = 5532550;                         // the demo app's id (created 2026-10-29); 0 = no relaunch
    public long fullGameAppId = 4427670;
    public string storeUrl = "https://store.steampowered.com/app/4427670/";

    /// <summary>Keep hand-edited values usable (a bad file must not break the demo).</summary>
    internal void Clamp()
    {
        if (!(startingValue > 0)) startingValue = 100000;
        dayMin = Mathf.Max(5f, dayMin);
        dayMax = Mathf.Max(dayMin, dayMax);
        taskStartDelay = Mathf.Max(0f, taskStartDelay);
        salaryMin = Math.Max(0, salaryMin);
        salaryMax = Math.Max(salaryMin, salaryMax);
        goldenChance = Mathf.Clamp01(goldenChance);
        skillGiftPoints = Math.Max(0, skillGiftPoints);
        hiresPerSkillPoint = Math.Max(1, hiresPerSkillPoint);
        maxSkillTier = Math.Max(1, maxSkillTier);
        lockedSkills = lockedSkills ?? new string[0];
        casinoAfterHires = Math.Max(0, casinoAfterHires);
        casinoFromDay = Math.Max(1, casinoFromDay);
        casinoDefaultBet = Math.Max(1, casinoDefaultBet);
        casinoMaxBetShare = Mathf.Clamp01(casinoMaxBetShare);
        maxDoubleStreak = Math.Max(0, maxDoubleStreak);
        heatBetBase = Mathf.Max(0f, heatBetBase);
        heatBetMin = Mathf.Max(0f, heatBetMin);
        auditChanceHot = Mathf.Clamp01(auditChanceHot);
        auditChanceOnFire = Mathf.Clamp01(auditChanceOnFire);
        buildGambleHeatScale = Mathf.Max(0f, buildGambleHeatScale);
        riskCardsAfterHires = Math.Max(0, riskCardsAfterHires);
        staffAtEmployees = Math.Max(0, staffAtEmployees);
        buildFromDay = Math.Max(1, buildFromDay);
        crisesFromDay = Math.Max(1, crisesFromDay);
        boardFirstDay = Math.Max(1, boardFirstDay);
        boardInterval = Math.Max(1, boardInterval);
        managerFromDay = Math.Max(1, managerFromDay);
        managerDecisionRate = Mathf.Max(0f, managerDecisionRate);
        lockedFeatures = lockedFeatures ?? new string[0];
        minMemoGapSeconds = Mathf.Max(0f, minMemoGapSeconds);
        firstRiskCardMin = Mathf.Max(1f, firstRiskCardMin);
        firstRiskCardMax = Mathf.Max(firstRiskCardMin, firstRiskCardMax);
        riskCardMin = Mathf.Max(1f, riskCardMin);
        riskCardMax = Mathf.Max(riskCardMin, riskCardMax);
        riskLossCap = Mathf.Clamp01(riskLossCap);
        firstCrisisMin = Mathf.Max(1f, firstCrisisMin);
        firstCrisisMax = Mathf.Max(firstCrisisMin, firstCrisisMax);
        crisisMin = Mathf.Max(1f, crisisMin);
        crisisMax = Mathf.Max(crisisMin, crisisMax);
        crisisRewardShare = Math.Min(1, Math.Max(0, crisisRewardShare));
        crisisPenaltyShare = Math.Min(1, Math.Max(0, crisisPenaltyShare));
        boardExpectedMinimum = Math.Max(0, boardExpectedMinimum);
        startingBuildBudget = Math.Max(0, startingBuildBudget);
        if (!(buildWinMultiplier >= 1)) buildWinMultiplier = 1;
        houseFlipMaxBet = Math.Max(0, houseFlipMaxBet);
        splashSeconds = Mathf.Max(0f, splashSeconds);
        fundingFirstFlipChance = Mathf.Clamp01(fundingFirstFlipChance);
        endCapSeconds = Mathf.Max(60f, endCapSeconds);
        endGraceSeconds = Mathf.Max(0f, endGraceSeconds);
        hardStopDay = Math.Max(boardFirstDay + boardInterval, hardStopDay);
        hardStopSeconds = Mathf.Max(endCapSeconds, hardStopSeconds);
        if (!(valueCap > startingValue)) valueCap = Math.Max(10000000, startingValue * 10);
        demoPortraitsPerGender = Math.Max(0, demoPortraitsPerGender);
        demoPortraitMaxSize = demoPortraitMaxSize <= 0 ? 0 : Mathf.ClosestPowerOfTwo(Mathf.Clamp(demoPortraitMaxSize, 64, 2048));
        steamAppId = Math.Max(0, steamAppId);
        if (fullGameAppId <= 0) fullGameAppId = 4427670;
        if (string.IsNullOrEmpty(storeUrl)) storeUrl = "https://store.steampowered.com/app/" + fullGameAppId + "/";
    }
}

/// <summary>
/// The Steam demo switch. Demo mode is on while Assets/Resources/DemoMode.json is in the build: Tools > Game > Demo Mode
/// adds and removes the file, and Tools > Game > Build Demo (Windows) writes it for the length of a demo build.
/// - It always wins over PlaytestMode (PlaytestMode.Active is false while this file exists; both build guards refuse a
///   build with both files).
/// - The demo keeps its own save file (savegame_demo.json), and starts no cloud save, UGS sign-in or achievements.
/// - Every demo behaviour sits behind Active, or behind a getter here that returns the full-game value when inactive.
/// </summary>
public static class DemoMode
{
    /// <summary>The Resources name of the switch file (SteamPlatform reads the same name: it can't see this class).</summary>
    public const string ResourceName = "DemoMode";
    /// <summary>The localization table of every demo text (Assets/Localization/Source/Demo.csv).</summary>
    public const string Table = "Demo";

    static readonly string[] CasinoLicenseOnly = { "chaos_5_casino" };
    static readonly string[] CasinoChain = { "chaos_1_luck", "chaos_2_lucky", "chaos_3_roulette", "chaos_4_energy", "chaos_5_casino" };

    static bool loaded;
    static DemoConfig config;                 // null while inactive
    static DemoConfig inactiveDefaults;       // what Config returns while inactive
    static readonly HashSet<Feature> lockedFeatures = new HashSet<Feature>();

    /// <summary>True while the switch file is in the build.</summary>
    public static bool Active
    {
        get
        {
            Load();
            return config != null;
        }
    }

    /// <summary>The demo's numbers. Never null: a shared default instance while inactive.</summary>
    public static DemoConfig Config
    {
        get
        {
            Load();
            if (config != null) return config;
            return inactiveDefaults ?? (inactiveDefaults = new DemoConfig());
        }
    }

    /// <summary>What a demo company starts with.</summary>
    public static double StartingValue => Config.startingValue;

    /// <summary>A new company's starting value: the demo's, else playtest mode's, else <paramref name="fullGameValue"/>.</summary>
    public static double StartingValueFor(double fullGameValue)
    {
        if (Active) return Config.startingValue;
        return PlaytestMode.Active ? PlaytestMode.StartingValue : fullGameValue;
    }

    /// <summary>The day of the Board verdict that ends the demo (first meeting day + interval: 12).</summary>
    public static int FinaleDay => Config.boardFirstDay + Config.boardInterval;

    /// <summary>True when the demo shows <paramref name="feature"/> with a FULL GAME stamp instead of opening it.</summary>
    public static bool IsLocked(Feature feature)
    {
        return Active && lockedFeatures.Contains(feature);
    }

    /// <summary>The largest share of company value one bad risk card may take (1 = no cap; the cap is off with the casino chain).</summary>
    public static float RiskLossCap => Active && !Config.grantCasinoChain ? Config.riskLossCap : 1f;

    /// <summary>The manager's decision rate multiplier (1 outside the demo).</summary>
    public static float ManagerDecisionRate => Active ? Config.managerDecisionRate : 1f;

    /// <summary>The skills the demo's free Casino License grants: the license alone, or the whole chaos chain in tree order.</summary>
    public static IReadOnlyList<string> CasinoGrantIds => Config.grantCasinoChain ? CasinoChain : CasinoLicenseOnly;

    /// <summary>A text from the Demo table, with named placeholders.</summary>
    public static string T(string key, params (string name, object value)[] args)
    {
        return L10n.Get(Table, key, args);
    }

    /// <summary>The full game's store page: the Steam overlay when it can, else the browser (with a toast saying so).</summary>
    public static void OpenStorePage()
    {
        DemoConfig c = Config;
        if (c.fullGameAppId > 0 && c.fullGameAppId <= uint.MaxValue && SteamPlatform.OpenStorePage((uint)c.fullGameAppId))
            return;
        string url = string.IsNullOrEmpty(c.storeUrl) ? "https://store.steampowered.com/app/" + c.fullGameAppId + "/" : c.storeUrl;
        Application.OpenURL(url);
        GameNotifications.Show(DemoMode.T("demo.wishlist.browser"));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Entering Play mode without a domain reload keeps statics, and the file may have been added or removed since
        Reload();
    }

    /// <summary>Forget the cached switch so the next read loads Resources/DemoMode.json again (editor tools call it after
    /// adding or removing the file).</summary>
    public static void Reload()
    {
        loaded = false;
        config = null;
        lockedFeatures.Clear();
    }

    static void Load()
    {
        if (loaded) return;
        loaded = true;
        var file = Resources.Load<TextAsset>(ResourceName);
        if (file == null) return;
        DemoConfig read;
        try
        {
            read = JsonUtility.FromJson<DemoConfig>(file.text) ?? new DemoConfig();
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Demo] Couldn't read Resources/" + ResourceName + ".json (" + e.Message + "): using the defaults.");
            read = new DemoConfig();
        }
        Resources.UnloadAsset(file);
        read.Clamp();

        lockedFeatures.Clear();
        var lockedNames = new List<string>();
        foreach (string name in read.lockedFeatures)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (Enum.TryParse(name.Trim(), true, out Feature feature) && Enum.IsDefined(typeof(Feature), feature))
            {
                if (lockedFeatures.Add(feature)) lockedNames.Add(feature.ToString());
            }
            else Debug.LogWarning("[Demo] Unknown feature in lockedFeatures: \"" + name + "\" (ignored).");
        }
        config = read;

        Debug.Log("[Demo] Demo mode is on: $" + read.startingValue.ToString("N0", CultureInfo.InvariantCulture) +
                  " start, verdict day " + FinaleDay +
                  ", locked: " + (lockedNames.Count > 0 ? string.Join(", ", lockedNames) : "nothing") +
                  "; no cloud save, no achievements.");
    }
}
