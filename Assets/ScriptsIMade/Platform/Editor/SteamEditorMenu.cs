using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Tools > Game > Steam > Status: is Steam compiled in, running and logged on, and is steam_appid.txt right.</summary>
public static class SteamEditorMenu
{
    [MenuItem("Tools/Game/Steam/Status", false, 1)]
    static void Status()
    {
        var text = new StringBuilder("[Steam] Status\n");
        text.Append("  ").Append(SteamPlatform.Describe()).Append('\n');
        text.Append("  Steamworks.NET compiled in: ").Append(SteamPlatform.IsCompiledIn ? "yes" : "no (package missing or unsupported build target)").Append('\n');

        string appIdFile = Path.Combine(Directory.GetCurrentDirectory(), "steam_appid.txt");
        string appId = File.Exists(appIdFile) ? File.ReadAllText(appIdFile).Trim() : null;
        text.Append("  steam_appid.txt (project root): ").Append(appId ?? "MISSING")
            .Append(appId != null && appId != SteamPlatform.DefaultAppId.ToString() ? "  <- expected " + SteamPlatform.DefaultAppId : "")
            .Append('\n');
        if (!Application.isPlaying)
            text.Append("  Steam starts in Play mode (the Steam client must be running and logged on).\n");
        text.Append("  ").Append(PlayerAccount.DescribeState()).Append('\n');
        Debug.Log(text.ToString());
    }
}
