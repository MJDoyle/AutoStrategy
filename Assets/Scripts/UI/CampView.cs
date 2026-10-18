using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// Shared world-coordinate constants used by both camp view and combat board.
public static class CampLayout
{
    public const float TILE_PX    = 64f;
    public const int   GRID_SIZE  = 30;
    public const float WORLD_SIZE = GRID_SIZE * TILE_PX;   // 1920

    // Board 9×9 unit cells mapped to world pixels — one tile (64 px) per cell.
    // Matches CampTerrain.UNIT_TILE_X0=17, UNIT_TILE_Y0=11 centred in the clearing.
    public const float UNIT_STRIDE = 64f;
    public const float COL0_X      = 1120f;   // TileCenterX(17) = 17*64+32
    public const float ROW0_Y      =  736f;   // TileCenterY(11) = 11*64+32

    // Combat canvas: three sections each WORLD_SIZE wide
    public const float MIDDLE_X = WORLD_SIZE;             // 3200
    public const float ENEMY_X  = 2f * WORLD_SIZE;        // 6400
    public const float COMBAT_W = 3f * WORLD_SIZE;        // 9600
    public const float COMBAT_H = WORLD_SIZE;             // 3200

    // Attack range: pixels per 1 range unit
    public const float RANGE_PX = 150f;

    public static float UnitCenterX(int col) => COL0_X + col * UNIT_STRIDE;
    public static float UnitCenterY(int row) => ROW0_Y + row * UNIT_STRIDE;

    // Enemy unit in the enemy section (mirrored)
    public static float EnemyUnitCenterX(int col) =>
        ENEMY_X + (WORLD_SIZE - COL0_X) - col * UNIT_STRIDE;
    public static float EnemyUnitCenterY(int row) => ROW0_Y + row * UNIT_STRIDE;

    public static float TileCenterX(int tx) => tx * TILE_PX + TILE_PX * 0.5f;
    public static float TileCenterY(int ty) => ty * TILE_PX + TILE_PX * 0.5f;
}

