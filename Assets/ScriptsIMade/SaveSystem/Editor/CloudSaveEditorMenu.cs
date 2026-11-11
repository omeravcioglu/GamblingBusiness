using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Game > Cloud Save: the account, the sync state and the local save at a glance, upload / reconcile right now,
/// and the local test server (the same SaveRules the GameSave module runs, in-process, against files in
/// persistentDataPath/cloudtest/), so the whole flow can be tried before the module is deployed.
/// </summary>
public static class CloudSaveEditorMenu
{
    const string Root = "Tools/Game/Cloud Save/";
    const string LocalMenu = Root + "Use Local Test Server";

    [MenuItem(Root + "Status", false, 1)]
    static async void Status()
    {
        Debug.Log(CloudSaveSync.DescribeLocal());
        if (Application.isPlaying)
            Debug.Log(await CloudSaveSync.DescribeServerAsync());
        else
            Debug.Log("[CloudSave] The sync runs in Play mode: enter it to see the server's copy.");
    }

    [MenuItem(Root + "Upload Now", false, 20)]
    static void UploadNow()
    {
        CloudSaveSync.UploadNow();
        Debug.Log("[CloudSave] Upload requested (a running company is saved first). Watch the Console for the answer.");
    }

    [MenuItem(Root + "Upload Now", true)]
    static bool UploadNowValidate() => Application.isPlaying;

    [MenuItem(Root + "Reconcile Now", false, 21)]
    static void ReconcileNow()
    {
        CloudSaveSync.ReconcileNow();
        Debug.Log("[CloudSave] Reconcile requested: the local save is compared with the server's.");
    }

    [MenuItem(Root + "Reconcile Now", true)]
    static bool ReconcileNowValidate() => Application.isPlaying;

    [MenuItem(LocalMenu, false, 40)]
    static void ToggleLocalTestServer()
    {
        bool on = !CloudSaveSync.UseLocalTestServer;
        EditorPrefs.SetBool(CloudSaveSync.LocalTestServerPrefKey, on);
        Debug.Log(on
            ? "[CloudSave] Using the LOCAL TEST SERVER (SaveRules in-process, files in " + LocalTestSaveBackend.Folder + "). Editor only."
            : "[CloudSave] Using the real server: the GameSave module on Unity Gaming Services (deploy GameSaveCCM first: Services > Deployment).");
        if (Application.isPlaying)
            CloudSaveSync.OnBackendSettingChanged();
    }

    [MenuItem(LocalMenu, true)]
    static bool ToggleLocalTestServerValidate()
    {
        Menu.SetChecked(LocalMenu, CloudSaveSync.UseLocalTestServer);
        return true;
    }

    [MenuItem(Root + "Open Local Test Data Folder", false, 41)]
    static void OpenLocalTestFolder()
    {
        string folder = LocalTestSaveBackend.Folder;
        Directory.CreateDirectory(folder);
        Application.OpenURL("file:///" + folder.Replace('\\', '/'));
    }
}
