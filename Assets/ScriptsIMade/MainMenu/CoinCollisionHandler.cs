using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Helper component to detect coin collisions and report back to MenuCoinToss
/// </summary>
public class CoinCollisionHandler : MonoBehaviour
{
    private MenuCoinToss coinToss;
    private float lastCollisionTime;
    private const float MIN_COLLISION_INTERVAL = 0.05f;
    
    public void Initialize(MenuCoinToss toss)
    {
        coinToss = toss;
    }
    
    private void OnCollisionEnter(Collision collision)
    {
        if (coinToss == null) return;
        if (Time.time - lastCollisionTime < MIN_COLLISION_INTERVAL) return;
        
        lastCollisionTime = Time.time;
        float impactForce = collision.relativeVelocity.magnitude;
        coinToss.OnCoinCollision(collision, impactForce);
    }
}
