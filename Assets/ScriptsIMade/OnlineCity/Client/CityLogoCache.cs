using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Player drawings (pixel logos) for the screens: fetched from the city by hash when a plot shows one, kept as small
/// point-filtered textures, and saved on disk so each drawing downloads once (a hash always means the same pixels).
/// </summary>
public static class CityLogoCache
{
    public enum State { Missing, Loading, Ready, Hidden }

    class Entry
    {
        public State State = State.Missing;
        public CityPixelLogo Logo;
        public Texture2D Texture;
    }

    static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
    static readonly Dictionary<string, HashSet<string>> wanted = new Dictionary<string, HashSet<string>>();
    static bool fetchScheduled;

    /// <summary>A drawing arrived (or turned out to be taken down): views showing this hash redraw.</summary>
    public static event Action<string> Changed;

    static string DiskFolder => Path.Combine(Application.persistentDataPath, "CityLogos");

    /// <summary>The drawing's texture, or null while it loads (it's requested from the given city then).</summary>
    public static Texture2D Texture(string cityId, string hash, out State state)
    {
        var entry = Find(hash);
        if (entry.State == State.Missing) Request(cityId, hash, entry);
        state = entry.State;
        return entry.Texture;
    }

    /// <summary>The pixels of a drawing already loaded (the editor starts from the current logo).</summary>
    public static CityPixelLogo Pixels(string hash)
    {
        return hash != null && entries.TryGetValue(hash, out var entry) ? entry.Logo : null;
    }

    /// <summary>A drawing this player just saved shows at once, without a download.</summary>
    public static void Put(string code)
    {
        var logo = CityPixelLogo.Decode(code);
        if (logo == null) return;
        string hash = CityPixelLogo.HashOf(code);
        var entry = Find(hash);
        Ready(hash, entry, logo);
        WriteDisk(hash, code);
    }

    static Entry Find(string hash)
    {
        if (!entries.TryGetValue(hash ?? "", out var entry))
        {
            entry = new Entry();
            entries[hash ?? ""] = entry;
        }
        return entry;
    }

    static void Request(string cityId, string hash, Entry entry)
    {
        if (string.IsNullOrEmpty(hash)) return;

        // Seen before on this PC?
        string code = ReadDisk(hash);
        var logo = code != null ? CityPixelLogo.Decode(code) : null;
        if (logo != null && CityPixelLogo.HashOf(logo.Encode()) == hash)
        {
            Ready(hash, entry, logo);
            return;
        }

        entry.State = State.Loading;
        if (cityId == null) return;
        if (!wanted.TryGetValue(cityId, out var set)) wanted[cityId] = set = new HashSet<string>();
        set.Add(hash);
        if (!fetchScheduled)
        {
            fetchScheduled = true;
            _ = FetchSoon();
        }
    }

    /// <summary>Everything requested in the same moment goes in one download.</summary>
    static async Task FetchSoon()
    {
        await Task.Yield();
        fetchScheduled = false;
        var batches = new List<KeyValuePair<string, HashSet<string>>>(wanted);
        wanted.Clear();
        var city = CityOnline.Instance;
        foreach (var batch in batches)
        {
            Dictionary<string, CityLogoRecord> found = null;
            try
            {
                if (city != null) found = await city.LoadLogos(batch.Key, new List<string>(batch.Value));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[City] Couldn't load player logos: {e.Message}");
            }

            foreach (string hash in batch.Value)
            {
                var entry = Find(hash);
                if (found != null && found.TryGetValue(hash, out var record) && record != null)
                {
                    if (record.Hidden || record.Pixels == null)
                    {
                        entry.State = State.Hidden;
                        Changed?.Invoke(hash);
                        continue;
                    }
                    var logo = CityPixelLogo.Decode(record.Pixels);
                    if (logo != null)
                    {
                        Ready(hash, entry, logo);
                        WriteDisk(hash, record.Pixels);
                        continue;
                    }
                }
                // Not there (yet): try again the next time a view asks
                entry.State = State.Missing;
            }
        }
    }

    static void Ready(string hash, Entry entry, CityPixelLogo logo)
    {
        entry.Logo = logo;
        if (entry.Texture == null) entry.Texture = MakeTexture(logo);
        else Paint(entry.Texture, logo);
        entry.State = State.Ready;
        Changed?.Invoke(hash);
    }

    /// <summary>A crisp 32x32 texture (drawn 4x bigger so it stays sharp when the UI scales it).</summary>
    public static Texture2D MakeTexture(CityPixelLogo logo)
    {
        const int scale = 4;
        int size = CityPixelLogo.Size * scale;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            name = "CityPixelLogo",
            hideFlags = HideFlags.DontSave,
        };
        Paint(texture, logo);
        return texture;
    }

    public static void Paint(Texture2D texture, CityPixelLogo logo)
    {
        int scale = texture.width / CityPixelLogo.Size;
        var pixels = new Color32[texture.width * texture.height];
        var colours = PaletteColours;
        for (int y = 0; y < CityPixelLogo.Size; y++)
        {
            for (int x = 0; x < CityPixelLogo.Size; x++)
            {
                var c = colours[logo[x, y]];
                // Texture rows go bottom-up
                int ty = (CityPixelLogo.Size - 1 - y) * scale;
                for (int dy = 0; dy < scale; dy++)
                {
                    int row = (ty + dy) * texture.width + x * scale;
                    for (int dx = 0; dx < scale; dx++) pixels[row + dx] = c;
                }
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false);
    }

    static Color32[] paletteColours;

    public static Color32[] PaletteColours
    {
        get
        {
            if (paletteColours != null) return paletteColours;
            var list = new Color32[CityPixelLogo.Palette.Length];
            for (int i = 0; i < list.Length; i++)
            {
                string hex = CityPixelLogo.Palette[i];
                list[i] = hex == null ? new Color32(0, 0, 0, 0) : (Color32)CityUi.Hex(hex);
            }
            paletteColours = list;
            return list;
        }
    }

    static string ReadDisk(string hash)
    {
        try
        {
            string path = Path.Combine(DiskFolder, hash + ".txt");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    static void WriteDisk(string hash, string code)
    {
        try
        {
            Directory.CreateDirectory(DiskFolder);
            File.WriteAllText(Path.Combine(DiskFolder, hash + ".txt"), code);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[City] Couldn't cache a player logo: {e.Message}");
        }
    }
}
