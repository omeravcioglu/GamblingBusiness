using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Manages spawning employee prefabs when hired and their placement in the office.
/// Employees will wander around the office using NavMesh (they stand still where there is no NavMesh).
/// </summary>
public class EmployeeSpawnManager : MonoBehaviour
{
    public static EmployeeSpawnManager Instance;

    [Header("Employee Prefab")]
    [Tooltip("The prefab to spawn for each employee")]
    public GameObject employeePrefab;
    [Tooltip("If true, creates a basic employee prefab if none assigned")]
    public bool createDefaultPrefabIfMissing = true;

    [Header("Spawn Settings")]
    [Tooltip("Where employees spawn when hired")]
    public Transform spawnPoint;
    [Tooltip("Area where employees can wander (leave empty to use entire NavMesh)")]
    public Transform wanderAreaCenter;
    public float wanderAreaRadius = 20f;

    [Header("Employee Appearance")]
    [Tooltip("Different materials/colors for variety")]
    public Material[] employeeMaterials;
    [Tooltip("Scale range for employee size variation")]
    public float minScale = 0.9f;
    public float maxScale = 1.1f;

    [Header("Performance")]
    [Tooltip("Maximum visible employees (older ones get pooled)")]
    public int maxVisibleEmployees = 50;

    // Track spawned employees
    private List<EmployeeVisual> spawnedEmployees = new List<EmployeeVisual>();
    private Dictionary<ApplicantData, EmployeeVisual> employeeMap = new Dictionary<ApplicantData, EmployeeVisual>();

    private CompanyManager subscribedCompany;
    private bool warnedNoPrefab;

