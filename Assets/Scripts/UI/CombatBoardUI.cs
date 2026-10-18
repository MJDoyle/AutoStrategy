using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class CombatBoardUI : MonoBehaviour,
    IScrollHandler, IPointerDownHandler, IPointerUpHandler, IDragHandler
{
    // ── Fighter state ─────────────────────────────────────────────────────────

    private enum Anim { Idle, Run, Attack }

    private class Fighter
    {
        public UnitInstance unit;
        public bool         isPlayer;
        public float        hp, maxHp;
        public Vector2      pos;
        public Fighter      target;
        public float        atkTimer;
        public bool         dead, dying;
        public float        dyingTimer;
        public Anim         currentAnim;
        public float        atkAnimTimer;
        public RectTransform rt;
        public Image         spriteImg;
        public SpriteAnimator anim;
        public RectTransform hpFillRT;
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly List<Fighter> all     = new();
    private readonly List<Fighter> players = new();
    private readonly List<Fighter> enemies = new();

    private RectTransform viewport;
    private RectTransform world;
    private RectTransform unitLayer;

    private float zoom          = 0.35f;
    private float minZoom       = 0.10f;
    private float maxZoom       = 1.5f;
    private bool  isDragging;
    private Vector2 dragStartScreen;
    private Vector2 worldPosAtDragStart;

    private bool              initialized, combatOver;
    private bool              needsInitialCamera;
    private float             spriteScale;
    private TextMeshProUGUI   speedLabel;
    private TextMeshProUGUI   leaveBtnLabel;
    private int               survivingPlayers, survivingEnemies;

    // ── Fog of war ─────────────────────────────────────────────────────────────

    private const int   FOG_SCALE  = 4;
    private const int   FOG_EXTEND = 5;
    private const float FOG_UNIT_R = 5f;
    private const float FOG_BLUR   = 1.5f;

    private Texture2D fogTex;
    private float[,]  fogVis;      // [gx, ty] gx spans all 3 sections: 0..3*SIZE-1
    private Color32[] fogPixels;

    // ── Bootstrap ─────────────────────────────────────────────────────────────

    private IEnumerator Start()
    {
        var gm = GameManager.Instance;
        Time.timeScale = gm.combatSpeed;
        spriteScale    = gm.spriteScale;

        yield return null;

        BuildWorld();
        BuildSpeedOverlay();
        BuildLeaveButton();
        SpawnFighters();
        needsInitialCamera = true;
        initialized = true;
    }

    // ── World construction ─────────────────────────────────────────────────────

    void BuildWorld()
    {
        var gm = GameManager.Instance;

        // Blocker Image: catches scroll/drag/click events for this component
        var bg = gameObject.GetComponent<Image>() ?? gameObject.AddComponent<Image>();
        bg.color = new Color(0, 0, 0, 0.01f);
        bg.raycastTarget = true;

        // Viewport with mask
        var vpGO = new GameObject("Viewport", typeof(RectTransform));
        vpGO.transform.SetParent(transform, false);
        viewport = vpGO.GetComponent<RectTransform>();
        viewport.anchorMin = Vector2.zero;
        viewport.anchorMax = Vector2.one;
        viewport.offsetMin = viewport.offsetMax = Vector2.zero;
        var vpImg = vpGO.AddComponent<Image>();
        vpImg.color = new Color(0, 0, 0, 0.01f);
        vpImg.raycastTarget = false;
        vpGO.AddComponent<Mask>().showMaskGraphic = false;

        // World container (9600×3200)
        var worldGO = new GameObject("World", typeof(RectTransform));
        worldGO.transform.SetParent(vpGO.transform, false);
        world = worldGO.GetComponent<RectTransform>();
        world.anchorMin = world.anchorMax = Vector2.zero;
        world.pivot     = Vector2.zero;
        world.sizeDelta = new Vector2(CampLayout.COMBAT_W, CampLayout.COMBAT_H);

        // Ground (three-section tinted background)
        var groundCfg = Resources.Load<TerrainConfig>("TerrainConfig");
        Sprite gt = groundCfg?.groundTile;
        SpawnGroundPanel(worldGO.transform, 0f,                   CampLayout.WORLD_SIZE, new Color(0.18f, 0.32f, 0.14f), gt);
        SpawnGroundPanel(worldGO.transform, CampLayout.MIDDLE_X,  CampLayout.WORLD_SIZE, new Color(0.22f, 0.18f, 0.10f), gt);
        SpawnGroundPanel(worldGO.transform, CampLayout.ENEMY_X,   CampLayout.WORLD_SIZE, new Color(0.30f, 0.14f, 0.12f), gt);

        // Terrain drawn globally by layer type so decorations always appear above all ground
        // regardless of which section they belong to.
        int last = CampTerrain.SIZE - 1;
        BuildCombatTerrain(worldGO.transform, groundCfg, new[]
        {
            new SectionDef { ct = gm.HumanPlayer.terrain,
                             xOffset = 0f,                  mirror = false,
                             eastNb  = gm.MiddleTerrain,    eastNbTx = 0 },
            new SectionDef { ct = gm.MiddleTerrain,
                             xOffset = CampLayout.MIDDLE_X, mirror = false,
                             westNb  = gm.HumanPlayer.terrain,     westNbTx = last,
                             eastNb  = gm.CombatOpponent?.terrain, eastNbTx = last },
            new SectionDef { ct = gm.CombatOpponent?.terrain,
                             xOffset = CampLayout.ENEMY_X,  mirror = true,
                             eastNb  = gm.MiddleTerrain,    eastNbTx = last },
        });

        // Section labels
        MakeWorldLabel(worldGO.transform, "Your Camp",
            gm.HumanPlayer.playerName,
            new Vector2(CampLayout.WORLD_SIZE * 0.5f, CampLayout.COMBAT_H - 80f),
            new Color(0.7f, 0.9f, 0.7f));
        string enemyName = gm.CombatOpponent?.playerName ?? "Enemy";
        MakeWorldLabel(worldGO.transform, enemyName,
            "",
            new Vector2(CampLayout.ENEMY_X + CampLayout.WORLD_SIZE * 0.5f, CampLayout.COMBAT_H - 80f),
            new Color(0.9f, 0.6f, 0.6f));

        // Unit layer (above terrain)
        var ulGO = new GameObject("UnitLayer", typeof(RectTransform));
        ulGO.transform.SetParent(worldGO.transform, false);
        unitLayer = ulGO.GetComponent<RectTransform>();
        unitLayer.anchorMin = unitLayer.anchorMax = Vector2.zero;
        unitLayer.pivot     = Vector2.zero;
        unitLayer.sizeDelta = new Vector2(CampLayout.COMBAT_W, CampLayout.COMBAT_H);

        // Fog of war (drawn above units)
        BuildFogLayer(worldGO.transform);
    }

    static void SpawnGroundPanel(Transform parent, float x, float w, Color tint, Sprite tile = null)
    {
        float h = CampLayout.COMBAT_H;
        var go = new GameObject("Ground", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot     = Vector2.zero;
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(x, 0);

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
            raw.color         = tint;
            raw.raycastTarget = false;
        }
        else
        {
            var img2 = go.AddComponent<Image>();
            img2.color = tint;
            img2.raycastTarget = false;
        }
    }

    const float ELEVATION_PX = 32f;

    struct SectionDef
    {
        public CampTerrain ct;
        public float       xOffset;
        public bool        mirror;
        public CampTerrain westNb; public int westNbTx;
        public CampTerrain eastNb; public int eastNbTx;

        public byte H(int tx, int ty)
        {
            if (ct == null || ty < 0 || ty >= CampTerrain.SIZE) return 0;
            if (tx < 0)                  return westNb != null ? westNb.heightMap[westNbTx, ty] : (byte)0;
            if (tx >= CampTerrain.SIZE)  return eastNb != null ? eastNb.heightMap[eastNbTx, ty] : (byte)0;
            return ct.heightMap[tx, ty];
        }
    }

    static Transform LayerGO(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.sizeDelta = new Vector2(CampLayout.COMBAT_W, CampLayout.COMBAT_H);
        return go.transform;
    }

    static Transform SectionGO(Transform parent, SectionDef s)
    {
        var go = new GameObject("S", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.sizeDelta = new Vector2(CampLayout.WORLD_SIZE, CampLayout.COMBAT_H);
        rt.anchoredPosition = s.mirror
            ? new Vector2(s.xOffset + CampLayout.WORLD_SIZE, 0)
            : new Vector2(s.xOffset, 0);
        if (s.mirror) rt.localScale = new Vector3(-1, 1, 1);
        return go.transform;
    }

    void BuildCombatTerrain(Transform parent, TerrainConfig cfg, SectionDef[] sections)
    {
        if (cfg == null) return;
        const float SHADOW_PX = 80f;

        // ── Water ──────────────────────────────────────────────────────────────
        var waterL = LayerGO(parent, "L_Water");
        foreach (var s in sections)
        {
            if (s.ct == null) continue;
            var sec = SectionGO(waterL, s);
            for (int tx = 0; tx < CampTerrain.SIZE; tx++)
            for (int ty = 0; ty < CampTerrain.SIZE; ty++)
            {
                // Skip interior land tiles — a fully-opaque ground sprite will cover them.
                bool needsWater = s.H(tx,     ty    ) < 1
                               || s.H(tx - 1, ty    ) < 1
                               || s.H(tx + 1, ty    ) < 1
                               || s.H(tx,     ty - 1) < 1
                               || s.H(tx,     ty + 1) < 1;
                if (!needsWater) continue;
                var img = SpawnSimpleImage(sec, CampLayout.TileCenterX(tx), CampLayout.TileCenterY(ty),
                                           CampLayout.TILE_PX, CampLayout.TILE_PX);
                if (cfg.waterTile != null) img.sprite = cfg.waterTile;
                else img.color = new Color(0.278f, 0.671f, 0.663f);
            }
        }

        // ── Flat ground h>=1 ───────────────────────────────────────────────────
        var flatL = LayerGO(parent, "L_FlatGround");
        foreach (var s in sections)
        {
            if (s.ct == null) continue;
            var sec = SectionGO(flatL, s);
            for (int tx = 0; tx < CampTerrain.SIZE; tx++)
            for (int ty = 0; ty < CampTerrain.SIZE; ty++)
            {
                if (s.H(tx, ty) < 1) continue;
                int bm = 0;
                if (s.H(tx,   ty+1) >= 1) bm |= 8;
                if (s.H(tx+1, ty  ) >= 1) bm |= 4;
                if (s.H(tx,   ty-1) >= 1) bm |= 2;
                if (s.H(tx-1, ty  ) >= 1) bm |= 1;
                var sp = cfg.GetTerrainTile(1, bm);
                if (sp == null) continue;
                SpawnSimpleImage(sec, CampLayout.TileCenterX(tx), CampLayout.TileCenterY(ty),
                                 CampLayout.TILE_PX, CampLayout.TILE_PX).sprite = sp;
            }
        }

        // ── Shadows ────────────────────────────────────────────────────────────
        if (cfg.shadowSprite != null)
        {
            var shadowL = LayerGO(parent, "L_Shadows");
            foreach (var s in sections)
            {
                if (s.ct == null) continue;
                var sec = SectionGO(shadowL, s);
                for (int tx = 0; tx < CampTerrain.SIZE; tx++)
                for (int ty = 0; ty < CampTerrain.SIZE; ty++)
                {
                    byte h = s.H(tx, ty);
                    if (h < 2) continue;
                    float wx = CampLayout.TileCenterX(tx);
                    SpawnSimpleImage(sec, wx, CampLayout.TileCenterY(ty), SHADOW_PX, SHADOW_PX).sprite = cfg.shadowSprite;
                    if (h >= 3 && ty + 1 < CampTerrain.SIZE)
                        SpawnSimpleImage(sec, wx, CampLayout.TileCenterY(ty + 1), SHADOW_PX, SHADOW_PX).sprite = cfg.shadowSprite;
                }
            }
        }

        // ── h2 cliff faces ─────────────────────────────────────────────────────
        var cliffH2L = LayerGO(parent, "L_CliffsH2");
        foreach (var s in sections)
        {
            if (s.ct == null) continue;
            var sec = SectionGO(cliffH2L, s);
            for (int tx = 0; tx < CampTerrain.SIZE; tx++)
            for (int ty = 0; ty < CampTerrain.SIZE; ty++)
            {
                if (s.H(tx, ty) < 2) continue;
                bool sw  = s.H(tx, ty - 1) == 0;
                bool lh  = s.H(tx - 1, ty) >= 2;
                bool rh  = s.H(tx + 1, ty) >= 2;
                int ci   = !lh && !rh ? 3 : lh && rh ? 1 : !lh ? 0 : 2;
                var sp   = cfg.GetCliffFace(sw, ci);
                if (sp == null) continue;
                SpawnSimpleImage(sec, CampLayout.TileCenterX(tx), CampLayout.TileCenterY(ty),
                                 CampLayout.TILE_PX, CampLayout.TILE_PX).sprite = sp;
            }
        }

        // ── h2 elevated ground (+1 tile) ───────────────────────────────────────
        var elevH2L = LayerGO(parent, "L_ElevatedH2");
        foreach (var s in sections)
        {
            if (s.ct == null) continue;
            var sec = SectionGO(elevH2L, s);
            for (int tx = 0; tx < CampTerrain.SIZE; tx++)
            for (int ty = 0; ty < CampTerrain.SIZE; ty++)
            {
                if (s.H(tx, ty) < 2) continue;
                int bm = 0;
                if (s.H(tx,   ty+1) >= 2) bm |= 8;
                if (s.H(tx+1, ty  ) >= 2) bm |= 4;
                if (s.H(tx,   ty-1) >= 2) bm |= 2;
                if (s.H(tx-1, ty  ) >= 2) bm |= 1;
                var sp = cfg.GetTerrainTile(2, bm);
                if (sp == null) continue;
                SpawnSimpleImage(sec, CampLayout.TileCenterX(tx), CampLayout.TileCenterY(ty) + CampLayout.TILE_PX,
                                 CampLayout.TILE_PX, CampLayout.TILE_PX).sprite = sp;
            }
        }

        // ── h3 cliff faces (+1 tile) ───────────────────────────────────────────
        var cliffH3L = LayerGO(parent, "L_CliffsH3");
        foreach (var s in sections)
        {
            if (s.ct == null) continue;
            var sec = SectionGO(cliffH3L, s);
            for (int tx = 0; tx < CampTerrain.SIZE; tx++)
            for (int ty = 0; ty < CampTerrain.SIZE; ty++)
            {
                if (s.H(tx, ty) < 3) continue;
                bool sl = s.H(tx, ty - 1) < 2;
                bool lh = s.H(tx - 1, ty) >= 3;
                bool rh = s.H(tx + 1, ty) >= 3;
                int ci  = !lh && !rh ? 3 : lh && rh ? 1 : !lh ? 0 : 2;
                var sp  = cfg.GetCliffFace(sl, ci);
                if (sp == null) continue;
                SpawnSimpleImage(sec, CampLayout.TileCenterX(tx), CampLayout.TileCenterY(ty) + CampLayout.TILE_PX,
                                 CampLayout.TILE_PX, CampLayout.TILE_PX).sprite = sp;
            }
        }

        // ── h3 elevated ground (+2 tiles) ─────────────────────────────────────
        var elevH3L = LayerGO(parent, "L_ElevatedH3");
        foreach (var s in sections)
        {
            if (s.ct == null) continue;
            var sec = SectionGO(elevH3L, s);
            for (int tx = 0; tx < CampTerrain.SIZE; tx++)
            for (int ty = 0; ty < CampTerrain.SIZE; ty++)
            {
                if (s.H(tx, ty) != 3) continue;
                int bm = 0;
                if (s.H(tx,   ty+1) >= 3) bm |= 8;
                if (s.H(tx+1, ty  ) >= 3) bm |= 4;
                if (s.H(tx,   ty-1) >= 3) bm |= 2;
                if (s.H(tx-1, ty  ) >= 3) bm |= 1;
                var sp = cfg.GetTerrainTile(3, bm);
                if (sp == null) continue;
                SpawnSimpleImage(sec, CampLayout.TileCenterX(tx), CampLayout.TileCenterY(ty) + 2 * CampLayout.TILE_PX,
                                 CampLayout.TILE_PX, CampLayout.TILE_PX).sprite = sp;
            }
        }

        // ── Decorations (last — never covered by ground from any section) ──────
        var decoL = LayerGO(parent, "L_Decorations");
        foreach (var s in sections)
        {
            if (s.ct == null) continue;
            var sec = SectionGO(decoL, s);
            for (int ty = CampTerrain.SIZE - 1; ty >= 0; ty--)
            for (int tx = 0; tx < CampTerrain.SIZE; tx++)
            {
                var cell = s.ct.grid[tx, ty];
                if (cell == TerrainCell.Clear) continue;
                float wx = CampLayout.TileCenterX(tx);
                float wy = CampLayout.TileCenterY(ty) + Mathf.Max(0, s.H(tx, ty) - 1) * CampLayout.TILE_PX;
                switch (cell)
                {
                    case TerrainCell.Tree:
                    {
                        if (s.H(tx, ty) == 0) break;
                        int v = s.ct.variant[tx, ty];
                        var frames = cfg.GetTreeFrames(v);
                        if (frames == null || frames.Length == 0) break;
                        var (tw, th) = cfg.GetTreeDimensions(v);
                        var img = SpawnTerrainImage(sec, wx, wy, tw, th, frames, 8f, s.ct.phase[tx, ty]);
                        img.rectTransform.pivot = new Vector2(0.5f, v < 2 ? 0.125f : (1f / 6f));
                        break;
                    }
                    case TerrainCell.Rock:
                    {
                        byte ht = s.H(tx, ty);
                        if (ht == 0)
                        {
                            int v = s.ct.variant[tx, ty] % 4;
                            var frames = cfg.GetWaterFrames(v);
                            if (frames == null || frames.Length == 0) break;
                            SpawnTerrainImage(sec, wx, wy, CampLayout.TILE_PX, CampLayout.TILE_PX,
                                              frames, 10f, s.ct.phase[tx, ty]).color = new Color(0.6f, 0.8f, 1f);
                        }
                        else
                        {
                            if (cfg.rockSprites == null || cfg.rockSprites.Length == 0) break;
                            int v = s.ct.variant[tx, ty] % cfg.rockSprites.Length;
                            if (cfg.rockSprites[v] == null) break;
                            SpawnSimpleImage(sec, wx, wy, CampLayout.TILE_PX, CampLayout.TILE_PX).sprite = cfg.rockSprites[v];
                        }
                        break;
                    }
                    case TerrainCell.Water:
                    {
                        if (s.H(tx, ty) > 0) break;
                        int v = s.ct.variant[tx, ty] % 4;
                        var frames = cfg.GetWaterFrames(v);
                        if (frames == null || frames.Length == 0) break;
                        SpawnTerrainImage(sec, wx, wy, CampLayout.TILE_PX, CampLayout.TILE_PX,
                                          frames, 10f, s.ct.phase[tx, ty]).color = new Color(0.6f, 0.8f, 1f);
                        break;
                    }
                }
            }
        }
    }

    // Inline terrain animation list for the combat board
    private struct TerrainVisual { public Image img; public Sprite[] frames; public float fps, phase; public int lastFrame; }
    private readonly List<TerrainVisual> terrainAnims = new();

    Image SpawnTerrainImage(Transform parent, float wx, float wy, float w, float h,
        Sprite[] frames, float fps, float phase)
    {
        var img = SpawnSimpleImage(parent, wx, wy, w, h);
        if (frames != null && frames.Length > 0 && frames[0] != null)
            img.sprite = frames[0];
        terrainAnims.Add(new TerrainVisual { img = img, frames = frames, fps = fps, phase = phase, lastFrame = -1 });
        return img;
    }

    static byte GetTerrainHeight(CampTerrain ct, int tx, int ty)
    {
        if (tx < 0 || tx >= CampTerrain.SIZE || ty < 0 || ty >= CampTerrain.SIZE) return 0;
        return ct.heightMap[tx, ty];
    }

    static Image SpawnSimpleImage(Transform parent, float wx, float wy, float w, float h)
    {
        var go = new GameObject("T", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(w, h);
        rt.anchoredPosition = new Vector2(wx, wy);
        var img = go.AddComponent<Image>();
        img.raycastTarget = false;  // visual only
        return img;
    }

    void MakeWorldLabel(Transform parent, string line1, string line2,
        Vector2 worldPos, Color col)
    {
        var go = new GameObject("Lbl", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(600, 80);
        rt.anchoredPosition = worldPos;
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = string.IsNullOrEmpty(line2) ? line1 : $"{line1}";
        tmp.fontSize  = 36;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = col;
    }

    // ── Speed overlay (stays in screen space, not in the world) ───────────────

    void BuildSpeedOverlay()
    {
        var bgGO = new GameObject("SpeedBar", typeof(RectTransform));
        bgGO.transform.SetParent(transform, false);
        var bgRT = bgGO.GetComponent<RectTransform>();
        bgRT.anchorMin = new Vector2(0.35f, 0.93f);
        bgRT.anchorMax = new Vector2(0.65f, 1.0f);
        bgRT.offsetMin = bgRT.offsetMax = Vector2.zero;
        bgGO.AddComponent<Image>().color = new Color(0, 0, 0, 0.7f);

        MakeSpeedBtn(bgGO.transform, "−", new Vector2(0, 0), new Vector2(0.2f, 1))
            .onClick.AddListener(DecreaseSpeed);
        MakeSpeedBtn(bgGO.transform, "+", new Vector2(0.8f, 0), new Vector2(1, 1))
            .onClick.AddListener(IncreaseSpeed);

        var lblGO = new GameObject("Lbl", typeof(RectTransform));
        lblGO.transform.SetParent(bgGO.transform, false);
        var lblRT = lblGO.GetComponent<RectTransform>();
        lblRT.anchorMin = new Vector2(0.2f, 0); lblRT.anchorMax = new Vector2(0.8f, 1);
        lblRT.offsetMin = lblRT.offsetMax = Vector2.zero;
        speedLabel = lblGO.AddComponent<TextMeshProUGUI>();
        speedLabel.alignment = TextAlignmentOptions.Center;
        speedLabel.fontSize  = 14;
        speedLabel.fontStyle = FontStyles.Bold;
        speedLabel.color     = Color.white;
        RefreshSpeedLabel();
    }

    Button MakeSpeedBtn(Transform parent, string lbl, Vector2 aMin, Vector2 aMax)
    {
        var go = new GameObject("B" + lbl, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = aMin; rt.anchorMax = aMax;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.AddComponent<Image>().color = new Color(0.3f, 0.3f, 0.3f);
        var btn = go.AddComponent<Button>();
        var cs = btn.colors; cs.highlightedColor = new Color(0.55f, 0.55f, 0.55f); btn.colors = cs;
        var tGO = new GameObject("T", typeof(RectTransform));
        tGO.transform.SetParent(go.transform, false);
        var tRT = tGO.GetComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = tRT.offsetMax = Vector2.zero;
        var tmp = tGO.AddComponent<TextMeshProUGUI>();
        tmp.text = lbl; tmp.fontSize = 16; tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center; tmp.color = Color.white;
        return btn;
    }

    void IncreaseSpeed() { var gm = GameManager.Instance; gm.combatSpeed = Mathf.Min(8f, gm.combatSpeed * 2f); Time.timeScale = gm.combatSpeed; RefreshSpeedLabel(); }
    void DecreaseSpeed() { var gm = GameManager.Instance; gm.combatSpeed = Mathf.Max(0.25f, gm.combatSpeed / 2f); Time.timeScale = gm.combatSpeed; RefreshSpeedLabel(); }
    void RefreshSpeedLabel() { if (speedLabel != null) { float s = GameManager.Instance.combatSpeed; speedLabel.text = s >= 1f ? $"{s:0.#}× speed" : "½× speed"; } }

    void BuildLeaveButton()
    {
        var go = new GameObject("LeaveBtn", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.70f, 0.93f);
        rt.anchorMax = new Vector2(0.99f, 1.0f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.AddComponent<Image>().color = new Color(0.35f, 0.15f, 0.10f);
        var btn = go.AddComponent<Button>();
        var cs = btn.colors;
        cs.highlightedColor = new Color(0.55f, 0.22f, 0.14f);
        cs.pressedColor     = new Color(0.22f, 0.08f, 0.06f);
        btn.colors = cs;
        btn.onClick.AddListener(LeaveOrContinue);

        var tGO = new GameObject("T", typeof(RectTransform));
        tGO.transform.SetParent(go.transform, false);
        var tRT = tGO.GetComponent<RectTransform>();
        tRT.anchorMin = Vector2.zero; tRT.anchorMax = Vector2.one;
        tRT.offsetMin = tRT.offsetMax = Vector2.zero;
        leaveBtnLabel = tGO.AddComponent<TextMeshProUGUI>();
        leaveBtnLabel.text = "Leave Fight";
        leaveBtnLabel.fontSize = 13;
        leaveBtnLabel.fontStyle = FontStyles.Bold;
        leaveBtnLabel.alignment = TextAlignmentOptions.Center;
        leaveBtnLabel.color = Color.white;
    }

    void LeaveOrContinue()
    {
        if (combatOver)
        {
            GameManager.Instance.EndCombat(survivingPlayers, survivingEnemies);
            return;
        }
        // Early exit — treat remaining enemies as having survived
        combatOver = true;
        survivingPlayers = players.FindAll(f => !f.dead && !f.dying).Count;
        survivingEnemies = enemies.FindAll(f => !f.dead && !f.dying).Count;
        GameManager.Instance.EndCombat(survivingPlayers, survivingEnemies);
    }

    // ── Fighter spawning ──────────────────────────────────────────────────────

    void SpawnFighters()
    {
        var pBoard = GameManager.Instance.HumanPlayer.board;
        var eBoard = GameManager.Instance.CombatOpponent.board;

        for (int c = 0; c < 9; c++)
        for (int r = 0; r < 9; r++)
        {
            if (pBoard[c, r] != null)
            {
                var pos = new Vector2(CampLayout.UnitCenterX(c), CampLayout.UnitCenterY(r));
                var f   = MakeFighter(pBoard[c, r], true, pos);
                all.Add(f); players.Add(f);
            }
            if (eBoard[c, r] != null)
            {
                var pos = new Vector2(CampLayout.EnemyUnitCenterX(c), CampLayout.EnemyUnitCenterY(r));
                var f   = MakeFighter(eBoard[c, r], false, pos);
                all.Add(f); enemies.Add(f);
            }
        }
    }

    Fighter MakeFighter(UnitInstance unit, bool isPlayer, Vector2 worldPos)
    {
        var frames = unit.definition.idleFrames;
        float w = 0, h = 0;
        if (frames != null && frames.Length > 0 && frames[0] != null)
        { w = frames[0].rect.width * spriteScale; h = frames[0].rect.height * spriteScale; }
        else { w = h = CampLayout.UNIT_STRIDE; }

        var rootGO = new GameObject(unit.definition.unitName, typeof(RectTransform));
        rootGO.transform.SetParent(unitLayer, false);
        var rootRT = rootGO.GetComponent<RectTransform>();
        rootRT.anchorMin        = rootRT.anchorMax = Vector2.zero;
        rootRT.pivot            = new Vector2(0.5f, 0.5f);
        rootRT.sizeDelta        = new Vector2(w, h);
        rootRT.anchoredPosition = worldPos;

        var sprGO = new GameObject("Spr", typeof(RectTransform));
        sprGO.transform.SetParent(rootGO.transform, false);
        var sprRT = sprGO.GetComponent<RectTransform>();
        sprRT.anchorMin = sprRT.anchorMax = new Vector2(0.5f, 0.5f);
        sprRT.pivot     = new Vector2(0.5f, 0.5f);
        sprRT.anchoredPosition = Vector2.zero;
        sprRT.sizeDelta = new Vector2(w, h);
        if (!isPlayer) sprRT.localScale = new Vector3(-1, 1, 1);

        var img  = sprGO.AddComponent<Image>();
        var anim = sprGO.AddComponent<SpriteAnimator>();
        if (frames != null && frames.Length > 0 && frames[0] != null)
        { img.color = Color.white; anim.Play(frames, 8f); }
        else if (unit.definition.idleSprite != null)
        { img.sprite = unit.definition.idleSprite; img.color = Color.white; }
        else
        { img.color = isPlayer ? new Color(0.3f, 0.5f, 1f) : new Color(1f, 0.3f, 0.3f); }

        // HP bar
        var hpBgGO = new GameObject("HpBg", typeof(RectTransform));
        hpBgGO.transform.SetParent(rootGO.transform, false);
        var hpBgRT = hpBgGO.GetComponent<RectTransform>();
        hpBgRT.anchorMin        = new Vector2(0, 1);
        hpBgRT.anchorMax        = new Vector2(1, 1);
        hpBgRT.pivot            = new Vector2(0, 0);
        hpBgRT.anchoredPosition = new Vector2(0, 4);
        hpBgRT.sizeDelta        = new Vector2(0, 8);
        hpBgGO.AddComponent<Image>().color = new Color(0.15f, 0.15f, 0.15f);

        var hpFillGO = new GameObject("HpFill", typeof(RectTransform));
        hpFillGO.transform.SetParent(hpBgGO.transform, false);
        var hpFillRT = hpFillGO.GetComponent<RectTransform>();
        hpFillRT.anchorMin = Vector2.zero; hpFillRT.anchorMax = Vector2.one;
        hpFillRT.offsetMin = hpFillRT.offsetMax = Vector2.zero;
        hpFillGO.AddComponent<Image>().color = isPlayer
            ? new Color(0.25f, 0.75f, 0.25f)
            : new Color(0.75f, 0.2f, 0.2f);

        return new Fighter
        {
            unit     = unit,
            isPlayer = isPlayer,
            hp       = unit.MaxHealth,
            maxHp    = unit.MaxHealth,
            pos      = worldPos,
            rt       = rootRT,
            spriteImg = img,
            anim     = anim,
            hpFillRT = hpFillRT,
        };
    }

    // ── Camera ────────────────────────────────────────────────────────────────

    void SetInitialCamera()
    {
        Canvas.ForceUpdateCanvases();
        ApplyZoom();
        float targetX = CampLayout.MIDDLE_X + CampLayout.WORLD_SIZE * 0.5f;
        float targetY = CampLayout.COMBAT_H * 0.5f;
        var vp = viewport.rect;
        world.anchoredPosition = new Vector2(
            vp.width  * 0.5f - targetX * zoom,
            vp.height * 0.5f - targetY * zoom);
        ClampWorldPosition();
    }

    // ── Pan / zoom ─────────────────────────────────────────────────────────────

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
        if (data.button == PointerEventData.InputButton.Middle ||
            data.button == PointerEventData.InputButton.Right)
        {
            isDragging = true;
            dragStartScreen     = data.position;
            worldPosAtDragStart = world.anchoredPosition;
        }
    }

    public void OnPointerUp(PointerEventData data) => isDragging = false;

    public void OnDrag(PointerEventData data)
    {
        if (!isDragging) return;
        world.anchoredPosition = worldPosAtDragStart + (data.position - dragStartScreen);
        ClampWorldPosition();
    }

    void ApplyZoom() => world.localScale = new Vector3(zoom, zoom, 1);

    void ClampWorldPosition()
    {
        if (world == null || viewport == null) return;
        var vp  = viewport.rect;
        var sz  = new Vector2(world.sizeDelta.x * zoom, world.sizeDelta.y * zoom);
        var pos = world.anchoredPosition;
        float padX = Mathf.Max(vp.width  * 0.15f, sz.x * 0.15f);
        float padY = Mathf.Max(vp.height * 0.15f, sz.y * 0.15f);
        pos.x = Mathf.Clamp(pos.x, -sz.x + padX, vp.width  - padX);
        pos.y = Mathf.Clamp(pos.y, -sz.y + padY, vp.height - padY);
        world.anchoredPosition = pos;
    }

    // ── Animation ─────────────────────────────────────────────────────────────

    void SetAnim(Fighter f, Anim next)
    {
        if (f.currentAnim == next && next != Anim.Attack) return;
        f.currentAnim = next;
        var def = f.unit.definition;
        switch (next)
        {
            case Anim.Run:
                var rf = def.runFrames;
                f.anim.Play(rf != null && rf.Length > 0 ? rf : def.idleFrames, 8f);
                break;
            case Anim.Attack:
                var af = def.attackFrames;
                if (af != null && af.Length > 0)
                { f.anim.Play(af, 10f); f.atkAnimTimer = af.Length / 10f; }
                else { f.atkAnimTimer = 0.25f; }
                break;
            default:
                f.anim.Play(def.idleFrames, 8f);
                break;
        }
    }

    // ── Simulation ────────────────────────────────────────────────────────────

    void Update()
    {
        if (needsInitialCamera && viewport != null && viewport.rect.width > 1f)
        {
            needsInitialCamera = false;
            SetInitialCamera();
        }

        float t = Time.time;
        for (int i = 0; i < terrainAnims.Count; i++)
        {
            var a = terrainAnims[i];
            if (a.img == null || a.frames == null || a.frames.Length == 0) continue;
            int fr = (int)((t * a.fps + a.phase) % a.frames.Length);
            if (fr == a.lastFrame) continue;
            a.lastFrame = fr;
            terrainAnims[i] = a;
            if (a.frames[fr] != null) a.img.sprite = a.frames[fr];
        }

        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
            LeaveOrContinue();

        if (!initialized || combatOver) return;

        RebuildCombatFog();

        float dt = Time.deltaTime;
        foreach (var f in all)
        {
            if (f.dead) continue;
            if (f.dying)
            {
                f.dyingTimer -= dt;
                if (f.dyingTimer <= 0) { f.dead = true; f.rt.gameObject.SetActive(false); }
                else { float a = f.dyingTimer / 0.4f; var c = f.spriteImg.color; f.spriteImg.color = new Color(c.r, c.g, c.b, a); }
                continue;
            }

            if (f.atkAnimTimer > 0)
            {
                f.atkAnimTimer -= dt;
                if (f.atkAnimTimer <= 0 && f.currentAnim == Anim.Attack)
                    SetAnim(f, Anim.Idle);
            }

            if (f.target == null || f.target.dead || f.target.dying)
                f.target = FindNearest(f);
            if (f.target == null) continue;

            float dist  = Vector2.Distance(f.pos, f.target.pos);
            float range = Mathf.Max(1, f.unit.definition.range) * CampLayout.RANGE_PX;

            if (dist <= range)
            {
                if (f.currentAnim == Anim.Run) SetAnim(f, Anim.Idle);
                f.atkTimer -= dt;
                if (f.atkTimer <= 0)
                {
                    f.atkTimer = 1f / Mathf.Max(0.1f, f.unit.definition.attackSpeed);
                    SetAnim(f, Anim.Attack);
                    HitFighter(f, f.target);
                }
            }
            else
            {
                SetAnim(f, Anim.Run);
                float speed = f.unit.definition.moveSpeed;
                f.pos += (f.target.pos - f.pos).normalized * speed * dt;
                f.rt.anchoredPosition = f.pos;
            }
        }

        bool anyPlayer = players.Exists(f => !f.dead && !f.dying);
        bool anyEnemy  = enemies.Exists(f => !f.dead && !f.dying);
        if (!anyPlayer || !anyEnemy)
        {
            combatOver = true;
            int ps = players.FindAll(f => !f.dead && !f.dying).Count;
            int es = enemies.FindAll(f => !f.dead && !f.dying).Count;
            StartCoroutine(FinishCombat(ps, es));
        }
    }

    void HitFighter(Fighter attacker, Fighter target)
    {
        target.hp = Mathf.Max(0, target.hp - attacker.unit.Attack);
        target.hpFillRT.anchorMax = new Vector2(target.hp / target.maxHp, 1);

        var proj = attacker.unit.definition.projectileSprite;
        if (proj != null)
            StartCoroutine(FireProjectile(proj, attacker.pos, target.pos, attacker.isPlayer));
        else
            StartCoroutine(FlashRed(target));

        if (target.hp <= 0 && !target.dying)
        { target.dying = true; target.dyingTimer = 0.4f; }
    }

    IEnumerator FireProjectile(Sprite sprite, Vector2 from, Vector2 to, bool isPlayer)
    {
        var go = new GameObject("Proj", typeof(RectTransform));
        go.transform.SetParent(unitLayer, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(sprite.rect.width * spriteScale,
                                   sprite.rect.height * spriteScale);
        rt.anchoredPosition = from;
        Vector2 dir = (to - from).normalized;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        if (!isPlayer) angle += 180f;
        rt.localEulerAngles = new Vector3(0, 0, angle);
        go.AddComponent<Image>().sprite = sprite;

        float dist = Vector2.Distance(from, to);
        float dur  = dist / 1600f; // fast projectile
        float t    = 0;
        while (t < dur && go != null)
        {
            t += Time.deltaTime;
            rt.anchoredPosition = Vector2.Lerp(from, to, t / dur);
            yield return null;
        }
        if (go != null) Destroy(go);
    }

    IEnumerator FlashRed(Fighter f)
    {
        var orig = f.spriteImg.color;
        f.spriteImg.color = new Color(1f, 0.2f, 0.2f, orig.a);
        yield return new WaitForSeconds(0.08f);
        if (!f.dead && !f.dying) f.spriteImg.color = orig;
    }

    Fighter FindNearest(Fighter f)
    {
        var pool = f.isPlayer ? enemies : players;
        Fighter best = null; float bestD = float.MaxValue;
        foreach (var e in pool)
        {
            if (e.dead || e.dying) continue;
            float d = Vector2.Distance(f.pos, e.pos);
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    IEnumerator FinishCombat(int ps, int es)
    {
        survivingPlayers = ps;
        survivingEnemies = es;
        ShowResult(ps, es);
        if (leaveBtnLabel != null) leaveBtnLabel.text = "Continue →";
        yield return null;
    }

    void ShowResult(int ps, int es)
    {
        var go = new GameObject("Result", typeof(RectTransform));
        go.transform.SetParent(transform, false); // screen-space, not in world
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.25f, 0.35f);
        rt.anchorMax = new Vector2(0.75f, 0.65f);
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        go.AddComponent<Image>().color = new Color(0, 0, 0, 0.88f);

        var lbl = new GameObject("Text", typeof(RectTransform));
        lbl.transform.SetParent(go.transform, false);
        var lrt = lbl.GetComponent<RectTransform>();
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;

        var tmp = lbl.AddComponent<TextMeshProUGUI>();
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize  = 28;

        string enemyName = GameManager.Instance.CombatOpponent?.playerName ?? "Enemy";
        if (es == 0 && ps > 0)
        { tmp.text = $"<size=40><b>Victory!</b></size>\n<size=22>{enemyName} takes {ps * 2} damage</size>"; tmp.color = new Color(1f, 0.88f, 0.2f); }
        else if (ps == 0 && es > 0)
        { tmp.text = $"<size=40><b>Defeat</b></size>\n<size=22>You take {es * 2} damage</size>"; tmp.color = new Color(0.9f, 0.3f, 0.3f); }
        else
        { tmp.text = "<size=40><b>Draw</b></size>"; tmp.color = new Color(0.8f, 0.8f, 0.8f); }
    }

    // ── Fog of war ─────────────────────────────────────────────────────────────

    void BuildFogLayer(Transform parent)
    {
        const int S     = CampTerrain.SIZE;
        const int TOTAL = 3 * S;
        int tilesW = TOTAL + 2 * FOG_EXTEND;
        int tilesH = S     + 2 * FOG_EXTEND;
        int texW   = tilesW * FOG_SCALE;
        int texH   = tilesH * FOG_SCALE;

        fogTex            = new Texture2D(texW, texH, TextureFormat.RGBA32, false);
        fogTex.filterMode = FilterMode.Bilinear;
        fogTex.wrapMode   = TextureWrapMode.Clamp;
        fogVis    = new float[TOTAL, S];
        fogPixels = new Color32[texW * texH];

        InitCombatFogBase();
        WriteCombatFogPixels();

        var fogGO = new GameObject("CombatFog", typeof(RectTransform));
        fogGO.transform.SetParent(parent, false);
        var rt = fogGO.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
        rt.sizeDelta        = new Vector2(tilesW * CampLayout.TILE_PX, tilesH * CampLayout.TILE_PX);
        rt.anchoredPosition = new Vector2(
            -FOG_EXTEND * CampLayout.TILE_PX,
            -FOG_EXTEND * CampLayout.TILE_PX);

        var img = fogGO.AddComponent<RawImage>();
        img.texture       = fogTex;
        img.raycastTarget = false;
    }

    void InitCombatFogBase()
    {
        const int S = CampTerrain.SIZE;
        for (int gx = 0; gx < 3 * S; gx++)
        for (int ty = 0; ty < S; ty++)
        {
            float vis = 0f;
            if (gx < S)  // section 0 — player camp clearing always visible
            {
                float cx    = gx + 0.5f;
                float cy    = ty + 0.5f;
                float nearX = Mathf.Clamp(cx, CampTerrain.SEG_X0, CampTerrain.SEG_X1);
                float dist  = Mathf.Sqrt((cx - nearX) * (cx - nearX) + (cy - CampTerrain.SEG_Y) * (cy - CampTerrain.SEG_Y));
                vis = 1f - Mathf.Clamp01((dist - (CampTerrain.CLEAR_R - FOG_BLUR)) / FOG_BLUR);
            }
            fogVis[gx, ty] = vis;
        }
    }

    void AddCombatReveal(float tcx, float tcy, float radius)
    {
        const int S = CampTerrain.SIZE;
        for (int gx = 0; gx < 3 * S; gx++)
        for (int ty = 0; ty < S; ty++)
        {
            float cx   = gx + 0.5f;
            float cy   = ty + 0.5f;
            float dist = Mathf.Sqrt((cx - tcx) * (cx - tcx) + (cy - tcy) * (cy - tcy));
            float vis  = 1f - Mathf.Clamp01((dist - (radius - FOG_BLUR)) / FOG_BLUR);
            if (vis > fogVis[gx, ty]) fogVis[gx, ty] = vis;
        }
    }

    void RebuildCombatFog()
    {
        if (fogTex == null || fogVis == null) return;
        InitCombatFogBase();
        foreach (var f in players)
        {
            if (f.dead || f.dying) continue;
            float tcx = f.pos.x / CampLayout.TILE_PX;
            float tcy = f.pos.y / CampLayout.TILE_PX;
            AddCombatReveal(tcx, tcy, FOG_UNIT_R);
        }
        WriteCombatFogPixels();
    }

    void WriteCombatFogPixels()
    {
        const int S     = CampTerrain.SIZE;
        const int TOTAL = 3 * S;
        int tilesW = TOTAL + 2 * FOG_EXTEND;
        int tilesH = S     + 2 * FOG_EXTEND;
        int texW   = tilesW * FOG_SCALE;
        int texH   = tilesH * FOG_SCALE;

        for (int py = 0; py < texH; py++)
        for (int px = 0; px < texW; px++)
        {
            float gx  = (px + 0.5f) / FOG_SCALE - FOG_EXTEND;
            float gy  = (py + 0.5f) / FOG_SCALE - FOG_EXTEND;
            float vis = SampleCombatFogVis(gx, gy);
            fogPixels[py * texW + px] = new Color32(0, 0, 0, (byte)((1f - vis) * 255f));
        }
        fogTex.SetPixels32(fogPixels);
        fogTex.Apply(false, false);
    }

    float SampleCombatFogVis(float gx, float gy)
    {
        if (fogVis == null) return 0f;
        float sx = gx - 0.5f;
        float sy = gy - 0.5f;
        int   ix = Mathf.FloorToInt(sx);
        int   iy = Mathf.FloorToInt(sy);
        float fx = sx - ix;
        float fy = sy - iy;
        float v00 = GetCombatTileVis(ix,   iy  );
        float v10 = GetCombatTileVis(ix+1, iy  );
        float v01 = GetCombatTileVis(ix,   iy+1);
        float v11 = GetCombatTileVis(ix+1, iy+1);
        return Mathf.Lerp(Mathf.Lerp(v00, v10, fx), Mathf.Lerp(v01, v11, fx), fy);
    }

    float GetCombatTileVis(int gx, int ty)
    {
        if (fogVis == null) return 0f;
        if (gx < 0 || gx >= 3 * CampTerrain.SIZE || ty < 0 || ty >= CampTerrain.SIZE) return 0f;
        return fogVis[gx, ty];
    }
}
