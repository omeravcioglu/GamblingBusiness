using UnityEngine;

/// <summary>
/// Individual die that detects which face is pointing up.
/// Attach to a cube with 6 face triggers or use raycast detection.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class VolumeDie : MonoBehaviour
{
    [Header("Detection Method")]
    public DetectionMethod method = DetectionMethod.Raycast;
    
    [Header("Face Values (for trigger method)")]
    [Tooltip("Assign 6 trigger colliders, one for each face")]
    public DieFaceTrigger[] faceTriggers;
    
    [Header("Settle Detection")]
    public float settleVelocityThreshold = 0.05f;
    public float settleAngularThreshold = 0.1f;
    
    [Header("Visual")]
    public TextMesh[] faceNumbers; // Optional: 3D text on each face
    public Material normalMaterial;
    public Material highlightMaterial;
    
    private Rigidbody rb;
    private int lastKnownValue = 1;
    private MeshRenderer meshRenderer;
    
    public enum DetectionMethod
    {
        Raycast,        // Cast ray up from die center
        TriggerColliders // Use trigger colliders on each face
    }
    
    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        meshRenderer = GetComponent<MeshRenderer>();
    }
    
    /// <summary>
    /// Returns true if the die has stopped moving
    /// </summary>
    public bool IsSettled()
    {
        if (rb == null) return true;
        return rb.linearVelocity.magnitude < settleVelocityThreshold && 
               rb.angularVelocity.magnitude < settleAngularThreshold;
    }
    
    /// <summary>
    /// Gets the value of the face pointing up
    /// </summary>
    public int GetTopFaceValue()
    {
        if (method == DetectionMethod.Raycast)
        {
            return GetTopFaceByRaycast();
        }
        else
        {
            return GetTopFaceByTriggers();
        }
    }
    
    /// <summary>
    /// Detects top face by checking which local axis points most upward.
    /// Standard die layout:
    /// - 1 is opposite 6
    /// - 2 is opposite 5
    /// - 3 is opposite 4
    /// </summary>
    int GetTopFaceByRaycast()
    {
        // Get the local up directions of each face
        Vector3 localUp = transform.up;
        Vector3 localRight = transform.right;
        Vector3 localForward = transform.forward;
        
        // Calculate which direction is most "up" (pointing toward world up)
        float[] dots = new float[6];
        
        // Face 1 = -Y (bottom in local space, so check -up)
        dots[0] = Vector3.Dot(-localUp, Vector3.up);     // Face 1
        dots[1] = Vector3.Dot(localUp, Vector3.up);       // Face 6 (opposite of 1)
        dots[2] = Vector3.Dot(-localRight, Vector3.up);   // Face 2
        dots[3] = Vector3.Dot(localRight, Vector3.up);    // Face 5 (opposite of 2)
        dots[4] = Vector3.Dot(-localForward, Vector3.up); // Face 3
        dots[5] = Vector3.Dot(localForward, Vector3.up);  // Face 4 (opposite of 3)
        
        // Find highest dot product (face pointing most upward)
        int bestIndex = 0;
        float bestDot = dots[0];
        
        for (int i = 1; i < 6; i++)
        {
            if (dots[i] > bestDot)
            {
                bestDot = dots[i];
                bestIndex = i;
            }
        }
        
        // Map index to die face value
        // Standard die: 1+6=7, 2+5=7, 3+4=7
        int[] faceValues = { 1, 6, 2, 5, 3, 4 };
        lastKnownValue = faceValues[bestIndex];
        
        return lastKnownValue;
    }
    
    int GetTopFaceByTriggers()
    {
        if (faceTriggers == null || faceTriggers.Length == 0)
        {
            Debug.LogWarning("[VolumeDie] No face triggers assigned, using raycast fallback");
            return GetTopFaceByRaycast();
        }
        
        // Find which face trigger is touching ground (lowest Y)
        DieFaceTrigger lowestTrigger = null;
        float lowestY = float.MaxValue;
        
        foreach (var trigger in faceTriggers)
        {
            if (trigger != null && trigger.transform.position.y < lowestY)
            {
                lowestY = trigger.transform.position.y;
                lowestTrigger = trigger;
            }
        }
        
        if (lowestTrigger != null)
        {
            // The opposite face is on top
            // Standard die: opposite faces sum to 7
            lastKnownValue = 7 - lowestTrigger.faceValue;
        }
        
        return lastKnownValue;
    }
    
    /// <summary>
    /// Highlights the die (e.g., when mouse hovers)
    /// </summary>
    public void SetHighlight(bool highlight)
    {
        if (meshRenderer != null && highlightMaterial != null && normalMaterial != null)
        {
            meshRenderer.material = highlight ? highlightMaterial : normalMaterial;
        }
    }
    
    void OnCollisionEnter(Collision collision)
    {
        // Play hit sound or effect when die hits something
        if (rb != null && rb.linearVelocity.magnitude > 1f)
        {
            // Could trigger a sound here
            // AudioSource.PlayClipAtPoint(hitSound, transform.position);
        }
    }
    
    void OnDrawGizmos()
    {
        // Draw face value in editor
        if (!Application.isPlaying) return;
        
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.5f, 0.1f);
        
        #if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 0.7f, 
            $"Value: {GetTopFaceValue()}");
        #endif
    }
}
