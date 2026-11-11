using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Cinematic coin toss with natural physics-based animation.
/// Features letterbox effect (cinematic black bars) for dramatic focus.
/// </summary>
public class MenuCoinToss : MonoBehaviour, IMenuCoin
{
    [Header("Coin Prefabs")]
    [SerializeField] private GameObject coinPrefabSuccess;
    [SerializeField] private GameObject coinPrefabFail;
    
    [Header("Hand Animation")]
    [SerializeField] private Animator handAnimator;
    [SerializeField] private string handAnimTrigger = "Toss";
    [SerializeField] private Transform coinSpawnPoint;
    [Tooltip("Delay after triggering hand anim before coin appears")]
    [SerializeField] private float spawnDelay = 0.15f;
    [Tooltip("How long coin follows hand during throw motion")]
    [SerializeField] private float followHandTime = 0.25f;
    
    [Header("Toss Arc")]
    [SerializeField] private Transform landingPoint;
    [Tooltip("How high the coin goes at peak")]
    [SerializeField] private float tossHeight = 2.5f;
    [Tooltip("Total flight time (hand to physics handoff)")]
    [SerializeField] private float flightDuration = 1.2f;
    [Tooltip("Number of full rotations during flight")]
    [SerializeField] private float spinRotations = 5f;
    [Tooltip("Slight tilt wobble for realism")]
    [SerializeField] private float wobbleAmount = 12f;
    
    [Header("Physics Handoff")]
    [Tooltip("How much of the arc to animate before physics takes over (0.7 = 70%)")]
    [Range(0.5f, 0.95f)]
    [SerializeField] private float animatedPortion = 0.75f;
    [Tooltip("Randomize landing position slightly")]
    [SerializeField] private float landingVariance = 0.08f;
    
    [Header("Bounce Physics")]
    [Tooltip("Bounciness of coin (0 = no bounce, 1 = super bouncy)")]
    [Range(0f, 1f)]
    [SerializeField] private float bounciness = 0.4f;
    [Tooltip("Friction when sliding")]
    [Range(0f, 1f)]
    [SerializeField] private float friction = 0.5f;
    [Tooltip("Extra downward force multiplier")]
    [SerializeField] private float gravityScale = 1.5f;
    [Tooltip("Angular drag to slow spinning")]
    [SerializeField] private float angularDrag = 1f;
    
    [Header("Camera Effects")]
    [SerializeField] private Camera mainCamera;
    [Tooltip("FOV zoom during toss (1 = no zoom, higher = more zoom)")]
    [SerializeField] private float cameraZoomAmount = 1.15f;
    [Tooltip("How much camera moves toward coin (0 = none, 1 = full)")]
    [Range(0f, 1f)]
    [SerializeField] private float cameraMoveAmount = 0.5f;
    [Tooltip("Camera tracks coin to keep it centered")]
    [SerializeField] private bool cameraTracksCoin = true;
    [Tooltip("Subtle shake on first landing")]
    [SerializeField] private float landingShakeIntensity = 0.015f;
    
    [Header("Cinematic Letterbox")]
    [Tooltip("Enable black bars effect")]
    [SerializeField] private bool enableLetterbox = true;
    [Tooltip("Canvas for letterbox UI (will create if null)")]
    [SerializeField] private Canvas letterboxCanvas;
    [Tooltip("How much of screen the bars cover (0.15 = 15% from each side)")]
    [Range(0.05f, 0.3f)]
    [SerializeField] private float letterboxSize = 0.12f;
    [Tooltip("Bar color (usually black)")]
    [SerializeField] private Color letterboxColor = Color.black;
    [Tooltip("Distance from camera for letterbox (coin renders in front if closer)")]
    [SerializeField] private float letterboxPlaneDistance = 5f;
    
