using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>One unlocked achievement in a profile.</summary>
[Serializable]
public sealed class AchievementUnlockRecord
{
    public string api = "";
    /// <summary>When it unlocked (Unix seconds, UTC).</summary>
    public long time;
    /// <summary>Steam has it (SetAchievement answered Ok): not sent again.</summary>
    public bool steam;
}

/// <summary>A counter or helper value in a profile.</summary>
[Serializable]
public sealed class AchievementValueRecord
{
    public string id = "";
    public long value;
    /// <summary>The value last handed to the counter's Steam stat (-1 = not yet, or to be sent again).</summary>
    public long sent = -1;
}

/// <summary>A set of distinct names in a profile (e.g. the crisis mini-games solved).</summary>
[Serializable]
public sealed class AchievementSetRecord
{
    public string id = "";
    public List<string> items = new List<string>();
}

/// <summary>What AchievementStore writes to disk (JsonUtility).</summary>
[Serializable]
public sealed class AchievementProfileData
{
    public int version = 1;
    /// <summary>The Steam ID the file belongs to, or "local" (played without Steam).</summary>
    public string profile = "";
    public List<AchievementUnlockRecord> unlocked = new List<AchievementUnlockRecord>();
    public List<AchievementValueRecord> counters = new List<AchievementValueRecord>();
    /// <summary>Helper values the hooks keep between sessions (streaks in progress).</summary>
    public List<AchievementValueRecord> vars = new List<AchievementValueRecord>();
    public List<AchievementSetRecord> sets = new List<AchievementSetRecord>();
    /// <summary>Progress pop-ups already shown: "ACH_X@50" (percent).</summary>
    public List<string> milestones = new List<string>();
}

/// <summary>
/// One player's lifetime achievement progress on this PC: unlocks (and whether Steam has them), counters (and what their
/// Steam stats were last given), helper values and sets. Kept per profile in
/// persistentDataPath/Achievements/achievements_&lt;Steam ID&gt;.json ("local" without Steam), so people sharing a PC keep their
/// own. Nothing here throws: a broken file is set aside (.broken) and the profile starts empty.
/// </summary>
public sealed class AchievementStore
{
    /// <summary>The profile of play without Steam (never sent to anyone's Steam account unless merged, see AchievementSystem).</summary>
    public const string LocalProfile = "local";

    readonly AchievementProfileData data;
    readonly Dictionary<string, AchievementUnlockRecord> unlocks = new Dictionary<string, AchievementUnlockRecord>(StringComparer.Ordinal);
    readonly Dictionary<string, AchievementValueRecord> counters = new Dictionary<string, AchievementValueRecord>(StringComparer.Ordinal);
    readonly Dictionary<string, AchievementValueRecord> vars = new Dictionary<string, AchievementValueRecord>(StringComparer.Ordinal);
    readonly Dictionary<string, AchievementSetRecord> sets = new Dictionary<string, AchievementSetRecord>(StringComparer.Ordinal);
    readonly HashSet<string> milestones = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Changed since the last Save.</summary>
    public bool Dirty { get; private set; }
    public string ProfileId => data.profile;

    public AchievementStore(string profileId) : this(new AchievementProfileData { profile = profileId ?? LocalProfile })
    {
    }

    AchievementStore(AchievementProfileData loaded)
    {
        data = loaded ?? new AchievementProfileData();
        if (data.unlocked == null) data.unlocked = new List<AchievementUnlockRecord>();
        if (data.counters == null) data.counters = new List<AchievementValueRecord>();
        if (data.vars == null) data.vars = new List<AchievementValueRecord>();
        if (data.sets == null) data.sets = new List<AchievementSetRecord>();
        if (data.milestones == null) data.milestones = new List<string>();
        Rebuild();
    }

    void Rebuild()
    {
        data.unlocked.RemoveAll(u => u == null || string.IsNullOrEmpty(u.api));
        data.counters.RemoveAll(c => c == null || string.IsNullOrEmpty(c.id));
        data.vars.RemoveAll(c => c == null || string.IsNullOrEmpty(c.id));
        data.sets.RemoveAll(s => s == null || string.IsNullOrEmpty(s.id));
        data.milestones.RemoveAll(string.IsNullOrEmpty);
        foreach (AchievementUnlockRecord u in data.unlocked) unlocks[u.api] = u;
        foreach (AchievementValueRecord c in data.counters)
        {
            if (c.value < 0) c.value = 0;
            counters[c.id] = c;
        }
        foreach (AchievementValueRecord v in data.vars) vars[v.id] = v;
        foreach (AchievementSetRecord s in data.sets)
        {
            if (s.items == null) s.items = new List<string>();
            sets[s.id] = s;
        }
        foreach (string m in data.milestones) milestones.Add(m);
    }

    // ------------------------------------------------------------------
    // Unlocks
    // ------------------------------------------------------------------

    public bool IsUnlocked(string api) => api != null && unlocks.ContainsKey(api);

    public IReadOnlyList<AchievementUnlockRecord> Unlocks => data.unlocked;

