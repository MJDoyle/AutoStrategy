using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class BoardGridUI : MonoBehaviour
{
    private readonly Dictionary<PlayerState, (GameObject go, CampView view)> viewCache = new();
    private PlayerState activePlayer;
    private int         selectedCol = -1;
    private int         selectedRow = -1;

    private GameObject ghostGO;
    private Image      ghostImage;
    private RectTransform ghostRT;

    CampView ActiveView => activePlayer != null && viewCache.TryGetValue(activePlayer, out var e)
        ? e.view : null;

    void Start()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;

        // Pre-build all camps; all start hidden except the human's
        foreach (var p in gm.Players)
            BuildCached(p);

        ShowPlayer(gm.HumanPlayer);
        gm.Board.OnCellChanged   += OnCellChanged;
        gm.OnViewedPlayerChanged += ShowPlayer;
        gm.OnBuildingsChanged    += OnBuildingsChanged;
        gm.OnPlacementChanged    += OnPlacementChanged;

        BuildGhost();
    }

    void BuildGhost()
    {
        var canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        ghostGO = new GameObject("PlacementGhost", typeof(RectTransform));
        ghostGO.transform.SetParent(canvas.transform, false);

        ghostRT          = ghostGO.GetComponent<RectTransform>();
        ghostRT.sizeDelta = new Vector2(80, 80);
        ghostRT.pivot     = new Vector2(0.5f, 0f);

        ghostImage                = ghostGO.AddComponent<Image>();
        ghostImage.color          = new Color(1f, 1f, 1f, 0.65f);
        ghostImage.preserveAspect = true;
        ghostImage.raycastTarget  = false;

        ghostGO.SetActive(false);
    }

    void Update()
    {
        if (ghostGO == null || !ghostGO.activeSelf) return;
        var mouse = UnityEngine.InputSystem.Mouse.current;
        if (mouse == null) return;
        var mp = mouse.position.ReadValue();
        ghostRT.position = new Vector3(mp.x, mp.y, 0f);
    }

    void OnPlacementChanged()
    {
        var gm = GameManager.Instance;
        if (gm == null || ghostGO == null) return;

        // Building ghosts are rendered in world space by CampView (correct size + alignment).
        if (!gm.IsPendingPlacement || gm.PendingBuilding != null)
        {
            ghostGO.SetActive(false);
            return;
        }

        // Unit placement only: show canvas ghost that follows the mouse.
        var sprite = gm.PendingSprite;
        ghostImage.sprite = sprite;
        ghostImage.color  = sprite != null ? new Color(1f, 1f, 1f, 0.65f) : Color.clear;

        if (sprite != null)
            ghostRT.sizeDelta = new Vector2(sprite.rect.width, sprite.rect.height);

        ghostGO.SetActive(true);
    }

    void BuildCached(PlayerState p)
    {
        var gm     = GameManager.Instance;
        bool human = (p == gm.HumanPlayer);

        var go = new GameObject("CampViewHolder", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.SetActive(false);

        var cv = go.AddComponent<CampView>();
        cv.Setup(terrain: p.terrain, player: p, interactive: human, spriteScale: gm.spriteScale);

        if (human)
        {
            cv.OnCellClicked    = HandleCellClick;
            cv.OnCellRightClick = HandleCellRightClick;
        }

        viewCache[p] = (go, cv);
    }

    void OnDestroy()
    {
        var gm = GameManager.Instance;
        if (gm == null) return;
        gm.Board.OnCellChanged   -= OnCellChanged;
        gm.OnViewedPlayerChanged -= ShowPlayer;
        gm.OnBuildingsChanged    -= OnBuildingsChanged;
        gm.OnPlacementChanged    -= OnPlacementChanged;

        if (ghostGO != null) Destroy(ghostGO);
    }

    void ShowPlayer(PlayerState p)
    {
        if (activePlayer != null && viewCache.TryGetValue(activePlayer, out var prev))
            prev.go.SetActive(false);

        selectedCol = selectedRow = -1;
        activePlayer = p;

        if (viewCache.TryGetValue(p, out var entry))
            entry.go.SetActive(true);
    }

    void OnCellChanged(int col, int row)
    {
        var gm = GameManager.Instance;
        if (gm != null && activePlayer == gm.HumanPlayer)
        {
            ActiveView?.RefreshCell(col, row);
            ActiveView?.RebuildFog();
        }
    }

    void OnBuildingsChanged()
    {
        var gm = GameManager.Instance;
        if (gm != null && activePlayer == gm.HumanPlayer)
            ActiveView?.RefreshBuildings();
    }

    void HandleCellClick(int col, int row)
    {
        var gm    = GameManager.Instance;
        var board = gm.HumanPlayer.board;
        var terr  = gm.HumanPlayer.terrain;
        var view  = ActiveView;

        if (selectedCol >= 0)
        {
            int fc = selectedCol, fr = selectedRow;
            selectedCol = selectedRow = -1;
            view.SetSelection(-1, -1);

            if (fc != col || fr != row)
            {
                if (terr != null && terr.IsCellBlocked(col, row)) return;
                gm.TryMoveUnit(fc, fr, col, row);
            }
            return;
        }

        if (board[col, row] != null)
        {
            selectedCol = col;
            selectedRow = row;
            view.SetSelection(col, row);
        }
    }

    void HandleCellRightClick(int col, int row)
    {
        selectedCol = selectedRow = -1;
        ActiveView?.SetSelection(-1, -1);
        GameManager.Instance.TrySellUnit(col, row);
    }
}
