using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

public class GameUI : MonoBehaviour
{
    private Transform canvasRoot;
    private GameObject leftPanel;
    private GameObject topPanel;
    private GameObject mainPanel;
    private GameObject combatPanel;

    void Awake()
    {
        EnsureEventSystem();

        var canvasGO = new GameObject("Canvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 0;
        canvasRoot = canvasGO.transform;

        var scaler = canvasGO.AddComponent<CanvasScaler>();
        scaler.uiScaleMode        = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight  = 0.5f;

        canvasGO.AddComponent<GraphicRaycaster>();
        canvasGO.AddComponent<Image>().color = Color.black;

        const float leftW = 0.12f;
        const float topH  = 0.22f;

        leftPanel = CreatePanel("LeftPanel", canvasRoot,
            new Vector2(0, 0), new Vector2(leftW, 1), Color.black);
        leftPanel.AddComponent<PlayerListPanel>();

        topPanel = CreatePanel("TopPanel", canvasRoot,
            new Vector2(leftW, 1f - topH), new Vector2(1, 1), Color.black);
        topPanel.AddComponent<ShopPanel>();

        mainPanel = CreatePanel("MainPanel", canvasRoot,
            new Vector2(leftW, 0), new Vector2(1, 1f - topH), Color.black);
        mainPanel.AddComponent<BoardGridUI>();
    }

    void Start()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnPhaseChanged += HandlePhaseChanged;
    }

    void OnDestroy()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
    }

    void HandlePhaseChanged(GamePhase phase)
    {
        if (phase == GamePhase.Combat)
        {
            leftPanel.SetActive(false);
            topPanel.SetActive(false);
            mainPanel.SetActive(false);

            if (combatPanel != null) Destroy(combatPanel);
            combatPanel = CreatePanel("CombatPanel", canvasRoot,
                Vector2.zero, Vector2.one, Color.black);
            combatPanel.AddComponent<CombatBoardUI>();
        }
        else
        {
            if (combatPanel != null) { Destroy(combatPanel); combatPanel = null; }
            leftPanel.SetActive(true);
            topPanel.SetActive(true);
            mainPanel.SetActive(true);
        }
    }

    static GameObject CreatePanel(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.AddComponent<Image>().color = color;
        return go;
    }

    static void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null) return;
        var esGO = new GameObject("EventSystem");
        esGO.AddComponent<EventSystem>();
        esGO.AddComponent<InputSystemUIInputModule>();
    }
}
