using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Adds the company casino (CasinoManager with the slot machine, the blackjack table and the coin flip) to every scene
/// that has a CompanyManager, when it loads, unless the scene already has a CasinoManager. So MainGame needs no scene
/// edit. Games already in the scene are used instead of new ones. The screens are built the first time the casino opens.
/// </summary>
public static class CasinoBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureCasino();   // the first scene loaded before this ran
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureCasino();
    }

    /// <summary>
    /// The scene's casino, created next to the CompanyManager when there is none. Null when the scene has no
    /// CompanyManager, unless <paramref name="evenWithoutCompany"/> (debug).
    /// </summary>
    public static CasinoManager EnsureCasino(bool evenWithoutCompany = false)
    {
        if (!Application.isPlaying) return null;
        CasinoManager existing = CasinoManager.Instance != null ? CasinoManager.Instance : Object.FindAnyObjectByType<CasinoManager>(FindObjectsInactive.Include);
        if (existing != null) return existing;

        CompanyManager company = CompanyManager.Instance != null ? CompanyManager.Instance : Object.FindAnyObjectByType<CompanyManager>();
        if (company == null && !evenWithoutCompany) return null;

        GameObject go = new GameObject("Casino (Runtime)");
        if (company != null)
        {
            Scene scene = company.gameObject.scene;
            if (scene.IsValid() && scene.isLoaded && go.scene != scene)
            {
                // Live and die with the company's scene
                try { SceneManager.MoveGameObjectToScene(go, scene); }
                catch (Exception e) { Debug.LogWarning("[Casino] Couldn't move the casino next to the CompanyManager: " + e.Message); }
            }
        }

        CasinoManager casino = go.AddComponent<CasinoManager>();
        casino.slotMachine = Object.FindAnyObjectByType<SlotMachineGame>(FindObjectsInactive.Include);
        if (casino.slotMachine == null) casino.slotMachine = AddGame<SlotMachineGame>(go, "Slots");
        casino.blackjack = Object.FindAnyObjectByType<CasinoBlackjack>(FindObjectsInactive.Include);
        if (casino.blackjack == null) casino.blackjack = AddGame<CasinoBlackjack>(go, "Blackjack");
        casino.coinFlip = Object.FindAnyObjectByType<CoinFlipGame>(FindObjectsInactive.Include);
        if (casino.coinFlip == null) casino.coinFlip = AddGame<CoinFlipGame>(go, "CoinFlip");
        return casino;
    }

    static T AddGame<T>(GameObject casino, string name) where T : MonoBehaviour
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(casino.transform, false);
        return go.AddComponent<T>();
    }
}
