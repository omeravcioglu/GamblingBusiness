using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Batch smoke test for the CRT crisis games (CrisisGames.prefab): in an empty scene each of the 20 panels runs the real
/// flow - warning card, Start now, setup, a moment of play, then a win (odd panels) or Give up (even panels), the
/// result on the glass and Back to work - and anything odd is reported: setup failures, exceptions, warnings, runs that
/// get stuck, a callback that doesn't fire once, a panel that stays up.
///   Unity.exe -batchmode -projectPath . -executeMethod CrisisGamesSmoke.Run -logFile smoke.log
/// Exit code 0 = all passed.
/// </summary>
[InitializeOnLoad]
public static class CrisisGamesSmoke
{
    const string RunningKey = "CrisisGamesSmoke.Running";
    const float Timeout = 240f;

    static readonly FieldInfo IsPlayingField = typeof(MiniGameBase).GetField("isPlaying", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly FieldInfo SetupFailedField = typeof(MiniGameBase).GetField("setupFailed", BindingFlags.NonPublic | BindingFlags.Instance);
    static readonly MethodInfo WinMethod = typeof(MiniGameBase).GetMethod("Win", BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);

    enum Step { Start, Warning, Play, Result, Closed }

    class GameRun
    {
        public MiniGameBase game;
        public CrtScreenView view;
        public bool win;
        public int callbacks;
        public bool callbackValue;
        public bool reachedPlay, sawWarning, sawResult;
        public readonly List<string> problems = new List<string>();
        public readonly List<string> logs = new List<string>();
    }

    static List<GameRun> runs;
    static int index;
    static Step step;
    static float stepStarted, started;
    static GameRun current;

    static CrisisGamesSmoke()
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        if (!Application.isBatchMode)
        {
            // it ends by quitting the editor: never in an interactive session
            SessionState.EraseBool(RunningKey);
            return;
        }
        // after the domain reload of entering play mode
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    public static void Run()
    {
        if (!Application.isBatchMode)
        {
            Debug.LogWarning("[CrisisGamesSmoke] Batch mode only (it quits the editor when done).");
            return;
        }
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CrisisGamesBuilder.PrefabPath);
            if (prefab == null) throw new Exception("No " + CrisisGamesBuilder.PrefabPath + " - build it first.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            root.name = "CrisisGames";
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            new GameObject("Camera", typeof(Camera));
            SessionState.SetBool(RunningKey, true);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            SessionState.EraseBool(RunningKey);
            EditorApplication.Exit(1);
        }
    }

