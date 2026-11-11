using UnityEngine;

/// <summary>
/// Everything the HQ tower offers and costs: rooms, floor and furniture prices, the furniture gambles, the seat penalty
/// and selling. Assign an asset to TowerBuilder.catalog to tune the numbers; without one the builder uses
/// TowerRules.CreateDefault(). Room ids are stored in save files.
/// </summary>
[CreateAssetMenu(fileName = "TowerCatalog", menuName = "Office/HQ Tower Catalog")]
public class TowerCatalog : ScriptableObject
{
    public TowerRules rules = TowerRules.CreateDefault();

    /// <summary>A catalog with the default rules (not saved as an asset).</summary>
    public static TowerCatalog CreateDefault()
    {
        TowerCatalog catalog = CreateInstance<TowerCatalog>();
        catalog.name = "Default Tower Catalog (runtime)";
        catalog.hideFlags = HideFlags.DontSave;
        catalog.rules = TowerRules.CreateDefault();
        return catalog;
    }

    [ContextMenu("Reset to the default rooms and prices")]
    void ResetToDefaults()
    {
        rules = TowerRules.CreateDefault();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }
}
