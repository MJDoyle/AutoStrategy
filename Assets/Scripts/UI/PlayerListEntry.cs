using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayerListEntry : MonoBehaviour
{
    private PlayerState player;
    private TextMeshProUGUI nameText;
    private TextMeshProUGUI goldText;
    private TextMeshProUGUI healthText;
    private Image healthFill;
    private Image background;
    private Image viewedOverlay;
    private Image opponentOverlay;

    static readonly Color HumanBg = new Color(0.25f, 0.20f, 0.06f);
    static readonly Color AiBg    = new Color(0.12f, 0.10f, 0.09f);
    static readonly Color HealthFull  = new Color(0.2f, 0.78f, 0.2f);
    static readonly Color HealthLow   = new Color(0.78f, 0.2f, 0.2f);

    public void Build(PlayerState p, System.Action onClick = null)
    {
        player = p;
        var rt = GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(0, 70);

        background = gameObject.AddComponent<Image>();
        background.color = p.isHuman ? HumanBg : AiBg;

        if (p.isHuman)
        {
            var outline = gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 0.82f, 0.1f, 0.8f);
            outline.effectDistance = new Vector2(2, 2);
        }

        // Name
        var nameGO = new GameObject("Name", typeof(RectTransform));
        nameGO.transform.SetParent(transform, false);
        var nameRT = nameGO.GetComponent<RectTransform>();
        nameRT.anchorMin = new Vector2(0, 0.55f);
        nameRT.anchorMax = new Vector2(1, 1f);
        nameRT.offsetMin = new Vector2(8, 0);
        nameRT.offsetMax = new Vector2(-8, -4);
        nameText = nameGO.AddComponent<TextMeshProUGUI>();
        nameText.fontSize = 14;
        nameText.fontStyle = FontStyles.Bold;
        nameText.color = p.isHuman ? new Color(1f, 0.9f, 0.4f) : Color.white;
        nameText.text = p.playerName;

        // Health bar background
        var hbgGO = new GameObject("HealthBG", typeof(RectTransform));
        hbgGO.transform.SetParent(transform, false);
        var hbgRT = hbgGO.GetComponent<RectTransform>();
        hbgRT.anchorMin = new Vector2(0, 0.18f);
        hbgRT.anchorMax = new Vector2(0.7f, 0.52f);
        hbgRT.offsetMin = new Vector2(8, 0);
        hbgRT.offsetMax = new Vector2(-2, 0);
        var hbg = hbgGO.AddComponent<Image>();
        hbg.color = new Color(0.2f, 0.1f, 0.1f);

        // Health fill
        var hfGO = new GameObject("HealthFill", typeof(RectTransform));
        hfGO.transform.SetParent(hbgGO.transform, false);
        var hfRT = hfGO.GetComponent<RectTransform>();
        hfRT.anchorMin = Vector2.zero;
        hfRT.anchorMax = Vector2.one;
        hfRT.offsetMin = Vector2.zero;
        hfRT.offsetMax = Vector2.zero;
        healthFill = hfGO.AddComponent<Image>();
        healthFill.type = Image.Type.Filled;
        healthFill.fillMethod = Image.FillMethod.Horizontal;
        healthFill.fillAmount = p.health / 100f;

        // Health number
        var hnGO = new GameObject("HP", typeof(RectTransform));
        hnGO.transform.SetParent(transform, false);
        var hnRT = hnGO.GetComponent<RectTransform>();
        hnRT.anchorMin = new Vector2(0.7f, 0.18f);
        hnRT.anchorMax = new Vector2(1f, 0.52f);
        hnRT.offsetMin = new Vector2(2, 0);
        hnRT.offsetMax = new Vector2(-4, 0);
        healthText = hnGO.AddComponent<TextMeshProUGUI>();
        healthText.fontSize = 11;
        healthText.color = new Color(0.9f, 0.9f, 0.9f);
        healthText.alignment = TextAlignmentOptions.MidlineRight;

        // Gold
        var goldGO = new GameObject("Gold", typeof(RectTransform));
        goldGO.transform.SetParent(transform, false);
        var goldRT = goldGO.GetComponent<RectTransform>();
        goldRT.anchorMin = new Vector2(0, 0f);
        goldRT.anchorMax = new Vector2(1, 0.22f);
        goldRT.offsetMin = new Vector2(8, 2);
        goldRT.offsetMax = new Vector2(-8, 0);
        goldText = goldGO.AddComponent<TextMeshProUGUI>();
        goldText.fontSize = 11;
        goldText.color = new Color(1f, 0.82f, 0.1f);

        // Click handler
        if (onClick != null)
        {
            var btn = gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => onClick());
        }

        // Viewed overlay — teal tint shown when this player's camp is selected
        var ovGO = new GameObject("ViewedOverlay", typeof(RectTransform));
        ovGO.transform.SetParent(transform, false);
        var ovRT = ovGO.GetComponent<RectTransform>();
        ovRT.anchorMin = Vector2.zero;
        ovRT.anchorMax = Vector2.one;
        ovRT.offsetMin = ovRT.offsetMax = Vector2.zero;
        viewedOverlay = ovGO.AddComponent<Image>();
        viewedOverlay.color = new Color(0.25f, 0.75f, 0.85f, 0.20f);
        viewedOverlay.raycastTarget = false;
        ovGO.SetActive(false);

        // Opponent overlay — red-orange tint + "VS" label on the next opponent
        var opGO = new GameObject("OpponentOverlay", typeof(RectTransform));
        opGO.transform.SetParent(transform, false);
        var opRT = opGO.GetComponent<RectTransform>();
        opRT.anchorMin = Vector2.zero;
        opRT.anchorMax = Vector2.one;
        opRT.offsetMin = opRT.offsetMax = Vector2.zero;
        opponentOverlay = opGO.AddComponent<Image>();
        opponentOverlay.color = new Color(0.85f, 0.25f, 0.1f, 0.22f);
        opponentOverlay.raycastTarget = false;

        var vsGO = new GameObject("VS", typeof(RectTransform));
        vsGO.transform.SetParent(opGO.transform, false);
        var vsRT = vsGO.GetComponent<RectTransform>();
        vsRT.anchorMin = new Vector2(1f, 0.5f);
        vsRT.anchorMax = new Vector2(1f, 0.5f);
        vsRT.pivot     = new Vector2(1f, 0.5f);
        vsRT.anchoredPosition = new Vector2(-6f, 0f);
        vsRT.sizeDelta = new Vector2(28f, 18f);
        var vsLabel = vsGO.AddComponent<TMPro.TextMeshProUGUI>();
        vsLabel.text = "VS";
        vsLabel.fontSize = 11;
        vsLabel.fontStyle = TMPro.FontStyles.Bold;
        vsLabel.color = new Color(1f, 0.55f, 0.2f);
        vsLabel.alignment = TMPro.TextAlignmentOptions.MidlineRight;
        vsLabel.raycastTarget = false;

        opGO.SetActive(false);

        Refresh();
    }

    public void SetViewed(bool active)
    {
        if (viewedOverlay != null)
            viewedOverlay.gameObject.SetActive(active);
    }

    public void SetNextOpponent(bool active)
    {
        if (opponentOverlay != null)
            opponentOverlay.gameObject.SetActive(active);
    }

    public void Refresh()
    {
        if (player == null) return;
        float pct = player.health / 100f;
        healthFill.fillAmount = pct;
        healthFill.color = Color.Lerp(HealthLow, HealthFull, pct);
        healthText.text = player.health.ToString();
        goldText.text = $"Gold: {player.gold}";
    }
}
