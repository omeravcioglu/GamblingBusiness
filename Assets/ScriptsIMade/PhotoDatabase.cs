using UnityEngine;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
/// Optional scene component that hands out applicant photos.
/// Photos assigned in the Inspector are used first; otherwise portraits are loaded one file at a time
/// from Resources through PortraitLibrary (never the whole folder).
/// </summary>
public class PhotoDatabase : MonoBehaviour
{
    [Header("Drag your photos here in the Inspector (optional)")]
    [Tooltip("Photos assigned here will be used. If empty, portraits are loaded one at a time from Resources.")]
    public Sprite[] malePhotos;
    public Sprite[] femalePhotos;

    [Header("Resources Portraits (used when the arrays above are empty)")]
    [Tooltip("Female portraits: Resources/<folder>/portrait_<number>")]
    public PortraitSet femalePortraits = PortraitSet.DefaultFemale();
    [Tooltip("Male portraits: Resources/<folder>/portrait_<number>")]
    public PortraitSet malePortraits = PortraitSet.DefaultMale();

    public Sprite GetRandomPhoto(string gender)
    {
        return GetRandomPhoto(gender, out _);
    }

    /// <summary>Random photo for the gender ("Other" = either). portraitIndex is the file number, or -1 for Inspector photos.</summary>
    public Sprite GetRandomPhoto(string gender, out int portraitIndex)
    {
        portraitIndex = -1;

        Sprite[] photoArray;
        if (gender == "Male")
            photoArray = malePhotos;
        else if (gender == "Female")
            photoArray = femalePhotos;
        else // Other - pick randomly from either
            photoArray = Random.value < 0.5f ? malePhotos : femalePhotos;

        if (photoArray != null && photoArray.Length > 0)
        {
            Sprite picked = photoArray[Random.Range(0, photoArray.Length)];
            if (picked != null)
                return picked;
        }

        return PortraitLibrary.LoadRandom(gender, femalePortraits, malePortraits, out portraitIndex);
    }

    // Helper method to get count of available photos
    public int GetPhotoCount(string gender)
    {
        int male = malePhotos != null && malePhotos.Length > 0 ? malePhotos.Length : (malePortraits != null ? malePortraits.Count : 0);
        int female = femalePhotos != null && femalePhotos.Length > 0 ? femalePhotos.Length : (femalePortraits != null ? femalePortraits.Count : 0);

        if (gender == "Male")
            return male;
        else if (gender == "Female")
            return female;
        else
            return male + female;
    }
}

/// <summary>A numbered run of portrait files in one Resources folder (portrait_00000, portrait_00001, ...).</summary>
[System.Serializable]
public class PortraitSet
{
    [Tooltip("Folder under Resources, e.g. Portraits/Female")]
    public string folder = "Portraits/Female";
    [Tooltip("File name pattern; {0} is the portrait number")]
    public string fileNameFormat = "portrait_{0:D5}";
    [Tooltip("Number of the first portrait in the folder")]
    public int firstIndex;
    [Tooltip("Number of the last portrait in the folder (inclusive)")]
    public int lastIndex;

    public const string DefaultFileNameFormat = "portrait_{0:D5}";

    public PortraitSet() { }

    public PortraitSet(string folder, int firstIndex, int lastIndex)
    {
        this.folder = folder;
        this.firstIndex = firstIndex;
        this.lastIndex = lastIndex;
    }

    // The portraits shipped in Assets/Resources/Portraits (numbering is contiguous across both folders)
    public static PortraitSet DefaultFemale() { return new PortraitSet("Portraits/Female", 0, 3698); }
    public static PortraitSet DefaultMale() { return new PortraitSet("Portraits/Male", 3699, 6711); }

    public int Count => Mathf.Max(0, lastIndex - firstIndex + 1);

    public bool Contains(int index) => index >= firstIndex && index <= lastIndex;

    /// <summary>Resources path of a portrait number (no extension), or null if the settings are broken.</summary>
    public string GetPath(int index)
    {
        if (string.IsNullOrEmpty(folder)) return null;
        string format = string.IsNullOrEmpty(fileNameFormat) ? DefaultFileNameFormat : fileNameFormat;
        string file;
        try
        {
            file = string.Format(CultureInfo.InvariantCulture, format, index);
        }
        catch (System.FormatException)
        {
            file = string.Format(CultureInfo.InvariantCulture, DefaultFileNameFormat, index);
        }
        return folder.TrimEnd('/') + "/" + file;
    }
}

/// <summary>
/// Loads portraits one file at a time with Resources.Load instead of Resources.LoadAll on thousands of
/// 1024px images (several GB). Keeps the most recent sprites and now and then unloads the ones nothing
/// uses any more (sprites still referenced by an applicant, employee or paper stay loaded).
/// </summary>
public static class PortraitLibrary
{
    const int MaxLoadAttempts = 6;       // random numbers tried before falling back to loading the whole folder
    const int RecentCacheSize = 16;
    const int LoadsBetweenUnloads = 60;  // Resources.UnloadUnusedAssets after this many portrait loads (about 20 batches)

    static readonly Dictionary<string, Sprite> recent = new Dictionary<string, Sprite>();
    static readonly Queue<string> recentOrder = new Queue<string>();
    static readonly HashSet<string> missingPaths = new HashSet<string>();
    static readonly Dictionary<string, Sprite[]> folderFallback = new Dictionary<string, Sprite[]>();
    static int loadsSinceUnload;

    // Last sets used, to resolve a bare portrait number (e.g. ApplicantData.portraitIndex)
    static PortraitSet knownFemale = PortraitSet.DefaultFemale();
    static PortraitSet knownMale = PortraitSet.DefaultMale();

