using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Tools > Online City: switch between the real cities and the local test cities, and quick play-mode helpers.</summary>
public static class CityEditorMenu
{
    const string LocalMenu = "Tools/Online City/Use Local Test Cities";

    [MenuItem(LocalMenu, false, 1)]
    static void ToggleLocal()
    {
        CityOnline.UseLocalTestCity = !CityOnline.UseLocalTestCity;
        Debug.Log(CityOnline.UseLocalTestCity
            ? "[City] Using the LOCAL TEST CITIES (only on this PC). Turn this off to use the real cities."
            : "[City] Using the REAL cities (Unity Gaming Services). Deploy the OnlineCity module first: Services > Deployment.");
        if (Application.isPlaying && CityOnline.Instance != null) _ = CityOnline.Instance.Reconnect();
    }

    [MenuItem(LocalMenu, true)]
    static bool ToggleLocalValidate()
    {
        Menu.SetChecked(LocalMenu, CityOnline.UseLocalTestCity);
        return true;
    }

    [MenuItem("Tools/Online City/Open the Cities (Play Mode)", false, 20)]
    static void OpenCity() => CityScreen.Open();

    [MenuItem("Tools/Online City/Open the Cities (Play Mode)", true)]
    static bool OpenCityValidate() => Application.isPlaying;

    [MenuItem("Tools/Online City/Unlock the Cities now (Play Mode)", false, 21)]
    static void Unlock() => CityOnline.Ensure().ForceUnlock();

    [MenuItem("Tools/Online City/Unlock the Cities now (Play Mode)", true)]
    static bool UnlockValidate() => Application.isPlaying;

    [MenuItem("Tools/Online City/Delete the Local Test Cities", false, 40)]
    static void DeleteLocal()
    {
        int found = 0;
        foreach (var world in CityLayout.Worlds)
            if (File.Exists(CityLocalBackend.FilePath(world.Id))) found++;
        if (found == 0)
        {
            Debug.Log("[City] There are no local test cities yet.");
            return;
        }
        if (!EditorUtility.DisplayDialog("Delete the local test cities?",
                "Every plot, balance and drawing of the local test cities is deleted. The real cities aren't touched.", "Delete", "Cancel"))
            return;
        foreach (var world in CityLayout.Worlds)
        {
            string path = CityLocalBackend.FilePath(world.Id);
            if (File.Exists(path)) File.Delete(path);
        }
        Debug.Log("[City] Local test cities deleted.");
    }

    [MenuItem("Tools/Online City/Deploy the Server...", false, 60)]
    static void Deploy()
    {
        if (!EditorApplication.ExecuteMenuItem("Services/Deployment"))
            EditorUtility.DisplayDialog("Deploy the city server",
                "Open Services > Deployment, pick your environment (production), then Deploy All.\n\n" +
                "It deploys OnlineCity.ccmr (the server code for all six cities) and OnlineCity.ac (stops players writing Cloud Save directly).", "OK");
    }
}
