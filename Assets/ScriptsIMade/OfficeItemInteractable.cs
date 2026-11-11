using UnityEngine;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections.Generic;

/// <summary>
/// An upgradeable office item (e.g. the sofa). Hover highlights it; a left click or the interact key
/// opens the upgrade choice (once per click). Ignored while typing, over UI, or while any panel,
/// mini-game or risk event is active. UIManager disables this component while a panel is open.
/// </summary>
public class OfficeItemInteractable : MonoBehaviour
{
    [Header("Item Data")]
    public OfficeItem itemData = new OfficeItem();

    [Header("Collider Setup")]
    [Tooltip("If empty, colliders in children are found automatically (and found again when the level visual changes). Assign specific colliders if needed.")]
    public Collider[] interactionColliders;     // Colliders that trigger hover (can be on children)

    [Header("Visual Feedback")]
    public GameObject highlightEffect;          // Optional glow/outline when hovering (legacy)
    [Tooltip("Visual per level (index = level). Empty slots and levels past the end use the highest visual below.")]
    public GameObject[] levelVisuals;           // Different visuals for each level (optional)

    [Header("Material Highlight")]
    public bool useMaterialHighlight = true;    // Use material color change for highlight
    public Color highlightColor = new Color(1.2f, 1.2f, 1.2f, 1f);  // Brighter color multiplier
    public float highlightEmission = 0.3f;      // Emission intensity when highlighted
    public bool includeChildren = true;         // Also highlight child renderers

    [Header("Hover UI")]
    public GameObject hoverUI;                  // World-space canvas showing item info
    public TextMeshProUGUI hoverNameText;
    public TextMeshProUGUI hoverLevelText;
    public TextMeshProUGUI hoverBonusText;

    [Header("Interaction")]
    public KeyCode interactKey = KeyCode.E;
    public float interactionRange = 50f;
    public bool useMouseHover = true;

    private bool isHovering = false;
    private Camera mainCamera;
    private readonly HashSet<Collider> registeredColliders = new HashSet<Collider>();
    private bool collidersInitialized = false;
    private bool autoFindColliders = false;
    private bool warnedNoColliders = false;
    private int lastInteractFrame = -1;

    // Material highlight: each renderer's materials are instanced once and remembered
    class MaterialState
    {
        public Material material;
        public string colorProperty;    // main colour property, or null
        public Color color;
        public bool hasEmission;        // legacy/URP "_EmissionColor"
        public Color emission;
        public bool emissionKeyword;
        public bool hasEmissive;        // HDRP "_EmissiveColor"
        public Color emissive;
        public bool hasExposureWeight;  // HDRP "_EmissiveExposureWeight"
        public float exposureWeight;
    }
    private readonly List<MaterialState> highlightMaterials = new List<MaterialState>();
    private readonly HashSet<Renderer> cachedRenderers = new HashSet<Renderer>();
    private bool highlightApplied = false;

    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    static readonly int EmissiveColorId = Shader.PropertyToID("_EmissiveColor");
    static readonly int EmissiveExposureWeightId = Shader.PropertyToID("_EmissiveExposureWeight");

    /// <summary>True while the pointer is over this item.</summary>
    public bool IsHovering => isHovering;

    /// <summary>The item under the pointer, or null (the designed hover tag follows it).</summary>
    public static OfficeItemInteractable Hovered { get; private set; }

    /// <summary>Set while the designed hover tag (HoverTagView) is in the scene: the world-space hoverUI then stays hidden.</summary>
    public static bool UseDesignedHoverTag;

    void Start()
    {
        mainCamera = Camera.main;

        // Hide hover UI at start
        if (hoverUI != null) hoverUI.SetActive(false);
        if (highlightEffect != null) highlightEffect.SetActive(false);

        // Initialize item data if not set
        if (itemData == null)
            itemData = new OfficeItem();
        if (string.IsNullOrEmpty(itemData.itemName))
            itemData.itemName = gameObject.name;

        UpdateVisuals();
        SetupColliders();
    }

