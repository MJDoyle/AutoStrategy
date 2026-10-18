using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

public class ShopSlotUI : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    public int slotIndex;

    private Image background;
    private Image unitImage;
    private TextMeshProUGUI nameText;
    private TextMeshProUGUI costText;

    static readonly Color SlotNormal  = new Color(0.22f, 0.16f, 0.10f);
    static readonly Color SlotHover   = new Color(0.32f, 0.24f, 0.14f);
    static readonly Color SlotEmpty   = new Color(0.14f, 0.10f, 0.07f);
    static readonly Color SlotBought  = new Color(0.12f, 0.12f, 0.12f);

    public void Build(int index, int minH = 120)
    {
        slotIndex = index;
        var le = gameObject.AddComponent<LayoutElement>();
        le.minWidth       = 100;
        le.minHeight      = minH;
        le.preferredWidth = 100;
        le.preferredHeight = minH;

        background = gameObject.AddComponent<Image>();
        background.color = SlotEmpty;

        var outline = gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.5f, 0.35f, 0.1f, 0.6f);
        outline.effectDistance = new Vector2(1, 1);

        // Unit portrait
        var imgGO = new GameObject("UnitImage", typeof(RectTransform));
        imgGO.transform.SetParent(transform, false);
        var imgRT = imgGO.GetComponent<RectTransform>();
        imgRT.anchorMin = new Vector2(0.1f, 0.35f);
        imgRT.anchorMax = new Vector2(0.9f, 0.95f);
        imgRT.offsetMin = Vector2.zero;
        imgRT.offsetMax = Vector2.zero;
        unitImage = imgGO.AddComponent<Image>();
        unitImage.preserveAspect = true;
        unitImage.color = Color.clear;
        unitImage.raycastTarget = false;

        // Unit name
        var nameGO = new GameObject("Name", typeof(RectTransform));
        nameGO.transform.SetParent(transform, false);
        var nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0, 0.18f);
        nameRT.anchorMax = new Vector2(1, 0.38f);
        nameRT.offsetMin = new Vector2(4, 0);
        nameRT.offsetMax = new Vector2(-4, 0);
        nameText = nameGO.AddComponent<TextMeshProUGUI>();
        nameText.fontSize = 10;
        nameText.alignment = TextAlignmentOptions.Center;
        nameText.color = Color.white;
        nameText.text = "";

        // Cost
        var costGO = new GameObject("Cost", typeof(RectTransform));
        costGO.transform.SetParent(transform, false);
        var costRT = costGO.GetComponent<RectTransform>();
        costRT.anchorMin = new Vector2(0, 0f);
        costRT.anchorMax = new Vector2(1, 0.20f);
        costRT.offsetMin = new Vector2(4, 2);
        costRT.offsetMax = new Vector2(-4, 0);
        costText = costGO.AddComponent<TextMeshProUGUI>();
        costText.fontSize = 11;
        costText.fontStyle = FontStyles.Bold;
        costText.alignment = TextAlignmentOptions.Center;
        costText.color = new Color(1f, 0.82f, 0.1f);
        costText.text = "";
    }

    public void Refresh(UnitDefinition def, int playerGold)
    {
        if (def == null)
        {
            background.color = SlotBought;
            unitImage.color = Color.clear;
            nameText.text = "";
            costText.text = "";
            return;
        }

        background.color = SlotNormal;
        unitImage.sprite = def.idleSprite;
        unitImage.color = def.idleSprite != null ? Color.white : Color.clear;
        nameText.text = def.unitName;
        costText.text = $"★ {def.cost}g";

        bool canAfford = playerGold >= def.cost;
        nameText.color = canAfford ? Color.white : new Color(0.5f, 0.5f, 0.5f);
        costText.color = canAfford ? new Color(1f, 0.82f, 0.1f) : new Color(0.5f, 0.4f, 0.1f);
    }

    public void OnPointerClick(PointerEventData e)
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        if (gm.PendingUnitSlot == slotIndex)
            gm.CancelPlacement();
        else
            gm.StartUnitPlacement(slotIndex);
    }
    public void OnPointerEnter(PointerEventData e) { if (background != null) background.color = SlotHover; }
    public void OnPointerExit(PointerEventData e)
    {
        if (background != null && GameManager.Instance?.Shop.slots[slotIndex] != null)
            background.color = SlotNormal;
    }
}