    public AchievementUnlockRecord GetUnlock(string api)
    {
        if (api == null) return null;
        unlocks.TryGetValue(api, out AchievementUnlockRecord record);
        return record;
    }

    /// <summary>Record an unlock (false when it was already unlocked: then only <paramref name="steam"/> = true is kept).</summary>
    public bool MarkUnlocked(string api, long unixTime, bool steam = false)
    {
        if (string.IsNullOrEmpty(api)) return false;
        if (unlocks.TryGetValue(api, out AchievementUnlockRecord existing))
        {
            if (steam && !existing.steam)
            {
                existing.steam = true;
                Dirty = true;
            }
            return false;
        }
        var record = new AchievementUnlockRecord { api = api, time = unixTime, steam = steam };
        data.unlocked.Add(record);
        unlocks[api] = record;
        Dirty = true;
        return true;
    }

    /// <summary>Whether Steam has the unlock (false = send it again).</summary>
    public void SetSteamHas(string api, bool has)
    {
        AchievementUnlockRecord record = GetUnlock(api);
        if (record == null || record.steam == has) return;
        record.steam = has;
        Dirty = true;
    }

    /// <summary>Forget an unlock (debug tools).</summary>
    public bool RemoveUnlock(string api)
    {
        AchievementUnlockRecord record = GetUnlock(api);
        if (record == null) return false;
        unlocks.Remove(api);
        data.unlocked.Remove(record);
        Dirty = true;
        return true;
    }

    /// <summary>Unlocks Steam doesn't have yet.</summary>
    public int PendingSteamUnlocks
    {
        get
        {
            int n = 0;
            foreach (AchievementUnlockRecord u in data.unlocked) if (!u.steam) n++;
            return n;
        }
    }

    // ------------------------------------------------------------------
    // Counters
    // ------------------------------------------------------------------

    public long GetCount(string id)
    {
        if (id == null) return 0;
        return counters.TryGetValue(id, out AchievementValueRecord record) ? record.value : 0;
    }

