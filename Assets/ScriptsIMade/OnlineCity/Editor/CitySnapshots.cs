using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Renders the city screens to PNG files (play mode, local test cities in their own files, nothing else touched).
/// Tools > Online City > Take UI Snapshots, or in batch mode:
///   Unity -batchmode -projectPath . -executeMethod CitySnapshots.RunBatch -snapshotDir &lt;folder&gt;
/// </summary>
[InitializeOnLoad]
public static class CitySnapshots
{
    const string PendingKey = "CitySnapshots.Pending";
    const string DirKey = "CitySnapshots.Dir";
    const string BatchKey = "CitySnapshots.Batch";
    const string TestPrefix = "city_snapshot_test_";

    static bool running;

    static CitySnapshots()
    {
        if (!SessionState.GetBool(PendingKey, false)) return;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        if (EditorApplication.isPlaying) EditorApplication.delayCall += () => _ = Flow();
    }

    [MenuItem("Tools/Online City/Take UI Snapshots", false, 80)]
    static void FromMenu()
    {
        Begin(Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/CitySnapshots")), false);
    }

    public static void RunBatch()
    {
        string dir = null;
        var args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "-snapshotDir") dir = args[i + 1];
        Begin(dir ?? Path.GetFullPath("Temp/CitySnapshots"), true);
    }

    static void Begin(string dir, bool batch)
    {
        Directory.CreateDirectory(dir);
        SessionState.SetBool(PendingKey, true);
        SessionState.SetString(DirKey, dir);
        SessionState.SetBool(BatchKey, batch);
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        if (EditorApplication.isPlaying) _ = Flow();
        else EditorApplication.EnterPlaymode();
    }