    // Clear caches when entering Play Mode (in case domain reload is turned off)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCaches()
    {
        recent.Clear();
        recentOrder.Clear();
        missingPaths.Clear();
        folderFallback.Clear();
        loadsSinceUnload = 0;
        knownFemale = PortraitSet.DefaultFemale();
        knownMale = PortraitSet.DefaultMale();
    }

    /// <summary>Random portrait for the gender ("Other" = either set). portraitIndex is -1 if it came from the folder fallback or nothing was found.</summary>
    public static Sprite LoadRandom(string gender, PortraitSet female, PortraitSet male, out int portraitIndex)
    {
        if (female != null) knownFemale = female;
        if (male != null) knownMale = male;

        PortraitSet set;
        if (gender == "Male")
            set = male;
        else if (gender == "Female")
            set = female;
        else
            set = Random.value < 0.5f ? male : female;

        if (set == null || set.Count == 0)
            set = set == male ? female : male;

        return LoadRandom(set, out portraitIndex);
    }

    /// <summary>Random portrait from one set. Retries a few numbers; falls back to loading the folder only if numbered loading keeps failing.</summary>
    public static Sprite LoadRandom(PortraitSet set, out int portraitIndex)
    {
        portraitIndex = -1;
        if (set == null) return null;

        int kept = KeptCount(set);   // Demo: only the files the demo build ships (the whole set in the full game)
        if (kept > 0)
        {
            for (int attempt = 0; attempt < MaxLoadAttempts; attempt++)
            {
                int index = Random.Range(set.firstIndex, set.firstIndex + kept);
                Sprite sprite = Load(set, index);
                if (sprite != null)
                {
                    portraitIndex = index;
                    return sprite;
                }
            }
        }

        return LoadFromFolder(set.folder);
    }

    /// <summary>Portrait by file number, looked up in the last female/male sets used. Null if not found.</summary>
    public static Sprite Load(int portraitIndex)
    {
        if (portraitIndex < 0) return null;
        if (knownFemale != null && knownFemale.Contains(portraitIndex)) return Load(knownFemale, portraitIndex);
        if (knownMale != null && knownMale.Contains(portraitIndex)) return Load(knownMale, portraitIndex);
        return null;
    }

    /// <summary>
    /// Resources path of a portrait file number in the last female/male sets used (the one Load would read), or null if
    /// it's in neither or known to be missing. For callers that load it themselves, e.g. with Resources.LoadAsync (the
    /// staff files): such loads skip this cache and never trigger the periodic Resources.UnloadUnusedAssets.
    /// </summary>
    public static string GetPath(int portraitIndex)
    {
        if (portraitIndex < 0) return null;
        PortraitSet set = knownFemale != null && knownFemale.Contains(portraitIndex) ? knownFemale
                        : knownMale != null && knownMale.Contains(portraitIndex) ? knownMale : null;
        string path = set != null ? set.GetPath(KeptIndex(set, portraitIndex)) : null;   // Demo: KeptIndex
        return path != null && !missingPaths.Contains(path) ? path : null;
    }

    /// <summary>Portrait by file number within a set. Null if the number is outside the set or the file has no sprite.</summary>
    public static Sprite Load(PortraitSet set, int index)
    {
        if (set == null || !set.Contains(index)) return null;

        string path = set.GetPath(KeptIndex(set, index));   // Demo: KeptIndex
        if (path == null || missingPaths.Contains(path)) return null;

        if (recent.TryGetValue(path, out Sprite cached) && cached != null)
            return cached;

        Sprite sprite = Resources.Load<Sprite>(path);
        if (sprite == null)
        {
            // Missing file, or a texture not imported as a single sprite
            missingPaths.Add(path);
            return null;
        }

        Remember(path, sprite);

        loadsSinceUnload++;
        if (loadsSinceUnload >= LoadsBetweenUnloads)
        {
            loadsSinceUnload = 0;
            Resources.UnloadUnusedAssets();
        }

        return sprite;
    }

    // Demo: the demo build ships only the first DemoConfig.demoPortraitsPerGender files of each set (DemoBuildPrep parks the
    // rest), so every pick and lookup stays inside them. A number past them (a fixed or saved one) maps onto them, always to
    // the same file, so a person keeps one face. With demo mode off, or demoPortraitsPerGender 0, nothing changes.
    /// <summary>How many files of the set this build has, counted from firstIndex: all of them outside the demo.</summary>
    static int KeptCount(PortraitSet set)
    {
        int perGender = DemoMode.Active ? DemoMode.Config.demoPortraitsPerGender : 0;
        return perGender > 0 ? Mathf.Min(set.Count, perGender) : set.Count;
    }

    /// <summary>The file number to read for a number inside the set: itself, or in the demo one of the kept files.</summary>
    static int KeptIndex(PortraitSet set, int index)
    {
        int kept = KeptCount(set);
        return kept > 0 && index - set.firstIndex >= kept ? set.firstIndex + (index - set.firstIndex) % kept : index;
    }

    static void Remember(string path, Sprite sprite)
    {
        recent[path] = sprite;
        recentOrder.Enqueue(path);
        while (recentOrder.Count > RecentCacheSize)
            recent.Remove(recentOrder.Dequeue());
    }

    // Old behaviour, only used when numbered files can't be found (e.g. the portraits were renamed)
    static Sprite LoadFromFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return null;

        if (!folderFallback.TryGetValue(folder, out Sprite[] sprites))
        {
            Debug.LogWarning($"PortraitLibrary: numbered portraits were not found in Resources/{folder}; loading the whole folder instead (slow, uses a lot of memory). Check the portrait settings.");
            sprites = Resources.LoadAll<Sprite>(folder) ?? new Sprite[0];
            folderFallback[folder] = sprites;
        }

        if (sprites.Length == 0) return null;
        return sprites[Random.Range(0, sprites.Length)];
    }
}
