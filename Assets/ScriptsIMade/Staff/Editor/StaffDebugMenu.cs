using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Game > Debug: the staff files while MainGame is playing.
/// - Open Staff Files: opens the roster (like the HUD's Staff tab).
/// - Fire Worst Employee: fires the employee with the lowest profit per day through StaffLedger.Fire (severance, morale,
///   skill-point progress and the toast included).
/// </summary>
public static class StaffDebugMenu
{
    const string Root = "Tools/Game/Debug/";

    [MenuItem(Root + "Open Staff Files")]
    static void OpenStaffFiles()
    {
        if (!Playing()) return;
        StaffFilesView view = StaffFilesView.Instance != null ? StaffFilesView.Instance : Object.FindAnyObjectByType<StaffFilesView>(FindObjectsInactive.Include);
        if (view == null)
        {
            Debug.LogWarning("[Staff] No StaffFilesView in this scene (the staff files are added to scenes with a CompanyManager, like MainGame).");
            return;
        }
        view.Open();
    }

    [MenuItem(Root + "Fire Worst Employee")]
    static void FireWorstEmployee()
    {
        if (!Playing()) return;
        StaffLedger ledger = StaffLedger.Instance != null ? StaffLedger.Instance : Object.FindAnyObjectByType<StaffLedger>();
        if (ledger == null)
        {
            Debug.LogWarning("[Staff] No StaffLedger in this scene (it is added to scenes with a CompanyManager, like MainGame).");
            return;
        }
        ApplicantData worst = ledger.FindWorst();
        if (worst == null)
        {
            Debug.LogWarning("[Staff] Nobody to fire: the company has no employees.");
            return;
        }
        double profit = ledger.GetStats(worst).profitPerDay;
        if (ledger.Fire(worst))
            Debug.Log("[Staff] Fired the worst employee, " + StaffLedger.LogName(worst) + " (" + UIFormat.SignedMoney(profit) + "/day).");
    }

    [MenuItem(Root + "Open Staff Files", true)]
    [MenuItem(Root + "Fire Worst Employee", true)]
    static bool CanUse()
    {
        return EditorApplication.isPlaying;
    }

    static bool Playing()
    {
        if (EditorApplication.isPlaying) return true;
        Debug.LogWarning("[Staff] Enter Play mode in MainGame first.");
        return false;
    }
}
