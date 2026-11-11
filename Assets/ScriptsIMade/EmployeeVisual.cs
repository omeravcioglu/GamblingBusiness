using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Represents a visual employee in the office that wanders around using NavMesh.
/// Without a NavMesh nearby the agent is switched off and the employee just stands there.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class EmployeeVisual : MonoBehaviour
{
    [Header("Employee Data")]
    public ApplicantData employeeData;

    [Header("Wandering Settings")]
    public float minWanderDelay = 2f;
    public float maxWanderDelay = 8f;
    public float minIdleTime = 1f;
    public float maxIdleTime = 5f;

    [Header("Work Spots")]
    [Tooltip("Optional: specific spots this employee can go to (desks, etc)")]
    public Transform[] workSpots;
    public float workSpotChance = 0.3f;

    [Header("Animation")]
    public Animator animator;
    public string walkParameterName = "IsWalking";
    public string speedParameterName = "Speed";

    [Header("Name Display")]
    public bool showNameTag = true;
    public float nameTagHeight = 2.2f;

    // Components
    private NavMeshAgent agent;
    private EmployeeSpawnManager spawnManager;

    // State
    private enum EmployeeState { Idle, Walking, Working }
    private EmployeeState currentState = EmployeeState.Idle;
    private float stateTimer = 0f;
    private float nextStateChangeTime = 0f;

    // Golden effect
    private GameObject goldenEffect;

    // Animator parameters the controller actually has (avoids a warning per frame for missing ones)
    private HashSet<int> animatorParameters;

    void Awake()
    {
        EnsureComponents();
    }

    void EnsureComponents()
    {
        if (agent == null)
            agent = GetComponent<NavMeshAgent>();
        // Keep an Animator assigned in the Inspector
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    /// <summary>True when the agent can path (enabled and standing on a NavMesh).</summary>
    bool CanNavigate => agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh;

    /// <summary>
    /// Initialize with employee data
    /// </summary>
    public void Initialize(ApplicantData data, EmployeeSpawnManager manager)
    {
        EnsureComponents();

        employeeData = data;
        spawnManager = manager;

        PlaceOnNavMesh();

        // Adjust speed based on traits
        if (agent != null && data != null)
        {
            float baseSpeed = 2f;

            // Adjust based on age (older = slower)
            float ageFactor = Mathf.Lerp(1.2f, 0.7f, (data.age - 20f) / 50f);

            // Adjust based on traits
            if (data.traits != null)
            {
                foreach (var trait in data.traits)
                {
                    if (trait == null) continue;
                    if (trait.traitName == "Hard Worker" || trait.traitName == "Never Late")
                        baseSpeed *= 1.2f;
                    else if (trait.traitName == "Lazy" || trait.traitName == "Works Slow")
                        baseSpeed *= 0.7f;
                }
            }

            agent.speed = baseSpeed * ageFactor;
        }

        // Add golden effect for golden employees
        if (data != null && data.isGolden && goldenEffect == null)
        {
            CreateGoldenEffect();
        }

        // Start wandering
        SetNewDestination();
    }

    // Put the agent on the NavMesh if there is one close by; otherwise switch it off
    void PlaceOnNavMesh()
    {
        if (agent == null || CanNavigate) return;

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2f, NavMesh.AllAreas))
        {
            if (agent.enabled)
            {
                agent.Warp(hit.position);
            }
            else
            {
                transform.position = hit.position;
                agent.enabled = true;
            }
        }
        else if (agent.enabled)
        {
            agent.enabled = false;
        }
    }

    void CreateGoldenEffect()
    {
        // Create a simple golden glow
        goldenEffect = new GameObject("GoldenGlow");
        goldenEffect.transform.SetParent(transform);
        goldenEffect.transform.localPosition = Vector3.up * 1.5f;

        // Add light
        Light light = goldenEffect.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.85f, 0.3f);
        light.intensity = 0.5f;
        light.range = 2f;
    }

    void Update()
    {
        stateTimer += Time.deltaTime;

        switch (currentState)
        {
            case EmployeeState.Idle:
                UpdateIdle();
                break;
            case EmployeeState.Walking:
                UpdateWalking();
                break;
            case EmployeeState.Working:
                UpdateWorking();
                break;
        }

        UpdateAnimation();
    }

    void UpdateIdle()
    {
        if (stateTimer >= nextStateChangeTime)
        {
            SetNewDestination();
        }
    }

    void UpdateWalking()
    {
        if (!CanNavigate)
        {
            SetIdle();
            return;
        }

        // Check if reached destination
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            // Arrived - switch to idle or working
            if (Random.value < 0.3f)
            {
                currentState = EmployeeState.Working;
                nextStateChangeTime = Random.Range(minIdleTime * 2f, maxIdleTime * 2f);
            }
            else
            {
                currentState = EmployeeState.Idle;
                nextStateChangeTime = Random.Range(minIdleTime, maxIdleTime);
            }
            stateTimer = 0f;
        }
    }

    void UpdateWorking()
    {
        // Just wait at the work spot
        if (stateTimer >= nextStateChangeTime)
        {
            SetNewDestination();
        }
    }

    void SetIdle()
    {
        currentState = EmployeeState.Idle;
        stateTimer = 0f;
        nextStateChangeTime = Random.Range(minIdleTime, maxIdleTime);
    }

    void SetNewDestination()
    {
        if (spawnManager == null || !CanNavigate)
        {
            // No NavMesh: stay idle and check again later
            SetIdle();
            return;
        }

        Vector3 destination;

        // Chance to go to a work spot
        if (workSpots != null && workSpots.Length > 0 && Random.value < workSpotChance)
        {
            Transform spot = workSpots[Random.Range(0, workSpots.Length)];
            if (spot != null)
            {
                destination = spot.position;
            }
            else
            {
                destination = spawnManager.GetRandomNavMeshPosition();
            }
        }
        else
        {
            destination = spawnManager.GetRandomNavMeshPosition();
        }

        agent.SetDestination(destination);
        currentState = EmployeeState.Walking;
        nextStateChangeTime = Random.Range(minWanderDelay, maxWanderDelay);
        stateTimer = 0f;
    }

    void UpdateAnimation()
    {
        if (animator == null || !animator.isActiveAndEnabled) return;

        float speed = CanNavigate ? agent.velocity.magnitude : 0f;
        bool isWalking = speed > 0.1f;

        // Set walking parameter
        if (HasAnimatorParameter(walkParameterName))
        {
            animator.SetBool(walkParameterName, isWalking);
        }

        // Set speed parameter
        if (HasAnimatorParameter(speedParameterName))
        {
            animator.SetFloat(speedParameterName, speed);
        }
    }

    bool HasAnimatorParameter(string parameterName)
    {
        if (string.IsNullOrEmpty(parameterName) || animator == null) return false;

        if (animatorParameters == null)
        {
            if (animator.runtimeAnimatorController == null || !animator.isInitialized) return false;

            animatorParameters = new HashSet<int>();
            foreach (AnimatorControllerParameter p in animator.parameters)
                animatorParameters.Add(p.nameHash);
        }

        return animatorParameters.Contains(Animator.StringToHash(parameterName));
    }

    /// <summary>
    /// Make this employee go to a specific position
    /// </summary>
    public void GoTo(Vector3 position)
    {
        if (CanNavigate)
        {
            agent.SetDestination(position);
            currentState = EmployeeState.Walking;
            stateTimer = 0f;
        }
    }

    /// <summary>
    /// Stop moving and idle
    /// </summary>
    public void Stop()
    {
        if (CanNavigate)
        {
            agent.ResetPath();
        }
        SetIdle();
    }

    void OnDrawGizmosSelected()
    {
        if (CanNavigate && agent.hasPath)
        {
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(transform.position, agent.destination);
            Gizmos.DrawWireSphere(agent.destination, 0.3f);
        }
    }
}