    void OnDisable()
    {
        // UIManager disables items while a panel is open: drop the hover state
        if (gameObject.activeInHierarchy)
        {
            EndHover();
        }
        else
        {
            // The whole object is being deactivated: don't toggle children now
            isHovering = false;
            if (Hovered == this) Hovered = null;
            ApplyMaterialHighlight(false);
        }
    }

    void SetupColliders()
    {
        if (!collidersInitialized)
        {
            // Nothing assigned: find colliders in children now and whenever the visuals change
            autoFindColliders = interactionColliders == null || interactionColliders.Length == 0;
            collidersInitialized = true;
        }

        if (autoFindColliders)
            interactionColliders = GetComponentsInChildren<Collider>();

        registeredColliders.Clear();

        // Register all colliders and add helper components
        foreach (Collider col in interactionColliders)
        {
            if (col == null) continue;

            registeredColliders.Add(col);

            // Add a helper component to relay mouse events back to this script
            OfficeItemColliderHelper helper = col.gameObject.GetComponent<OfficeItemColliderHelper>();
            if (helper == null)
            {
                helper = col.gameObject.AddComponent<OfficeItemColliderHelper>();
            }
            helper.parentInteractable = this;
        }

        if (registeredColliders.Count == 0 && !warnedNoColliders)
        {
            warnedNoColliders = true;
            Debug.LogWarning($"OfficeItemInteractable on '{gameObject.name}' has no colliders! Add colliders to children or assign them manually.");
        }
    }

    /// <summary>Instance and remember the materials of renderers not seen yet (done lazily, before highlighting).</summary>
    void CacheRenderers()
    {
        // Get renderers from this object and optionally children
        Renderer[] renderers;
        if (includeChildren)
            renderers = GetComponentsInChildren<Renderer>();
        else
            renderers = GetComponents<Renderer>();

        foreach (Renderer rend in renderers)
        {
            if (rend == null || cachedRenderers.Contains(rend)) continue;

            // Skip particle systems, sprites and 3D text
            if (rend is SpriteRenderer || rend is ParticleSystemRenderer || rend.GetComponent<TMP_Text>() != null)
                continue;

            cachedRenderers.Add(rend);

            // Accessing .materials instances them for this renderer; do it once and keep them
            foreach (Material mat in rend.materials)
            {
                if (mat == null) continue;

                var state = new MaterialState { material = mat };

                if (mat.HasProperty("_BaseColor")) state.colorProperty = "_BaseColor";
                else if (mat.HasProperty("_Color")) state.colorProperty = "_Color";
                else if (mat.HasProperty("_UnlitColor")) state.colorProperty = "_UnlitColor";
                if (state.colorProperty != null) state.color = mat.GetColor(state.colorProperty);

                state.hasEmission = mat.HasProperty(EmissionColorId);
                if (state.hasEmission)
                {
                    state.emission = mat.GetColor(EmissionColorId);
                    state.emissionKeyword = mat.IsKeywordEnabled("_EMISSION");
                }

                state.hasEmissive = mat.HasProperty(EmissiveColorId);
                if (state.hasEmissive) state.emissive = mat.GetColor(EmissiveColorId);

                state.hasExposureWeight = mat.HasProperty(EmissiveExposureWeightId);
                if (state.hasExposureWeight) state.exposureWeight = mat.GetFloat(EmissiveExposureWeightId);

                highlightMaterials.Add(state);
            }
        }
    }

    void Update()
    {
        if (!useMouseHover)
        {
            // Raycast-based hover
            CheckHoverRaycast();
        }
        else if (isHovering && !HoverAllowed())
        {
            // The pointer moved onto UI covering the item, or a mini-game/event/typing started
            EndHover();
        }

        if (isHovering)
        {
            // One interaction per click / key press
            if (Input.GetMouseButtonDown(0))
                TryInteract(true);
            else if (Input.GetKeyDown(interactKey))
                TryInteract(false);
        }

        // Make hover UI face camera
        if (hoverUI != null && hoverUI.activeSelf)
        {
            Camera cam = mainCamera != null ? mainCamera : Camera.main;
            if (cam != null)
            {
                hoverUI.transform.LookAt(hoverUI.transform.position + cam.transform.rotation * Vector3.forward,
                                          cam.transform.rotation * Vector3.up);
            }
        }
    }

