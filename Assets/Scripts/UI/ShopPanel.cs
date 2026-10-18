using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ShopPanel : MonoBehaviour
{
    private ShopSlotUI[]     slotUIs   = new ShopSlotUI[ShopManager.ShopSize];
    private TextMeshProUGUI  goldLabel;
    private CanvasGroup      canvasGroup;

    // Building shop
    private BuildingDefinition[] buildingDefs;
    private TextMeshProUGUI[]    buildingCostLabels;

    void Start()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        canvasGroup = gameObject.AddComponent<CanvasGroup>();

        var bg = GetComponent<Image>() ?? gameObject.AddComponent<Image>();
        bg.color = new Color(0.18f, 0.12f, 0.07f);

        // ── Top row: gold + unit slots + reroll ──────────────────────────────

        // Gold display
        var goldGO = new GameObject("GoldDisplay", typeof(RectTransform));
        goldGO.transform.SetParent(transform, false);
        var goldRT = goldGO.GetComponent<RectTransform>();
        goldRT.anchorMin = new Vector2(0f,    0.5f);
        goldRT.anchorMax = new Vector2(0.09f, 1.0f);
        goldRT.offsetMin = new Vector2(10, 0);
        goldRT.offsetMax = new Vector2(0, -4);
        goldLabel = goldGO.AddComponent<TextMeshProUGUI>();
        goldLabel.fontSize  = 16;
        goldLabel.fontStyle = FontStyles.Bold;
        goldLabel.color     = new Color(1f, 0.82f, 0.1f);
        goldLabel.alignment = TextAlignmentOptions.MidlineLeft;

        // Unit slots container
        var slotsGO = new GameObject("Slots", typeof(RectTransform));
        slotsGO.transform.SetParent(transform, false);
        var slotsRT = slotsGO.GetComponent<RectTransform>();
        slotsRT.anchorMin = new Vector2(0.09f, 0.5f);
        slotsRT.anchorMax = new Vector2(0.70f, 1.0f);
        slotsRT.offsetMin = new Vector2(4,  4);
        slotsRT.offsetMax = new Vector2(-4, -4);

        var layout = slotsGO.AddComponent<HorizontalLayoutGroup>();
        layout.spacing              = 6;
        layout.childControlWidth    = true;
        layout.childControlHeight   = true;
        layout.childForceExpandWidth  = false;
        layout.childForceExpandHeight = false;
        layout.childAlignment       = TextAnchor.MiddleCenter;
        layout.padding              = new RectOffset(4, 4, 4, 4);

        for (int i = 0; i < ShopManager.ShopSize; i++)
        {
            var slotGO = new GameObject($"Slot_{i}", typeof(RectTransform));
            slotGO.transform.SetParent(slotsGO.transform, false);
            var slotUI = slotGO.AddComponent<ShopSlotUI>();
            slotUI.Build(i, minH: 100);
            slotUIs[i] = slotUI;
        }

        // Reroll button
        var rerollGO = new GameObject("RerollBtn", typeof(RectTransform));
        rerollGO.transform.SetParent(transform, false);
        var rerollRT = rerollGO.GetComponent<RectTransform>();
        rerollRT.anchorMin = new Vector2(0.71f, 0.55f);
        rerollRT.anchorMax = new Vector2(0.82f, 0.95f);
        rerollRT.offsetMin = rerollRT.offsetMax = Vector2.zero;

        rerollGO.AddComponent<Image>().color = new Color(0.3f, 0.55f, 0.25f);
        var rerollBtn  = rerollGO.AddComponent<Button>();
        var rerollCol  = rerollBtn.colors;
        rerollCol.highlightedColor = new Color(0.4f, 0.7f, 0.3f);
        rerollCol.pressedColor     = new Color(0.2f, 0.4f, 0.18f);
        rerollBtn.colors = rerollCol;
        rerollBtn.onClick.AddListener(OnReroll);

        var rerollLblGO = new GameObject("Label", typeof(RectTransform));
        rerollLblGO.transform.SetParent(rerollGO.transform, false);
        var rerollLblRT = rerollLblGO.GetComponent<RectTransform>();
        rerollLblRT.anchorMin = Vector2.zero; rerollLblRT.anchorMax = Vector2.one;
        rerollLblRT.offsetMin = new Vector2(4, 2); rerollLblRT.offsetMax = new Vector2(-4, -2);
        var rerollLbl = rerollLblGO.AddComponent<TextMeshProUGUI>();
        rerollLbl.text      = $"Reroll\n({ShopManager.RerollCost}g)";
        rerollLbl.fontSize  = 12;
        rerollLbl.fontStyle = FontStyles.Bold;
        rerollLbl.alignment = TextAlignmentOptions.Center;
        rerollLbl.color     = Color.white;

        // ── Bottom row: building shop ─────────────────────────────────────────

        // "Build" label
        var buildLblGO = new GameObject("BuildLabel", typeof(RectTransform));
        buildLblGO.transform.SetParent(transform, false);
        var buildLblRT = buildLblGO.GetComponent<RectTransform>();
        buildLblRT.anchorMin = new Vector2(0f,    0.0f);
        buildLblRT.anchorMax = new Vector2(0.09f, 0.48f);
        buildLblRT.offsetMin = new Vector2(8, 0);
        buildLblRT.offsetMax = Vector2.zero;
        var buildLbl = buildLblGO.AddComponent<TextMeshProUGUI>();
        buildLbl.text      = "Build";
        buildLbl.fontSize  = 13;
        buildLbl.fontStyle = FontStyles.Bold;
        buildLbl.alignment = TextAlignmentOptions.MidlineLeft;
        buildLbl.color     = new Color(0.7f, 0.85f, 1f);

        // Building slots container
        buildingDefs = Resources.LoadAll<BuildingDefinition>("BuildingDefinitions");
        System.Array.Sort(buildingDefs, (a, b) => a.cost.CompareTo(b.cost));

        var bSlotsGO = new GameObject("BuildingSlots", typeof(RectTransform));
        bSlotsGO.transform.SetParent(transform, false);
        var bSlotsRT = bSlotsGO.GetComponent<RectTransform>();
        bSlotsRT.anchorMin = new Vector2(0.09f, 0.0f);
        bSlotsRT.anchorMax = new Vector2(0.82f, 0.48f);
        bSlotsRT.offsetMin = new Vector2(4,  4);
        bSlotsRT.offsetMax = new Vector2(-4, -4);

        var bLayout = bSlotsGO.AddComponent<HorizontalLayoutGroup>();
        bLayout.spacing               = 6;
        bLayout.childControlWidth     = true;
        bLayout.childControlHeight    = true;
        bLayout.childForceExpandWidth  = false;
        bLayout.childForceExpandHeight = false;
        bLayout.childAlignment        = TextAnchor.MiddleCenter;
        bLayout.padding               = new RectOffset(4, 4, 4, 4);

        buildingCostLabels = new TextMeshProUGUI[buildingDefs.Length];
        for (int i = 0; i < buildingDefs.Length; i++)
            buildingCostLabels[i] = BuildBuildingSlot(bSlotsGO.transform, buildingDefs[i], i);

        // ── Fight button (spans full height, right side) ───────────────────────

        var fightGO = new GameObject("FightBtn", typeof(RectTransform));
        fightGO.transform.SetParent(transform, false);
        var fightRT = fightGO.GetComponent<RectTransform>();
        fightRT.anchorMin = new Vector2(0.84f, 0.03f);
        fightRT.anchorMax = new Vector2(0.99f, 0.97f);
        fightRT.offsetMin = fightRT.offsetMax = Vector2.zero;

        fightGO.AddComponent<Image>().color = new Color(0.7f, 0.15f, 0.1f);
        var fightBtn    = fightGO.AddComponent<Button>();
        var fightColors = fightBtn.colors;
        fightColors.highlightedColor = new Color(0.9f, 0.2f, 0.12f);
        fightColors.pressedColor     = new Color(0.5f, 0.1f, 0.07f);
        fightBtn.colors = fightColors;
        fightBtn.onClick.AddListener(() => GameManager.Instance.StartFight());

        var fightLblGO = new GameObject("Label", typeof(RectTransform));
        fightLblGO.transform.SetParent(fightGO.transform, false);
        var fightLblRT = fightLblGO.GetComponent<RectTransform>();
        fightLblRT.anchorMin = Vector2.zero; fightLblRT.anchorMax = Vector2.one;
        fightLblRT.offsetMin = new Vector2(4, 2); fightLblRT.offsetMax = new Vector2(-4, -2);
        var fightLbl = fightLblGO.AddComponent<TextMeshProUGUI>();
        fightLbl.text      = "Fight!";
        fightLbl.fontSize  = 16;
        fightLbl.fontStyle = FontStyles.Bold;
        fightLbl.alignment = TextAlignmentOptions.Center;
        fightLbl.color     = Color.white;

        // ── Events ───────────────────────────────────────────────────────────

        gm.OnShopChanged           += Refresh;
        gm.OnGoldChanged           += Refresh;
        gm.OnViewedPlayerChanged   += OnViewedPlayerChanged;
        gm.OnPlacementChanged      += Refresh;
        Refresh();
    }

    TextMeshProUGUI BuildBuildingSlot(Transform parent, BuildingDefinition def, int idx)
    {
        var slotGO = new GameObject($"BSlot_{idx}", typeof(RectTransform));
        slotGO.transform.SetParent(parent, false);

        var le = slotGO.AddComponent<LayoutElement>();
        le.minWidth       = 88;
        le.minHeight      = 88;
        le.preferredWidth = 88;

        slotGO.AddComponent<Image>().color = new Color(0.15f, 0.22f, 0.30f);
        slotGO.AddComponent<Outline>().effectColor = new Color(0.4f, 0.6f, 0.9f, 0.5f);

        var btn = slotGO.AddComponent<Button>();
        int capturedIdx = idx;
        btn.onClick.AddListener(() =>
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            if (gm.PendingBuilding == buildingDefs[capturedIdx])
                gm.CancelPlacement();
            else
                gm.StartBuildingPlacement(buildingDefs[capturedIdx]);
        });
        var btnCol = btn.colors;
        btnCol.highlightedColor = new Color(0.25f, 0.38f, 0.52f);
        btnCol.pressedColor     = new Color(0.10f, 0.15f, 0.22f);
        btn.colors = btnCol;

        // Portrait
        var imgGO = new GameObject("Icon", typeof(RectTransform));
        imgGO.transform.SetParent(slotGO.transform, false);
        var imgRT = imgGO.GetComponent<RectTransform>();
        imgRT.anchorMin = new Vector2(0.08f, 0.30f);
        imgRT.anchorMax = new Vector2(0.92f, 0.96f);
        imgRT.offsetMin = imgRT.offsetMax = Vector2.zero;
        var icon = imgGO.AddComponent<Image>();
        icon.sprite          = def.sprite;
        icon.color           = def.sprite != null ? Color.white : Color.clear;
        icon.preserveAspect  = true;
        icon.raycastTarget   = false;

        // Name
        var nameGO = new GameObject("Name", typeof(RectTransform));
        nameGO.transform.SetParent(slotGO.transform, false);
        var nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0f, 0.16f);
        nameRT.anchorMax = new Vector2(1f, 0.32f);
        nameRT.offsetMin = new Vector2(3, 0); nameRT.offsetMax = new Vector2(-3, 0);
        var nameTmp = nameGO.AddComponent<TextMeshProUGUI>();
        nameTmp.text      = def.buildingName;
        nameTmp.fontSize  = 8;
        nameTmp.alignment = TextAlignmentOptions.Center;
        nameTmp.color     = Color.white;
        nameTmp.raycastTarget = false;

        // Cost
        var costGO = new GameObject("Cost", typeof(RectTransform));
        costGO.transform.SetParent(slotGO.transform, false);
        var costRT = costGO.GetComponent<RectTransform>();
        costRT.anchorMin = new Vector2(0f,  0.0f);
        costRT.anchorMax = new Vector2(1f, 0.18f);
        costRT.offsetMin = new Vector2(3, 2); costRT.offsetMax = new Vector2(-3, 0);
        var costTmp = costGO.AddComponent<TextMeshProUGUI>();
        costTmp.text      = $"{def.cost}g";
        costTmp.fontSize  = 9;
        costTmp.fontStyle = FontStyles.Bold;
        costTmp.alignment = TextAlignmentOptions.Center;
        costTmp.color     = new Color(1f, 0.82f, 0.1f);
        costTmp.raycastTarget = false;

        return costTmp;
    }

    void OnDestroy()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        gm.OnShopChanged           -= Refresh;
        gm.OnGoldChanged           -= Refresh;
        gm.OnViewedPlayerChanged   -= OnViewedPlayerChanged;
        gm.OnPlacementChanged      -= Refresh;
    }

    void OnViewedPlayerChanged(PlayerState viewed)
    {
        var gm = GameManager.Instance;
        bool isOwn = (viewed == gm.HumanPlayer);
        canvasGroup.interactable   = isOwn;
        canvasGroup.blocksRaycasts = isOwn;
        canvasGroup.alpha          = isOwn ? 1f : 0.4f;
    }

    void OnReroll() => GameManager.Instance.TryReroll();

    void Refresh()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        goldLabel.text = $"Gold\n{gm.HumanPlayer.gold}";

        for (int i = 0; i < ShopManager.ShopSize; i++)
            slotUIs[i].Refresh(gm.Shop.slots[i], gm.HumanPlayer.gold);

        // Update building cost label colours to reflect affordability.
        if (buildingCostLabels != null && buildingDefs != null)
        {
            int gold = gm.HumanPlayer.gold;
            for (int i = 0; i < buildingCostLabels.Length; i++)
            {
                if (buildingCostLabels[i] == null) continue;
                bool canAfford = gold >= buildingDefs[i].cost;
                buildingCostLabels[i].color = canAfford
                    ? new Color(1f, 0.82f, 0.1f)
                    : new Color(0.5f, 0.4f, 0.1f);
            }
        }
    }
}