    static void OnLog(string message, string stack, LogType type)
    {
        if (current == null || type == LogType.Log) return;
        current.logs.Add(type + ": " + message);
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        float now = Time.realtimeSinceStartup;
        if (runs == null)
        {
            started = now;
            runs = new List<GameRun>();
            var root = GameObject.Find("CrisisGames");
            if (root == null) { Finish("CrisisGames not in the scene"); return; }
            foreach (Transform panel in root.transform)
            {
                var game = panel.GetComponent<MiniGameBase>();
                if (game != null)
                    runs.Add(new GameRun { game = game, view = panel.GetComponent<CrtScreenView>(), win = runs.Count % 2 == 1 });
            }
            index = -1;
            NextGame(now);
            return;
        }
        if (now - started > Timeout) { Finish("timed out"); return; }
        if (current == null) return;

        var r = current;
        float t = now - stepStarted;
        switch (step)
        {
            case Step.Warning:
                if (!r.sawWarning && r.view.warning != null && r.view.warning.activeInHierarchy) r.sawWarning = true;
                if (t > 0.4f)
                {
                    if (!r.sawWarning) r.problems.Add("no warning card");
                    r.view.startNowButton.onClick.Invoke();
                    Go(Step.Play, now);
                }
                break;

            case Step.Play:
                if ((bool)SetupFailedField.GetValue(r.game)) r.problems.Add("setup failed");
                if ((bool)IsPlayingField.GetValue(r.game))
                {
                    if (!r.reachedPlay)
                    {
                        r.reachedPlay = true;
                        stepStarted = now;
                        if (r.view.gameArea == null || !r.view.gameArea.activeInHierarchy) r.problems.Add("GameArea hidden in play");
                        if (r.view.warning != null && r.view.warning.activeSelf) r.problems.Add("warning still up in play");
                    }
                    else if (t > 1.2f)
                    {
                        if (r.win) WinMethod.Invoke(r.game, null);
                        else r.view.giveUpButton.onClick.Invoke();
                        Go(Step.Result, now);
                    }
                }
                else if (!r.game.IsRunning || t > 6f)
                {
                    r.problems.Add(r.game.IsRunning ? "never reached play" : "ended before play");
                    Go(Step.Result, now);
                }
                break;

            case Step.Result:
                if (!r.sawResult && r.view.resultLayer != null && r.view.resultLayer.activeInHierarchy)
                {
                    r.sawResult = true;
                    stepStarted = now;
                    string headline = r.view.resultHeadline != null ? r.view.resultHeadline.text : "";
                    string expected = CrisisText.Get(r.win ? "crisis.screen.result.headline.solved" : "crisis.screen.result.headline.failed");   // CRISIS SOLVED / FAILED in the current language
                    if (headline != expected) r.problems.Add("result says '" + headline + "'");
                }
                if (r.sawResult && now - stepStarted > 0.3f)
                {
                    r.view.backButton.onClick.Invoke();
                    Go(Step.Closed, now);
                }
                else if (!r.game.IsRunning || t > 6f)
                {
                    if (!r.sawResult) r.problems.Add("no result on the glass");
                    Go(Step.Closed, now);
                }
                break;

            case Step.Closed:
                if (t < 0.2f) break;
                if (r.game.IsRunning) r.problems.Add("still running after Back to work");
                if (r.game.gameObject.activeSelf) r.problems.Add("panel still up");
                if (r.callbacks != 1) r.problems.Add("callback fired " + r.callbacks + " times");
                else if (r.callbackValue != r.win) r.problems.Add("callback said " + r.callbackValue);
                NextGame(now);
                break;
        }
    }

    static void Go(Step s, float now)
    {
        step = s;
        stepStarted = now;
    }

    static void NextGame(float now)
    {
        index++;
        if (index >= runs.Count)
        {
            current = null;
            Finish(null);
            return;
        }
        current = runs[index];
        var r = current;
        try
        {
            r.game.gameObject.SetActive(true);
            r.game.SetProblem("Server is crashing! Get systems back online!");
            r.game.SetStakes(2000f, -1000f);
            r.game.StartGame(ok => { r.callbacks++; r.callbackValue = ok; });
            Go(Step.Warning, now);
        }
        catch (Exception e)
        {
            r.problems.Add("StartGame threw " + e.Message);
            Go(Step.Closed, now);
        }
    }

    static void Finish(string fatal)
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        SessionState.EraseBool(RunningKey);
        var lines = new List<string>();
        int failed = 0;
        if (fatal != null) { lines.Add("FATAL: " + fatal); failed++; }
        if (runs != null)
        {
            foreach (var r in runs)
            {
                bool ok = r.problems.Count == 0 && r.logs.Count == 0;
                if (!ok) failed++;
                lines.Add((ok ? "PASS " : "FAIL ") + r.game.name + " (" + (r.win ? "win" : "give up") + ")");
                foreach (var p in r.problems) lines.Add("    problem: " + p);
                foreach (var l in r.logs) lines.Add("    log: " + l.Split('\n')[0]);
            }
        }
        lines.Add(failed == 0 ? "All crisis games passed." : failed + " failed.");
        Debug.Log("[CrisisGamesSmoke]\n" + string.Join("\n", lines));
        EditorApplication.Exit(failed == 0 ? 0 : 1);
    }
}
