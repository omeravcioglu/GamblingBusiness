using UnityEngine;

/// <summary>
/// Attach to a trigger collider on each face of the die.
/// When this face is touching the ground, the opposite face is on top.
/// </summary>
[RequireComponent(typeof(Collider))]
public class DieFaceTrigger : MonoBehaviour
{
    [Tooltip("The value shown on THIS face (1-6)")]
    public int faceValue = 1;
    
    [Tooltip("Is this face currently touching the ground?")]
    public bool isTouchingGround = false;
    
    private Collider col;
    
    void Awake()
    {
        col = GetComponent<Collider>();
        if (col != null)
        {
            col.isTrigger = true;
        }
    }
    
    void OnTriggerEnter(Collider other)
    {
        // Check if we're touching ground/table
        if (other.CompareTag("Ground") || other.gameObject.layer == LayerMask.NameToLayer("Ground"))
        {
            isTouchingGround = true;
        }
    }
    
    void OnTriggerStay(Collider other)
    {
        if (other.CompareTag("Ground") || other.gameObject.layer == LayerMask.NameToLayer("Ground"))
        {
            isTouchingGround = true;
        }
    }
    
    void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Ground") || other.gameObject.layer == LayerMask.NameToLayer("Ground"))
        {
            isTouchingGround = false;
        }
    }
    
    /// <summary>
    /// Returns the value of the OPPOSITE face (the one on top)
    /// </summary>
    public int GetOppositeValue()
    {
        // Standard die: opposite faces sum to 7
        return 7 - faceValue;
    }
    
    void OnDrawGizmos()
    {
        Gizmos.color = isTouchingGround ? Color.red : Color.green;
        Gizmos.DrawWireSphere(transform.position, 0.05f);
    }
}