    [Header("Brightness Adjustment")]
    [Tooltip("Dim screen during zoom to prevent bloom flash")]
    [SerializeField] private bool dimDuringZoom = true;
    [Tooltip("How much to dim (0 = no dim, 0.3 = 30% darker)")]
    [Range(0f, 0.5f)]
    [SerializeField] private float dimAmount = 0.15f;
    
    [Header("Peak Slow Motion")]
    [Tooltip("Enable slow-mo at peak of arc")]
    [SerializeField] private bool enableSlowMo = true;
    [Tooltip("Time scale at peak (0.3 = 3x slower)")]
    [Range(0.1f, 0.8f)]
    [SerializeField] private float slowMoTimeScale = 0.4f;
    [Tooltip("How long the slow-mo effect lasts (real seconds)")]
    [SerializeField] private float slowMoDuration = 0.4f;
    
    [Header("UI Fade During Cinematic")]
    [Tooltip("Canvas group to fade out during zoom/letterbox")]
    [SerializeField] private CanvasGroup uiToFade;
    [Tooltip("How much to fade the UI (0 = invisible, 1 = no fade)")]
    [Range(0f, 1f)]
    [SerializeField] private float uiFadeMinAlpha = 0.1f;
    
    [Header("Success Chance")]
    [Range(0f, 1f)]
    [SerializeField] private float successChance = 0.5f;
    
