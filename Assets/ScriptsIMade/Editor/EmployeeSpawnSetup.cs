#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.AI;

/// <summary>
/// Editor tool to set up the Employee Spawn system
/// </summary>
public class EmployeeSpawnSetup : EditorWindow
{
    private Transform spawnPoint;
    private Transform wanderCenter;
    private float wanderRadius = 15f;
    
    [MenuItem("Tools/Setup Employee Spawn System")]
    public static void ShowWindow()
    {
        GetWindow<EmployeeSpawnSetup>("Employee Spawn Setup");
    }
    
    void OnGUI()
    {
        GUILayout.Label("Employee Spawn System Setup", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        
        EditorGUILayout.HelpBox(
            "This sets up the system to spawn employee models when you hire them.\n\n" +
            "Employees will walk around the office using NavMesh.\n\n" +
            "REQUIREMENT: Your scene needs a NavMesh baked!\n" +
            "(Window > AI > Navigation > Bake)",
            MessageType.Info);
        
        EditorGUILayout.Space();
        
        GUILayout.Label("Optional Transforms:", EditorStyles.boldLabel);
        spawnPoint = (Transform)EditorGUILayout.ObjectField("Spawn Point", spawnPoint, typeof(Transform), true);
        wanderCenter = (Transform)EditorGUILayout.ObjectField("Wander Area Center", wanderCenter, typeof(Transform), true);
        wanderRadius = EditorGUILayout.FloatField("Wander Radius", wanderRadius);
        
        EditorGUILayout.Space();
        
        if (GUILayout.Button("Create Employee Spawn Manager", GUILayout.Height(40)))
        {
            CreateSpawnManager();
        }
        
        EditorGUILayout.Space();
        
        if (GUILayout.Button("Create Spawn Point Marker", GUILayout.Height(30)))
        {
            CreateSpawnPointMarker();
        }
        
        if (GUILayout.Button("Create Wander Area Marker", GUILayout.Height(30)))
        {
            CreateWanderAreaMarker();
        }
        
        EditorGUILayout.Space();
        
        // NavMesh check
        GUILayout.Label("NavMesh Status:", EditorStyles.boldLabel);
        
        NavMeshTriangulation navMesh = NavMesh.CalculateTriangulation();
        bool hasNavMesh = navMesh.vertices.Length > 0;
        
        if (hasNavMesh)
        {
            EditorGUILayout.HelpBox($"NavMesh found with {navMesh.vertices.Length} vertices.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "No NavMesh detected!\n\n" +
                "1. Go to Window > AI > Navigation\n" +
                "2. Select your floor/walkable surfaces\n" +
                "3. Mark them as 'Navigation Static'\n" +
                "4. Go to the Bake tab and click 'Bake'",
                MessageType.Warning);
            
            if (GUILayout.Button("Open Navigation Window"))
            {
                EditorApplication.ExecuteMenuItem("Window/AI/Navigation");
            }
        }
        
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "After setup:\n" +
            "1. Make sure your scene has a NavMesh\n" +
            "2. Position the spawn point where employees appear\n" +
            "3. Position the wander center in the middle of the walkable area\n" +
            "4. Hire employees and watch them spawn!",
            MessageType.Info);
    }
    
