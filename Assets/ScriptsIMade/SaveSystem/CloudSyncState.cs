using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// What this PC knows about its local save chain's place on the server (CloudSaveSync), stored as JSON
/// (persistentDataPath/cloudsync.json; cloudsync_localtest.json for the Editor's local test server).
/// - One state per player: when another player (or, on a shared PC, another Steam user) uses this PC, the current state
///   is set aside as cloudsync_&lt;playerId&gt;.json (cloudsync_steam_&lt;steamId&gt;.json) together with the company
///   PlayerPrefs of its save, and that player's own state (or a fresh one) takes its place.
/// - Written after every change that matters (accept, adopt, new run, end run), atomically (temp file + swap).
/// </summary>
[Serializable]
public class CloudSyncState
{
    public int version = 1;
    /// <summary>The player (Unity player id) this state and the local save chain belong to (empty = nobody yet).</summary>
    public string ownerId;
    /// <summary>That player's Steam ID (empty without Steam): offline saves are only stamped with ownerId for the same Steam user.</summary>
    public string ownerSteamId;
    /// <summary>The run (company) of the local chain (empty = none: no company yet, or its run just ended).</summary>
    public string runId;
    /// <summary>The server seq this local chain continues from: the seq of the last accepted or downloaded save.</summary>
    public long baseSeq;
    /// <summary>The server's highest counted ledger index for runId (ledger entries at or below it are already counted).</summary>
    public long ledgerIndex;
    /// <summary>This PC started runId (New Game) and the server hasn't registered it yet: NewRun is still to be called.</summary>
    public bool pendingNewRun;
    /// <summary>The Steam user who started runId here: a pending NewRun of another Steam user (a shared PC) is dropped.</summary>
    public string newRunSteamId;
    /// <summary>endedRunId ended here (Game Over) and the server hasn't been told yet: EndRun is still to be called.</summary>
    public bool pendingEndRun;
    public string endedRunId;
    /// <summary>The Steam user who ended endedRunId here: a pending EndRun of another Steam user (a shared PC) is dropped.</summary>
    public string endedSteamId;
    /// <summary>
    /// What EndRun sends so the server can tell whether the ending copy was up to date: the seq its chain continued from
    /// (-1 = unknown) and its unanswered upload (seq -1 = none). An out-of-date copy's Game Over doesn't end the run.
    /// </summary>
    public long endedBaseSeq = -1;
    public long endedInFlightSeq = -1;
    public string endedInFlightHash;
    /// <summary>Server time (unix seconds) of the last accepted save.</summary>
    public long lastAcceptedAt;
    /// <summary>The newest meta.AcceptedAt this client has seen from the server (0 = never synced): NewRun's knownAcceptedAt.</summary>
    public long knownAcceptedAt;
    /// <summary>knownAcceptedAt when runId was started here (New Game): sent with its NewRun, so a New Game made on an
    /// out-of-date PC can't replace a company another PC kept playing (the answer is then superseded).</summary>
    public long newRunKnownAcceptedAt;
    /// <summary>
    /// The upload whose answer hasn't arrived (run, seq and SHA-256 of the exact save text): if the answer was lost but the
    /// server stored it, the next reconcile finds that save on the server and counts it as accepted.
    /// </summary>
    public string inFlightRun;
    public long inFlightSeq;
    public string inFlightHash;
    /// <summary>
    /// The owner's company PlayerPrefs (company name, legacy notes), kept here while this state is set aside for another
    /// user of a shared PC and put back when the owner returns. Empty while the owner is the one playing.
    /// </summary>
    public List<PrefValue> companyPrefs = new List<PrefValue>();
    /// <summary>This PC's clock (unix seconds) at the last successful sync (accept, download, new run), for the status tool.</summary>
    public long lastSyncAt;
    /// <summary>This PC's clock (unix seconds) when runId was started here (New Game).</summary>
    public long runStartedAt;

    /// <summary>One PlayerPrefs value and its type.</summary>
    [Serializable]
    public class PrefValue
    {
        public const int Text = 1;
        public const int Int = 2;
        public const int Float = 3;

        public string key;
        public int kind;
        public string text;
        public int number;
        public float real;
    }

    /// <summary>File of the current player's state for a backend ("cloudsync" or "cloudsync_localtest").</summary>
    public static string CurrentPath(string baseName)
    {
        return Path.Combine(Application.persistentDataPath, baseName + ".json");
    }

    /// <summary>File a player's state is set aside in while another player uses this PC.</summary>
    public static string SetAsidePath(string baseName, string playerId)
    {
        return Path.Combine(Application.persistentDataPath, baseName + "_" + SafeFilePart(playerId) + ".json");
    }

    /// <summary>The state in this file (or the temp copy an interrupted write left), or null when there is none.</summary>
    public static CloudSyncState Load(string path)
    {
        foreach (string candidate in new[] { path, path + ".tmp" })
        {
            try
            {
                if (!File.Exists(candidate)) continue;
                CloudSyncState state = JsonUtility.FromJson<CloudSyncState>(File.ReadAllText(candidate));
                if (state != null) return state;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[CloudSave] Couldn't read " + Path.GetFileName(candidate) + " (" + e.Message + ").");
            }
        }
        return null;
    }

    /// <summary>Write the state (temp file + swap). False (logged) when it couldn't be written.</summary>
    public bool Save(string path)
    {
        try
        {
            WriteAtomically(path, JsonUtility.ToJson(this, true));
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[CloudSave] Couldn't write " + Path.GetFileName(path) + ": " + e.Message);
            return false;
        }
    }

    public CloudSyncState Clone()
    {
        return JsonUtility.FromJson<CloudSyncState>(JsonUtility.ToJson(this));
    }

    /// <summary>Write a text file through a temp file, so a crash never leaves half a file.</summary>
    public static void WriteAtomically(string path, string contents)
    {
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
        string temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        if (File.Exists(path))
        {
            try
            {
                File.Replace(temp, path, null, true);
                return;
            }
            catch (Exception)
            {
                // Some file systems don't support Replace
            }
            File.Delete(path);
        }
        File.Move(temp, path);
    }

    /// <summary>A player id as a safe file name part (Unity ids are letters and digits already).</summary>
    public static string SafeFilePart(string value)
    {
        if (string.IsNullOrEmpty(value)) return "none";
        var chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_';
            if (!ok) chars[i] = '_';
        }
        return new string(chars);
    }
}
