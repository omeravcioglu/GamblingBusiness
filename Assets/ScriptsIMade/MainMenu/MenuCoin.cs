using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Attach to coin prefab for landing detection and visual effects.
/// The prefab should have:
/// - Rigidbody (set to kinematic initially, MenuCoinToss will enable it)
/// - Collider for physics
/// - Optional: Animator for initial throw animation
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class MenuCoin : MonoBehaviour
{
    [Header("Landing Effects")]
    [SerializeField] private ParticleSystem landingParticles;
    [SerializeField] private AudioClip landSound;

    [Header("Trail Effect")]
    [SerializeField] private TrailRenderer trail;

    [Header("Glow Effect")]
    [SerializeField] private GameObject glowEffect;
    [SerializeField] private bool isSuccessCoin = true; // Set in prefab

    [Header("Events")]
    public UnityEvent OnLanded;

    private bool hasLanded = false;
    private Rigidbody rb;
    private int groundHits = 0;

    public bool IsSuccessCoin => isSuccessCoin;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    void Start()
    {
        // Enable trail if present
        if (trail != null)
        {
            trail.emitting = true;
        }

        // Enable glow
        if (glowEffect != null)
        {
            glowEffect.SetActive(true);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        // First ground hit = landing effects
        if (!hasLanded && collision.relativeVelocity.magnitude > 0.5f)
        {
            groundHits++;

            if (groundHits == 1)
            {
                hasLanded = true;

                // Play landing particles
                if (landingParticles != null)
                {
                    landingParticles.transform.position = collision.contactCount > 0
                        ? collision.GetContact(0).point
                        : transform.position;
                    landingParticles.Play();
                }

                // Play landing sound
                if (landSound != null)
                {
                    AudioSource.PlayClipAtPoint(landSound, transform.position, GameSettings.SfxVolume);
                }

                // Disable trail on landing
                if (trail != null)
                {
                    trail.emitting = false;
                }

                OnLanded?.Invoke();
            }
        }
    }

    /// <summary>
    /// Get which side the coin landed on (based on up direction)
    /// </summary>
    public bool IsHeadsUp()
    {
        // Assuming "heads" is when the coin's local up vector points world up
        return Vector3.Dot(transform.up, Vector3.up) > 0;
    }

    /// <summary>
    /// Reset for another toss.
    /// (Not called Reset: Unity calls a method with that name in the Editor when the component is added or reset.)
    /// </summary>
    public void ResetCoin()
    {
        hasLanded = false;
        groundHits = 0;

        if (trail != null)
        {
            trail.Clear();
            trail.emitting = true;
        }
    }

    /// <summary>
    /// Add a dramatic sparkle/highlight effect
    /// </summary>
    public void SetHighlight(bool enabled)
    {
        if (glowEffect != null)
        {
            glowEffect.SetActive(enabled);
        }
    }

    #if UNITY_EDITOR
    [ContextMenu("Setup Basic Coin")]
    private void SetupBasicCoin()
    {
        // Add collider if missing
        if (GetComponent<Collider>() == null)
        {
            var capsule = gameObject.AddComponent<CapsuleCollider>();
            capsule.direction = 1; // Y-axis (for flat coin)
            capsule.radius = 0.5f;
            capsule.height = 0.1f;
            Debug.Log("Added CapsuleCollider for coin shape");
        }

        // Setup rigidbody
        if (rb == null) rb = GetComponent<Rigidbody>();
        rb.mass = 0.05f; // Light coin
        rb.linearDamping = 0.5f;
        rb.angularDamping = 0.5f;
        rb.isKinematic = true; // Start kinematic
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        Debug.Log("Coin setup complete!");
    }
    #endif
}
