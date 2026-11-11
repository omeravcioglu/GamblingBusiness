using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Batch smoke test for RunEndScreens.prefab: in an empty scene (no RunEndDirector, so nothing touches the save) the
/// game over sheet opens and closes, the win screen fills its cover, drops its confetti into place and closes with
/// Keep playing, and the $1B certificate answers Later, Pick a city and its close button exactly once each; the game
/// stays paused while each one is up. Exit code 0 = all passed.
///   Unity.exe -batchmode -projectPath . -executeMethod EndingsSmoke.Run -logFile smoke.log
/// </summary>
[InitializeOnLoad]
public static class EndingsSmoke
{
    const string RunningKey = "EndingsSmoke.Running";

    static Step step;
    static float stepStarted;
    static readonly List<string> problems = new List<string>();
    static readonly List<string> logs = new List<string>();
    static GameOverView over;
    static WinView win;
    static BillionCertificateView cert;
    static Vector2[] confettiHome;
    static int enterCalls, laterCalls;
    static bool started;

    enum Step { Over, OverClose, Win, WinSettled, WinClose, CertLater, CertEnter, CertClose, Done }

    static EndingsSmoke()
    {
        if (!SessionState.GetBool(RunningKey, false)) return;
        if (!Application.isBatchMode)
        {
            SessionState.EraseBool(RunningKey);
            return;
        }
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
    }

    public static void Run()
    {
        if (!Application.isBatchMode)
        {
            Debug.LogWarning("[EndingsSmoke] Batch mode only (it quits the editor when done).");
            return;
        }
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EndingsBuilder.PrefabPath);
            if (prefab == null) throw new Exception("No " + EndingsBuilder.PrefabPath + " - build it first.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            PrefabUtility.InstantiatePrefab(prefab);
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
        if (type == LogType.Log || !started) return;
        logs.Add(type + ": " + message.Split('\n')[0]);
    }

    static RunSummary Summary() => new RunSummary
    {
        companyName = "Nova Holdings", day = 164, companyValue = 10000000000d, peakValue = 10000000000d, hires = 386, goldenHires = 4,
        riskCardsPlayed = 40, biggestWin = 55000, biggestWinTitle = "Cure Discovery", biggestLoss = -30000, biggestLossTitle = "Factory Fire",
        crisesSolved = 18, crisesTotal = 27, casinoNet = 2100000, hasGambled = true
    };

    static void Check(bool ok, string problem)
    {
        if (!ok) problems.Add(problem);
    }

    static bool Paused(object owner) => UIManager.Instance != null && UIManager.Instance.IsPanelOpen(owner) && Time.timeScale == 0f;