// Renders one camp (terrain + unit icons) in a zoomable/pannable viewport.
public class CampView : MonoBehaviour,
    IScrollHandler, IPointerDownHandler, IPointerUpHandler,
    IPointerClickHandler, IDragHandler,
    IPointerEnterHandler, IPointerExitHandler
{
    // ── Terrain animation ──────────────────────────────────────────────────────

    public struct TerrainAnim
    {
        public Image    img;
        public Sprite[] frames;
        public float    fps;
        public float    phase;
        public int      lastFrame;
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private RectTransform viewport;
    private RectTransform world;
    private Transform     unitLayerTf;
    private float         zoom         = 0.5f;
    private float         minZoom      = 0.15f;
    private float         maxZoom      = 2.0f;
    private Vector2       dragStartScreen;
    private Vector2       worldPosAtDragStart;
    private bool          isDragging;
    private bool          needsInitialCamera;

    private readonly List<TerrainAnim> animated = new();

    private PlayerState player;
    private CampTerrain terrain;
    private float        spriteScale;
    private bool         interactive;
    private Image        selectionHighlight;
    private GameObject   buildingGhostGO;
    private Image        buildingGhostImg;
    private float        debugXOffset;
    private Image        tileDebugHighlight;
    private Image        tileCursorHighlight;
    private Transform    footprintLayer;
    private List<Image>  footprintPool = new List<Image>();
    private int          footprintUsed;
    private Sprite       footprintOverlaySprite;
    private Sprite       flatTreeSprite;
    private Sprite       flatRockSprite;
    private GameObject          terrainLayersGO;
    private int                 terrainFlattenLevel;
    private int                 lastValidPlaceTileX = -1;
    private int                 lastValidPlaceTileY = -1;
    private BuildingDefinition  lastValidPlaceDef;
    private TMP_Text     tileDebugTooltip;
    private GameObject   shadowH2Container;
    private GameObject   shadowH3Container;
    private GameObject   waterContainer;
    private GameObject   flatGroundContainer;
    private GameObject   cliffH2Container;
    private GameObject   elevatedH2Container;
    private GameObject   cliffH3Container;
    private GameObject   elevatedH3Container;
    private GameObject   decorationsContainer;
    private GameObject   buildingsContainer;
    private GameObject   debugMenuPanel;

    private bool             showCoords;
    private RectTransform    coordOverlay;
    private TextMeshProUGUI  coordLabel;

    private Texture2D  fogTex;
    private float[,]   fogVisibility;      // [tx, ty] visibility 0..1, SIZE×SIZE
    private GameObject fogLayerGO;

    private bool       showFlatOverlay;
    private GameObject flatOverlayContainer;

    public System.Action<int, int> OnCellClicked;
    public System.Action<int, int> OnCellRightClick;

    // ── Setup ─────────────────────────────────────────────────────────────────

    public void Setup(CampTerrain terrain, PlayerState player,
                      bool interactive, float spriteScale,
                      float worldW = CampLayout.WORLD_SIZE,
                      float worldH = CampLayout.WORLD_SIZE,
                      float xOffset = 0f)
    {
        this.terrain     = terrain;
        this.player      = player;
        this.interactive = interactive;
        this.spriteScale = spriteScale;

        // Transparent but raycaster-visible background so this object catches input
        var blocker = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
        blocker.color = new Color(0, 0, 0, 0.01f);

        // Viewport with mask
        var vpGO = new GameObject("Viewport", typeof(RectTransform));
        vpGO.transform.SetParent(transform, false);
        viewport = vpGO.GetComponent<RectTransform>();
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = viewport.offsetMax = Vector2.zero;
        // Mask image must NOT intercept raycasts (CampView's blocker does that)
        var vpImg = vpGO.AddComponent<Image>();
        vpImg.color = new Color(0, 0, 0, 0.01f);
        vpImg.raycastTarget = false;
        vpGO.AddComponent<Mask>().showMaskGraphic = false;

        // World container
        var worldGO = new GameObject("World", typeof(RectTransform));
        worldGO.transform.SetParent(vpGO.transform, false);
        world = worldGO.GetComponent<RectTransform>();
        world.anchorMin = world.anchorMax = Vector2.zero;
        world.pivot     = Vector2.zero;
        world.sizeDelta = new Vector2(worldW, worldH);

        // Ground background — solid colour fallback for uncovered areas.
        var cfg = Resources.Load<TerrainConfig>("TerrainConfig");
        SpawnTiledGround(worldGO.transform, worldW * 0.5f + xOffset, worldH * 0.5f,
            worldW, worldH, new Color(0.20f, 0.38f, 0.14f), cfg?.groundTile);

        // Terrain layers container — stays below buildings/units so rebuilding keeps correct order.
        var tlGO = MakeContainer(worldGO.transform, "TerrainLayers");
        terrainLayersGO = tlGO;

        // Heightmap terrain tiles (drawn above the background, below decorations).
        if (terrain != null) BuildHeightMapTiles(tlGO.transform, terrain, xOffset, cfg);

        // Terrain decorations
        if (terrain != null) BuildTerrain(tlGO.transform, terrain, xOffset, cfg);

        // Buildings (above terrain decorations, below units)
        BuildBuildings(worldGO.transform, xOffset);

        // Flat diagnostic overlay — hidden by default, toggled with V key
        if (terrain != null) BuildFlatOverlay(worldGO.transform, xOffset);

        // Unit layer
        var ulGO = new GameObject("Units", typeof(RectTransform));
        ulGO.transform.SetParent(worldGO.transform, false);
        var ulRT = ulGO.GetComponent<RectTransform>();
        ulRT.anchorMin = ulRT.anchorMax = Vector2.zero;
        ulRT.pivot     = Vector2.zero;
        ulRT.sizeDelta = new Vector2(worldW, worldH);
        unitLayerTf = ulGO.transform;

        if (player != null) BuildUnitCells(unitLayerTf);

        // Selection highlight
        if (interactive)
        {
            var hlGO = new GameObject("SelectHL", typeof(RectTransform));
            hlGO.transform.SetParent(unitLayerTf, false);
            var hlRT = hlGO.GetComponent<RectTransform>();
            hlRT.anchorMin = hlRT.anchorMax = Vector2.zero;
            hlRT.pivot     = new Vector2(0.5f, 0.5f);
            hlRT.sizeDelta = new Vector2(CampLayout.TILE_PX, CampLayout.TILE_PX);
            var hlImg = hlGO.AddComponent<Image>();
            hlImg.color = new Color(1f, 1f, 0.3f, 0.35f);
            hlImg.raycastTarget = false;
            hlGO.SetActive(false);
            selectionHighlight = hlImg;
        }

        // Fog of war — above terrain and units, below debug highlight
        if (terrain != null) BuildFogOfWar(worldGO.transform, xOffset);

        // Debug tile highlight — lives in world space so it pans/zooms with the terrain.
        debugXOffset = xOffset;
        {
            var go = new GameObject("TileHL", typeof(RectTransform));
            go.transform.SetParent(worldGO.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(CampLayout.TILE_PX, CampLayout.TILE_PX);
            var img = go.AddComponent<Image>();
            img.color = new Color(1f, 0.9f, 0f, 0.45f);
            img.raycastTarget = false;
            go.SetActive(false);
            tileDebugHighlight = img;
        }

        // Footprint overlay — tiles added/removed here each frame during building placement.
        {
            var go = new GameObject("FootprintLayer", typeof(RectTransform));
            go.transform.SetParent(worldGO.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            footprintLayer = go.transform;
        }

        // Overlay sprites used for footprint tiles and flattened terrain
        Sprite LoadOverlay(string name)
        {
            var tex = Resources.Load<Texture2D>(name);
            return tex != null ? Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 1f) : null;
        }
        footprintOverlaySprite = LoadOverlay("green_overlay");
        flatTreeSprite         = LoadOverlay("tree_overlay");
        flatRockSprite         = LoadOverlay("rock_overlay");

        // World-space building ghost — snaps to the anchor tile during building placement.
        {
            var bgg = new GameObject("BuildingGhost", typeof(RectTransform));
            bgg.transform.SetParent(worldGO.transform, false);
            var bgRT = bgg.GetComponent<RectTransform>();
            bgRT.anchorMin = bgRT.anchorMax = Vector2.zero;
            bgRT.pivot     = new Vector2(0.5f, 0.5f);
            bgRT.sizeDelta = Vector2.one;
            var bgImg = bgg.AddComponent<Image>();
            bgImg.color         = new Color(1f, 1f, 1f, 0.65f);
            bgImg.raycastTarget = false;
            bgg.SetActive(false);
            buildingGhostGO  = bgg;
            buildingGhostImg = bgImg;
        }

        // Placement cursor — selection square sprite that snaps to the hovered tile.
        {
            var cursorSprite = Resources.Load<Sprite>("selection_square_64x64_thick");
            var go = new GameObject("TileCursor", typeof(RectTransform));
            go.transform.SetParent(worldGO.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(CampLayout.TILE_PX, CampLayout.TILE_PX);
            var img = go.AddComponent<Image>();
            img.sprite        = cursorSprite;
            img.color         = Color.white;
            img.raycastTarget = false;
            img.preserveAspect = false;
            go.SetActive(false);
            tileCursorHighlight = img;
        }

        // Tooltip panel — lives in viewport space so it stays fixed on screen.
        {
            var panel = new GameObject("TileTooltip", typeof(RectTransform));
            panel.transform.SetParent(vpGO.transform, false);
            var rt = panel.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot     = Vector2.zero;
            rt.sizeDelta = new Vector2(250f, 76f);
            rt.anchoredPosition = new Vector2(8f, 8f);
            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0.75f);
            bg.raycastTarget = false;

            var textGO = new GameObject("Text", typeof(RectTransform));
            textGO.transform.SetParent(panel.transform, false);
            var trt = textGO.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(8f, 4f);
            trt.offsetMax = new Vector2(-8f, -4f);
            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.fontSize  = 13f;
            tmp.color     = Color.white;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            panel.SetActive(false);
            tileDebugTooltip = tmp;
        }

        if (terrain != null) BuildDebugMenu(vpGO.transform);

        BuildCoordOverlay();

        needsInitialCamera = true;
    }

    void BuildCoordOverlay()
    {
        // Parent to viewport — same as the TileTooltip panel.  The Mask fills the whole
        // CampView so nothing inside it is actually clipped, and viewport local-space
        // coordinates come directly from ScreenPointToLocalPointInRectangle(viewport).
        var go = new GameObject("CoordOverlay", typeof(RectTransform));
        go.transform.SetParent(viewport, false);
        coordOverlay = go.GetComponent<RectTransform>();
        coordOverlay.anchorMin = coordOverlay.anchorMax = Vector2.zero;  // bottom-left anchor
        coordOverlay.pivot     = new Vector2(0f, 1f);   // top-left pivot so offset goes right+down
        coordOverlay.sizeDelta = new Vector2(240f, 60f);

        var bg = go.AddComponent<Image>();
        bg.color         = new Color(0f, 0f, 0f, 0.72f);
        bg.raycastTarget = false;

        var lblGO = new GameObject("Label", typeof(RectTransform));
        lblGO.transform.SetParent(go.transform, false);
        var lrt = lblGO.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = new Vector2(6, 4); lrt.offsetMax = new Vector2(-6, -4);

        coordLabel = lblGO.AddComponent<TextMeshProUGUI>();
        coordLabel.fontSize     = 11f;
        coordLabel.color        = Color.white;
        coordLabel.raycastTarget = false;

        go.SetActive(false);
    }

    // ── Heightmap ─────────────────────────────────────────────────────────────

    // Visual Y lift per height level above 1 — keeps decorations on top of their cliff face.
    const float ELEVATION_PX = 32f;

    static byte GetHeight(CampTerrain ct, int tx, int ty)
    {
        if (tx < 0 || tx >= CampTerrain.SIZE || ty < 0 || ty >= CampTerrain.SIZE) return 0;
        return ct.heightMap[tx, ty];
    }

    // Returns the visual display height after flattening.
    // Water (h=0) and ground (h=1) are never changed; higher tiles are lowered by flattenLevel.
    // flattenLevel=0 → no change; =1 → h=3 shown at h=2; =2 → h=2 and h=3 both shown at h=1.
    int FlatH(int h) => h <= 0 ? 0 : Mathf.Clamp(h, 1, 3 - terrainFlattenLevel);
    int FlatHAt(CampTerrain ct, int tx, int ty) => FlatH(GetHeight(ct, tx, ty));

    // Returns true if def placed at anchor (tx,ty) is legal: all footprint tiles are in-bounds,
    // share the same visual elevation, have clear terrain (no rocks/trees), and don't overlap
    // any existing building footprint.
    bool IsPlaceable(BuildingDefinition def, int tx, int ty)
    {
        if (terrain == null || player == null) return false;
        const int S = CampTerrain.SIZE;
        int fi = def.EffI, fj = def.EffJ;
        int ai = def.EffAI, aj = def.EffAJ;
        int anchorElev = FlatH(GetHeight(terrain, tx, ty));
        for (int di = 0; di < fi; di++)
        for (int dj = 0; dj < fj; dj++)
        {
            int ftx = tx - ai + di;
            int fty = ty + aj - dj;
            if (ftx < 0 || ftx >= S || fty < 0 || fty >= S) return false;
            if (FlatH(GetHeight(terrain, ftx, fty)) != anchorElev)            return false;
            if (terrain.grid[ftx, fty] != TerrainCell.Clear)                  return false;
            foreach (var b in player.buildings)
                if (b.def != null && b.def.ContainsTile(b.tileX, b.tileY, ftx, fty)) return false;
        }
        return true;
    }

    void BuildHeightMapTiles(Transform parent, CampTerrain ct, float xOffset, TerrainConfig cfg)
    {
        if (cfg == null) return;

        var waterGO = MakeContainer(parent, "Water");
        var flatGO  = MakeContainer(parent, "FlatGround");

        for (int tx = 0; tx < CampTerrain.SIZE; tx++)
        for (int ty = 0; ty < CampTerrain.SIZE; ty++)
        {
            float wx      = xOffset + CampLayout.TileCenterX(tx);
            float wy      = CampLayout.TileCenterY(ty);
            int   actualH = GetHeight(ct, tx, ty);
            int   visualH = FlatH(actualH);

            bool needsWater = actualH < 1
                           || GetHeight(ct, tx - 1, ty) < 1 || GetHeight(ct, tx + 1, ty) < 1
                           || GetHeight(ct, tx, ty - 1) < 1 || GetHeight(ct, tx, ty + 1) < 1;
            if (needsWater)
            {
                var waterImg = SpawnImg(waterGO.transform, wx, wy, CampLayout.TILE_PX, CampLayout.TILE_PX, Color.white);
                if (cfg.waterTile != null) waterImg.sprite = cfg.waterTile;
                else waterImg.color = new Color(0.278f, 0.671f, 0.663f);
            }

            if (actualH < 1) continue;

            // Ground-level tile for every land tile.
            // Tiles flattened to h=1 retain their actual-height texture colour.
            int bm1 = 0;
            if (GetHeight(ct, tx,   ty+1) >= 1) bm1 |= 8;
            if (GetHeight(ct, tx+1, ty  ) >= 1) bm1 |= 4;
            if (GetHeight(ct, tx,   ty-1) >= 1) bm1 |= 2;
            if (GetHeight(ct, tx-1, ty  ) >= 1) bm1 |= 1;
            byte groundTex = (byte)(visualH == 1 && actualH > 1 ? actualH : 1);
            var gs = cfg.GetTerrainTile(groundTex, bm1);
            if (gs != null)
            {
                var img = SpawnImg(flatGO.transform, wx, wy, CampLayout.TILE_PX, CampLayout.TILE_PX, Color.white);
                img.sprite = gs;
            }
        }

        // H=2 shadows — only tiles whose visual height is exactly 2 cast this shadow.
        var sh2GO = MakeContainer(parent, "ShadowsH2");
        shadowH2Container = sh2GO;
        if (cfg.shadowSprite != null)
        {
            const float SHADOW_PX = 80f;
            for (int tx = 0; tx < CampTerrain.SIZE; tx++)
            for (int ty = 0; ty < CampTerrain.SIZE; ty++)
            {
                if (FlatHAt(ct, tx, ty) != 2) continue;
                float wx = xOffset + CampLayout.TileCenterX(tx);
                var s = SpawnImg(sh2GO.transform, wx, CampLayout.TileCenterY(ty), SHADOW_PX, SHADOW_PX, Color.white);
                s.sprite = cfg.shadowSprite;
            }
        }

        // H=2 cliff faces — all tiles at visual h>=2 rise from the ground here.
        var cliffH2GO = MakeContainer(parent, "CliffsH2");
        for (int tx = 0; tx < CampTerrain.SIZE; tx++)
        for (int ty = 0; ty < CampTerrain.SIZE; ty++)
        {
            if (FlatHAt(ct, tx, ty) < 2) continue;
            float wx = xOffset + CampLayout.TileCenterX(tx);
            float wy = CampLayout.TileCenterY(ty);
            bool southIsWater = GetHeight(ct, tx, ty - 1) == 0;
            bool leftHigh     = FlatHAt(ct, tx - 1, ty) >= 2;
            bool rightHigh    = FlatHAt(ct, tx + 1, ty) >= 2;
            int ci = (!leftHigh && !rightHigh) ? 3 : (leftHigh && rightHigh) ? 1 : !leftHigh ? 0 : 2;
            var cs = cfg.GetCliffFace(southIsWater, ci);
            if (cs == null) continue;
            SpawnImg(cliffH2GO.transform, wx, wy, CampLayout.TILE_PX, CampLayout.TILE_PX, Color.white).sprite = cs;
        }

        // H=2 elevated ground (+1 tile up) — tiles at visual h>=2.
        // Flattened h=3 tiles retain their actual-height texture colour here.
        var elevH2GO = MakeContainer(parent, "ElevatedH2");
        for (int tx = 0; tx < CampTerrain.SIZE; tx++)
        for (int ty = 0; ty < CampTerrain.SIZE; ty++)
        {
            if (FlatHAt(ct, tx, ty) < 2) continue;
            int actualH = GetHeight(ct, tx, ty);
            float wx = xOffset + CampLayout.TileCenterX(tx);
            float wy = CampLayout.TileCenterY(ty) + CampLayout.TILE_PX;
            int bm = 0;
            if (FlatHAt(ct, tx,   ty+1) >= 2) bm |= 8;
            if (FlatHAt(ct, tx+1, ty  ) >= 2) bm |= 4;
            if (FlatHAt(ct, tx,   ty-1) >= 2) bm |= 2;
            if (FlatHAt(ct, tx-1, ty  ) >= 2) bm |= 1;
            // Use actual-height texture if tile is being flattened from h=3 to visual h=2.
            byte texLevel = (byte)(FlatH(actualH) == 2 && actualH > 2 ? actualH : 2);
            var sp = cfg.GetTerrainTile(texLevel, bm);
            if (sp == null) continue;
            SpawnImg(elevH2GO.transform, wx, wy, CampLayout.TILE_PX, CampLayout.TILE_PX, Color.white).sprite = sp;
        }

        // H=3 shadows — tiles whose visual height is >=3 (only when flattenLevel=0).
        var sh3GO = MakeContainer(parent, "ShadowsH3");
        shadowH3Container = sh3GO;
        if (cfg.shadowSprite != null)
        {
            const float SHADOW_PX = 80f;
            for (int tx = 0; tx < CampTerrain.SIZE; tx++)
            for (int ty = 0; ty < CampTerrain.SIZE; ty++)
            {
                if (FlatHAt(ct, tx, ty) < 3) continue;
                if (ty + 1 >= CampTerrain.SIZE) continue;
                float wx = xOffset + CampLayout.TileCenterX(tx);
                var s = SpawnImg(sh3GO.transform, wx, CampLayout.TileCenterY(ty + 1), SHADOW_PX, SHADOW_PX, Color.white);
                s.sprite = cfg.shadowSprite;
            }
        }

        // H=3 cliff faces at +1 tile up.
        var cliffH3GO = MakeContainer(parent, "CliffsH3");
        for (int tx = 0; tx < CampTerrain.SIZE; tx++)
        for (int ty = 0; ty < CampTerrain.SIZE; ty++)
        {
            if (FlatHAt(ct, tx, ty) < 3) continue;
            float wx = xOffset + CampLayout.TileCenterX(tx);
            float wy = CampLayout.TileCenterY(ty) + CampLayout.TILE_PX;
            bool southIsLow = FlatHAt(ct, tx, ty - 1) < 2;
            bool leftHigh   = FlatHAt(ct, tx - 1, ty) >= 3;
            bool rightHigh  = FlatHAt(ct, tx + 1, ty) >= 3;
            int ci = (!leftHigh && !rightHigh) ? 3 : (leftHigh && rightHigh) ? 1 : !leftHigh ? 0 : 2;
            var cs = cfg.GetCliffFace(southIsLow, ci);
            if (cs == null) continue;
            SpawnImg(cliffH3GO.transform, wx, wy, CampLayout.TILE_PX, CampLayout.TILE_PX, Color.white).sprite = cs;
        }

        // H=3 elevated ground (+2 tiles up).
        var elevH3GO = MakeContainer(parent, "ElevatedH3");
        for (int tx = 0; tx < CampTerrain.SIZE; tx++)
        for (int ty = 0; ty < CampTerrain.SIZE; ty++)
        {
            if (FlatHAt(ct, tx, ty) < 3) continue;
            float wx = xOffset + CampLayout.TileCenterX(tx);
            float wy = CampLayout.TileCenterY(ty) + 2 * CampLayout.TILE_PX;
            int bm = 0;
            if (FlatHAt(ct, tx,   ty+1) >= 3) bm |= 8;
            if (FlatHAt(ct, tx+1, ty  ) >= 3) bm |= 4;
            if (FlatHAt(ct, tx,   ty-1) >= 3) bm |= 2;
            if (FlatHAt(ct, tx-1, ty  ) >= 3) bm |= 1;
            var sp = cfg.GetTerrainTile(3, bm);
            if (sp == null) continue;
            SpawnImg(elevH3GO.transform, wx, wy, CampLayout.TILE_PX, CampLayout.TILE_PX, Color.white).sprite = sp;
        }

        waterContainer      = waterGO;
        flatGroundContainer = flatGO;
        cliffH2Container    = cliffH2GO;
        elevatedH2Container = elevH2GO;
        cliffH3Container    = cliffH3GO;
        elevatedH3Container = elevH3GO;
    }

    // ── Terrain ────────────────────────────────────────────────────────────────

    // Renders cliff faces + decorations together in Y-sorted order (high ty first = drawn behind).
    // For each row, cliff faces that belong to the row above are drawn first (behind decorations
    // at the same row), ensuring correct overlap when a tree sits at the base of a cliff.
    void BuildTerrain(Transform parent, CampTerrain ct, float xOffset, TerrainConfig cfg = null)
    {
        if (cfg == null) cfg = Resources.Load<TerrainConfig>("TerrainConfig");
        if (cfg == null) return;

        var decGO = MakeContainer(parent, "Decorations");
        decorationsContainer = decGO;

        for (int ty = CampTerrain.SIZE - 1; ty >= 0; ty--)
        {
            for (int tx = 0; tx < CampTerrain.SIZE; tx++)
            {
                var cell = ct.grid[tx, ty];
                if (cell == TerrainCell.Clear) continue;

                int  actualH   = GetHeight(ct, tx, ty);
                int  visualH   = FlatH(actualH);
                bool flattened = visualH < actualH;

                float wx = xOffset + CampLayout.TileCenterX(tx);
                // Decorations lifted by visual height, not actual height.
                float wy = CampLayout.TileCenterY(ty)
                         + Mathf.Max(0, visualH - 1) * CampLayout.TILE_PX;

                switch (cell)
                {
                    case TerrainCell.Tree:
                    {
                        if (actualH == 0) break; // no trees on water
                        if (flattened)
                        {
                            if (flatTreeSprite != null)
                                SpawnImg(decGO.transform, wx, wy, CampLayout.TILE_PX, CampLayout.TILE_PX, Color.white).sprite = flatTreeSprite;
                            break;
                        }
                        int v = ct.variant[tx, ty];
                        var frames = cfg.GetTreeFrames(v);
                        if (frames == null || frames.Length == 0) break;
                        var (tw, th) = cfg.GetTreeDimensions(v);
                        var img = SpawnImg(decGO.transform, wx, wy, tw, th, Color.white);
                        float py = (cfg.treePivotY != null && v < cfg.treePivotY.Length) ? cfg.treePivotY[v] : (v < 2 ? 0.125f : 1f / 6f);
                        float px = (cfg.treePivotX != null && v < cfg.treePivotX.Length) ? cfg.treePivotX[v] : 0.5f;
                        img.rectTransform.pivot = new Vector2(px, py);
                        img.sprite = frames[0];
                        animated.Add(new TerrainAnim { img = img, frames = frames, fps = 8f, phase = ct.phase[tx, ty], lastFrame = -1 });
                        break;
                    }
                    case TerrainCell.Rock:
                    {
                        if (actualH == 0)
                        {
                            int v = ct.variant[tx, ty] % 4;
                            var frames = cfg.GetWaterFrames(v);
                            if (frames == null || frames.Length == 0) break;
                            var img = SpawnImg(decGO.transform, wx, wy, CampLayout.TILE_PX, CampLayout.TILE_PX, new Color(0.6f, 0.8f, 1f));
                            img.sprite = frames[0];
                            animated.Add(new TerrainAnim { img = img, frames = frames, fps = 10f, phase = ct.phase[tx, ty], lastFrame = -1 });
                        }
                        else if (flattened)
                        {
                            if (flatRockSprite != null)
                                SpawnImg(decGO.transform, wx, wy, CampLayout.TILE_PX, CampLayout.TILE_PX, Color.white).sprite = flatRockSprite;
                        }
                        else
                        {
                            if (cfg.rockSprites == null || cfg.rockSprites.Length == 0) break;
                            int v = ct.variant[tx, ty] % cfg.rockSprites.Length;
                            if (cfg.rockSprites[v] == null) break;
                            float rox = (cfg.rockOffsetX != null && v < cfg.rockOffsetX.Length) ? cfg.rockOffsetX[v] : 0f;
                            float roy = (cfg.rockOffsetY != null && v < cfg.rockOffsetY.Length) ? cfg.rockOffsetY[v] : 0f;
                            SpawnImg(decGO.transform, wx + rox, wy + roy, CampLayout.TILE_PX, CampLayout.TILE_PX, Color.white).sprite = cfg.rockSprites[v];
                        }
                        break;
                    }
                    case TerrainCell.Water:
                    {
                        if (actualH > 0) break;
                        int v = ct.variant[tx, ty] % 4;
                        var frames = cfg.GetWaterFrames(v);
                        if (frames == null || frames.Length == 0) break;
                        var img = SpawnImg(decGO.transform, wx, wy, CampLayout.TILE_PX, CampLayout.TILE_PX, new Color(0.6f, 0.8f, 1f));
                        img.sprite = frames[0];
                        animated.Add(new TerrainAnim { img = img, frames = frames, fps = 10f, phase = ct.phase[tx, ty], lastFrame = -1 });
                        break;
                    }
                }
            }
        }
    }

    // ── Buildings ─────────────────────────────────────────────────────────────

    void BuildBuildings(Transform parent, float xOffset)
    {
        var ctr = MakeContainer(parent, "Buildings");
        buildingsContainer = ctr;

        // Insert before the unit layer when refreshing (it's already ordered correctly on first call)
        if (unitLayerTf != null)
            ctr.transform.SetSiblingIndex(unitLayerTf.GetSiblingIndex());

        if (player?.buildings == null) return;
        foreach (var b in player.buildings)
        {
            if (b.def == null) continue;

            // When the building's anchor tile is flattened, replace the sprite with overlay tiles.
            if (terrain != null && footprintOverlaySprite != null)
            {
                int tileH   = GetHeight(terrain, b.tileX, b.tileY);
                int visualH = FlatH(tileH);
                if (visualH < tileH)
                {
                    float elev = Mathf.Max(0, visualH - 1) * CampLayout.TILE_PX;
                    for (int di = 0; di < b.def.EffI; di++)
                    for (int dj = 0; dj < b.def.EffJ; dj++)
                    {
                        int ftx = b.tileX - b.def.EffAI + di;
                        int fty = b.tileY + b.def.EffAJ - dj;
                        if (ftx < 0 || ftx >= CampTerrain.SIZE || fty < 0 || fty >= CampTerrain.SIZE) continue;
                        float wy = CampLayout.TileCenterY(fty) + elev;
                        SpawnImg(ctr.transform, xOffset + CampLayout.TileCenterX(ftx), wy,
                            CampLayout.TILE_PX, CampLayout.TILE_PX, Color.white).sprite = footprintOverlaySprite;
                    }
                    continue;
                }
            }

            if (b.def.sprite == null) continue;
            float sw = b.def.sprite.rect.width;
            float sh = b.def.sprite.rect.height;
            var img = SpawnImg(ctr.transform,
                xOffset + b.worldX + b.def.spriteOffsetX,
                b.worldY + sh * 0.5f + b.def.spriteOffsetY,
                sw, sh, Color.white);
            img.sprite = b.def.sprite;
        }
    }

    void RebuildTerrainVisualization()
    {
        // Destroy all terrain layer children; new ones are created below.
        if (terrainLayersGO != null)
            for (int i = terrainLayersGO.transform.childCount - 1; i >= 0; i--)
                Destroy(terrainLayersGO.transform.GetChild(i).gameObject);

        // Tree/water animations reference Image components that are now destroyed; clear the list.
        animated.Clear();

        var cfg = Resources.Load<TerrainConfig>("TerrainConfig");
        if (terrain != null && terrainLayersGO != null)
        {
            BuildHeightMapTiles(terrainLayersGO.transform, terrain, debugXOffset, cfg);
            BuildTerrain(terrainLayersGO.transform, terrain, debugXOffset, cfg);
        }

        RefreshBuildings();
        RefreshFlatOverlay();
    }

    public void RefreshBuildings()
    {
        if (buildingsContainer != null)
        {
            Destroy(buildingsContainer);
            buildingsContainer = null;
        }
        if (world != null)
            BuildBuildings(world, debugXOffset);
        RefreshFlatOverlay();
    }

    // ── Unit cells ─────────────────────────────────────────────────────────────

    void BuildUnitCells(Transform parent)
    {
        for (int c = 0; c < 9; c++)
        for (int r = 0; r < 9; r++)
            BuildCell(parent, c, r);
    }

    void BuildCell(Transform parent, int col, int row)
    {
        var go = new GameObject($"Cell_{col}_{row}", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(CampLayout.UNIT_STRIDE, CampLayout.UNIT_STRIDE);
        rt.anchoredPosition = new Vector2(CampLayout.UnitCenterX(col), CampLayout.UnitCenterY(row));
        RefreshCellGO(rt, col, row);
    }

    public void RefreshCell(int col, int row)
    {
        if (unitLayerTf == null) return;
        var name = $"Cell_{col}_{row}";
        var tf = unitLayerTf.Find(name);
        if (tf == null) return;
        RefreshCellGO(tf.GetComponent<RectTransform>(), col, row);
    }

    void RefreshCellGO(RectTransform rt, int col, int row)
    {
        bool hadUnit = rt.Find("Spr") != null;  // true only if a unit sprite was already present
        for (int i = rt.childCount - 1; i >= 0; i--)
            Destroy(rt.GetChild(i).gameObject);
        if (player == null) return;
        var unit = player.board[col, row];
        if (unit == null) return;

        var frames = unit.definition.idleFrames;
        if (frames == null || frames.Length == 0 || frames[0] == null) return;
        float w = frames[0].rect.width  * spriteScale;
        float h = frames[0].rect.height * spriteScale;

        var sGO = new GameObject("Spr", typeof(RectTransform));
        sGO.transform.SetParent(rt, false);
        var sRT = sGO.GetComponent<RectTransform>();
        sRT.anchorMin = sRT.anchorMax = new Vector2(0.5f, 0.5f);
        sRT.pivot     = new Vector2(0.5f, 0.5f);
        sRT.sizeDelta = new Vector2(w, h);
        sRT.anchoredPosition = Vector2.zero;
        var img = sGO.AddComponent<Image>();
        img.sprite = frames[0];
        img.raycastTarget = false;
        sGO.AddComponent<SpriteAnimator>().Play(frames, 8f);

        // Spawn dust FX when a unit first appears (not on a swap/refresh of an existing one).
        if (!hadUnit)
        {
            var cfg = Resources.Load<TerrainConfig>("TerrainConfig");
            if (cfg != null && cfg.spawnDustFrames != null && cfg.spawnDustFrames.Length > 0)
            {
                var dGO = new GameObject("SpawnDust", typeof(RectTransform));
                dGO.transform.SetParent(rt, false);
                var dRT = dGO.GetComponent<RectTransform>();
                dRT.anchorMin = dRT.anchorMax = new Vector2(0.5f, 0.5f);
                dRT.pivot     = new Vector2(0.5f, 0.5f);
                dRT.sizeDelta = new Vector2(CampLayout.TILE_PX, CampLayout.TILE_PX);
                dRT.anchoredPosition = Vector2.zero;
                var dImg = dGO.AddComponent<Image>();
                dImg.raycastTarget = false;
                dGO.AddComponent<SpriteAnimator>().PlayOnce(cfg.spawnDustFrames, 15f);
            }
        }
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    public void SetSelection(int col, int row)
    {
        if (selectionHighlight == null) return;
        if (col < 0) { selectionHighlight.gameObject.SetActive(false); return; }
        selectionHighlight.gameObject.SetActive(true);
        selectionHighlight.rectTransform.anchoredPosition =
            new Vector2(CampLayout.UnitCenterX(col), CampLayout.UnitCenterY(row));
    }

    // ── Pan / zoom ─────────────────────────────────────────────────────────────

    void SetInitialCamera()
    {
        Canvas.ForceUpdateCanvases();
        ApplyZoom();
        float targetX = CampLayout.UnitCenterX(4);
        float targetY = CampLayout.UnitCenterY(4);
        var vp = viewport.rect;
        world.anchoredPosition = new Vector2(vp.width * 0.5f - targetX * zoom,
                                             vp.height * 0.5f - targetY * zoom);
        ClampWorldPosition();
    }

    public void OnScroll(PointerEventData data)
    {
        float factor  = data.scrollDelta.y > 0 ? 1.12f : 1f / 1.12f;
        float newZoom = Mathf.Clamp(zoom * factor, minZoom, maxZoom);
        if (Mathf.Approximately(newZoom, zoom)) return;
        Vector2 local;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            viewport, data.position, data.pressEventCamera, out local);
        Vector2 wc = (local - world.anchoredPosition) / zoom;
        zoom = newZoom;
        ApplyZoom();
        world.anchoredPosition = local - wc * zoom;
        ClampWorldPosition();
    }

    public void OnPointerDown(PointerEventData data)
    {
        // Don't start a pan-drag when the player is in placement mode and right-clicks to cancel.
        if (GameManager.Instance?.IsPendingPlacement == true &&
            data.button == PointerEventData.InputButton.Right)
            return;

        if (data.button == PointerEventData.InputButton.Middle ||
            data.button == PointerEventData.InputButton.Right)
        {
            isDragging = true;
            dragStartScreen     = data.position;
            worldPosAtDragStart = world.anchoredPosition;
        }
    }

    public void OnPointerUp(PointerEventData data) => isDragging = false;

    public void OnPointerEnter(PointerEventData data) => Cursor.visible = false;
    public void OnPointerExit(PointerEventData data)  => Cursor.visible = true;
    void OnDestroy() => Cursor.visible = true;

    public void OnDrag(PointerEventData data)
    {
        if (!isDragging) return;
        world.anchoredPosition = worldPosAtDragStart + (data.position - dragStartScreen);
        ClampWorldPosition();
    }

    public void OnPointerClick(PointerEventData data)
    {
        if (data.dragging) return;

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                viewport, data.position, data.pressEventCamera, out local)) return;
        // viewport.pivot defaults to (0.5,0.5), so local-space origin is at viewport center.
        // world anchor is at viewport.rect.min, so we must subtract rect.min to get world coords.
        Vector2 worldPos = (local - world.anchoredPosition - (Vector2)viewport.rect.min) / zoom;

        // ── Debug tile selection (left-click, works regardless of interactive mode) ──
        if (data.button == PointerEventData.InputButton.Left
            && terrain != null && tileDebugHighlight != null && tileDebugTooltip != null)
        {
            int dtx = Mathf.FloorToInt((worldPos.x - debugXOffset) / CampLayout.TILE_PX);
            int dty = Mathf.FloorToInt(worldPos.y / CampLayout.TILE_PX);

            // Elevated tiles display (h-1) rows above their logical row.
            // h=3 at ty shows at ty+2; h=2 at ty shows at ty+1.
            // Prefer the deeper remap (h=3) so clicking a peak always selects the peak tile.
            if (dty >= 2 && dtx >= 0 && dtx < CampTerrain.SIZE
                && terrain.heightMap[dtx, dty - 2] >= 3)
                dty -= 2;
            else if (dty > 0 && dtx >= 0 && dtx < CampTerrain.SIZE
                     && terrain.heightMap[dtx, dty - 1] >= 2)
                dty--;

            if (dtx >= 0 && dtx < CampTerrain.SIZE && dty >= 0 && dty < CampTerrain.SIZE)
            {
                byte h = terrain.heightMap[dtx, dty];
                var  c = terrain.grid[dtx, dty];
                string cellInfo = c == TerrainCell.Clear  ? "—"
                                : c == TerrainCell.Tree   ? $"Tree  (variant {terrain.variant[dtx,dty]})"
                                : c == TerrainCell.Rock   ? $"Rock  (variant {terrain.variant[dtx,dty]})"
                                : c == TerrainCell.Water  ? "Water decoration"
                                : c.ToString();

                tileDebugTooltip.text = $"Tile ({dtx}, {dty})    h = {h}\n{cellInfo}";
                tileDebugTooltip.transform.parent.gameObject.SetActive(true);

                float highlightY = CampLayout.TileCenterY(dty)
                                 + (h >= 2 ? (h - 1) * CampLayout.TILE_PX : 0f);
                tileDebugHighlight.rectTransform.anchoredPosition =
                    new Vector2(debugXOffset + CampLayout.TileCenterX(dtx), highlightY);
                tileDebugHighlight.gameObject.SetActive(true);
            }
        }

        // ── Placement mode ────────────────────────────────────────────────────
        var gm = GameManager.Instance;
        if (interactive && gm != null && gm.IsPendingPlacement)
        {
            if (data.button == PointerEventData.InputButton.Right)
            {
                gm.CancelPlacement();
                return;
            }
            if (data.button == PointerEventData.InputButton.Left)
            {
                int ptx = Mathf.FloorToInt(worldPos.x / CampLayout.TILE_PX);
                int pty = Mathf.FloorToInt(worldPos.y / CampLayout.TILE_PX);

                if (gm.PendingBuilding != null)
                {
                    gm.ConfirmBuildingPlacement(ptx, pty);
                }
                else if (gm.PendingUnitSlot >= 0)
                {
                    if (ptx >= CampTerrain.UNIT_TILE_X0 && pty >= CampTerrain.UNIT_TILE_Y0)
                    {
                        int col = (ptx - CampTerrain.UNIT_TILE_X0) / CampTerrain.UNIT_TILE_STRIDE;
                        int row = (pty - CampTerrain.UNIT_TILE_Y0) / CampTerrain.UNIT_TILE_STRIDE;
                        if (col >= 0 && col <= 8 && row >= 0 && row <= 8)
                            gm.ConfirmUnitPlacement(col, row);
                    }
                }
                return;
            }
        }

        // ── Unit cell selection (requires interactive mode) ──
        if (!interactive) return;
        if (data.button != PointerEventData.InputButton.Left &&
            data.button != PointerEventData.InputButton.Right) return;

        int tx = Mathf.FloorToInt(worldPos.x / CampLayout.TILE_PX);
        int ty = Mathf.FloorToInt(worldPos.y / CampLayout.TILE_PX);

        if (tx < CampTerrain.UNIT_TILE_X0 || ty < CampTerrain.UNIT_TILE_Y0) return;

        int col2 = (tx - CampTerrain.UNIT_TILE_X0) / CampTerrain.UNIT_TILE_STRIDE;
        int row2 = (ty - CampTerrain.UNIT_TILE_Y0) / CampTerrain.UNIT_TILE_STRIDE;
        if (col2 < 0 || col2 > 8 || row2 < 0 || row2 > 8) return;

        // Unit footprint is 64×64 (one tile) — only the centre tile of each 3×3 cell responds.
        int cx = CampTerrain.UNIT_TILE_X0 + col2 * CampTerrain.UNIT_TILE_STRIDE + CampTerrain.UNIT_TILE_STRIDE / 2;
        int cy = CampTerrain.UNIT_TILE_Y0 + row2 * CampTerrain.UNIT_TILE_STRIDE + CampTerrain.UNIT_TILE_STRIDE / 2;
        if (tx != cx || ty != cy) return;

        if (data.button == PointerEventData.InputButton.Right)
            OnCellRightClick?.Invoke(col2, row2);
        else
            OnCellClicked?.Invoke(col2, row2);
    }

    // ── Building footprint pool ───────────────────────────────────────────────

    Image LeaseFootprintTile()
    {
        if (footprintUsed < footprintPool.Count)
        {
            var t = footprintPool[footprintUsed++];
            t.gameObject.SetActive(true);
            return t;
        }
        var go = new GameObject("FP", typeof(RectTransform));
        go.transform.SetParent(footprintLayer, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(CampLayout.TILE_PX, CampLayout.TILE_PX);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        footprintPool.Add(img);
        footprintUsed++;
        return img;
    }

    void ReturnAllFootprintTiles()
    {
        for (int i = 0; i < footprintUsed; i++)
            footprintPool[i].gameObject.SetActive(false);
        footprintUsed = 0;
    }

    void ShowFootprintTile(int tx, float worldY, Color color)
    {
        var img = LeaseFootprintTile();
        img.sprite = footprintOverlaySprite;
        img.color  = color;
        img.rectTransform.anchoredPosition =
            new Vector2(debugXOffset + CampLayout.TileCenterX(tx), worldY);
    }

    void ShowFootprintTile(int tx, int ty, Color color) =>
        ShowFootprintTile(tx, CampLayout.TileCenterY(ty), color);

    // ── Helpers ───────────────────────────────────────────────────────────────

    void ApplyZoom() => world.localScale = new Vector3(zoom, zoom, 1);

    void ClampWorldPosition()
    {
        if (world == null || viewport == null) return;
        var vp   = viewport.rect;
        var sz   = new Vector2(world.sizeDelta.x * zoom, world.sizeDelta.y * zoom);
        var pos  = world.anchoredPosition;
        float px = Mathf.Max(vp.width  * 0.25f, sz.x * 0.25f);
        float py = Mathf.Max(vp.height * 0.25f, sz.y * 0.25f);
        pos.x = Mathf.Clamp(pos.x, -sz.x + px, vp.width  - px);
        pos.y = Mathf.Clamp(pos.y, -sz.y + py, vp.height - py);
        world.anchoredPosition = pos;
    }

    // Spawns a ground panel. When tile is provided and readable, extracts its pixels into
    // a WrapMode.Repeat Texture2D and uses RawImage+uvRect for reliable sub-sprite tiling.
    // Image.Type.Tiled silently falls back to stretched when the atlas texture isn't readable,
    // so we use the RawImage path instead.
    static void SpawnTiledGround(Transform parent, float cx, float cy, float w, float h,
                                  Color fallbackCol, Sprite tile)
    {
        var go = new GameObject("Ground", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(cx, cy);

        if (tile != null && tile.texture != null && tile.texture.isReadable)
        {
            var rect = tile.textureRect;
            int tw = Mathf.Max(1, (int)rect.width);
            int th = Mathf.Max(1, (int)rect.height);
            var tileTex = new Texture2D(tw, th, TextureFormat.RGBA32, false);
            tileTex.filterMode = FilterMode.Point;
            tileTex.wrapMode   = TextureWrapMode.Repeat;
            tileTex.SetPixels(tile.texture.GetPixels((int)rect.x, (int)rect.y, tw, th));
            tileTex.Apply(false);

            var raw = go.AddComponent<RawImage>();
            raw.texture       = tileTex;
            raw.uvRect        = new Rect(0, 0, w / tw, h / th);
            raw.color         = Color.white;
            raw.raycastTarget = false;
        }
        else
        {
            var img = go.AddComponent<Image>();
            img.color         = fallbackCol;
            img.raycastTarget = false;
        }
    }

    static GameObject MakeContainer(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.sizeDelta = Vector2.zero;
        return go;
    }

    static Image SpawnImg(Transform parent, float wx, float wy, float w, float h, Color col)
    {
        var go = new GameObject("T", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(wx, wy);
        var img = go.AddComponent<Image>();
        img.color = col;
        img.raycastTarget = false;  // all world-space images are visual only
        return img;
    }

    // ── Debug layer menu ──────────────────────────────────────────────────────

    void BuildDebugMenu(Transform vpParent)
    {
        const float W       = 192f;
        const float HDR_H   = 28f;
        const float ROW_H   = 26f;
        const int   ROWS    = 11;

        var panelGO = new GameObject("DebugLayersPanel", typeof(RectTransform));
        panelGO.transform.SetParent(vpParent, false);
        var panelRT = panelGO.GetComponent<RectTransform>();
        panelRT.anchorMin = panelRT.anchorMax = new Vector2(1f, 1f);
        panelRT.pivot     = new Vector2(1f, 1f);
        panelRT.anchoredPosition = new Vector2(-8f, -8f);
        panelRT.sizeDelta = new Vector2(W, HDR_H + ROWS * ROW_H + 6f);
        var bg = panelGO.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.82f);

        // Header
        var hdrGO = new GameObject("Hdr", typeof(RectTransform));
        hdrGO.transform.SetParent(panelGO.transform, false);
        var hdrRT = hdrGO.GetComponent<RectTransform>();
        hdrRT.anchorMin = new Vector2(0f, 1f);
        hdrRT.anchorMax = new Vector2(1f, 1f);
        hdrRT.pivot     = new Vector2(0f, 1f);
        hdrRT.anchoredPosition = Vector2.zero;
        hdrRT.sizeDelta = new Vector2(0f, HDR_H);
        var hdrTmp = hdrGO.AddComponent<TextMeshProUGUI>();
        hdrTmp.text      = "Debug Layers  [D]";
        hdrTmp.fontSize  = 13f;
        hdrTmp.fontStyle = FontStyles.Bold;
        hdrTmp.color     = new Color(1f, 0.85f, 0.3f);
        hdrTmp.alignment = TextAlignmentOptions.MidlineLeft;
        hdrTmp.margin    = new Vector4(8f, 0f, 0f, 0f);
        hdrTmp.raycastTarget = false;

        float y = -HDR_H;
        void Row(string label, GameObject target) =>
            AddDebugToggle(panelGO.transform, label, target, ref y);

        Row("Water BG",    waterContainer);
        Row("Flat Ground", flatGroundContainer);
        Row("H=2 Shadows", shadowH2Container);
        Row("H=3 Shadows", shadowH3Container);
        Row("H=2 Cliffs",  cliffH2Container);
        Row("H=2 Ground",  elevatedH2Container);
        Row("H=3 Cliffs",  cliffH3Container);
        Row("H=3 Ground",  elevatedH3Container);
        Row("Decorations", decorationsContainer);
        Row("Buildings",   buildingsContainer);
        Row("Fog of War",  fogLayerGO);

        panelGO.SetActive(false);
        debugMenuPanel = panelGO;
    }

    void AddDebugToggle(Transform parent, string label, GameObject target, ref float y)
    {
        const float ROW_H  = 26f;
        const float CB     = 16f;
        const float PAD    = 8f;

        var rowGO = new GameObject(label, typeof(RectTransform));
        rowGO.transform.SetParent(parent, false);
        var rowRT = rowGO.GetComponent<RectTransform>();
        rowRT.anchorMin = new Vector2(0f, 1f);
        rowRT.anchorMax = new Vector2(1f, 1f);
        rowRT.pivot     = new Vector2(0f, 1f);
        rowRT.anchoredPosition = new Vector2(0f, y);
        rowRT.sizeDelta = new Vector2(0f, ROW_H);

        // Checkbox background
        var cbGO = new GameObject("CB", typeof(RectTransform));
        cbGO.transform.SetParent(rowGO.transform, false);
        var cbRT = cbGO.GetComponent<RectTransform>();
        cbRT.anchorMin = cbRT.anchorMax = new Vector2(0f, 0.5f);
        cbRT.pivot = new Vector2(0f, 0.5f);
        cbRT.anchoredPosition = new Vector2(PAD, 0f);
        cbRT.sizeDelta = new Vector2(CB, CB);
        var cbImg = cbGO.AddComponent<Image>();
        cbImg.color = new Color(0.85f, 0.85f, 0.85f);

        // Checkmark
        var ckGO = new GameObject("CK", typeof(RectTransform));
        ckGO.transform.SetParent(cbGO.transform, false);
        var ckRT = ckGO.GetComponent<RectTransform>();
        ckRT.anchorMin = Vector2.zero;
        ckRT.anchorMax = Vector2.one;
        ckRT.offsetMin = new Vector2(2f, 2f);
        ckRT.offsetMax = new Vector2(-2f, -2f);
        var ckImg = ckGO.AddComponent<Image>();
        ckImg.color = new Color(0.15f, 0.65f, 0.25f);

        // Label
        var lblGO = new GameObject("Lbl", typeof(RectTransform));
        lblGO.transform.SetParent(rowGO.transform, false);
        var lblRT = lblGO.GetComponent<RectTransform>();
        lblRT.anchorMin = Vector2.zero;
        lblRT.anchorMax = Vector2.one;
        lblRT.offsetMin = new Vector2(PAD + CB + 5f, 0f);
        lblRT.offsetMax = new Vector2(-PAD, 0f);
        var tmp = lblGO.AddComponent<TextMeshProUGUI>();
        tmp.text      = label;
        tmp.fontSize  = 12f;
        tmp.color     = Color.white;
        tmp.alignment = TextAlignmentOptions.MidlineLeft;
        tmp.raycastTarget = false;

        var toggle = rowGO.AddComponent<Toggle>();
        toggle.targetGraphic = cbImg;
        toggle.graphic       = ckImg;
        toggle.isOn          = true;
        if (target != null)
            toggle.onValueChanged.AddListener(val => target.SetActive(val));

        y -= ROW_H;
    }

    // ── Fog visibility array ───────────────────────────────────────────────────

    void InitBaseFogVisibility()
    {
        int size = CampTerrain.SIZE;
        fogVisibility = new float[size, size];
        // Everything starts fully fogged; buildings and units reveal their surroundings.
    }

    // Max-blend a circular reveal (in tile cx/cy units) into the visibility array.
    void AddFogReveal(float tcx, float tcy, float radius)
    {
        const float BLUR = 1.5f;
        int size = CampTerrain.SIZE;
        for (int ty = 0; ty < size; ty++)
        for (int tx = 0; tx < size; tx++)
        {
            float cx   = tx + 0.5f;
            float cy   = ty + 0.5f;
            float dist = Mathf.Sqrt((cx - tcx) * (cx - tcx) + (cy - tcy) * (cy - tcy));
            float vis  = 1f - Mathf.Clamp01((dist - (radius - BLUR)) / BLUR);
            if (vis > fogVisibility[tx, ty]) fogVisibility[tx, ty] = vis;
        }
    }

    // Rebuild visibility from buildings + placed units, then repaint texture.
    public void RebuildFog()
    {
        if (fogTex == null) return;

        InitBaseFogVisibility();

        // Buildings reveal fog based on their fogRevealRadius.
        if (player?.buildings != null)
        {
            foreach (var b in player.buildings)
            {
                if (b.def == null || b.def.fogRevealRadius <= 0f) continue;
                float tcx = b.worldX / CampLayout.TILE_PX;
                float tcy = b.worldY / CampLayout.TILE_PX;
                AddFogReveal(tcx, tcy, b.def.fogRevealRadius);
            }
        }

        if (player?.board != null)
        {
            const float UNIT_REVEAL_R = 4f;
            const int   BOARD_W = 9;
            for (int r = 0; r < BOARD_W; r++)
            for (int c = 0; c < BOARD_W; c++)
            {
                if (player.board[c, r] == null) continue;
                float tcx = CampTerrain.UNIT_TILE_X0 + c + 0.5f;
                float tcy = CampTerrain.UNIT_TILE_Y0 + r + 0.5f;
                AddFogReveal(tcx, tcy, UNIT_REVEAL_R);
            }
        }

        WriteFogPixels();
    }

    void WriteFogPixels()
    {
        const int SCALE  = 8;
        const int EXTEND = 5;
        int tiles = CampTerrain.SIZE + 2 * EXTEND;
        int texW  = tiles * SCALE;
        int texH  = tiles * SCALE;
        var pixels = new Color32[texW * texH];

        for (int py = 0; py < texH; py++)
        for (int px = 0; px < texW; px++)
        {
            float tx = (px + 0.5f) / SCALE - EXTEND;
            float ty = (py + 0.5f) / SCALE - EXTEND;
            float vis = SampleFogVisibility(tx, ty);
            pixels[py * texW + px] = new Color32(0, 0, 0, (byte)((1f - vis) * 255f));
        }

        fogTex.SetPixels32(pixels);
        fogTex.Apply(false, false);
    }

    float SampleFogVisibility(float tx, float ty)
    {
        if (fogVisibility == null) return 0f;
        // fogVisibility[i,j] lives at tile-centre (i+0.5, j+0.5); shift to array-index space.
        float sx = tx - 0.5f;
        float sy = ty - 0.5f;
        int   ix = Mathf.FloorToInt(sx);
        int   iy = Mathf.FloorToInt(sy);
        float fx = sx - ix;
        float fy = sy - iy;
        float v00 = GetTileVis(ix,   iy  );
        float v10 = GetTileVis(ix+1, iy  );
        float v01 = GetTileVis(ix,   iy+1);
        float v11 = GetTileVis(ix+1, iy+1);
        return Mathf.Lerp(Mathf.Lerp(v00, v10, fx), Mathf.Lerp(v01, v11, fx), fy);
    }

    float GetTileVis(int tx, int ty)
    {
        if (fogVisibility == null
         || tx < 0 || tx >= CampTerrain.SIZE
         || ty < 0 || ty >= CampTerrain.SIZE) return 0f;
        return fogVisibility[tx, ty];
    }

    void BuildFlatOverlay(Transform parent, float xOffset)
    {
        var go = MakeContainer(parent, "FlatOverlay");
        flatOverlayContainer = go;
        go.SetActive(false);

        var treeTex  = Resources.Load<Texture2D>("tree_overlay");
        var rockTex  = Resources.Load<Texture2D>("rock_overlay");
        var greenTex = Resources.Load<Texture2D>("green_overlay");

        Sprite MakeSprite(Texture2D tex)
        {
            if (tex == null) return null;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height),
                                 new Vector2(0.5f, 0.5f), 1f);
        }

        Sprite treeSprite  = MakeSprite(treeTex);
        Sprite rockSprite  = MakeSprite(rockTex);
        Sprite greenSprite = MakeSprite(greenTex);

        float px = CampLayout.TILE_PX;

        // ── Terrain feature tiles ────────────────────────────────────────────
        // Use the SAME elevation formula as BuildDecorations so overlays line up with sprites.
        for (int ty = 0; ty < CampTerrain.SIZE; ty++)
        for (int tx = 0; tx < CampTerrain.SIZE; tx++)
        {
            Sprite spr;
            switch (terrain.grid[tx, ty])
            {
                case TerrainCell.Tree:  spr = treeSprite; break;
                case TerrainCell.Rock:  spr = rockSprite; break;
                default: continue;
            }
            if (spr == null) continue;
            // Use flattened visual height so overlay tiles match the lowered terrain.
            float wy = CampLayout.TileCenterY(ty)
                     + Mathf.Max(0f, FlatH(GetHeight(terrain, tx, ty)) - 1) * CampLayout.TILE_PX;
            var img = SpawnImg(go.transform, xOffset + CampLayout.TileCenterX(tx), wy, px, px, Color.white);
            img.sprite = spr;
        }

        // ── Building footprint tiles (green) ────────────────────────────────
        // Use the building's stored worldY so elevation matches the actual sprite position.
        if (greenSprite != null && player?.buildings != null)
        {
            foreach (var b in player.buildings)
            {
                if (b.def == null) continue;
                // Use flattened visual elevation so overlay tiles match lowered terrain.
                int   anchorH  = terrain != null ? GetHeight(terrain, b.tileX, b.tileY) : 1;
                float elev     = Mathf.Max(0f, FlatH(anchorH) - 1) * CampLayout.TILE_PX;
                for (int di = 0; di < b.def.EffI; di++)
                for (int dj = 0; dj < b.def.EffJ; dj++)
                {
                    int ftx = b.tileX - b.def.EffAI + di;
                    int fty = b.tileY + b.def.EffAJ - dj;
                    if (ftx < 0 || ftx >= CampTerrain.SIZE || fty < 0 || fty >= CampTerrain.SIZE) continue;
                    float wy = CampLayout.TileCenterY(fty) + elev;
                    var img = SpawnImg(go.transform, xOffset + CampLayout.TileCenterX(ftx), wy, px, px, Color.white);
                    img.sprite = greenSprite;
                }
            }
        }
    }

    void RefreshFlatOverlay()
    {
        if (flatOverlayContainer != null)
            Destroy(flatOverlayContainer);
        flatOverlayContainer = null;
        if (world != null && terrain != null)
        {
            BuildFlatOverlay(world, debugXOffset);
            flatOverlayContainer?.SetActive(showFlatOverlay);
        }
    }

    void BuildFogOfWar(Transform parent, float xOffset)
    {
        const int   SCALE  = 8;
        const int   EXTEND = 5;

        int   tiles  = CampTerrain.SIZE + 2 * EXTEND;
        int   texSz  = tiles * SCALE;
        float sizePx = tiles * CampLayout.TILE_PX;

        fogTex            = new Texture2D(texSz, texSz, TextureFormat.RGBA32, false);
        fogTex.filterMode = FilterMode.Bilinear;
        fogTex.wrapMode   = TextureWrapMode.Clamp;
        RebuildFog();

        fogLayerGO = new GameObject("FogOfWar", typeof(RectTransform));
        var fogGO = fogLayerGO;
        fogGO.transform.SetParent(parent, false);
        var rt = fogGO.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.sizeDelta        = new Vector2(sizePx, sizePx);
        rt.anchoredPosition = new Vector2(xOffset - EXTEND * CampLayout.TILE_PX,
                                          -EXTEND * CampLayout.TILE_PX);

        var img = fogGO.AddComponent<RawImage>();
        img.texture       = fogTex;
        img.raycastTarget = false;
    }

    // ── Update ────────────────────────────────────────────────────────────────

    void Update()
    {
        if (needsInitialCamera && viewport != null && viewport.rect.width > 1f)
        {
            needsInitialCamera = false;
            SetInitialCamera();
        }

        if (debugMenuPanel != null &&
            UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.dKey.wasPressedThisFrame)
            debugMenuPanel.SetActive(!debugMenuPanel.activeSelf);

        // C key toggles world-coordinate overlay
        if (UnityEngine.InputSystem.Keyboard.current != null &&
            UnityEngine.InputSystem.Keyboard.current.cKey.wasPressedThisFrame)
        {
            showCoords = !showCoords;
            if (coordOverlay != null) coordOverlay.gameObject.SetActive(showCoords);
        }

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null)
        {
            // F key toggles fog of war
            if (kb.fKey.wasPressedThisFrame && fogLayerGO != null)
                fogLayerGO.SetActive(!fogLayerGO.activeSelf);

            // Page Down / Page Up lower or raise terrain view (flatten top layers)
            if (kb.pageDownKey.wasPressedThisFrame && terrainFlattenLevel < 2)
            {
                terrainFlattenLevel++;
                RebuildTerrainVisualization();
            }
            if (kb.pageUpKey.wasPressedThisFrame && terrainFlattenLevel > 0)
            {
                terrainFlattenLevel--;
                RebuildTerrainVisualization();
            }

            // V key toggles flat tile overlay (hides decorations + buildings, shows coloured tiles)
            if (kb.vKey.wasPressedThisFrame)
            {
                showFlatOverlay = !showFlatOverlay;
                if (decorationsContainer != null) decorationsContainer.SetActive(!showFlatOverlay);
                if (buildingsContainer  != null) buildingsContainer .SetActive(!showFlatOverlay);
                if (flatOverlayContainer != null) flatOverlayContainer.SetActive(showFlatOverlay);
            }
        }

        if (showCoords && coordOverlay != null && viewport != null && world != null)
        {
            Vector2 screenPos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();

            // viewport anchor=(0,0)→bottom-left, pivot=(0.5,0.5)→center, so local (0,0)=centre.
            // overlay anchor=(0,0), pivot=(0,1): anchoredPosition is bottom-left of parent → pivot.
            // Shift local by half-size to get bottom-left offset, then add cursor gap.
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                viewport, screenPos, null, out Vector2 vpLocal);
            var vr   = viewport.rect;
            // vpLocal is relative to viewport pivot (center). Convert to bottom-left space:
            Vector2 bl = vpLocal - vr.min;   // now (0,0) = bottom-left of viewport
            float ox = Mathf.Clamp(bl.x + 16f, 0f, vr.width  - coordOverlay.sizeDelta.x);
            float oy = Mathf.Clamp(bl.y - 16f, coordOverlay.sizeDelta.y, vr.height);
            coordOverlay.anchoredPosition = new Vector2(ox, oy);

            Vector2 wp = (vpLocal - world.anchoredPosition) / zoom;
            int  tileX = Mathf.FloorToInt(wp.x / CampLayout.TILE_PX);
            int  tileY = Mathf.FloorToInt(wp.y / CampLayout.TILE_PX);
            float cxv  = wp.x / CampLayout.TILE_PX;
            float cyv  = wp.y / CampLayout.TILE_PX;
            coordLabel.text =
                $"world  ({wp.x:F0}, {wp.y:F0})\n" +
                $"tile   ({tileX}, {tileY})\n" +
                $"cx/cy  ({cxv:F2}, {cyv:F2})";
        }

        // ── Mouse tile (shared by cursor + footprint) ─────────────────────────
        int mouseTileX = -1, mouseTileY = -1;
        if (viewport != null && world != null && UnityEngine.InputSystem.Mouse.current != null)
        {
            var screenPos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                viewport, screenPos, null, out Vector2 vpLocal);
            Vector2 wp = (vpLocal - world.anchoredPosition - (Vector2)viewport.rect.min) / zoom;
            mouseTileX = Mathf.FloorToInt((wp.x - debugXOffset) / CampLayout.TILE_PX);
            mouseTileY = Mathf.FloorToInt(wp.y / CampLayout.TILE_PX);
        }

        // ── Tile cursor ───────────────────────────────────────────────────────
        if (tileCursorHighlight != null)
        {
            bool onMap = mouseTileX >= 0 && mouseTileX < CampTerrain.SIZE
                      && mouseTileY >= 0 && mouseTileY < CampTerrain.SIZE;
            if (onMap)
            {
                tileCursorHighlight.rectTransform.anchoredPosition = new Vector2(
                    debugXOffset + CampLayout.TileCenterX(mouseTileX),
                    CampLayout.TileCenterY(mouseTileY));
                tileCursorHighlight.gameObject.SetActive(true);
            }
            else
            {
                tileCursorHighlight.gameObject.SetActive(false);
            }
        }

        // ── Building placement ghost (world-space, snaps to anchor tile) ─────
        if (buildingGhostGO != null)
        {
            var gmGhost = GameManager.Instance;
            var def = interactive ? gmGhost?.PendingBuilding : null;

            // Reset last-valid tracker whenever the pending building changes.
            if (def != lastValidPlaceDef)
            {
                lastValidPlaceDef   = def;
                lastValidPlaceTileX = -1;
                lastValidPlaceTileY = -1;
            }

            // Advance last-valid position if the cursor is over a placeable tile.
            if (def != null
                && mouseTileX >= 0 && mouseTileX < CampTerrain.SIZE
                && mouseTileY >= 0 && mouseTileY < CampTerrain.SIZE
                && IsPlaceable(def, mouseTileX, mouseTileY))
            {
                lastValidPlaceTileX = mouseTileX;
                lastValidPlaceTileY = mouseTileY;
            }

            bool ghostVisible = def?.sprite != null && lastValidPlaceTileX >= 0;
            if (ghostVisible)
            {
                float sw = def.sprite.rect.width;
                float sh = def.sprite.rect.height;
                float elev = terrain != null
                    ? Mathf.Max(0f, FlatH(GetHeight(terrain, lastValidPlaceTileX, lastValidPlaceTileY)) - 1) * CampLayout.TILE_PX
                    : 0f;
                var bgRT = buildingGhostGO.GetComponent<RectTransform>();
                bgRT.sizeDelta        = new Vector2(sw, sh);
                bgRT.anchoredPosition = new Vector2(
                    debugXOffset + CampLayout.TileCenterX(lastValidPlaceTileX) + def.spriteOffsetX,
                    CampLayout.TileCenterY(lastValidPlaceTileY) + elev + sh * 0.5f + def.spriteOffsetY);
                buildingGhostImg.sprite = def.sprite;
                buildingGhostGO.SetActive(true);
            }
            else
            {
                buildingGhostGO.SetActive(false);
            }
        }

        // ── Building footprints (placement mode only) ─────────────────────────
        ReturnAllFootprintTiles();
        var gmFp = GameManager.Instance;
        if (interactive && footprintLayer != null
            && gmFp != null && gmFp.PendingBuilding != null
            && player?.buildings != null && terrain != null)
        {
            const int S = CampTerrain.SIZE;
            var pending = gmFp.PendingBuilding;

            // ── Existing building footprints (blue-tinted overlay) ─────────
            var existingColor = new Color(0.6f, 0.85f, 1f, 0.55f);
            foreach (var b in player.buildings)
            {
                if (b.def == null) continue;
                // If the building's anchor tile is on flattened terrain, BuildBuildings already
                // drew overlay tiles for it in buildingsContainer — don't draw a second set here.
                if (terrain != null)
                {
                    int ancH = GetHeight(terrain, b.tileX, b.tileY);
                    if (FlatH(ancH) < ancH) continue;
                }
                float elev = b.worldY - CampLayout.TileCenterY(b.tileY);
                for (int di = 0; di < b.def.EffI; di++)
                for (int dj = 0; dj < b.def.EffJ; dj++)
                {
                    int ftx = b.tileX - b.def.EffAI + di;
                    int fty = b.tileY + b.def.EffAJ - dj;
                    if (ftx >= 0 && ftx < S && fty >= 0 && fty < S)
                        ShowFootprintTile(ftx, CampLayout.TileCenterY(fty) + elev, existingColor);
                }
            }

            // ── Pending building footprint (white) — only shown at last valid position ──
            if (lastValidPlaceTileX >= 0)
            {
                int fi = pending.EffI, fj = pending.EffJ;
                int ai = pending.EffAI, aj = pending.EffAJ;
                float pendingElev = terrain != null
                    ? Mathf.Max(0f, FlatH(GetHeight(terrain, lastValidPlaceTileX, lastValidPlaceTileY)) - 1) * CampLayout.TILE_PX
                    : 0f;
                var tileColor = new Color(1f, 1f, 1f, 0.85f);
                for (int di = 0; di < fi; di++)
                for (int dj = 0; dj < fj; dj++)
                {
                    int ftx = lastValidPlaceTileX - ai + di;
                    int fty = lastValidPlaceTileY + aj - dj;
                    if (ftx >= 0 && ftx < S && fty >= 0 && fty < S)
                        ShowFootprintTile(ftx, CampLayout.TileCenterY(fty) + pendingElev, tileColor);
                }
            }
        }

        float t = Time.time;
        for (int i = 0; i < animated.Count; i++)
        {
            var a = animated[i];
            if (a.img == null || a.frames == null || a.frames.Length == 0) continue;
            int fr = (int)((t * a.fps + a.phase) % a.frames.Length);
            if (fr == a.lastFrame) continue;
            a.lastFrame = fr;
            animated[i] = a;
            if (a.frames[fr] != null) a.img.sprite = a.frames[fr];
        }
    }

    // Public accessor for use by CombatBoardUI terrain layers
    public List<TerrainAnim> GetAnimated() => animated;
}