    // Tint properties: _BaseColor for HDRP/URP Lit, _Color for older shaders (renderer.material.color is ignored by HDRP)
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (subscribedCompany != null)
            subscribedCompany.StateLoaded -= OnCompanyStateLoaded;
        if (Instance == this)
            Instance = null;
    }

    void Start()
    {
        // Create default prefab if needed
        if (employeePrefab == null && createDefaultPrefabIfMissing)
        {
            employeePrefab = CreateDefaultEmployeePrefab();
        }

        // Create spawn point if not assigned
        if (spawnPoint == null)
        {
            GameObject spawnObj = new GameObject("EmployeeSpawnPoint");
            spawnObj.transform.position = transform.position;
            spawnPoint = spawnObj.transform;
        }

        // Rebuild visuals whenever a save replaces the employee list
        if (CompanyManager.Instance != null)
        {
            subscribedCompany = CompanyManager.Instance;
            subscribedCompany.StateLoaded += OnCompanyStateLoaded;
        }

        // Spawn existing employees from save
        SpawnExistingEmployees();
    }

    void OnCompanyStateLoaded()
    {
        ClearAllVisuals();
        SpawnExistingEmployees();
    }

    void SpawnExistingEmployees()
    {
        if (CompanyManager.Instance == null) return;

        // Only the most recent hires get a visual (older ones would be removed right away)
        List<ApplicantData> list = CompanyManager.Instance.employees;
        int first = Mathf.Max(0, list.Count - Mathf.Max(0, maxVisibleEmployees));
        for (int i = first; i < list.Count; i++)
        {
            SpawnEmployeeVisual(list[i], randomPosition: true);
        }

        Debug.Log($"[EmployeeSpawnManager] Spawned {spawnedEmployees.Count} existing employees");
    }

    /// <summary>
    /// Called when a new employee is hired - spawns their visual
    /// </summary>
    public void OnEmployeeHired(ApplicantData employee)
    {
        SpawnEmployeeVisual(employee, randomPosition: false);
    }

    void SpawnEmployeeVisual(ApplicantData employee, bool randomPosition)
    {
        if (employee == null) return;

        if (employeePrefab == null)
        {
            if (!warnedNoPrefab)
            {
                warnedNoPrefab = true;
                Debug.LogWarning("[EmployeeSpawnManager] No employee prefab assigned!");
            }
            return;
        }

        // Check if already spawned
        if (employeeMap.TryGetValue(employee, out EmployeeVisual existing))
        {
            if (existing != null) return;
            employeeMap.Remove(employee); // its object was destroyed elsewhere
        }

        // Determine spawn position
        Vector3 spawnPos;
        if (randomPosition)
        {
            spawnPos = GetRandomNavMeshPosition();
        }
        else
        {
            spawnPos = spawnPoint != null ? spawnPoint.position : transform.position;
        }

        // Spawn employee
        GameObject empObj = Instantiate(employeePrefab, spawnPos, Quaternion.identity, transform);
        empObj.name = "Employee_" + employee.name;

        // Add or get EmployeeVisual component
        EmployeeVisual visual = empObj.GetComponent<EmployeeVisual>();
        if (visual == null)
        {
            visual = empObj.AddComponent<EmployeeVisual>();
        }

        // Apply visual variety
        ApplyVisualVariety(empObj, employee);

        // The runtime default prefab is kept inactive, so its clones start inactive too
        if (!empObj.activeSelf)
            empObj.SetActive(true);

        // Initialize with employee data
        visual.Initialize(employee, this);

        // Track
        spawnedEmployees.Add(visual);
        employeeMap[employee] = visual;

        // Enforce max limit
        EnforceMaxEmployees();

        Debug.Log($"[EmployeeSpawnManager] Spawned employee: {employee.name}");
    }

    void ApplyVisualVariety(GameObject empObj, ApplicantData employee)
    {
        // Random scale
        float scale = Random.Range(minScale, maxScale);
        empObj.transform.localScale = Vector3.one * scale;

        // Random material/color if available
        if (employeeMaterials != null && employeeMaterials.Length > 0)
        {
            Renderer renderer = empObj.GetComponentInChildren<Renderer>(true);
            Material material = employeeMaterials[Random.Range(0, employeeMaterials.Length)];
            if (renderer != null && material != null)
            {
                renderer.sharedMaterial = material;
            }
        }
        else
        {
            // Generate color based on employee traits
            ApplyTint(empObj, GenerateEmployeeColor(employee));
        }
    }

    // Per-renderer tint without creating material copies
    static void ApplyTint(GameObject obj, Color color)
    {
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        foreach (Renderer renderer in obj.GetComponentsInChildren<Renderer>(true))
        {
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            renderer.SetPropertyBlock(block);
        }
    }

    Color GenerateEmployeeColor(ApplicantData employee)
    {
        // Base color on gender with some randomness
        float hue;
        if (employee.gender == "Male")
        {
            hue = Random.Range(0.55f, 0.7f); // Blue-ish
        }
        else
        {
            hue = Random.Range(0.85f, 1f); // Pink/red-ish
            if (hue > 1f) hue -= 1f;
        }

        // Saturation based on traits
        float saturation = 0.4f;
        if (employee.isGolden) saturation = 0.8f;

        // Value based on IQ (brighter = smarter)
        float value = Mathf.Lerp(0.5f, 1f, (employee.IQ - 70f) / 80f);

        return Color.HSVToRGB(hue, saturation, value);
    }

    void EnforceMaxEmployees()
    {
        int max = Mathf.Max(0, maxVisibleEmployees);
        while (spawnedEmployees.Count > max)
        {
            // Remove oldest
            EmployeeVisual oldest = spawnedEmployees[0];
            spawnedEmployees.RemoveAt(0);
            RemoveFromMap(oldest);

            if (oldest != null)
            {
                Destroy(oldest.gameObject);
            }
        }
    }

    void RemoveFromMap(EmployeeVisual visual)
    {
        if (visual != null && visual.employeeData != null && employeeMap.TryGetValue(visual.employeeData, out EmployeeVisual mapped) && mapped == visual)
        {
            employeeMap.Remove(visual.employeeData);
            return;
        }

        // Destroyed elsewhere or data missing: drop any entry that points at it (or at nothing)
        List<ApplicantData> stale = null;
        foreach (var pair in employeeMap)
        {
            if (pair.Value == visual || pair.Value == null)
            {
                if (stale == null) stale = new List<ApplicantData>();
                stale.Add(pair.Key);
            }
        }
        if (stale != null)
        {
            foreach (var key in stale)
                employeeMap.Remove(key);
        }
    }

    void ClearAllVisuals()
    {
        foreach (var visual in spawnedEmployees)
        {
            if (visual != null)
                Destroy(visual.gameObject);
        }
        spawnedEmployees.Clear();
        employeeMap.Clear();
    }

    /// <summary>
    /// Get a random position on the NavMesh within the wander area
    /// </summary>
    public Vector3 GetRandomNavMeshPosition()
    {
        Vector3 center = wanderAreaCenter != null ? wanderAreaCenter.position : transform.position;

        for (int i = 0; i < 30; i++)
        {
            Vector3 randomPoint = center + Random.insideUnitSphere * wanderAreaRadius;
            randomPoint.y = center.y;

            NavMeshHit hit;
            if (NavMesh.SamplePosition(randomPoint, out hit, 5f, NavMesh.AllAreas))
            {
                return hit.position;
            }
        }

        // Fallback to spawn point
        return spawnPoint != null ? spawnPoint.position : transform.position;
    }

    /// <summary>
    /// Remove employee visual when fired (if you implement firing)
    /// </summary>
    public void OnEmployeeFired(ApplicantData employee)
    {
        if (employee == null) return;

        if (employeeMap.TryGetValue(employee, out EmployeeVisual visual))
        {
            spawnedEmployees.Remove(visual);
            employeeMap.Remove(employee);

            if (visual != null)
            {
                Destroy(visual.gameObject);
            }
        }
    }

    /// <summary>
    /// Create a basic employee prefab at runtime (kept inactive; spawned copies are activated)
    /// </summary>
    GameObject CreateDefaultEmployeePrefab()
    {
        GameObject prefab = new GameObject("DefaultEmployeePrefab");
        prefab.SetActive(false);

        // Create body (capsule)
        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.name = "Body";
        body.transform.SetParent(prefab.transform);
        body.transform.localPosition = new Vector3(0, 1f, 0);
        body.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        RemoveCollider(body);

        // Create head (sphere)
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.name = "Head";
        head.transform.SetParent(prefab.transform);
        head.transform.localPosition = new Vector3(0, 1.8f, 0);
        head.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);
        RemoveCollider(head);

        // Add NavMeshAgent (disabled: EmployeeVisual turns it on only where there is a NavMesh)
        NavMeshAgent agent = prefab.AddComponent<NavMeshAgent>();
        agent.speed = 2f;
        agent.angularSpeed = 120f;
        agent.acceleration = 8f;
        agent.stoppingDistance = 0.5f;
        agent.radius = 0.3f;
        agent.height = 1.8f;
        agent.enabled = false;

        // Add EmployeeVisual component
        prefab.AddComponent<EmployeeVisual>();

        prefab.transform.SetParent(transform);

        Debug.Log("[EmployeeSpawnManager] Created default employee prefab");

        return prefab;
    }

    // Wandering capsules shouldn't block clicks and raycasts on office items.
    // DestroyImmediate so copies made this frame don't inherit the collider.
    static void RemoveCollider(GameObject obj)
    {
        Collider collider = obj.GetComponent<Collider>();
        if (collider != null)
            DestroyImmediate(collider);
    }

    void OnDrawGizmosSelected()
    {
        // Draw wander area
        Vector3 center = wanderAreaCenter != null ? wanderAreaCenter.position : transform.position;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(center, wanderAreaRadius);

        // Draw spawn point
        if (spawnPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(spawnPoint.position, 0.5f);
        }
    }
}
