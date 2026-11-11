using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// An index card in the campus's catalogs (the "Rooms" and "Campus" pages of the left drawer): icon box in the room's
/// colour, name and price, what it does and its width (with its upkeep). Locked ones show why (dashed, greyed): a skill, the
/// company's progress, another room first, or a limit reached. TowerView clones a template per room, building type and
/// link type. Names, widths and reasons are in the current language (TowerText); a language change binds it again.
/// </summary>
public class TowerCatalogCard : MonoBehaviour
{
    public Button button;
    public BlueprintBox background;
    [Tooltip("The index card's ruled lines (hidden while locked)")]
    public Graphic lines;
    public BlueprintBox iconBox;
    public TowerIcon icon;
    public RectTransform lockBadge;
    public TextMeshProUGUI nameText;
    public TextMeshProUGUI costText;
    public TextMeshProUGUI effectText;
    public TextMeshProUGUI tilesText;
    public TextMeshProUGUI reasonText;
    [Tooltip("Card height when open / locked (the design: 90 / 102)")]
    public float openHeight = 90f;
    public float lockedHeight = 102f;

    /// <summary>The room type this card shows (Rooms page).</summary>
    [NonSerialized] public TowerRoomDefinition definition;
    /// <summary>The building type this card shows (Campus page).</summary>
    [NonSerialized] public TowerBuildingType buildingType;
    /// <summary>The link type this card shows (Campus page).</summary>
    [NonSerialized] public TowerConnectionType connectionType;

    int shownKey;

    /// <summary>A room card with the room's own effect line and width (the design's card).</summary>
    public void Bind(TowerRoomDefinition room, bool locked, bool affordable, bool picked, string effect)
    {
        Bind(room, locked, affordable, picked, effect, room != null ? TowerText.Get("tower.catalog.tiles", ("count", room.Tiles)) : "",
             room != null ? TowerText.LockReason(room) : "");
    }

    /// <summary>
    /// A room card: <paramref name="right"/> goes under the price (its width and upkeep), <paramref name="reason"/> replaces
    /// the effect line while it is locked (what it needs, or the limit it reached).
    /// </summary>
    public void Bind(TowerRoomDefinition room, bool locked, bool affordable, bool picked, string effect, string right, string reason,
                     double price = -1.0)
    {
        if (room != null && price < 0.0) price = room.cost;   // the shown price: the plan's (discounted) one when given
        int key;
        unchecked { key = Key(room, locked, affordable, picked, effect, right, reason) * 31 + price.GetHashCode(); }
        if (key == shownKey && definition == room) return;
        shownKey = key;
        definition = room;
        buildingType = null;
        connectionType = null;
        if (room == null) return;
        Fill(TowerText.RoomName(room), UIFormat.Money(price), effect, right, reason, locked, affordable, picked,
             TowerArt.IconFor(room.role), room.paper, room.ink);
    }

    /// <summary>A card for anything else (a building type, a link type): the words, price and icon come ready.</summary>
    public void BindCustom(string name, double cost, string effect, string right, string reason, bool locked, bool affordable, bool picked,
                           TowerIconKind iconKind, Color paper, Color ink, bool fromPrice = false)
    {
        string price = fromPrice ? TowerText.Get("tower.catalog.from", ("price", UIFormat.Money(cost))) : UIFormat.Money(cost);
        int key;
        unchecked
        {
            key = Key(null, locked, affordable, picked, effect, right, reason) * 31 + (name != null ? name.GetHashCode() : 0);
            key = key * 31 + price.GetHashCode() + (int)iconKind;
        }
        if (key == shownKey && definition == null) return;
        shownKey = key;
        definition = null;
        Fill(name, price, effect, right, reason, locked, affordable, picked, iconKind, paper, ink);
    }

    void Fill(string name, string price, string effect, string right, string reason, bool locked, bool affordable, bool picked,
              TowerIconKind iconKind, Color paper, Color ink)
    {
        if (nameText != null) nameText.text = name;
        if (costText != null)
        {
            costText.text = price;
            costText.color = locked ? UIColors.Pencil : affordable ? UIColors.Ink : UIColors.Loss;
        }
        if (effectText != null) { effectText.text = effect; effectText.gameObject.SetActive(!locked); }
        if (tilesText != null) { tilesText.text = right; tilesText.gameObject.SetActive(!locked && !string.IsNullOrEmpty(right)); }
        if (reasonText != null) { reasonText.text = reason; reasonText.gameObject.SetActive(locked); }
        if (lockBadge != null) lockBadge.gameObject.SetActive(locked);
        if (lines != null) lines.gameObject.SetActive(!locked);
        if (nameText != null) nameText.color = locked ? UIColors.Pencil : UIColors.Ink;

        if (background != null)
        {
            background.SetStyle(locked ? UIColors.PaperShade : UIColors.PaperHi, locked ? UIColors.Pencil : UIColors.Ink, locked ? 3f : 2f);
            background.Dashed = locked;
            background.SetShadow(locked || picked ? Color.clear : BlueprintUI.InkShadow, new Vector2(3f, 4f));
            background.SetRings(picked && !locked ? UIColors.Highlight : Color.clear, picked ? 5f : 0f,
                                picked && !locked ? UIColors.Ink : Color.clear, picked ? 2f : 0f);
        }
        if (iconBox != null)
        {
            iconBox.SetStyle(locked ? UIColors.Paper : paper, locked ? UIColors.Pencil : UIColors.Ink, 2f);
            iconBox.Dashed = locked;
        }
        if (icon != null)
        {
            icon.Kind = iconKind;
            icon.SetColor(locked ? UIColors.Pencil : ink);
        }
        LayoutElement layout = GetComponent<LayoutElement>();
        float height = locked ? lockedHeight : openHeight;
        if (layout != null && layout.preferredHeight != height)
        {
            layout.preferredHeight = height;
            layout.minHeight = height;
        }
        // A locked card stays clickable: the click says what unlocks it
    }

    static int Key(TowerRoomDefinition room, bool locked, bool affordable, bool picked, string effect, string right, string reason)
    {
        unchecked
        {
            int k = room != null ? room.GetHashCode() : 1;
            k = k * 31 + (locked ? 1 : 0);
            k = k * 31 + (affordable ? 1 : 0);
            k = k * 31 + (picked ? 1 : 0);
            k = k * 31 + (effect != null ? effect.GetHashCode() : 0);
            k = k * 31 + (right != null ? right.GetHashCode() : 0);
            k = k * 31 + (reason != null ? reason.GetHashCode() : 0);
            k = k * 31 + TowerText.LanguageStamp;   // the name, width and reason follow the language
            return k == 0 ? 1 : k;
        }
    }
}