    public void SetCount(string id, long value)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (value < 0) value = 0;
        if (!counters.TryGetValue(id, out AchievementValueRecord record))
        {
            if (value == 0) return;
            record = new AchievementValueRecord { id = id };
            data.counters.Add(record);
            counters[id] = record;
        }
        if (record.value == value) return;
        record.value = value;
        Dirty = true;
    }

    /// <summary>The value last handed to the counter's Steam stat (-1 = not yet).</summary>
    public long GetSent(string id)
    {
        if (id == null) return -1;
        return counters.TryGetValue(id, out AchievementValueRecord record) ? record.sent : -1;
    }

    public void SetSent(string id, long sent)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (!counters.TryGetValue(id, out AchievementValueRecord record))
        {
            record = new AchievementValueRecord { id = id };
            data.counters.Add(record);
            counters[id] = record;
        }
        if (record.sent == sent) return;
        record.sent = sent;
        Dirty = true;
    }

    public IReadOnlyList<AchievementValueRecord> CounterRecords => data.counters;

    // ------------------------------------------------------------------
    // Helper values and sets
    // ------------------------------------------------------------------

    public long GetVar(string id)
    {
        if (id == null) return 0;
        return vars.TryGetValue(id, out AchievementValueRecord record) ? record.value : 0;
    }

    public void SetVar(string id, long value)
    {
        if (string.IsNullOrEmpty(id)) return;
        if (!vars.TryGetValue(id, out AchievementValueRecord record))
        {
            if (value == 0) return;
            record = new AchievementValueRecord { id = id };
            data.vars.Add(record);
            vars[id] = record;
        }
        if (record.value == value) return;
        record.value = value;
        Dirty = true;
    }

    public IReadOnlyList<AchievementValueRecord> VarRecords => data.vars;

    /// <summary>Add a name to a set; returns how many names the set has now.</summary>
    public int AddToSet(string id, string item)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        if (!sets.TryGetValue(id, out AchievementSetRecord set))
        {
            set = new AchievementSetRecord { id = id };
            data.sets.Add(set);
            sets[id] = set;
        }
        if (!string.IsNullOrEmpty(item) && !set.items.Contains(item))
        {
            set.items.Add(item);
            Dirty = true;
        }
        return set.items.Count;
    }

    public IReadOnlyList<string> GetSet(string id)
    {
        if (id != null && sets.TryGetValue(id, out AchievementSetRecord set)) return set.items;
        return Array.Empty<string>();
    }

    public IReadOnlyList<AchievementSetRecord> SetRecords => data.sets;

    public bool HasMilestone(string key) => key != null && milestones.Contains(key);

    public void AddMilestone(string key)
    {
        if (string.IsNullOrEmpty(key) || !milestones.Add(key)) return;
        data.milestones.Add(key);
        Dirty = true;
    }

    public IReadOnlyList<string> Milestones => data.milestones;

    /// <summary>Nothing unlocked, counted or remembered.</summary>
    public bool IsEmpty
    {
        get
        {
            if (data.unlocked.Count > 0 || data.milestones.Count > 0) return false;
            foreach (AchievementValueRecord c in data.counters) if (c.value != 0) return false;
            foreach (AchievementValueRecord v in data.vars) if (v.value != 0) return false;
            foreach (AchievementSetRecord s in data.sets) if (s.items.Count > 0) return false;
            return true;
        }
    }

    /// <summary>Forget everything (Reset All).</summary>
    public void Clear()
    {
        data.unlocked.Clear();
        data.counters.Clear();
        data.vars.Clear();
        data.sets.Clear();
        data.milestones.Clear();
        unlocks.Clear();
        counters.Clear();
        vars.Clear();
        sets.Clear();
        milestones.Clear();
        Dirty = true;
    }

    /// <summary>
    /// Take another profile's progress in: unlocks (as not yet on Steam), the higher of each counter and helper value, the
    /// union of sets and milestones. Returns what was new (for the log).
    /// </summary>
    public int MergeFrom(AchievementStore other)
    {
        if (other == null || other == this) return 0;
        int changes = 0;
        foreach (AchievementUnlockRecord u in other.data.unlocked)
            if (MarkUnlocked(u.api, u.time, false)) changes++;
        foreach (AchievementValueRecord c in other.data.counters)
        {
            if (c.value > GetCount(c.id))
            {
                SetCount(c.id, c.value);
                SetSent(c.id, -1);
                changes++;
            }
        }
        foreach (AchievementValueRecord v in other.data.vars)
        {
            if (v.value > GetVar(v.id))
            {
                SetVar(v.id, v.value);
                changes++;
            }
        }
        foreach (AchievementSetRecord s in other.data.sets)
        {
            int before = GetSet(s.id).Count;
            foreach (string item in s.items) AddToSet(s.id, item);
            if (GetSet(s.id).Count != before) changes++;
        }
        foreach (string m in other.data.milestones) AddMilestone(m);
        return changes;
    }

    // ------------------------------------------------------------------
    // Files
    // ------------------------------------------------------------------

    /// <summary>persistentDataPath/Achievements.</summary>
    public static string Folder => Path.Combine(Application.persistentDataPath, "Achievements");

    /// <summary>The file of a profile ("local" or a Steam ID).</summary>
    public static string FilePath(string profileId)
    {
        return Path.Combine(Folder, "achievements_" + SafeName(profileId) + ".json");
    }

    static string SafeName(string profileId)
    {
        if (string.IsNullOrEmpty(profileId)) return LocalProfile;
        var chars = profileId.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-')) chars[i] = '_';
        }
        return new string(chars);
    }

    public string ToJson() => JsonUtility.ToJson(data, true);

    /// <summary>A store from JSON (empty for the profile when the text can't be read).</summary>
    public static AchievementStore FromJson(string json, string profileId)
    {
        AchievementProfileData loaded = null;
        if (!string.IsNullOrWhiteSpace(json))
        {
            try { loaded = JsonUtility.FromJson<AchievementProfileData>(json); }
            catch (Exception) { loaded = null; }
        }
        if (loaded == null) return new AchievementStore(profileId);
        loaded.profile = profileId ?? LocalProfile;
        return new AchievementStore(loaded);
    }

    /// <summary>Load a profile from its file (a new empty one when there is none; a broken file is set aside).</summary>
    public static AchievementStore Load(string profileId)
    {
        string path = FilePath(profileId);
        try
        {
            if (!File.Exists(path)) return new AchievementStore(profileId);
            string json = File.ReadAllText(path);
            AchievementProfileData loaded = null;
            try { loaded = JsonUtility.FromJson<AchievementProfileData>(json); }
            catch (Exception) { loaded = null; }
            if (loaded == null)
            {
                string broken = path + ".broken";
                try
                {
                    if (File.Exists(broken)) File.Delete(broken);
                    File.Move(path, broken);
                }
                catch (Exception) { }
                Debug.LogWarning("[Achievements] " + Path.GetFileName(path) + " couldn't be read; it was set aside as .broken and the profile starts empty.");
                return new AchievementStore(profileId);
            }
            loaded.profile = profileId ?? LocalProfile;
            return new AchievementStore(loaded);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Achievements] Couldn't load " + path + ": " + e.Message);
            return new AchievementStore(profileId);
        }
    }

    /// <summary>Write the profile to its file (a temporary file first, so a crash can't leave half a file). False on failure (logged).</summary>
    public bool Save()
    {
        string path = FilePath(ProfileId);
        try
        {
            Directory.CreateDirectory(Folder);
            string temp = path + ".tmp";
            File.WriteAllText(temp, ToJson());
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
            Dirty = false;
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Achievements] Couldn't save " + path + ": " + e.Message);
            return false;
        }
    }

    /// <summary>Delete a profile's file (after its progress was merged elsewhere, or by Reset All).</summary>
    public static void DeleteFile(string profileId)
    {
        try
        {
            string path = FilePath(profileId);
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Achievements] Couldn't delete the " + profileId + " profile: " + e.Message);
        }
    }
}