    static bool IsPointerOverUI()
    {
        return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
    }

    /// <summary>Hover is allowed when gameplay input is (no typing, panel, mini-game or event) and the pointer isn't on UI.</summary>
    bool HoverAllowed()
    {
        return isActiveAndEnabled && UIManager.GameplayInputAllowed() && !IsPointerOverUI();
    }

    // Raycast-based hover check (for first-person style games)
    void CheckHoverRaycast()
    {
        Camera cam = mainCamera != null ? mainCamera : Camera.main;
        if (cam == null) return;

        bool hovering = false;
        if (HoverAllowed())
        {
            // Raycast from mouse position
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, interactionRange))
                hovering = registeredColliders.Contains(hit.collider) || hit.collider.transform.IsChildOf(transform);
        }

        // Update UI on hover change
        if (hovering && !isHovering)
            BeginHover();
        else if (!hovering && isHovering)
            EndHover();
    }

    void BeginHover()
    {
        if (isHovering) return;
        isHovering = true;
        Hovered = this;

        // Show legacy highlight effect
        if (highlightEffect != null) highlightEffect.SetActive(true);

        // Apply material highlight
        if (useMaterialHighlight)
        {
            CacheRenderers();
            ApplyMaterialHighlight(true);
        }

        // Show and update hover UI
        if (hoverUI != null && !UseDesignedHoverTag)
        {
            hoverUI.SetActive(true);
            UpdateHoverUI();
        }
    }

    void EndHover()
    {
        isHovering = false;
        if (Hovered == this) Hovered = null;

        // Hide legacy highlight effect
        if (highlightEffect != null) highlightEffect.SetActive(false);

        // Remove material highlight
        ApplyMaterialHighlight(false);

        // Hide hover UI
        if (hoverUI != null) hoverUI.SetActive(false);
    }

    void ApplyMaterialHighlight(bool highlight)
    {
        if (highlight == highlightApplied) return;
        highlightApplied = highlight;

        foreach (MaterialState s in highlightMaterials)
        {
            Material mat = s.material;
            if (mat == null) continue;

            if (highlight)
            {
                // Brighten the color
                if (s.colorProperty != null)
                {
                    Color brightened = s.color * highlightColor;
                    brightened.a = s.color.a; // Keep original alpha
                    mat.SetColor(s.colorProperty, brightened);
                }

                // Add emission for glow effect (legacy/URP and HDRP property names)
                Color baseColor = s.colorProperty != null ? s.color : Color.white;
                Color glow = baseColor * highlightEmission;
                glow.a = 1f;
                if (s.hasEmission)
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor(EmissionColorId, glow);
                }
                if (s.hasEmissive)
                {
                    mat.SetColor(EmissiveColorId, glow);
                    // HDRP: show the glow regardless of camera exposure
                    if (s.hasExposureWeight) mat.SetFloat(EmissiveExposureWeightId, 0f);
                }
            }
            else
            {
                // Restore original color
                if (s.colorProperty != null)
                    mat.SetColor(s.colorProperty, s.color);

                // Restore original emission
                if (s.hasEmission)
                {
                    mat.SetColor(EmissionColorId, s.emission);
                    if (!s.emissionKeyword)
                        mat.DisableKeyword("_EMISSION");
                }
                if (s.hasEmissive)
                {
                    mat.SetColor(EmissiveColorId, s.emissive);
                    if (s.hasExposureWeight) mat.SetFloat(EmissiveExposureWeightId, s.exposureWeight);
                }
            }
        }
    }

    void UpdateHoverUI()
    {
        if (itemData == null) return;

        if (hoverNameText != null)
            hoverNameText.text = itemData.itemName;

        if (hoverLevelText != null)
        {
            if (itemData.CanUpgrade())
                hoverLevelText.text = itemData.GetCurrentLevelName() + " (Lvl " + itemData.currentLevel + "/" + itemData.maxLevel + ")\n[Click or Press " + interactKey + " to Upgrade]";
            else
                hoverLevelText.text = itemData.GetCurrentLevelName() + " (MAX LEVEL)";
        }

        if (hoverBonusText != null)
        {
            float dailyBonus = itemData.GetTotalDailyBonus();
            float prodBonus = itemData.GetTotalProductivityBonus();
            hoverBonusText.text = "+$" + dailyBonus.ToString("N0") + "/day | +" + (prodBonus * 100f).ToString("0") + "% productivity";
        }
    }

    void TryInteract(bool fromMouse)
    {
        // Both the click and the key can't fire twice in one frame
        if (lastInteractFrame == Time.frameCount) return;
        if (!UIManager.GameplayInputAllowed()) return;
        if (fromMouse && IsPointerOverUI()) return;

        lastInteractFrame = Time.frameCount;
        Interact();
    }

    /// <summary>Open the upgrade choice for this item (if it can still be upgraded).</summary>
    public void Interact()
    {
        if (itemData == null) return;

        if (!itemData.CanUpgrade())
        {
            Debug.Log(itemData.itemName + " is already at max level!");
            return;
        }

        // Open upgrade choice panel
        if (OfficeUpgradeManager.Instance != null)
        {
            OfficeUpgradeManager.Instance.ShowUpgradeChoice(this);
        }
        else
        {
            Debug.LogWarning("OfficeUpgradeManager.Instance is null! Make sure you have an OfficeUpgradeManager in the scene.");
        }
    }

    /// <summary>Call after itemData.currentLevel changed (upgrade, downgrade or loading a save).</summary>
    public void OnUpgraded()
    {
        UpdateVisuals();
        UpdateHoverUI();
        Debug.Log(itemData.itemName + " is now level " + itemData.currentLevel);
    }

    void UpdateVisuals()
    {
        if (itemData == null) return;

        // Swap visuals without a highlight baked into the "original" colours
        bool wasHighlighted = highlightApplied;
        if (wasHighlighted) ApplyMaterialHighlight(false);

        // Update level-specific visuals if available: show exactly one
        GameObject shown = GetVisualForLevel(itemData.currentLevel);
        if (shown != null)
        {
            foreach (GameObject visual in levelVisuals)
            {
                if (visual != null)
                    visual.SetActive(visual == shown);
            }
        }

        // Re-setup colliders since active children may have changed
        if (collidersInitialized)
            SetupColliders();

        if (wasHighlighted && isHovering && useMaterialHighlight)
        {
            CacheRenderers();
            ApplyMaterialHighlight(true);
        }
    }

    /// <summary>
    /// The visual for a level: the highest assigned one at or below it, else the lowest one above.
    /// Null if no visuals are assigned (then nothing is changed).
    /// </summary>
    GameObject GetVisualForLevel(int level)
    {
        if (levelVisuals == null || levelVisuals.Length == 0) return null;

        for (int i = Mathf.Min(level, levelVisuals.Length - 1); i >= 0; i--)
        {
            if (levelVisuals[i] != null) return levelVisuals[i];
        }
        for (int i = Mathf.Max(0, level + 1); i < levelVisuals.Length; i++)
        {
            if (levelVisuals[i] != null) return levelVisuals[i];
        }
        return null;
    }

    // Called by OfficeItemColliderHelper on child colliders
    public void OnChildMouseEnter()
    {
        if (useMouseHover && !isHovering && HoverAllowed())
            BeginHover();
    }

    // Also sent every frame while over the item, so hover resumes after a panel closes or the pointer leaves UI
    public void OnChildMouseOver()
    {
        if (useMouseHover && !isHovering && HoverAllowed())
            BeginHover();
    }

    public void OnChildMouseExit()
    {
        if (useMouseHover && isHovering)
            EndHover();
    }

    // Clean up materials when destroyed
    void OnDestroy()
    {
        // Restore original colors
        ApplyMaterialHighlight(false);
        if (Hovered == this) Hovered = null;
    }

    // Re-register colliders when visuals change (in case new colliders appeared)
    public void RefreshColliders()
    {
        SetupColliders();
    }

    // Draw interaction range in editor
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactionRange);
    }
}