    [Header("Audio")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip coinFlipSound;
    [SerializeField] private AudioClip coinBounceSound;
    [SerializeField] private AudioClip coinSettleSound;
    
    [Header("Events")]
    public UnityEvent OnTossStarted;
    public UnityEvent OnCoinLaunched;
    public UnityEvent OnPhysicsStart;
    public UnityEvent OnCoinLanded;
    public UnityEvent OnSuccess;
    public UnityEvent OnFail;
    public UnityEvent OnComplete;
    
    private GameObject currentCoin;
    private Rigidbody coinRb;
    private Collider coinCollider;
    private bool isTossing;
    private bool lastResult;
    private PhysicsMaterial coinPhysMat;
    
    // Tracking for velocity calculation
    private Vector3 lastPosition;
    private Quaternion lastRotation;
    private Vector3 currentVelocity;
    private Vector3 currentAngularVelocity;
    
    // Bounce tracking
    private bool hasLandedOnce;
    private int bounceCount;
    
    // Camera
    private float originalFOV;
    private Vector3 originalCamPos;
    private Quaternion originalCamRot;
    
    // Letterbox UI
    private RectTransform topBar;
    private RectTransform bottomBar;
    private Image topBarImage;
    private Image bottomBarImage;
    private float currentLetterboxAmount;
    
    // Dim overlay
    private Image dimOverlay;
    
    // UI fade
    private float originalUIAlpha = 1f;

    // Slow motion (the only place allowed to change Time.timeScale; always put back)
    private float baseFixedDeltaTime = 0.02f;
    private bool slowMoActive;

    public bool IsTossing => isTossing;
    public bool LastResultWasSuccess => lastResult;

    /// <summary>The chance set in the Inspector: the odds of the first flip.</summary>
    public float BaseSuccessChance => successChance;

    private void Awake()
    {
        // The project's physics step, restored after slow motion
        baseFixedDeltaTime = Time.fixedDeltaTime;

        if (mainCamera == null)
            mainCamera = Camera.main;
            
        if (mainCamera != null)
        {
            originalFOV = mainCamera.fieldOfView;
            originalCamPos = mainCamera.transform.position;
            originalCamRot = mainCamera.transform.rotation;
        }
        
        // Create physics material for bouncing
        coinPhysMat = new PhysicsMaterial("CoinBounce")
        {
            bounciness = bounciness,
            dynamicFriction = friction,
            staticFriction = friction,
            bounceCombine = PhysicsMaterialCombine.Maximum,
            frictionCombine = PhysicsMaterialCombine.Average
        };
        
        // Setup letterbox UI
        if (enableLetterbox)
        {
            SetupLetterboxUI();
        }
        
        // Store original UI alpha
        if (uiToFade != null)
        {
            originalUIAlpha = uiToFade.alpha;
        }
    }
    
    private void SetupLetterboxUI()
    {
        // Create canvas if not assigned
        if (letterboxCanvas == null)
        {
            GameObject canvasObj = new GameObject("CoinToss_LetterboxCanvas");
            letterboxCanvas = canvasObj.AddComponent<Canvas>();
            
            // Use Screen Space - Camera so 3D objects (coin) can render IN FRONT
            letterboxCanvas.renderMode = RenderMode.ScreenSpaceCamera;
            letterboxCanvas.worldCamera = mainCamera;
            letterboxCanvas.planeDistance = letterboxPlaneDistance;
            letterboxCanvas.sortingOrder = 0;
            
            // Add CanvasScaler for proper scaling
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            
            canvasObj.AddComponent<GraphicRaycaster>();
        }
        else
        {
            // If user provided canvas, ensure it's set up for coin-in-front rendering
            if (letterboxCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                letterboxCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                letterboxCanvas.worldCamera = mainCamera;
                letterboxCanvas.planeDistance = letterboxPlaneDistance;
            }
        }
        
        // Create top bar (start transparent to prevent flash)
        GameObject topBarObj = new GameObject("TopBar");
        topBarObj.transform.SetParent(letterboxCanvas.transform, false);
        topBarImage = topBarObj.AddComponent<Image>();
        topBarImage.color = new Color(letterboxColor.r, letterboxColor.g, letterboxColor.b, 0f); // Start transparent
        topBarImage.raycastTarget = false;
        topBar = topBarObj.GetComponent<RectTransform>();
        
        // Anchor to top, stretch horizontally
        topBar.anchorMin = new Vector2(0, 1);
        topBar.anchorMax = new Vector2(1, 1);
        topBar.pivot = new Vector2(0.5f, 1);
        topBar.anchoredPosition = Vector2.zero;
        topBar.sizeDelta = new Vector2(0, 0);
        
        // Create bottom bar (start transparent to prevent flash)
        GameObject bottomBarObj = new GameObject("BottomBar");
        bottomBarObj.transform.SetParent(letterboxCanvas.transform, false);
        bottomBarImage = bottomBarObj.AddComponent<Image>();
        bottomBarImage.color = new Color(letterboxColor.r, letterboxColor.g, letterboxColor.b, 0f); // Start transparent
        bottomBarImage.raycastTarget = false;
        bottomBar = bottomBarObj.GetComponent<RectTransform>();
        
        // Anchor to bottom, stretch horizontally
        bottomBar.anchorMin = new Vector2(0, 0);
        bottomBar.anchorMax = new Vector2(1, 0);
        bottomBar.pivot = new Vector2(0.5f, 0);
        bottomBar.anchoredPosition = Vector2.zero;
        bottomBar.sizeDelta = new Vector2(0, 0);
        
        // Create dim overlay (fullscreen, behind letterbox bars)
        if (dimDuringZoom)
        {
            GameObject dimObj = new GameObject("DimOverlay");
            dimObj.transform.SetParent(letterboxCanvas.transform, false);
            dimObj.transform.SetAsFirstSibling(); // Behind the bars
            
            dimOverlay = dimObj.AddComponent<Image>();
            dimOverlay.color = new Color(0, 0, 0, 0); // Start transparent
            dimOverlay.raycastTarget = false;
            
            RectTransform dimRect = dimObj.GetComponent<RectTransform>();
            // Fullscreen
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = Vector2.zero;
            dimRect.offsetMax = Vector2.zero;
        }
        
        currentLetterboxAmount = 0f;
    }
    
    private void SetLetterboxAmount(float amount)
    {
        if (topBar == null || bottomBar == null) return;
        
        currentLetterboxAmount = amount;
        float screenHeight = letterboxCanvas != null ? 
            ((RectTransform)letterboxCanvas.transform).rect.height : Screen.height;
        float barHeight = screenHeight * amount;
        
        topBar.sizeDelta = new Vector2(0, barHeight);
        bottomBar.sizeDelta = new Vector2(0, barHeight);
        
        // Fade alpha with size to prevent flash (smooth fade in/out)
        float alpha = Mathf.Clamp01(amount / letterboxSize); // 0 to 1 based on how open
        if (topBarImage != null)
            topBarImage.color = new Color(letterboxColor.r, letterboxColor.g, letterboxColor.b, alpha);
        if (bottomBarImage != null)
            bottomBarImage.color = new Color(letterboxColor.r, letterboxColor.g, letterboxColor.b, alpha);
    }
    
    public void StartCoinToss()
    {
        StartCoinToss(successChance);
    }

    /// <summary>Toss with this chance of success (the main menu raises it after each failed flip).</summary>
    public void StartCoinToss(float chance)
    {
        if (isTossing) return;
        StartCoroutine(TossSequence(Mathf.Clamp01(chance)));
    }

    private IEnumerator TossSequence(float chance)
    {
        isTossing = true;
        hasLandedOnce = false;
        bounceCount = 0;
        lastResult = chance >= 1f || Random.value < chance; // Random.value can return exactly 1
        
        OnTossStarted?.Invoke();
        
        // Trigger hand animation
        if (handAnimator != null && !string.IsNullOrEmpty(handAnimTrigger))
        {
            handAnimator.SetTrigger(handAnimTrigger);
        }
        
        // Wait for hand to wind up
        yield return new WaitForSeconds(spawnDelay);
        
        // Spawn coin
        GameObject prefab = lastResult ? coinPrefabSuccess : coinPrefabFail;
        if (prefab == null)
        {
            Debug.LogError("[MenuCoinToss] No coin prefab!");
            isTossing = false;
            yield break;
        }
        
        Vector3 spawnPos = coinSpawnPoint != null ? coinSpawnPoint.position : transform.position;
        currentCoin = Instantiate(prefab, spawnPos, Quaternion.identity);
        
        // Setup physics components
        coinRb = currentCoin.GetComponent<Rigidbody>();
        coinCollider = currentCoin.GetComponent<Collider>();
        
        if (coinRb != null)
        {
            coinRb.isKinematic = true;
            coinRb.useGravity = false;
            coinRb.interpolation = RigidbodyInterpolation.Interpolate;
            coinRb.collisionDetectionMode = CollisionDetectionMode.Continuous;
        }
        
        // Apply physics material
        if (coinCollider != null)
        {
            coinCollider.material = coinPhysMat;
        }
        
        // Add collision listener for bounce sounds
        CoinCollisionHandler collisionHandler = currentCoin.AddComponent<CoinCollisionHandler>();
        collisionHandler.Initialize(this);
        
        PlaySound(coinFlipSound);
        lastPosition = currentCoin.transform.position;
        lastRotation = currentCoin.transform.rotation;
        
        // Follow hand during throw motion
        float followElapsed = 0f;
        while (followElapsed < followHandTime && coinSpawnPoint != null)
        {
            currentCoin.transform.position = coinSpawnPoint.position;
            followElapsed += Time.deltaTime;
            yield return null;
        }
        
        OnCoinLaunched?.Invoke();
        
        // Calculate landing target
        Vector3 landTarget = landingPoint != null ? landingPoint.position : spawnPos + Vector3.down * 2f;
        landTarget += new Vector3(
            Random.Range(-landingVariance, landingVariance),
            0,
            Random.Range(-landingVariance, landingVariance)
        );
        
        // Animate the toss - physics takes over partway through
        yield return StartCoroutine(AnimateTossWithPhysicsHandoff(currentCoin.transform.position, landTarget));
        
        // Wait for coin to settle
        yield return StartCoroutine(WaitForSettle());
        
        // Final landing effects
        OnCoinLanded?.Invoke();
        PlaySound(coinSettleSound);
        
        if (landingShakeIntensity > 0 && mainCamera != null && !hasLandedOnce)
        {
            StartCoroutine(CameraShake(0.12f, landingShakeIntensity));
        }
        
        // Small pause for dramatic effect
        yield return new WaitForSeconds(0.4f);
        
        // Fire result
        if (lastResult)
            OnSuccess?.Invoke();
        else
            OnFail?.Invoke();
        
        OnComplete?.Invoke();
        isTossing = false;
    }
    
    private IEnumerator AnimateTossWithPhysicsHandoff(Vector3 start, Vector3 end)
    {
        float elapsed = 0f;
        float handoffTime = flightDuration * animatedPortion;
        
        // Rotation setup
        Vector3 flipAxis = (Random.value > 0.5f ? Vector3.right : Vector3.forward).normalized;
        float totalRotation = spinRotations * 360f;
        float wobblePhase = Random.Range(0f, Mathf.PI * 2f);
        
        // Camera/Letterbox effects
        bool doEffects = mainCamera != null;
        bool doZoom = cameraZoomAmount != 1f && doEffects;
        bool doLetterbox = enableLetterbox && topBar != null && bottomBar != null;
        
        // Slow-mo tracking
        float peakTime = handoffTime * 0.5f; // Peak is at 50% of animated portion
        float slowMoStart = peakTime - (slowMoDuration * 0.5f);
        float slowMoEnd = peakTime + (slowMoDuration * 0.5f);
        bool wasInSlowMo = false;
        
        lastPosition = start;
        lastRotation = Quaternion.identity;
        
        while (elapsed < handoffTime)
        {
            // t is normalized to full flight, not just animated portion
            float t = elapsed / flightDuration;
            float tNorm = elapsed / handoffTime; // For easing within animated portion
            
            // --- SLOW MOTION AT PEAK ---
            if (enableSlowMo)
            {
                bool inSlowMoZone = elapsed >= slowMoStart && elapsed <= slowMoEnd;
                
                if (inSlowMoZone)
                {
                    // Smooth slow-mo curve (ease in and out)
                    float slowMoProgress = (elapsed - slowMoStart) / slowMoDuration;
                    float slowMoCurve = Mathf.Sin(slowMoProgress * Mathf.PI); // 0 -> 1 -> 0
                    SetSlowMotion(Mathf.Lerp(1f, slowMoTimeScale, slowMoCurve));
                    wasInSlowMo = true;
                }
                else if (wasInSlowMo)
                {
                    // Exited slow-mo zone, restore time
                    RestoreTime();
                    wasInSlowMo = false;
                }
            }

            // --- POSITION ---
            Vector3 pos = Vector3.Lerp(start, end, t);

            // Parabolic arc
            float heightT = 4f * t * (1f - t);
            pos.y = Mathf.Lerp(start.y, end.y, t) + tossHeight * heightT;

            // Track velocity (for physics handoff); a paused frame has no delta
            if (Time.deltaTime > 0f)
                currentVelocity = (pos - lastPosition) / Time.deltaTime;
            lastPosition = pos;
            
            currentCoin.transform.position = pos;
            
            // --- ROTATION ---
            float spinProgress = (1f - Mathf.Cos(t * Mathf.PI)) / 2f;
            float currentRotationAngle = totalRotation * spinProgress;
            
            Quaternion flipRot = Quaternion.AngleAxis(currentRotationAngle, flipAxis);
            
            // Wobble
            float wobbleAngle = Mathf.Sin(t * Mathf.PI * 4f + wobblePhase) * wobbleAmount * (1f - t * 0.5f);
            Quaternion wobbleRot = Quaternion.Euler(
                Mathf.Sin(t * 7f) * wobbleAngle,
                Mathf.Cos(t * 5f) * wobbleAngle * 0.5f,
                Mathf.Sin(t * 6f + 1f) * wobbleAngle
            );
            
            Quaternion newRot = wobbleRot * flipRot;
            
            // Track angular velocity
            Quaternion deltaRot = newRot * Quaternion.Inverse(lastRotation);
            deltaRot.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            if (Time.deltaTime > 0f && !float.IsInfinity(axis.x) && !float.IsNaN(axis.x))
                currentAngularVelocity = axis * (angle * Mathf.Deg2Rad / Time.deltaTime);
            lastRotation = newRot;
            
            currentCoin.transform.rotation = newRot;
            
            // --- CINEMATIC EFFECTS (SYNCED) ---
            // All effects follow the same curve - in together, out together
            // Using smoothstep for gentler transitions (less flash from bloom)
            float effectT = Mathf.Sin(tNorm * Mathf.PI);
            effectT = effectT * effectT * (3f - 2f * effectT); // Smoothstep - gentler peak
            
            if (doEffects)
            {
                // FOV Zoom (gradual to avoid bloom flash)
                if (doZoom)
                {
                    float targetFOV = originalFOV / cameraZoomAmount;
                    mainCamera.fieldOfView = Mathf.Lerp(originalFOV, targetFOV, effectT);
                }
                
                // Camera moves toward coin
                if (cameraMoveAmount > 0)
                {
                    Vector3 dirToCoin = (pos - originalCamPos).normalized;
                    float distToCoin = Vector3.Distance(originalCamPos, pos);
                    float moveDistance = distToCoin * cameraMoveAmount * effectT;
                    mainCamera.transform.position = originalCamPos + dirToCoin * moveDistance;
                }
                
                // Camera looks at coin (keeps it centered)
                if (cameraTracksCoin)
                {
                    Quaternion targetRot = Quaternion.LookRotation(pos - mainCamera.transform.position);
                    mainCamera.transform.rotation = Quaternion.Slerp(originalCamRot, targetRot, effectT);
                }
            }
            
            if (doLetterbox)
            {
                SetLetterboxAmount(letterboxSize * effectT);
            }
            
            // Dim overlay (reduces bloom flash)
            if (dimOverlay != null)
            {
                dimOverlay.color = new Color(0, 0, 0, dimAmount * effectT);
            }
            
            // Fade out UI during cinematic
            if (uiToFade != null)
            {
                uiToFade.alpha = Mathf.Lerp(originalUIAlpha, uiFadeMinAlpha, effectT);
            }
            
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        // --- PHYSICS HANDOFF ---
        OnPhysicsStart?.Invoke();

        // Ensure time is restored
        RestoreTime();
        
        // Ensure all effects are reset
        if (doEffects)
        {
            mainCamera.fieldOfView = originalFOV;
            mainCamera.transform.position = originalCamPos;
            mainCamera.transform.rotation = originalCamRot;
        }
        if (doLetterbox) SetLetterboxAmount(0f);
        if (dimOverlay != null) dimOverlay.color = new Color(0, 0, 0, 0);
        if (uiToFade != null) uiToFade.alpha = originalUIAlpha;
        
        if (coinRb != null)
        {
            coinRb.isKinematic = false;
            coinRb.useGravity = true;
            coinRb.angularDamping = angularDrag;
            
            // Apply tracked velocity (coin continues its arc naturally)
            coinRb.linearVelocity = currentVelocity;
            
            // Apply angular velocity (maintains spin direction and speed)
            Vector3 clampedAngular = Vector3.ClampMagnitude(currentAngularVelocity, 30f);
            coinRb.angularVelocity = clampedAngular;
            
            // Apply extra gravity for snappier feel
            if (gravityScale != 1f)
            {
                StartCoroutine(ApplyExtraGravity());
            }
        }
    }
    
    private IEnumerator ApplyExtraGravity()
    {
        while (coinRb != null && !coinRb.isKinematic && coinRb.linearVelocity.magnitude > 0.1f)
        {
            coinRb.AddForce(Physics.gravity * (gravityScale - 1f), ForceMode.Acceleration);
            yield return new WaitForFixedUpdate();
        }
    }
    
    private IEnumerator WaitForSettle()
    {
        float maxWait = 5f;
        float elapsed = 0f;
        float settleThreshold = 0.01f;
        float settleTime = 0f;
        float requiredSettleTime = 0.3f;
        
        yield return new WaitForSeconds(0.1f);
        elapsed = 0.1f;
        
        while (elapsed < maxWait && coinRb != null)
        {
            bool isSettled = coinRb.linearVelocity.sqrMagnitude < settleThreshold &&
                            coinRb.angularVelocity.sqrMagnitude < settleThreshold;
            
            if (isSettled)
            {
                settleTime += Time.deltaTime;
                if (settleTime >= requiredSettleTime)
                    break;
            }
            else
            {
                settleTime = 0f;
            }
            
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        // Ensure coin is fully stopped
        if (coinRb != null)
        {
            coinRb.linearVelocity = Vector3.zero;
            coinRb.angularVelocity = Vector3.zero;
            coinRb.isKinematic = true;
        }
    }
    
    // Called by CoinCollisionHandler
    public void OnCoinCollision(Collision collision, float impactForce)
    {
        if (!hasLandedOnce)
        {
            hasLandedOnce = true;
            if (landingShakeIntensity > 0 && mainCamera != null)
            {
                StartCoroutine(CameraShake(0.1f, landingShakeIntensity * Mathf.Clamp01(impactForce / 5f)));
            }
        }
        
        bounceCount++;
        
        // Play bounce sound with volume based on impact
        if (coinBounceSound != null && impactForce > 0.5f)
        {
            float volume = Mathf.Clamp(impactForce / 8f, 0.1f, 0.7f);
            PlaySoundWithVolume(coinBounceSound, volume);
        }
    }
    
    private IEnumerator CameraShake(float duration, float intensity)
    {
        float elapsed = 0f;
        Vector3 shakeStartPos = mainCamera.transform.position;
        
        while (elapsed < duration)
        {
            float t = elapsed / duration;
            float falloff = 1f - t;
            
            // Shake in camera's local space (right/up directions)
            Vector3 offset = (mainCamera.transform.right * Random.Range(-1f, 1f) + 
                             mainCamera.transform.up * Random.Range(-1f, 1f)) * intensity * falloff;
            
            mainCamera.transform.position = shakeStartPos + offset;
            
            elapsed += Time.deltaTime;
            yield return null;
        }
        mainCamera.transform.position = shakeStartPos;
    }
    
    private void PlaySound(AudioClip clip)
    {
        PlaySoundWithVolume(clip, 1f);
    }

    private void PlaySoundWithVolume(AudioClip clip, float volume)
    {
        if (clip == null) return;
        volume *= GameSettings.SfxVolume;
        if (audioSource != null)
            audioSource.PlayOneShot(clip, volume);
        else if (mainCamera != null)
            AudioSource.PlayClipAtPoint(clip, mainCamera.transform.position, volume);
    }

    /// <summary>Slow time down for the peak of the toss (scales the physics step with it).</summary>
    private void SetSlowMotion(float scale)
    {
        scale = Mathf.Clamp(scale, 0.01f, 1f);
        Time.timeScale = scale;
        Time.fixedDeltaTime = baseFixedDeltaTime * scale;
        slowMoActive = true;
    }

    /// <summary>Put time back to normal if the toss slowed it down (no-op otherwise, so it never un-pauses UI).</summary>
    private void RestoreTime()
    {
        if (!slowMoActive) return;
        slowMoActive = false;
        Time.timeScale = 1f;
        Time.fixedDeltaTime = baseFixedDeltaTime;
    }

    /// <summary>Camera, letterbox, dim overlay and faded UI back to their normal state.</summary>
    private void ResetCinematicEffects()
    {
        if (mainCamera != null)
        {
            mainCamera.fieldOfView = originalFOV;
            mainCamera.transform.position = originalCamPos;
            mainCamera.transform.rotation = originalCamRot;
        }

        if (topBar != null) topBar.sizeDelta = new Vector2(0, 0);
        if (bottomBar != null) bottomBar.sizeDelta = new Vector2(0, 0);
        if (topBarImage != null) topBarImage.color = new Color(letterboxColor.r, letterboxColor.g, letterboxColor.b, 0f);
        if (bottomBarImage != null) bottomBarImage.color = new Color(letterboxColor.r, letterboxColor.g, letterboxColor.b, 0f);
        if (dimOverlay != null) dimOverlay.color = new Color(0, 0, 0, 0);
        if (uiToFade != null) uiToFade.alpha = originalUIAlpha;
        currentLetterboxAmount = 0f;
    }
    
    public void CleanupCoin()
    {
        if (currentCoin != null)
        {
            Destroy(currentCoin);
            currentCoin = null;
        }
    }
    
    public void ForceStop()
    {
        StopAllCoroutines();

        // Reset time, camera, letterbox and UI fade
        RestoreTime();
        ResetCinematicEffects();

        CleanupCoin();
        isTossing = false;
    }

    private void OnDisable()
    {
        // Coroutines stop with the component: never leave the game in slow motion or zoomed in
        if (isTossing || slowMoActive)
        {
            RestoreTime();
            ResetCinematicEffects();
            isTossing = false;
        }
    }

    private void OnDestroy()
    {
        // Ensure time is restored (only if the toss changed it)
        RestoreTime();

        if (mainCamera != null)
        {
            mainCamera.fieldOfView = originalFOV;
            mainCamera.transform.position = originalCamPos;
            mainCamera.transform.rotation = originalCamRot;
        }

        if (coinPhysMat != null)
        {
            Destroy(coinPhysMat);
        }
        
        // Cleanup letterbox canvas if we created it
        if (letterboxCanvas != null && letterboxCanvas.name == "CoinToss_LetterboxCanvas")
        {
            Destroy(letterboxCanvas.gameObject);
        }
        
        CleanupCoin();
    }
    
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 spawn = coinSpawnPoint != null ? coinSpawnPoint.position : transform.position;
        Vector3 land = landingPoint != null ? landingPoint.position : spawn + Vector3.down * 2f;
        
        // Draw full arc
        Gizmos.color = Color.yellow;
        Vector3 prev = spawn;
        for (int i = 1; i <= 40; i++)
        {
            float t = i / 40f;
            float heightT = 4f * t * (1f - t);
            Vector3 pos = Vector3.Lerp(spawn, land, t);
            pos.y = Mathf.Lerp(spawn.y, land.y, t) + tossHeight * heightT;
            
            // Color changes at physics handoff point
            Gizmos.color = t < animatedPortion ? Color.yellow : Color.cyan;
            Gizmos.DrawLine(prev, pos);
            prev = pos;
        }
        
        // Handoff point
        float ht = animatedPortion;
        float hHeightT = 4f * ht * (1f - ht);
        Vector3 handoffPos = Vector3.Lerp(spawn, land, ht);
        handoffPos.y = Mathf.Lerp(spawn.y, land.y, ht) + tossHeight * hHeightT;
        
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(handoffPos, 0.08f);
        
        // Points
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(spawn, 0.05f);
        Gizmos.DrawWireSphere(land, 0.1f);
        
        Vector3 peak = Vector3.Lerp(spawn, land, 0.5f) + Vector3.up * tossHeight;
        Gizmos.color = new Color(1f, 1f, 0f, 0.5f);
        Gizmos.DrawWireSphere(peak, 0.06f);
        
        // Labels
        UnityEditor.Handles.Label(spawn, "Start");
        UnityEditor.Handles.Label(peak, "Peak");
        UnityEditor.Handles.Label(handoffPos, "Physics");
        UnityEditor.Handles.Label(land, "Land");
    }
#endif
}
