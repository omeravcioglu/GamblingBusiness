using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Renders the cash floor screen to PNGs in batch mode, without Play mode (the views are built in edit mode with a
/// stand-in company and a populated floor; private steps are called through reflection, as ManagerPreview does):
///   Unity -batchmode -projectPath . -executeMethod CashGrabPreview.Run -previewDir &lt;dir&gt; [-langs tr,ja] [-previewSize 2560x1080] -quit
/// Shots: an early floor (hands, Backend only, the welcome tip), a busy late floor (scoop full of bills, all three
/// departments, a firewall, a pop-up, a magnet, a telegraph), the vacuum with its hose, the shop, and the desk's chip.
/// </summary>
public static class CashGrabPreview
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    public static void Run()
    {
        var log = new List<string>();
        int code = 0;
        try
        {
            string dir = Arg("-previewDir", Path.Combine(Path.GetTempPath(), "CashGrabPreview"));
            Directory.CreateDirectory(dir);
            UIPreview.ReadScreenSizeArg();
            try { LocalizationCsvImporter.ImportChanged("cash floor preview"); }
            catch (Exception e) { log.Add("CSV import: " + e.Message); }

            string langs = Arg("-langs", "en");
            foreach (string lang in langs.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string code2 = lang.Trim();
                string previous = PlayerPrefs.GetString("selected-locale", null);
                if (code2 != "en") L10n.SetLanguage(code2);
                try
                {
                    RenderAll(dir, code2 == "en" ? "" : "_" + code2, log);
                }
                finally
                {
                    if (code2 != "en")
                    {
                        L10n.SetLanguage("en");
                        if (previous != null) PlayerPrefs.SetString("selected-locale", previous);
                    }
                }
            }
        }
        catch (Exception e)
        {
            log.Add("FAILED: " + e);
            code = 1;
        }
        Debug.Log("[CashGrabPreview]\n" + string.Join("\n", log));
        if (Application.isBatchMode) EditorApplication.Exit(code);
    }

    static string Arg(string name, string fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
    }

    // ------------------------------------------------------------------

    static void RenderAll(string dir, string suffix, List<string> log)
    {
        CompanyManager company = null;
        CashGrabSystem system = null;
        CashFloorView view = null;

        var shots = new List<UIPreview.Shot>
        {
            new UIPreview.Shot(Path.Combine(dir, "cash_early" + suffix + ".png"), () => Early(system, view)),
            new UIPreview.Shot(Path.Combine(dir, "cash_busy" + suffix + ".png"), () => Busy(system, view)),
            new UIPreview.Shot(Path.Combine(dir, "cash_vacuum" + suffix + ".png"), () => Vacuum(system, view)),
            new UIPreview.Shot(Path.Combine(dir, "cash_extras" + suffix + ".png"), () => Extras(system, view)),
            new UIPreview.Shot(Path.Combine(dir, "cash_count" + suffix + ".png"), () => CountShot(system, view)),
            new UIPreview.Shot(Path.Combine(dir, "cash_shop" + suffix + ".png"), () => ShopShot(system, view)),
            new UIPreview.Shot(Path.Combine(dir, "cash_peek" + suffix + ".png"), () => PeekShot(system, view)),
        };

        try
        {
            UIPreview.RenderShots(log, cam =>
            {
                var made = new List<GameObject>();
                var companyGo = new GameObject("Company (preview)");
                company = companyGo.AddComponent<CompanyManager>();
                CompanyManager.Instance = company;
                company.companyValue = company.displayedValue = 2450000;
                company.currentDay = 14;
                made.Add(companyGo);

                var systemGo = new GameObject("Cash Floor (preview)");
                system = systemGo.AddComponent<CashGrabSystem>();
                Call(system, "Awake");
                SetAuto(system, "Ready", true);
                made.Add(systemGo);

                view = CashFloorView.Create(system);
                SetAuto(system, "View", view);
                Canvas canvas = view.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 10f;
                made.Add(view.gameObject);

                CashDeskPeek.Create(system);
                var peek = UnityEngine.Object.FindAnyObjectByType<CashDeskPeek>();
                if (peek != null)
                {
                    Canvas pc = peek.GetComponent<Canvas>();
                    pc.renderMode = RenderMode.ScreenSpaceCamera;
                    pc.worldCamera = cam;
                    pc.planeDistance = 5f;
                    pc.enabled = false;
                    made.Add(peek.gameObject);
                }
                return made;
            }, shots);
        }
        finally
        {
            if (CompanyManager.Instance == company) CompanyManager.Instance = null;
            SetStatic(typeof(CashGrabSystem), "Instance", null);
        }
    }

    // ------------------------------------------------------------------
    // States
    // ------------------------------------------------------------------

    static void Setup(CashGrabSystem system, double cash, double[] budgets, int[] depts, int[] tools, int[] gadgets, CashTool tool, CashGadget gadget, int seats)
    {
        CashFloorSim sim = system.Sim;
        sim.ClearFloor();
        system.Ledger.Restore(cash, budgets, budgets);
        int[] levels = (int[])Get(system, "deptLevels");
        for (int d = 0; d < levels.Length; d++)
        {
            levels[d] = depts[d];
            sim.SetDeptLevel(d, depts[d]);
        }
        int[] toolLevels = (int[])Get(system, "toolLevels");
        Array.Copy(tools, toolLevels, toolLevels.Length);
        int[] gadgetLevels = (int[])Get(system, "gadgetLevels");
        Array.Copy(gadgets, gadgetLevels, gadgetLevels.Length);
        Set(system, "selectedTool", tool);
        Set(system, "selectedGadget", gadget);
        Call(system, "ApplyEquipment");
        sim.SetLayout(CashRules.FloorScale(seats), CashRules.TubeCount(seats));
        sim.Visible = true;
    }

    static void Spawn(CashFloorSim sim, int count, double[] values, int seed, bool fromTubes = false)
    {
        var rng = new System.Random(seed);
        for (int i = 0; i < count; i++)
        {
            double v = values[rng.Next(values.Length)];
            if (fromTubes) sim.SpawnFromTube(v, rng.Next(sim.TubeMouths.Length));
            else sim.SpawnScattered(v);
        }
    }

    static void StepFor(CashFloorSim sim, float seconds, Func<float, CashFloorSim.PlayerInput> input)
    {
        int steps = Mathf.CeilToInt(seconds * 60f);
        for (int s = 0; s < steps; s++)
        {
            sim.Input = input(s / 60f);
            sim.Step(1f / 60f);
            sim.Events.Clear();
        }
    }

    static void Show(CashFloorView view, CashGrabSystem system)
    {
        if (!view.IsOpen) view.Open();
        Call(view, "WriteStaticTexts");
        Call(view, "LayoutFelt");
        Call(view, "RefreshPanels", true);
        for (int i = 0; i < 4; i++)
        {
            Call(view, "RefreshExtras", 1f / 60f);
            Call(view, "Render", 1f / 60f);
        }
    }

    /// <summary>The design canvas's extras: a full house in bare hands, a jackpot, golden and marked bills, a written-up
    /// worker and one caught mid-foul, the day's tally strip.</summary>
    static void Extras(CashGrabSystem system, CashFloorView view)
    {
        Setup(system, 48200, new double[] { 9100, 12400, 6800 }, new[] { 2, 2, 2 }, new[] { 1, 1, 0, 0, 0, 0 }, new[] { 1, 0, 0, 0 },
              CashTool.Hands, CashGadget.None, 12);
        CashFloorSim sim = system.Sim;
        Spawn(sim, 60, new double[] { 10, 20, 50, 100, 100 }, 41);
        // Golden and marked bills lying around
        int specials = 0;
        for (int i = 0; i < sim.Bills.Length && specials < 4; i++)
        {
            if (sim.Bills[i].state != BillState.Free) continue;
            sim.Bills[i].special = specials < 2 ? CashSpecial.Golden : CashSpecial.Marked;
            specials++;
        }
        // Five bills in the bare hand: a full house (kings over sevens)
        byte[] cards = { 11 * 4 + 0, 11 * 4 + 1, 11 * 4 + 2, 5 * 4 + 3, 5 * 4 + 1 };
        int held = 0;
        for (int i = sim.Bills.Length - 1; i >= 0 && held < 5; i--)
        {
            if (sim.Bills[i].state != BillState.Free || sim.Bills[i].special != CashSpecial.None) continue;
            sim.Bills[i].card = cards[held++];
            Call(sim, "Hold", i);
        }
        Vector2 hand = new Vector2(sim.Size.x * 0.62f, sim.Size.y * 0.55f);
        StepFor(sim, 0.6f, t => new CashFloorSim.PlayerInput { onFloor = true, cursor = hand, held = true });
        // A jackpot just hit
        sim.StartBoost(CashBoost.Jackpot);
        for (int r = 0; r < sim.Reels.Length; r++) sim.Reels[r] = 0;
        // One worker written up, one mid-foul
        bool stamped = false, fouling = false;
        foreach (CashFloorSim.Agent a in sim.agents)
        {
            if (!a.active) continue;
            if (!stamped) { a.stun = 6f; a.writtenUp = 6f; a.state = CashFloorSim.AgentState.Stunned; stamped = true; }
            else if (!fouling) { a.state = CashFloorSim.AgentState.Dash; a.dashMode = CashInterference.Yoink; a.timer = 1f; fouling = true; }
        }
        sim.Events.Clear();
        Show(view, system);
        var count = new CashGrabSystem.DayCount { day = 13 };
        count.takes[3] = 21400; count.takes[0] = 18200; count.takes[1] = 9800; count.takes[2] = 12600;
        Call(view.Count, "ShowTally", count);
        Call(view, "Float", "Full house! ×7", sim.SafeCenter + new Vector2(0f, -250f), (Color)UIColors.Highlight, 46f, 1.9f, 0, 0.0, null);
        Call(view, "Float", "WRITTEN UP!", new Vector2(sim.Size.x * 0.3f, sim.Size.y * 0.4f), (Color)UIColors.Loss, 34f, 1.6f, 0, 0.0, null);
        Call(view, "RefreshExtras", 1f / 60f);
        Call(view, "Render", 1f / 60f);
    }

    /// <summary>The weekly Count: the CEO won and picks a perk.</summary>
    static void CountShot(CashGrabSystem system, CashFloorView view)
    {
        Call(view.Count, "Update");   // the floor is open: nothing to hide
        Setup(system, 96400, new double[] { 31200, 22800, 18900 }, new[] { 3, 2, 2 }, new[] { 1, 2, 1, 0, 0, 0 }, new[] { 1, 1, 0, 0 },
              CashTool.Scoop, CashGadget.Stapler, 24);
        Spawn(system.Sim, 50, new double[] { 100, 1000 }, 51);
        Show(view, system);
        SetAuto(system, "PerkPending", true);
        SetAuto(system, "BestHand", (int)CashPoker.Rank.FullHouse);
        SetAuto(system, "WrittenUpCount", 7);
        SetAuto(system, "LifetimeBonus", 18450.0);
        var count = new CashGrabSystem.DayCount { day = 14, weekly = true, winner = 3 };
        count.takes[3] = 46900; count.takes[0] = 44300; count.takes[1] = 31200; count.takes[2] = 21700;
        Set(view.Count, "weekly", count);
        Call(view.Count, "ShowCard", count);
    }

    static void Early(CashGrabSystem system, CashFloorView view)
    {
        Setup(system, 1840, new double[] { 260, 0, 0 }, new[] { 1, 0, 0 }, new[] { 1, 1, 0, 0, 0, 0 }, new int[4], CashTool.Hands, CashGadget.None, 6);
        CashFloorSim sim = system.Sim;
        Spawn(sim, 26, new double[] { 5, 10, 20, 20, 50 }, 3);
        Spawn(sim, 4, new double[] { 20, 50 }, 4, true);
        var held = new Vector2(sim.SafeCenter.x - 210f, sim.SafeCenter.y - 180f);
        StepFor(sim, 1.2f, t => new CashFloorSim.PlayerInput { onFloor = true, cursor = sim.Bills[0].pos, down = t < 0.02f, held = true });
        StepFor(sim, 0.5f, t => new CashFloorSim.PlayerInput { onFloor = true, cursor = held, held = true });
        Show(view, system);
        view.Coach.Tip("welcome");
        Call(view.Coach, "Update");
        Call(view.Coach, "Show", "welcome");
    }

    static void Busy(CashGrabSystem system, CashFloorView view)
    {
        view.Coach.Dismiss();
        Setup(system, 2184300, new double[] { 142000, 318500, 96200 }, new[] { 3, 4, 3 }, new[] { 1, 2, 1, 0, 0, 0 },
              new[] { 2, 1, 1, 1 }, CashTool.Scoop, CashGadget.Stapler, 38);
        CashFloorSim sim = system.Sim;
        Spawn(sim, 120, new double[] { 100, 1000, 1000, 1000, 5000, 10000, 50000 }, 11);
        Spawn(sim, 10, new double[] { 1000, 5000, 100000 }, 12, true);
        Vector2 pile = new Vector2(sim.Size.x * 0.58f, sim.Size.y * 0.48f);
        StepFor(sim, 2.5f, t => new CashFloorSim.PlayerInput { onFloor = true, cursor = pile + new Vector2(Mathf.Sin(t * 3f) * 140f, Mathf.Cos(t * 2f) * 60f), down = t < 0.02f, held = true });
        // A firewall, a pop-up, a magnet and a worker about to strike
        var hazards = sim.hazards;
        hazards.Add(new CashFloorSim.Hazard { kind = CashFloorSim.HazardKind.Firewall, rect = new Rect(sim.Size.x * 0.25f, sim.Size.y * 0.2f, 250f, 180f), time = 2.5f, duration = 3.5f, dept = 0 });
        hazards.Add(new CashFloorSim.Hazard { kind = CashFloorSim.HazardKind.Popup, rect = new Rect(sim.Size.x * 0.68f, sim.Size.y * 0.16f, 270f, 176f), time = 4f, duration = 5f, dept = 1 });
        hazards.Add(new CashFloorSim.Hazard { kind = CashFloorSim.HazardKind.Magnet, center = sim.DeptCenter[2], radius = 400f, time = 3f, duration = 4.5f, dept = 2 });
        hazards.Add(new CashFloorSim.Hazard { kind = CashFloorSim.HazardKind.Paperweight, center = new Vector2(sim.Size.x * 0.42f, sim.Size.y * 0.62f), radius = 100f, time = 6f, duration = 10f, dept = -1 });
        foreach (CashFloorSim.Agent a in sim.agents)
        {
            if (!a.active || a.dept != 1) continue;
            a.state = CashFloorSim.AgentState.Telegraph;
            a.telegraph = 0.5f;
            break;
        }
        Set(sim, "streakTimer", 2f);
        SetAuto(sim, "Streak", 24);
        SetAuto(sim, "StreakTier", 2);
        Show(view, system);
        Call(view, "Float", "+$48,600", sim.SafeCenter + new Vector2(0f, -130f), (Color)UIColors.Highlight, 34f, 1.3f, 0, 0.0, null);
        Call(view, "Float", "Hot hands! ×24", new Vector2(sim.Size.x * 0.5f, sim.Size.y * 0.3f), (Color)UIColors.Highlight, 48f, 1.6f, 0, 0.0, null);
        Call(view, "Render", 1f / 60f);
    }

    static void Vacuum(CashGrabSystem system, CashFloorView view)
    {
        Setup(system, 18400000, new double[] { 2400000, 3100000, 900000 }, new[] { 5, 5, 4 }, new[] { 1, 3, 3, 3, 2, 0 },
              new[] { 2, 2, 2, 2 }, CashTool.Vacuum, CashGadget.Fan, 95);
        CashFloorSim sim = system.Sim;
        Spawn(sim, 140, new double[] { 100000, 100000, 500000, 1000000, 5000000 }, 21);
        Vector2 spot = new Vector2(sim.Size.x * 0.4f, sim.Size.y * 0.42f);
        StepFor(sim, 1.6f, t => new CashFloorSim.PlayerInput { onFloor = true, cursor = spot + new Vector2(t * 120f, 0f), down = t < 0.02f, held = true });
        Show(view, system);
    }

    static void ShopShot(CashGrabSystem system, CashFloorView view)
    {
        Call(view.Count, "CloseCard");
        SetAuto(system, "PerkPending", false);
        Setup(system, 64200, new double[] { 12000, 8000, 0 }, new[] { 2, 1, 0 }, new[] { 1, 2, 1, 0, 0, 0 }, new[] { 1, 0, 0, 0 },
              CashTool.Scoop, CashGadget.Stapler, 28);
        Show(view, system);
        view.Shop.Open();
        Call(view.Shop, "Refresh");
    }

    static void PeekShot(CashGrabSystem system, CashFloorView view)
    {
        view.Shop.Close();
        view.Close();
        Setup(system, 64200, new double[] { 12000, 8000, 0 }, new[] { 2, 1, 0 }, new[] { 1, 2, 1, 0, 0, 0 }, new int[4], CashTool.Scoop, CashGadget.None, 28);
        Spawn(system.Sim, 40, new double[] { 100, 1000 }, 31);
        // Edit-mode objects never get OnDestroy: unhook the view from the language event now (the last shot)
        Call(view, "OnDestroy");
        var peek = UnityEngine.Object.FindAnyObjectByType<CashDeskPeek>();
        if (peek == null) return;
        Set(peek, "system", system);
        Set(peek, "remindedOnce", true);   // no toast in a preview
        CashDeskPeek.NoteGrab(1);
        Call(peek, "Update");
        Canvas pc = peek.GetComponent<Canvas>();
        pc.enabled = true;
        Set(peek, "refresh", 0f);
        Call(peek, "Update");
        pc.enabled = true;
    }

    // ------------------------------------------------------------------
    // Reflection helpers
    // ------------------------------------------------------------------

    static object Call(object target, string method, params object[] args)
    {
        Type type = target.GetType();
        foreach (MethodInfo m in type.GetMethods(Any))
        {
            if (m.Name != method || m.GetParameters().Length != args.Length) continue;
            return m.Invoke(target, args);
        }
        throw new MissingMethodException(type.Name, method);
    }

    static object Get(object target, string field)
    {
        FieldInfo f = target.GetType().GetField(field, Any);
        if (f == null) throw new MissingFieldException(target.GetType().Name, field);
        return f.GetValue(target);
    }

    static void Set(object target, string field, object value)
    {
        FieldInfo f = target.GetType().GetField(field, Any);
        if (f == null) throw new MissingFieldException(target.GetType().Name, field);
        f.SetValue(target, value);
    }

    static void SetAuto(object target, string property, object value)
    {
        PropertyInfo p = target.GetType().GetProperty(property, Any);
        MethodInfo setter = p != null ? p.GetSetMethod(true) : null;
        if (setter == null) throw new MissingMemberException(target.GetType().Name, property);
        setter.Invoke(target, new[] { value });
    }

    static void SetStatic(Type type, string property, object value)
    {
        PropertyInfo p = type.GetProperty(property, Any);
        MethodInfo setter = p != null ? p.GetSetMethod(true) : null;
        if (setter != null) setter.Invoke(null, new[] { value });
    }
}
