using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Game > Manager > Debug Window (Play mode): the manager's state, the pending decisions, and every situation with
/// its weight now or the reason it can't come up, with buttons to put one on the desk (Force ignores cooldowns and
/// conditions but still needs its targets).
/// </summary>
public class ManagerDebugWindow : EditorWindow
{
    Vector2 scroll;
    string filter = "";
    float satisfaction = 50f;
    string lastMessage = "";

    public static void Open()
    {
        ManagerDebugWindow w = GetWindow<ManagerDebugWindow>("Manager Debug");
        w.minSize = new Vector2(520f, 400f);
    }

    void OnInspectorUpdate()
    {
        if (Application.isPlaying) Repaint();
    }

    void OnGUI()
    {
        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play mode (MainGame) to debug the manager. Validate the files any time from Tools > Game > Manager.", MessageType.Info);
            if (GUILayout.Button("Validate Situations")) ManagerMenu.Validate();
            return;
        }
        ManagerSystem s = ManagerSystem.Instance;
        if (s == null || !s.IsStateKnown)
        {
            EditorGUILayout.HelpBox("No manager system yet (no CompanyManager in this scene, or the save is still loading).", MessageType.Warning);
            return;
        }

        // State
        EditorGUILayout.LabelField("Phase", s.Phase);
        if (s.HasManager)
        {
            ManagerPerson m = s.Manager;
            EditorGUILayout.LabelField("Manager", m.name + " (" + m.personality + "), hired day " + m.hiredDay);
            EditorGUILayout.LabelField("Salary", UIFormat.Money(m.salary) + " / yr, expects " + UIFormat.Money(ManagerSystem.ExpectedSalary(m)));
            EditorGUILayout.LabelField("Satisfaction", m.satisfaction.ToString("0.0") + "  (delegation " + (s.DelegateChance() * 100f).ToString("0") + "%, competence " + m.competence.ToString("0.00") + ")");
            EditorGUILayout.BeginHorizontal();
            satisfaction = EditorGUILayout.Slider(satisfaction, 0f, 100f);
            if (GUILayout.Button("Set", GUILayout.Width(50f))) ManagerMenu.SetSatisfaction(satisfaction);
            EditorGUILayout.EndHorizontal();
        }
        else if (s.Phase == ManagerPhase.Gap)
        {
            EditorGUILayout.LabelField("Next candidates", "day " + s.GapUntilDay);
        }
        ManagerSaveState st = s.DebugState;
        EditorGUILayout.LabelField("Contracts", st.policies.Count + " policies, " + st.rentals.Count + " rentals (" + s.RentedSeats + " seats), " + st.shares.Count + " shares, " + st.perks.Count + " perks");
        EditorGUILayout.LabelField("Stats", st.stats.created + " created, " + st.stats.decided + " decided, " + st.stats.expired + " expired, followed " + st.stats.followed + " / overrode " + st.stats.overridden + ", recurring paid " + UIFormat.Money(st.stats.recurringPaid));

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Open desk")) s.OpenDesk();
        if (s.Phase == ManagerPhase.None || s.Phase == ManagerPhase.Gap) { if (GUILayout.Button("Candidates now")) { s.StartPicking("debug"); s.OpenPick(); } }
        else if (s.Phase == ManagerPhase.Picking) { if (GUILayout.Button("Open pick")) s.OpenPick(); }
        if (s.HasManager && GUILayout.Button("Resign")) s.Resign("debug");
        if (GUILayout.Button("Roll a situation")) lastMessage = s.TryCreate("day") ? "A situation came up." : "Nothing eligible.";
        if (GUILayout.Button("Reload files")) s.ReloadLibrary();
        EditorGUILayout.EndHorizontal();
        if (!string.IsNullOrEmpty(lastMessage)) EditorGUILayout.HelpBox(lastMessage, MessageType.None);

        // Pending
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("On the desk (" + s.PendingCount + ")", EditorStyles.boldLabel);
        foreach (ManagerPending p in new List<ManagerPending>(s.Pending))
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(p.situation + (p.urgent ? " (urgent)" : "") + "  day " + p.createdDay + " -> " + p.expiresDay, GUILayout.MinWidth(260f));
            if (GUILayout.Button("Show", GUILayout.Width(50f))) s.OpenDesk(p.uid);
            if (GUILayout.Button("Expire", GUILayout.Width(55f))) s.ExpireNow(p.uid);
            EditorGUILayout.EndHorizontal();
        }

        // Situations
        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Situations (" + s.Library.Situations.Count + ")", EditorStyles.boldLabel);
        filter = EditorGUILayout.TextField(filter);
        EditorGUILayout.EndHorizontal();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (ManagerSituationDef def in s.Library.Situations)
        {
            if (!string.IsNullOrEmpty(filter) && !def.Id.Contains(filter) && (def.Category == null || !def.Category.Contains(filter))) continue;
            bool ok = s.Eligible(def, null, out List<ManagerTargetState> targets, out string why);
            EditorGUILayout.BeginHorizontal();
            GUI.color = ok ? Color.white : new Color(1f, 1f, 1f, 0.55f);
            EditorGUILayout.LabelField(def.Category + " / " + def.Id, GUILayout.Width(250f));
            EditorGUILayout.LabelField(ok ? "weight " + s.Weight(def, targets).ToString("0.##") : why + (def.FollowupOnly ? " (follow-up only)" : ""));
            GUI.color = Color.white;
            if (GUILayout.Button("Add", GUILayout.Width(40f))) lastMessage = s.Force(def.Id, false, out string w) ? def.Id + " is on the desk." : def.Id + ": " + w;
            if (GUILayout.Button("Force", GUILayout.Width(50f))) lastMessage = s.Force(def.Id, true, out string w2) ? def.Id + " is on the desk (forced)." : def.Id + ": " + w2;
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndScrollView();
    }
}