    void CreateSpawnManager()
    {
        // Check if one already exists
        EmployeeSpawnManager existing = FindObjectOfType<EmployeeSpawnManager>();
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Manager Exists", 
                "An EmployeeSpawnManager already exists. Select it?", 
                "Yes", "Create New"))
            {
                Selection.activeGameObject = existing.gameObject;
                return;
            }
        }
        
        // Create manager object
        GameObject managerObj = new GameObject("EmployeeSpawnManager");
        EmployeeSpawnManager manager = managerObj.AddComponent<EmployeeSpawnManager>();
        
        // Create spawn point
        if (spawnPoint == null)
        {
            GameObject spawnObj = new GameObject("EmployeeSpawnPoint");
            spawnObj.transform.position = Vector3.zero;
            spawnPoint = spawnObj.transform;
            
            // Try to find a good spawn position on NavMesh
            NavMeshHit hit;
            if (NavMesh.SamplePosition(Vector3.zero, out hit, 10f, NavMesh.AllAreas))
            {
                spawnObj.transform.position = hit.position;
            }
            
            AddGizmoDrawer(spawnObj, Color.green, 0.5f);
        }
        manager.spawnPoint = spawnPoint;
        
        // Create wander center
        if (wanderCenter == null)
        {
            GameObject wanderObj = new GameObject("EmployeeWanderArea");
            wanderObj.transform.position = spawnPoint != null ? spawnPoint.position : Vector3.zero;
            wanderCenter = wanderObj.transform;
            
            AddGizmoDrawer(wanderObj, Color.cyan, wanderRadius);
        }
        manager.wanderAreaCenter = wanderCenter;
        manager.wanderAreaRadius = wanderRadius;
        
        // Select the created object
        Selection.activeGameObject = managerObj;
        EditorUtility.SetDirty(managerObj);
        
        Debug.Log("[EmployeeSpawnSetup] Employee Spawn Manager created!\n" +
                  "- Employees will spawn at: " + spawnPoint.position + "\n" +
                  "- Wander area radius: " + wanderRadius + "m");
    }
    
    void CreateSpawnPointMarker()
    {
        GameObject spawnObj = new GameObject("EmployeeSpawnPoint");
        
        // Try to place on NavMesh
        NavMeshHit hit;
        Vector3 pos = SceneView.lastActiveSceneView != null ? 
            SceneView.lastActiveSceneView.camera.transform.position + SceneView.lastActiveSceneView.camera.transform.forward * 5f : 
            Vector3.zero;
        pos.y = 0;
        
        if (NavMesh.SamplePosition(pos, out hit, 10f, NavMesh.AllAreas))
        {
            spawnObj.transform.position = hit.position;
        }
        else
        {
            spawnObj.transform.position = pos;
        }
        
        AddGizmoDrawer(spawnObj, Color.green, 0.5f);
        
        spawnPoint = spawnObj.transform;
        Selection.activeGameObject = spawnObj;
        
        // Try to assign to existing manager
        EmployeeSpawnManager manager = FindObjectOfType<EmployeeSpawnManager>();
        if (manager != null)
        {
            manager.spawnPoint = spawnPoint;
            EditorUtility.SetDirty(manager);
        }
    }
    
    void CreateWanderAreaMarker()
    {
        GameObject wanderObj = new GameObject("EmployeeWanderArea");
        wanderObj.transform.position = spawnPoint != null ? spawnPoint.position : Vector3.zero;
        
        AddGizmoDrawer(wanderObj, Color.cyan, wanderRadius);
        
        wanderCenter = wanderObj.transform;
        Selection.activeGameObject = wanderObj;
        
        // Try to assign to existing manager
        EmployeeSpawnManager manager = FindObjectOfType<EmployeeSpawnManager>();
        if (manager != null)
        {
            manager.wanderAreaCenter = wanderCenter;
            manager.wanderAreaRadius = wanderRadius;
            EditorUtility.SetDirty(manager);
        }
    }
    
    void AddGizmoDrawer(GameObject obj, Color color, float radius)
    {
        var drawer = obj.GetComponent<SimpleGizmoDrawer>();
        if (drawer == null)
        {
            drawer = obj.AddComponent<SimpleGizmoDrawer>();
        }
        drawer.gizmoColor = color;
        drawer.radius = radius;
    }
}

/// <summary>
/// Simple component to draw a gizmo sphere for visualization
/// </summary>
public class SimpleGizmoDrawer : MonoBehaviour
{
    public Color gizmoColor = Color.green;
    public float radius = 0.5f;
    public bool showLabel = true;
    
    void OnDrawGizmos()
    {
        Gizmos.color = gizmoColor;
        Gizmos.DrawWireSphere(transform.position, radius);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * radius * 2);
        
        #if UNITY_EDITOR
        if (showLabel)
        {
            UnityEditor.Handles.Label(transform.position + Vector3.up * (radius + 0.2f), gameObject.name);
        }
        #endif
    }
}
#endif
