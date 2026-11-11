/// <summary>
/// A MonoBehaviour that stores its own state in the save file.
/// SaveSystem finds every active ISaveParticipant in the loaded scenes,
/// stores CaptureState() under SaveKey, and hands it back to RestoreState() on load.
/// Use JsonUtility for the payload so it stays readable in the save file.
/// </summary>
public interface ISaveParticipant
{
    /// <summary>Stable unique key, e.g. "stocks" or "building". Never change it once shipped.</summary>
    string SaveKey { get; }

    /// <summary>Return this system's state as a JSON string.</summary>
    string CaptureState();

    /// <summary>Restore state from a JSON string produced by CaptureState(). Must tolerate old or partial data.</summary>
    void RestoreState(string json);
}