    static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false)) _ = Flow();
    }

    static void DeleteTestCities()
    {
        foreach (var world in CityLayout.Worlds)
        {
            string path = CityLocalBackend.FilePath(world.Id);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    /// <summary>A little drawing for the test company: a gold coin with a red heart.</summary>
    static string TestDrawing()
    {
        var logo = new CityPixelLogo();
        byte gold = (byte)Array.IndexOf(CityPixelLogo.Palette, "E2B13C");
        byte ink = (byte)Array.IndexOf(CityPixelLogo.Palette, "1D2433");
        byte red = (byte)Array.IndexOf(CityPixelLogo.Palette, "C0352B");
        for (int y = 0; y < CityPixelLogo.Size; y++)
        {
            for (int x = 0; x < CityPixelLogo.Size; x++)
            {
                float dx = x + 0.5f - 16f, dy = y + 0.5f - 16f;
                float d = dx * dx + dy * dy;
                if (d <= 14.5f * 14.5f) logo[x, y] = d >= 12.5f * 12.5f ? ink : gold;
                float hx = dx / 8f, hy = -(dy + 1.5f) / 8f;
                float a = hx * hx + hy * hy - 1f;
                if (a * a * a - hx * hx * hy * hy * hy <= 0f) logo[x, y] = red;
            }
        }
        return logo.Encode();
    }

    static async Task Flow()
    {
        if (running) return;
        running = true;
        string dir = SessionState.GetString(DirKey, Path.GetFullPath("Temp/CitySnapshots"));
        bool batch = SessionState.GetBool(BatchKey, false);
        SessionState.SetBool(PendingKey, false);
        try
        {
            CityOnline.ForceLocalTestCity = true;
            CityLocalBackend.FilePrefixOverride = TestPrefix;
            DeleteTestCities();

            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32) { name = "CitySnapshot" };
            rt.Create();
            CityUi.RenderTarget = rt;

            var city = CityOnline.Ensure();
            await city.Connect();
            Log($"Connected: {city.State} {city.LastError}");

            CityScreen.Open();
            await Frames(20);
            Capture(dir, "00_picker");

            var screen = CityScreen.Instance;
            await screen.DebugShow("city", "ist");
            await Frames(20);
            Capture(dir, "01_join_draw");
            await screen.DebugShow("close");

            string drawing = TestDrawing();
            Log("Join: " + Describe(await city.Join("Nova Holdings", null, drawing)));
            CityLogoCache.Put(drawing);
            Log(await city.LocalTestAction("money"));
            Log(await city.LocalTestAction("money"));
            var map = city.Map;
            var district = map.Districts[1];
            var free = map.Plots.Where(p => p.DistrictId == district.Id && city.PlotState(p) == null && p.Size != CityPlotSize.Landmark).Take(6).ToList();
            foreach (var plot in free) Log($"Buy {plot.Id}: " + Describe(await city.BuyPlot(plot.Id, plot.BasePrice)));
            var landmark = map.Plots.FirstOrDefault(p => p.Size == CityPlotSize.Landmark && city.PlotState(p) == null);
            if (landmark != null) Log("Buy landmark: " + Describe(await city.BuyPlot(landmark.Id, landmark.BasePrice)));
            Log(await city.LocalTestAction("challenge"));
            Log(await city.LocalTestAction("challenge"));
            await city.ReloadMap();
            await city.RefreshAccount();

            await screen.DebugShow("fit");
            await Frames(15);
            Capture(dir, "02_map");

            await screen.DebugShow("zoom", null, 2.6f);
            await Frames(15);
            Capture(dir, "03_map_zoomed");

            var mine = free.FirstOrDefault(p => city.IsMine(city.PlotState(p))) ?? free[0];
            await screen.DebugShow("select", mine.Id, 1.0f);
            await Frames(15);
            Capture(dir, "04_plot_mine");

            var rival = map.Plots.First(p => city.PlotState(p) != null && CityRules.IsNpc(city.PlotState(p).OwnerId) && p.Size != CityPlotSize.Landmark);
            await screen.DebugShow("select", rival.Id, 1.0f);
            await Frames(15);
            Capture(dir, "05_plot_rival");

            var freePlot = map.Plots.First(p => city.PlotState(p) == null && p.DistrictId == district.Id);
            await screen.DebugShow("select", freePlot.Id, 1.3f);
            await Frames(15);
            Capture(dir, "06_plot_free");

            await screen.DebugShow("company");
            await Frames(15);
            Capture(dir, "07_company_draw");
            await screen.DebugShow("close");

            await screen.DebugShow("presets");
            await Frames(15);
            Capture(dir, "08_company_presets");
            await screen.DebugShow("close");

            await screen.DebugShow("bank");
            await Frames(15);
            Capture(dir, "09_bank");
            await screen.DebugShow("close");

            await screen.DebugShow("inbox");
            await Frames(15);
            Capture(dir, "10_inbox");
            await screen.DebugShow("close");

            await screen.DebugShow("select", rival.Id, 1.0f);
            await screen.DebugShow("challenge", rival.Id);
            await Frames(15);
            Capture(dir, "11_challenge");
            Log("Challenge start: " + Describe(await city.StartChallenge(rival.Id)));
            await screen.DebugShow("close");
            await screen.DebugShow("challenge", rival.Id);
            await Frames(15);
            Capture(dir, "12_blackjack");
            await screen.DebugShow("close");

            await screen.DebugShow("tools");
            await Frames(15);
            Capture(dir, "13_tools");
            await screen.DebugShow("close");

            // Every other city, seen from afar
            int n = 20;
            foreach (var world in CityLayout.Worlds.Where(w => w.Id != "ist"))
            {
                await screen.DebugShow("city", world.Id);
                await screen.DebugShow("close");
                await screen.DebugShow("fit");
                await Frames(20);
                Capture(dir, $"{n++}_{world.Id}_map");
            }
            await screen.DebugShow("city", "sha");
            await screen.DebugShow("close");
            await screen.DebugShow("zoom", null, 2.2f);
            await Frames(15);
            Capture(dir, "26_sha_zoomed");
            await screen.DebugShow("prices");
            await screen.DebugShow("fit");
            await Frames(15);
            Capture(dir, "27_sha_prices");
            await screen.DebugShow("owners");

            await screen.DebugShow("picker");
            await city.RefreshMyAccounts();
            await Frames(20);
            Capture(dir, "30_picker_joined");

            Log("Snapshots written to " + dir);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            running = false;
            CityUi.RenderTarget = null;
            DeleteTestCities();
            CityLocalBackend.FilePrefixOverride = null;
            CityOnline.ForceLocalTestCity = false;
            if (batch) EditorApplication.Exit(0);
            else EditorApplication.ExitPlaymode();
        }
    }

    static string Describe(CityResult r) => r == null ? "null" : r.Ok ? "ok " + r.Message : "FAILED " + r.Error;

    static void Log(string message) => Debug.Log("[CitySnapshots] " + message);

    static Task Frames(int count)
    {
        var done = new TaskCompletionSource<bool>();
        int left = count;
        void Tick()
        {
            if (--left > 0) return;
            EditorApplication.update -= Tick;
            done.TrySetResult(true);
        }
        EditorApplication.update += Tick;
        return done.Task;
    }

    static void Capture(string dir, string name)
    {
        var rt = CityUi.RenderTarget;
        if (rt == null) return;
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var texture = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), texture.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(texture);
        Log("Captured " + name);
    }
}