    static void Go(Step s)
    {
        step = s;
        stepStarted = Time.realtimeSinceStartup;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        float t = Time.realtimeSinceStartup - stepStarted;
        try
        {
            if (!started)
            {
                started = true;
                over = UnityEngine.Object.FindAnyObjectByType<GameOverView>(FindObjectsInactive.Include);
                win = UnityEngine.Object.FindAnyObjectByType<WinView>(FindObjectsInactive.Include);
                cert = UnityEngine.Object.FindAnyObjectByType<BillionCertificateView>(FindObjectsInactive.Include);
                if (over == null || win == null || cert == null) { Finish("views missing from the prefab"); return; }
                Check(BillionCertificateView.Instance == cert, "certificate didn't register itself");
                over.Show(Summary());
                Go(Step.Over);
                return;
            }
            if (t > 20f) { Finish("stuck at " + step); return; }

            switch (step)
            {
                case Step.Over:
                    if (t < 0.3f) break;
                    Check(over.IsOpen && over.root.gameObject.activeInHierarchy, "game over not up");
                    Check(Paused(over), "game over doesn't pause");
                    Check(over.headlineText.text == "Nova Holdings is out of business.", "headline: " + over.headlineText.text);
                    Check(over.rowValues[0].text == "164" && over.rowValues[6].text == "18 of 27", "statement rows not filled");
                    Check(over.rowNotes[4].text == "(Cure Discovery)", "biggest win note: " + over.rowNotes[4].text);
                    over.Hide();
                    Go(Step.OverClose);
                    break;

                case Step.OverClose:
                    if (t < 0.2f) break;
                    Check(!over.IsOpen && !over.root.gameObject.activeSelf, "game over didn't close");
                    Check(!UIManager.Instance.IsPanelOpen(over) && Time.timeScale > 0f, "game still paused after the game over closed");
                    confettiHome = new Vector2[win.confetti.Length];
                    for (int i = 0; i < win.confetti.Length; i++) confettiHome[i] = win.confetti[i].anchoredPosition;
                    win.Show(Summary(), 10000000000d);
                    Go(Step.Win);
                    break;

                case Step.Win:
                    if (t < 0.2f) break;
                    Check(win.IsOpen && Paused(win), "win not up / not paused");
                    Check(win.issueText.text.StartsWith("Day 164 issue"), "issue line: " + win.issueText.text);
                    Check(win.coverAmountText.text.Contains("$10 BILLION"), "amount: " + win.coverAmountText.text);
                    string line1 = win.coverLines[1].text, line0 = win.coverLines[0].text;
                    Check(!line1.Contains("<u") && line1.Contains("<link=\"ul#"), "underline not converted: " + line1);
                    Check(line0.Contains("<nobr>"), "highlight not kept together: " + line0);
                    var underlines = win.coverLines[1].transform.parent.Find("Underlines");
                    Check(underlines != null && underlines.GetComponentInChildren<UnityEngine.UI.Image>() != null, "no underline drawn");
                    bool moving = false;
                    for (int i = 0; i < win.confetti.Length; i++)
                        if ((win.confetti[i].anchoredPosition - confettiHome[i]).sqrMagnitude > 1f) moving = true;
                    Check(moving, "confetti didn't start above its spots");
                    Go(Step.WinSettled);
                    break;

                case Step.WinSettled:
                    if (t < 2f) break;
                    float worst = 0f;
                    for (int i = 0; i < win.confetti.Length; i++)
                        worst = Mathf.Max(worst, (win.confetti[i].anchoredPosition - confettiHome[i]).magnitude);
                    Check(worst < 0.5f, "confetti didn't land (off by " + worst.ToString("0.0") + ")");
                    win.keepPlayingButton.onClick.Invoke();
                    Go(Step.WinClose);
                    break;

                case Step.WinClose:
                    if (t < 0.2f) break;
                    Check(!win.IsOpen && !win.root.gameObject.activeSelf, "Keep playing didn't close the win screen");
                    Check(Time.timeScale > 0f, "game still paused after Keep playing");
                    Check(BillionCertificateView.TryShow("Nova Holdings", 1000000000d, () => enterCalls++, () => laterCalls++), "TryShow said no");
                    Go(Step.CertLater);
                    break;

                case Step.CertLater:
                    if (t < 0.3f) break;
                    Check(cert.IsOpen && Paused(cert), "certificate not up / not paused");
                    Check(cert.companyText.text == "NOVA HOLDINGS", "company: " + cert.companyText.text);
                    Check(cert.reachedText.text.Contains("$1,000,000,000"), "reached line: " + cert.reachedText.text);
                    cert.laterButton.onClick.Invoke();
                    Check(laterCalls == 1 && enterCalls == 0 && !cert.IsOpen, "Later: later " + laterCalls + ", enter " + enterCalls);
                    BillionCertificateView.TryShow("Nova Holdings", 1000000000d, () => enterCalls++, () => laterCalls++);
                    Go(Step.CertEnter);
                    break;

                case Step.CertEnter:
                    if (t < 0.2f) break;
                    cert.enterButton.onClick.Invoke();
                    Check(enterCalls == 1 && laterCalls == 1 && !cert.IsOpen, "Enter: later " + laterCalls + ", enter " + enterCalls);
                    BillionCertificateView.TryShow("Nova Holdings", 1000000000d, () => enterCalls++, () => laterCalls++);
                    Go(Step.CertClose);
                    break;

                case Step.CertClose:
                    if (t < 0.2f) break;
                    cert.closeButton.onClick.Invoke();
                    Check(laterCalls == 2 && enterCalls == 1 && !cert.IsOpen, "Close: later " + laterCalls + ", enter " + enterCalls);
                    Check(Time.timeScale > 0f && !UIManager.Instance.IsPanelOpen(cert), "game still paused after the certificate");
                    Finish(null);
                    break;
            }
        }
        catch (Exception e)
        {
            Finish("exception: " + e);
        }
    }

    static void Finish(string fatal)
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= OnLog;
        SessionState.EraseBool(RunningKey);
        var lines = new List<string>();
        if (fatal != null) lines.Add("FATAL: " + fatal);
        foreach (var p in problems) lines.Add("problem: " + p);
        foreach (var l in logs) lines.Add("log: " + l);
        bool ok = fatal == null && problems.Count == 0 && logs.Count == 0;
        lines.Add(ok ? "All endings passed." : "Endings smoke test FAILED.");
        Debug.Log("[EndingsSmoke]\n" + string.Join("\n", lines));
        EditorApplication.Exit(ok ? 0 : 1);
    }
}
