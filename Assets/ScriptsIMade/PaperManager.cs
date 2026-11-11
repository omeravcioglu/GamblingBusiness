using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PaperManager : MonoBehaviour
{
    private Animator anim;
    private Vector3 targetPos;

    [Header("Animation Trigger Names")]
    [Tooltip("Name of the entrance animation trigger in the Animator (skipped if the controller has no such trigger)")]
    public string entranceTrigger = "Entrance";
    [Tooltip("Name of the skip animation trigger in the Animator")]
    public string skipTrigger = "Skip";
    [Tooltip("Name of the hire animation trigger in the Animator")]
    public string hireTrigger = "Hire";

    [Header("Decision stamp")]
    [Tooltip("Seconds the HIRED / SKIPPED stamp shows on the CV (CVPaperView) before the paper flies off. 0 = fly at once.")]
    public float stampHold = 0.35f;

    [Header("Fallback")]
    [Tooltip("If the Animator has no hire/skip trigger, the paper slides to its target over this many seconds")]
    public float fallbackMoveDuration = 0.5f;

    // Trigger names the Animator controller actually has (read once the Animator is running)
    private HashSet<int> triggerHashes;
    private bool pendingHire;

    void Awake()
    {
        anim = GetComponent<Animator>();
    }

    /// <summary>
    /// Play the entrance animation when paper spawns
    /// </summary>
    public void PlayEntrance()
    {
        TrySetTrigger(entranceTrigger);
    }

    /// <summary>
    /// Play entrance with a delay (for staggered batch spawning)
    /// </summary>
    public void PlayEntranceDelayed(float delay)
    {
        if (delay <= 0f)
        {
            PlayEntrance();
            return;
        }
        Invoke(nameof(PlayEntrance), delay);
    }

    public void PlaySkip(Vector3 trashPos)
    {
        targetPos = trashPos;
        CancelInvoke(nameof(PlayEntrance));
        Decide(false);
    }

    public void PlayHire(Vector3 hirePos)
    {
        targetPos = hirePos;
        CancelInvoke(nameof(PlayEntrance));
        Decide(true);
    }

    // Stamp the designed CV, hold it for a moment, then play the hire / skip animation
    void Decide(bool hire)
    {
        pendingHire = hire;
        CVPaperView cv = GetComponentInChildren<CVPaperView>();
        if (cv != null)
            cv.ShowStamp(hire);

        if (cv != null && stampHold > 0f && isActiveAndEnabled)
            Invoke(nameof(PlayDecision), stampHold);
        else
            PlayDecision();
    }

    void PlayDecision()
    {
        if (!TrySetTrigger(pendingHire ? hireTrigger : skipTrigger))
            SlideToTarget();
    }

    // Called at middle point of animation (using Animation Event)
    public void MoveToTarget()
    {
        transform.position = targetPos;
    }

    /// <summary>True if the Animator's controller has a trigger with this name.</summary>
    public bool HasTrigger(string triggerName)
    {
        if (anim == null || string.IsNullOrEmpty(triggerName)) return false;

        if (triggerHashes == null)
        {
            // Parameters can only be read while the Animator is running a controller
            if (anim.runtimeAnimatorController == null || !anim.isActiveAndEnabled || !anim.isInitialized)
                return false;

            triggerHashes = new HashSet<int>();
            foreach (AnimatorControllerParameter p in anim.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Trigger)
                    triggerHashes.Add(p.nameHash);
            }
        }

        return triggerHashes.Contains(Animator.StringToHash(triggerName));
    }

    // SetTrigger only when the controller has the trigger (avoids "Parameter does not exist" warnings)
    bool TrySetTrigger(string triggerName)
    {
        if (!HasTrigger(triggerName)) return false;
        anim.SetTrigger(triggerName);
        return true;
    }

    void SlideToTarget()
    {
        if (!isActiveAndEnabled || fallbackMoveDuration <= 0f)
        {
            MoveToTarget();
            return;
        }

        // The Animator would keep writing its default pose over our movement
        if (anim != null)
            anim.enabled = false;

        StopAllCoroutines();
        StartCoroutine(SlideRoutine());
    }

    IEnumerator SlideRoutine()
    {
        Vector3 start = transform.position;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / fallbackMoveDuration;
            transform.position = Vector3.Lerp(start, targetPos, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
    }
}
