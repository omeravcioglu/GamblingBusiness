using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// One batch-mode entry for all the new-UI builders:
///   Unity.exe -batchmode -projectPath . -executeMethod NewUIBatch.Run [-install] [-preview folder] [-langs tr,zh-Hans,...]
///   [-previewSize 3440x1440]   (the previews as on that screen; 1920x1080 without it)
/// Builds the prefabs (office HUD, pause menu, ..., the CV paper face, the crisis games, the endings), installs them in MainGame with -install and
/// renders PNG previews into the folder with -preview (and again per language into folder/<code>/ with -langs).
/// Tools > Game > New UI > Build All Screens builds and installs everything from the Editor.
/// </summary>
public static class NewUIBatch
{
    public static void Run()
    {
        int code = 0;
        var log = new List<string>();
        try
        {
            UIPreview.EnsureProjectPipeline(log);
            var args = System.Environment.GetCommandLineArgs();
            bool install = System.Array.IndexOf(args, "-install") >= 0;
            string previewDir = Arg("-preview");
            string langs = Arg("-langs");
            UIPreview.ReadScreenSizeArg();   // -previewSize 3440x1440: the previews as on that screen

            var screens = BuildAll(log);
            if (!screens.AllBuilt) code = 1;

            if (install)
                InstallAll(screens, log);

            if (previewDir != null && screens.hud != null)
            {
                RenderPreviews(screens, previewDir, log);
                if (!string.IsNullOrEmpty(langs))
                    RenderLanguagePreviews(screens, previewDir, langs.Split(','), log);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        try
        {
            UIPreview.EnsureProjectPipeline(log);   // never exit with the render pipeline cleared
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
        Debug.Log("[NewUIBatch]\n" + string.Join("\n", log));
        EditorApplication.Exit(code);
    }

    /// <summary>
    /// Every builder from the Editor in one go: builds all the prefabs and installs them in their scenes as each Build menu
    /// does (asks to save open scene changes first), then reopens the scene that was open.
    /// </summary>
    [MenuItem("Tools/Game/New UI/Build All Screens")]
    public static void BuildAllFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Build All Screens", "Stop Play mode first.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        string openScene = SceneManager.GetActiveScene().path;
        var log = new List<string>();
        try
        {
            UIPreview.EnsureProjectPipeline(log);
            var screens = BuildAll(log);
            InstallAll(screens, log);
            log.Insert(0, screens.AllBuilt ? "All screens built and installed." : "Some screens failed to build: see the lines below.");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            log.Insert(0, "Stopped by an error: " + e.Message);
        }
        finally
        {
            if (!string.IsNullOrEmpty(openScene) && SceneManager.GetActiveScene().path != openScene)
                EditorSceneManager.OpenScene(openScene);
        }

        string summary = string.Join("\n", log);
        Debug.Log("[NewUIBatch] Build All Screens\n" + summary);
        EditorUtility.DisplayDialog("Build All Screens", summary.Length > 1500 ? summary.Substring(0, 1500) + "\n…" : summary, "OK");
    }

    /// <summary>The built prefabs (null where a builder failed).</summary>
    sealed class Screens
    {
        public GameObject hud, pause, names, risk, upgrade, skills, mainMenu, cv, crisis, endings, stocks;

        public bool AllBuilt => hud != null && pause != null && names != null && risk != null && upgrade != null && skills != null
                                && mainMenu != null && cv != null && crisis != null && endings != null && stocks != null;
    }

    static Screens BuildAll(List<string> log)
    {
        return new Screens
        {
            hud = OfficeHudBuilder.BuildPrefab(log),
            pause = PauseMenuBuilder.BuildPrefab(log),
            names = CompanyNameBuilder.BuildPrefab(log),
            risk = RiskEventBuilder.BuildPrefab(log),
            upgrade = UpgradeFlowBuilder.BuildPrefab(log),
            skills = SkillBoardBuilder.BuildPrefab(log),
            mainMenu = MainMenuBuilder.BuildPrefab(log),
            cv = CVPaperBuilder.BuildPrefab(log),
            crisis = CrisisGamesBuilder.BuildPrefab(log),
            endings = EndingsBuilder.BuildPrefab(log),
            stocks = StockMarketBuilder.BuildPrefab(log),
        };
    }

    static void InstallAll(Screens s, List<string> log)
    {
        if (s.hud != null) OfficeHudBuilder.InstallInMainGame(s.hud, log);
        if (s.pause != null) PauseMenuBuilder.Install(s.pause, log);
        if (s.names != null) CompanyNameBuilder.Install(s.names, log);
        if (s.risk != null) RiskEventBuilder.Install(s.risk, log);
        if (s.upgrade != null) UpgradeFlowBuilder.Install(s.upgrade, log);
        if (s.skills != null) SkillBoardBuilder.Install(s.skills, log);
        if (s.cv != null) CVPaperBuilder.Install(s.cv, log);
        if (s.crisis != null) CrisisGamesBuilder.Install(s.crisis, log);
        if (s.endings != null) EndingsBuilder.Install(s.endings, log);
        if (s.stocks != null) StockMarketBuilder.Install(s.stocks, log);
        if (s.mainMenu != null) MainMenuBuilder.Install(s.mainMenu, log);   // last: opens the Main_Menu scene
    }

    /// <summary>
    /// The previews again in other languages ("-langs tr,zh-Hans"), each into previewDir/&lt;code&gt;/. The saved language
    /// choice (PlayerPrefs) is put back afterwards. Sample texts the previews write themselves stay English.
    /// </summary>
    static void RenderLanguagePreviews(Screens s, string previewDir, string[] codes, List<string> log)
    {
        string prefKey = "selected-locale";
        foreach (var selector in UnityEngine.Localization.Settings.LocalizationSettings.StartupLocaleSelectors)
            if (selector is UnityEngine.Localization.Settings.PlayerPrefLocaleSelector prefs) prefKey = prefs.PlayerPreferenceKey;
        bool hadPref = PlayerPrefs.HasKey(prefKey);
        string savedPref = hadPref ? PlayerPrefs.GetString(prefKey) : null;
        string before = L10n.CurrentCode;
        try
        {
            foreach (string raw in codes)
            {
                string code = raw.Trim();
                if (code.Length == 0) continue;
                if (!L10n.SetLanguage(code))
                {
                    log.Add("Language " + code + " isn't available: skipped.");
                    continue;
                }
                RenderPreviews(s, Path.Combine(previewDir, code), log);
            }
        }
        finally
        {
            L10n.SetLanguage(before);
            if (hadPref) PlayerPrefs.SetString(prefKey, savedPref);
            else PlayerPrefs.DeleteKey(prefKey);
            PlayerPrefs.Save();
            log.Add("Language back to " + before + ".");
        }
    }

    static string Arg(string name)
    {
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i + 1 < args.Length; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    static void RenderPreviews(Screens s, string previewDir, List<string> log)
    {
        GameObject hud = s.hud, pause = s.pause, names = s.names, risk = s.risk, upgrade = s.upgrade, skills = s.skills,
            mainMenu = s.mainMenu, cv = s.cv, crisis = s.crisis, endings = s.endings, stocks = s.stocks;
        UIPreview.RenderOfficeHud(hud, Path.Combine(previewDir, "hud.png"), log);
        if (pause != null)
        {
            UIPreview.Render(Path.Combine(previewDir, "pause.png"), log, new UIPreview.Layer(hud), new UIPreview.Layer(pause));
            UIPreview.Render(Path.Combine(previewDir, "pause_settings.png"), log, new UIPreview.Layer(hud), new UIPreview.Layer(pause, g =>
            {
                var v = g.GetComponent<PauseMenuView>();
                v.menuView.SetActive(false);
                v.settingsView.SetActive(true);
            }));
        }
        if (risk != null)
        {
            UIPreview.Render(Path.Combine(previewDir, "risk_cards.png"), log, new UIPreview.Layer(hud), new UIPreview.Layer(risk, g =>
            {
                // Beginner's Luck (+15%) as on the design board
                var v = g.GetComponentInChildren<RiskCardsView>(true);
                foreach (var c in new[] { v.highRisk, v.balanced, v.lowRisk })
                {
                    float from = c.goodSegment.anchorMax.x;
                    c.goodSegment.anchorMax = new Vector2(from, 1f);
                    c.bonusSegment.gameObject.SetActive(true);
                    c.bonusSegment.anchorMin = new Vector2(from, 0f);
                    c.bonusSegment.anchorMax = new Vector2(from + 0.15f, 1f);
                    c.bonusSegment.offsetMin = c.bonusSegment.offsetMax = Vector2.zero;
                    c.badSegment.anchorMin = new Vector2(from + 0.15f, 0f);
                    c.badSegment.offsetMin = c.badSegment.offsetMax = Vector2.zero;
                    int b = Mathf.RoundToInt(from * 100f), n = b + 15;
                    c.odds.text = "<color=#676B74><s>" + b + "%</s></color> <color=#1E7B45>" + n + "% good</color> \u00B7 <color=#C0352B>" + (100 - n) + "% bad</color>";
                    c.chip.SetActive(true);
                    c.chipText.text = "+15% Beginner\u2019s Luck";
                }
            }));
            UIPreview.Render(Path.Combine(previewDir, "risk_good.png"), log, new UIPreview.Layer(hud), new UIPreview.Layer(risk, g =>
            {
                g.transform.Find("Cards").gameObject.SetActive(false);
                g.transform.Find("Result").gameObject.SetActive(true);
            }));
            UIPreview.Render(Path.Combine(previewDir, "risk_bad.png"), log, new UIPreview.Layer(hud), new UIPreview.Layer(risk, g =>
            {
                g.transform.Find("Cards").gameObject.SetActive(false);
                g.transform.Find("Result").gameObject.SetActive(true);
                var v = g.GetComponentInChildren<RiskResultView>(true);
                v.verdict.text = "BAD NEWS";
                v.verdict.color = UIColors.Loss;
                v.headline.text = "Data Breach";
                v.story.text = "Hackers stole customer data \u2014 massive fines incoming!";
                v.notes.text = "UNLUCKY PROTECTION: 25% less damage.\nYou played: High risk (40% good)";
                v.stampText.text = UIFormat.Minus + "$20,000";
                v.stampText.color = UIColors.Loss;
                foreach (var img in v.stampLines) img.color = UIColors.Loss;
                v.artIcon.sprite = v.badArt;
                v.artIcon.color = UIColors.Loss;
                v.ouchSticky.SetActive(true);
            }));
        }
        if (upgrade != null)
            RenderUpgradeFlow(hud, upgrade, previewDir, log);
        if (skills != null)
            RenderSkillBoard(hud, skills, previewDir, log);
        if (mainMenu != null)
            RenderMainMenu(mainMenu, previewDir, log);
        if (names != null)
        {
            UIPreview.Render(Path.Combine(previewDir, "name_first.png"), log, new UIPreview.Layer(hud), new UIPreview.Layer(names));
            UIPreview.Render(Path.Combine(previewDir, "name_rename_error.png"), log, new UIPreview.Layer(hud), new UIPreview.Layer(names, g =>
            {
                // what CompanyNameView and CompanyNameUI do at runtime for a rename with a too-short name (in the
                // preview's language; the button row places the buttons)
                var v = g.GetComponent<CompanyNameView>();
                v.title.text = L10n.Get("Hud", "hud.name.title.rename");
                v.hint.text = L10n.Get("Hud", "hud.name.hint.rename", ("min", 3), ("max", 25), ("company", "Nova Holdings"));
                v.confirmLabel.text = L10n.Get("Hud", "hud.name.confirm.rename");
                v.confirmIcon.sprite = v.saveIcon;
                var c = v.confirmRect;
                c.sizeDelta = new Vector2(v.renameWidth, c.sizeDelta.y);
                v.cancelButton.gameObject.SetActive(true);
                v.closeButton.gameObject.SetActive(true);
                v.input.text = "Co";
                v.counter.text = "2/25";
                v.counter.color = UIColors.Loss;
                v.inputBorder.color = UIColors.Loss;
                v.cardName.text = "CO";
                v.errorRow.SetActive(true);
                v.errorRow.GetComponent<TMPro.TMP_Text>().text = CompanyText.Get("company.name.error.short", ("count", 3));
            }));
        }
        if (crisis != null)
            CrisisGamesBuilder.RenderPreviews(hud, crisis, previewDir, log);
        if (endings != null)
            EndingsBuilder.RenderPreviews(hud, endings, previewDir, log);
        if (stocks != null)
            StockMarketBuilder.RenderPreviews(hud, stocks, previewDir, log);
        if (cv != null)
            CVPaperBuilder.RenderPreviews(cv, previewDir, log);   // last: its desk renders open MainGame (never saved)
    }

    /// <summary>The sofa of the design boards: +$100/day and +2% productivity per level, level 3 costs about $18,821.</summary>
    static OfficeItem Sofa(int level)
    {
        return new OfficeItem
        {
            itemName = "Sofa", currentLevel = level, maxLevel = 5, dailyBonusPerLevel = 100f, productivityPerLevel = 0.02f,
            baseCost = 5577, costMultiplier = 1.5f
        };
    }

    static void RenderUpgradeFlow(GameObject hud, GameObject flow, string dir, List<string> log)
    {
        System.Action<GameObject> Page(string name, System.Action<GameObject> fill) => g =>
        {
            g.transform.Find("HoverTag").gameObject.SetActive(false);
            g.transform.Find(name).gameObject.SetActive(true);
            fill(g);
        };

        UIPreview.Render(Path.Combine(dir, "upgrade_choice.png"), log, new UIPreview.Layer(hud), new UIPreview.Layer(flow, Page("Choice", g =>
            g.GetComponentInChildren<UpgradeChoiceView>(true).Show(Sofa(3), 0.6f, 0.3f, 0.1f, true, 0))));
        UIPreview.Render(Path.Combine(dir, "upgrade_choice_broke.png"), log, new UIPreview.Layer(hud), new UIPreview.Layer(flow, Page("Choice", g =>
        {
            g.GetComponentInChildren<UpgradeChoiceView>(true).Show(Sofa(0), 0.6f, 0.4f, 0f, false, 2300);
            foreach (var b in new[] { "Choice/Sheet/BlackjackOption/DealButton", "Choice/Sheet/DiceOption/RollButton" })
            {
                g.transform.Find(b).GetComponent<UnityEngine.UI.Button>().interactable = false;
                LookDisabled(g.transform.Find(b));
            }
        })));

        UIPreview.Render(Path.Combine(dir, "upgrade_table.png"), log, new UIPreview.Layer(hud), new UIPreview.Layer(flow, Page("Blackjack", g =>
        {
            var v = g.GetComponentInChildren<BlackjackTableView>(true);
            v.Show(new[] { 12, 5 }, true, new[] { 19, 47 }, "10 + ?", "16", false, BlackjackTableView.RulesLine, UIColors.Ink);
            v.SetStake("At stake: Sofa upgrade · <b>$18,821</b>", null);
        })));
        UIPreview.Render(Path.Combine(dir, "upgrade_table_end.png"), log, new UIPreview.Layer(hud), new UIPreview.Layer(flow, Page("Blackjack", g =>
        {
            var v = g.GetComponentInChildren<BlackjackTableView>(true);
            v.Show(new[] { 12, 20, 4 }, false, new[] { 19, 47, 29 }, "23 · bust", "20", true, "Dealer busts with 23. You win.", UIColors.Money);
            v.SetStake("At stake: $250,000 of company value (a win pays $375,000 into the build budget)", v.flow.stakeIcon);
        })));

        var results = new[]
        {
            ("upgrade_result_up.png", OfficeUpgradeManager.ResultKind.Upgraded, 4, false),
            ("upgrade_result_nothing.png", OfficeUpgradeManager.ResultKind.Nothing, 3, false),
            ("upgrade_result_down.png", OfficeUpgradeManager.ResultKind.Downgraded, 2, false),
            ("upgrade_result_bust.png", OfficeUpgradeManager.ResultKind.BlackjackLoss, 3, true),
            ("upgrade_result_push.png", OfficeUpgradeManager.ResultKind.BlackjackPush, 3, false),
        };
        foreach (var (file, kind, level, bust) in results)
        {
            UIPreview.Render(Path.Combine(dir, file), log, new UIPreview.Layer(hud), new UIPreview.Layer(flow, Page("Result", g =>
                g.GetComponentInChildren<UpgradeResultView>(true).Show(kind, Sofa(level), 18821, bust))));
        }

        UIPreview.Render(Path.Combine(dir, "hover_tag.png"), log, new UIPreview.Layer(hud), new UIPreview.Layer(flow, g =>
        {
            var v = g.GetComponentInChildren<HoverTagView>(true);
            ShowTag(v);
            v.Show(Sofa(3), KeyCode.E);
            v.Place(new Rect(70f, 330f, 400f, 190f), new Vector2(1920f, 1080f));
        }));
        UIPreview.Render(Path.Combine(dir, "hover_tag_left.png"), log, new UIPreview.Layer(hud), new UIPreview.Layer(flow, g =>
        {
            var v = g.GetComponentInChildren<HoverTagView>(true);
            ShowTag(v);
            v.Show(Sofa(5), KeyCode.E);
            v.Place(new Rect(1500f, 600f, 380f, 200f), new Vector2(1920f, 1080f));
        }));
    }

    /// <summary>The three design boards: Growth (Public Relations selected), Productivity (Mentorship just unlocked),
    /// Chaos (Casino License selected, locked). A real SkillTreeManager with the design's owned skills drives them.</summary>
    static void RenderSkillBoard(GameObject hud, GameObject board, string dir, List<string> log)
    {
        var previous = SkillTreeManager.Instance;
        try
        {
            // UIPreview.Render starts a new scene, so the manager is made inside each layer setup
            void Board(string file, int sp, int hires, System.Func<SkillTreeManager, IEnumerable<string>> owned, SkillBranch branch,
                string selected, string stamped)
            {
                UIPreview.Render(Path.Combine(dir, file), log, new UIPreview.Layer(hud), new UIPreview.Layer(board, g =>
                {
                    var manager = new GameObject("PreviewSkills").AddComponent<SkillTreeManager>();
                    typeof(SkillTreeManager).GetMethod("InitializeSkillTree",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(manager, null);
                    manager.ApplySavedState(sp, hires, owned(manager));
                    SkillTreeManager.Instance = manager;
                    var v = g.GetComponent<SkillBoardView>();
                    v.page.SetActive(true);
                    v.ShowBranch(branch, selected, stamped);
                }));
            }

            string[] growth = { "growth_1_investor", "growth_1_salary", "growth_1_cashback" };
            Board("skills_growth.png", 2, 5, m => growth, SkillBranch.Growth, "growth_2_pr", null);
            Board("skills_productivity.png", 0, 5,
                m => new[] { "growth_1_investor", "growth_1_salary", "growth_1_cashback", "prod_1_efficiency", "prod_1_training", "prod_1_workspace", "prod_2_mentor" },
                SkillBranch.Productivity, "prod_2_mentor", "prod_2_mentor");
            Board("skills_chaos.png", 2, 5, m => new[] { "growth_1_investor", "growth_1_salary", "growth_1_cashback", "chaos_1_luck" },
                SkillBranch.Chaos, "chaos_5_casino", null);
            // late game: Growth tiers 1-4 owned, strings everywhere
            Board("skills_growth_late.png", 7, 3, m =>
            {
                var late = new List<string>();
                foreach (var sk in m.allSkills)
                    if (sk.branch == SkillBranch.Growth && sk.tier <= 4 && sk.id != SkillTreeManager.SecretSkillId) late.Add(sk.id);
                return late;
            }, SkillBranch.Growth, "growth_5_manipulation", null);
        }
        finally
        {
            SkillTreeManager.Instance = previous;
        }
    }

    static void RenderMainMenu(GameObject menu, string dir, List<string> log)
    {
        System.Action<GameObject> With(System.Action<MainMenuView, MenuCoinFlip2D, MenuDiceVolume2D> f) => g =>
            f(g.GetComponent<MainMenuView>(), g.GetComponentInChildren<MenuCoinFlip2D>(true), g.GetComponentInChildren<MenuDiceVolume2D>(true));

        UIPreview.Render(Path.Combine(dir, "menu.png"), log, new UIPreview.Layer(menu));
        UIPreview.Render(Path.Combine(dir, "menu_coin_air.png"), log, new UIPreview.Layer(menu, With((v, c, d) =>
        {
            c.ShowInAir();
            v.airNote.SetActive(true);
            v.menuGroup.alpha = v.fadedAlpha;
        })));
        UIPreview.Render(Path.Combine(dir, "menu_tails.png"), log, new UIPreview.Layer(menu, With((v, c, d) =>
        {
            c.ShowResult(false);
            v.PreviewResult(false, "Next flip: 1 in 2 gets you funded.");
            v.tailsContent.transform.Find("FlipAgainButton").gameObject.SetActive(true);
        })));
        UIPreview.Render(Path.Combine(dir, "menu_heads.png"), log, new UIPreview.Layer(menu, With((v, c, d) =>
        {
            c.ShowResult(true);
            v.PreviewResult(true, null);
        })));
        UIPreview.Render(Path.Combine(dir, "menu_confirm.png"), log, new UIPreview.Layer(menu, g => g.transform.Find("Confirm").gameObject.SetActive(true)));
        UIPreview.Render(Path.Combine(dir, "menu_settings.png"), log, new UIPreview.Layer(menu, g => g.transform.Find("Settings").gameObject.SetActive(true)));
        UIPreview.Render(Path.Combine(dir, "menu_dice_rolling.png"), log, new UIPreview.Layer(menu, With((v, c, d) => d.ShowRolling())));
        UIPreview.Render(Path.Combine(dir, "menu_dice_loud.png"), log, new UIPreview.Layer(menu, With((v, c, d) => d.ShowRoll(5, 6))));
        UIPreview.Render(Path.Combine(dir, "menu_dice_quiet.png"), log, new UIPreview.Layer(menu, With((v, c, d) => d.ShowRoll(1, 2))));
        UIPreview.Render(Path.Combine(dir, "menu_credits.png"), log, new UIPreview.Layer(menu, g =>
        {
            g.transform.Find("Credits").gameObject.SetActive(true);
            g.GetComponentInChildren<CreditsRollView>(true).ShowAt(0f);
        }));
        UIPreview.Render(Path.Combine(dir, "menu_credits_end.png"), log, new UIPreview.Layer(menu, g =>
        {
            g.transform.Find("Credits").gameObject.SetActive(true);
            g.GetComponentInChildren<CreditsRollView>(true).ShowAt(1f);
        }));
        // The demo's menu: DemoMenuDecor's DEMO stamp and wishlist sticky, placed on the logo (built here: its Start doesn't
        // run in edit mode)
        UIPreview.Render(Path.Combine(dir, "menu_demo.png"), log, new UIPreview.Layer(menu, g =>
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var decor = g.AddComponent<DemoMenuDecor>();
            typeof(DemoMenuDecor).GetField("view", flags)?.SetValue(decor, g.GetComponent<MainMenuView>());
            try
            {
                typeof(DemoMenuDecor).GetMethod("BuildDecor", flags)?.Invoke(decor, null);
            }
            catch (System.Exception e)
            {
                log.Add("Demo menu decor couldn't be built in the preview: " + (e.InnerException ?? e).Message);
            }
        }));
    }

    /// <summary>
    /// Only the main menu, quicker than Run while it changes: builds and installs it, and renders its previews (the demo's
    /// one too) into -preview folder, as on -previewSize WxH if given.
    /// </summary>
    public static void RunMainMenu()
    {
        int code = 0;
        var log = new List<string>();
        try
        {
            UIPreview.EnsureProjectPipeline(log);
            UIPreview.ReadScreenSizeArg();
            var menu = MainMenuBuilder.BuildPrefab(log);
            if (menu == null) code = 1;
            else
            {
                MainMenuBuilder.Install(menu, log);
                string dir = Arg("-preview");
                if (dir != null) RenderMainMenu(menu, dir, log);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        try
        {
            UIPreview.EnsureProjectPipeline(log);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
        Debug.Log("[NewUIBatch] main menu\n" + string.Join("\n", log));
        EditorApplication.Exit(code);
    }

    static void ShowTag(HoverTagView v)
    {
        v.tagRect.gameObject.SetActive(true);
        v.stringCurve.gameObject.SetActive(true);
        v.knot.gameObject.SetActive(true);
    }

    /// <summary>MarkerButton's disabled look (it applies it in LateUpdate, which doesn't run in edit mode).</summary>
    static void LookDisabled(Transform button)
    {
        var m = button.GetComponent<MarkerButton>();
        if (m.shadow != null) m.shadow.SetActive(false);
        if (m.fill != null) m.fill.color = m.disabledFillColor;
        if (m.solidBorder != null) m.solidBorder.SetActive(false);
        if (m.dashedBorder != null) m.dashedBorder.SetActive(true);
        if (m.content != null)
            foreach (var c in m.content)
                if (c != null) c.color = m.disabledContentColor;
    }
}
