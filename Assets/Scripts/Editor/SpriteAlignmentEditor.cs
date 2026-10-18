using UnityEditor;
using UnityEngine;

// Tools > AutoStrategy > Sprite Alignment
//
// Shows any building, tree, or rock sprite overlaid on its tile footprint so
// you can verify and adjust the sprite's alignment and the footprint's (i,j) size.
//
// Coordinate note (matching the game canvas):
//   Game Y = UP.   Editor GUI Y = DOWN (flipped).
//   Footprint (i,j): i = column (0=left), j = row (0=TOP row, higher game Y = smaller j).
//   "Anchor tile" inside the footprint maps to the object's (tileX, tileY) in game space.
public class SpriteAlignmentEditor : EditorWindow
{
    // ── Mode ─────────────────────────────────────────────────────────────────
    enum Mode { Building, Tree, Rock }
    Mode mode;
    int  selectedTab;
    static readonly string[] Tabs = { "Building", "Tree", "Rock" };

    // ── Building ─────────────────────────────────────────────────────────────
    BuildingDefinition building;

    // ── Tree ─────────────────────────────────────────────────────────────────
    TerrainConfig terrainCfg;
    int  treeVariant;
    static readonly string[] TreeVariantNames = { "Variant 0 (tall)", "Variant 1 (tall)", "Variant 2 (square)", "Variant 3 (square)" };

    // ── Rock ─────────────────────────────────────────────────────────────────
    int rockVariant;

    // ── Preview ──────────────────────────────────────────────────────────────
    float previewScale = 1.5f;
    bool  showGrid     = true;
    bool  showAnchor   = true;
    Vector2 scroll;

    const float TILE_PX = 64f;

    [MenuItem("Tools/AutoStrategy/Sprite Alignment")]
    static void Open() => GetWindow<SpriteAlignmentEditor>("Sprite Alignment");

    // ─────────────────────────────────────────────────────────────────────────
    void OnGUI()
    {
        EditorGUILayout.Space(4);

        int newTab = GUILayout.Toolbar(selectedTab, Tabs);
        if (newTab != selectedTab) { selectedTab = newTab; mode = (Mode)newTab; }

        EditorGUILayout.Space(6);

        switch (mode)
        {
            case Mode.Building: DrawBuildingMode(); break;
            case Mode.Tree:     DrawTreeMode();     break;
            case Mode.Rock:     DrawRockMode();     break;
        }
    }

    // ── Building mode ─────────────────────────────────────────────────────────
    void DrawBuildingMode()
    {
        building = (BuildingDefinition)EditorGUILayout.ObjectField(
            "Building Definition", building, typeof(BuildingDefinition), false);

        if (building == null)
        {
            EditorGUILayout.HelpBox("Select a BuildingDefinition asset.", MessageType.Info);
            return;
        }

        DrawViewOptions();

        // ── Footprint (i, j) controls ──────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Footprint", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        int ni = Mathf.Max(1, EditorGUILayout.IntField("  Columns (i)", building.EffI));
        int nj = Mathf.Max(1, EditorGUILayout.IntField("  Rows    (j)", building.EffJ));
        int nai = Mathf.Clamp(EditorGUILayout.IntField("  Anchor column (0=left)", building.EffAI), 0, ni - 1);
        int naj = Mathf.Clamp(EditorGUILayout.IntField("  Anchor row    (0=top) ", building.EffAJ), 0, nj - 1);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(building, "Edit Building Footprint");
            building.footprintI = ni;
            building.footprintJ = nj;
            building.anchorI    = nai;
            building.anchorJ    = naj;
            // Keep legacy footprintRadius in sync (max half-extent, symmetric approximation)
            building.footprintRadius = Mathf.Max(
                Mathf.Max(building.ExtLeft, building.ExtRight),
                Mathf.Max(building.ExtAbove, building.ExtBelow));
            EditorUtility.SetDirty(building);
        }

        EditorGUILayout.LabelField("  → Extents",
            $"L={building.ExtLeft} R={building.ExtRight} U={building.ExtAbove} D={building.ExtBelow}",
            EditorStyles.miniLabel);

        // ── Sprite offset controls ─────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Sprite Alignment", EditorStyles.boldLabel);

        float maxRange = (Mathf.Max(ni, nj) + 2) * TILE_PX;
        EditorGUI.BeginChangeCheck();
        float ox = EditorGUILayout.Slider("  Offset X (← →)", building.spriteOffsetX, -maxRange, maxRange);
        float oy = EditorGUILayout.Slider("  Offset Y (↓ ↑)", building.spriteOffsetY, -maxRange, maxRange);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(building, "Adjust Building Sprite Offset");
            building.spriteOffsetX = ox;
            building.spriteOffsetY = oy;
            EditorUtility.SetDirty(building);
        }

        if (GUILayout.Button("Reset Sprite Offsets"))
        {
            Undo.RecordObject(building, "Reset Building Sprite Offset");
            building.spriteOffsetX = building.spriteOffsetY = 0f;
            EditorUtility.SetDirty(building);
        }

        if (building.sprite != null)
            EditorGUILayout.LabelField("Sprite size",
                $"{(int)building.sprite.rect.width}×{(int)building.sprite.rect.height} px",
                EditorStyles.miniLabel);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("─── Preview ───", EditorStyles.boldLabel);

        DrawPreview(
            building.sprite,
            building.EffI, building.EffJ, building.EffAI, building.EffAJ,
            building.spriteOffsetX, building.spriteOffsetY,
            pivotX: 0.5f, pivotY: 0f);
    }

    // ── Tree mode ─────────────────────────────────────────────────────────────
    void DrawTreeMode()
    {
        LoadTerrainCfg();
        if (terrainCfg == null) { EditorGUILayout.HelpBox("No TerrainConfig found in Resources.", MessageType.Warning); return; }

        treeVariant = EditorGUILayout.Popup("Tree Variant", treeVariant, TreeVariantNames);
        DrawViewOptions();

        var frames = terrainCfg.GetTreeFrames(treeVariant);
        Sprite spr = (frames != null && frames.Length > 0) ? frames[0] : null;
        var (tw, th) = terrainCfg.GetTreeDimensions(treeVariant);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Footprint: 1×1 tile  (anchor 0,0)", EditorStyles.miniLabel);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Pivot (fraction of sprite dims)", EditorStyles.boldLabel);
        EnsureTreeArrays();
        EditorGUI.BeginChangeCheck();
        float px = EditorGUILayout.Slider("  Pivot X (0=left,  1=right)", terrainCfg.treePivotX[treeVariant], 0f, 1f);
        float py = EditorGUILayout.Slider("  Pivot Y (0=bottom,1=top)  ", terrainCfg.treePivotY[treeVariant], 0f, 1f);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(terrainCfg, "Adjust Tree Pivot");
            terrainCfg.treePivotX[treeVariant] = px;
            terrainCfg.treePivotY[treeVariant] = py;
            EditorUtility.SetDirty(terrainCfg);
        }
        if (spr != null) EditorGUILayout.LabelField("Sprite size", $"{(int)tw}×{(int)th} px", EditorStyles.miniLabel);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("─── Preview ───", EditorStyles.boldLabel);

        DrawPreview(spr, 1, 1, 0, 0, 0f, 0f, px, py);
    }

    // ── Rock mode ─────────────────────────────────────────────────────────────
    void DrawRockMode()
    {
        LoadTerrainCfg();
        if (terrainCfg == null) { EditorGUILayout.HelpBox("No TerrainConfig found in Resources.", MessageType.Warning); return; }
        if (terrainCfg.rockSprites == null || terrainCfg.rockSprites.Length == 0)
        { EditorGUILayout.HelpBox("No rock sprites assigned in TerrainConfig.", MessageType.Warning); return; }

        int rockCount = terrainCfg.rockSprites.Length;
        string[] rockNames = new string[rockCount];
        for (int i = 0; i < rockCount; i++) rockNames[i] = $"Rock {i}";
        rockVariant = EditorGUILayout.Popup("Rock Variant", Mathf.Clamp(rockVariant, 0, rockCount - 1), rockNames);
        DrawViewOptions();

        Sprite spr = terrainCfg.rockSprites[rockVariant];

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Footprint: 1×1 tile  (anchor 0,0)", EditorStyles.miniLabel);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Sprite Offset (world pixels)", EditorStyles.boldLabel);
        EnsureRockArrays(rockCount);
        EditorGUI.BeginChangeCheck();
        float rox = EditorGUILayout.Slider("  Offset X (← →)", terrainCfg.rockOffsetX[rockVariant], -TILE_PX, TILE_PX);
        float roy = EditorGUILayout.Slider("  Offset Y (↓ ↑)", terrainCfg.rockOffsetY[rockVariant], -TILE_PX, TILE_PX);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(terrainCfg, "Adjust Rock Offset");
            terrainCfg.rockOffsetX[rockVariant] = rox;
            terrainCfg.rockOffsetY[rockVariant] = roy;
            EditorUtility.SetDirty(terrainCfg);
        }

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("─── Preview ───", EditorStyles.boldLabel);

        // For rocks the sprite is 64×64 centred on the tile; offset shifts it relative to tile centre.
        DrawPreview(spr, 1, 1, 0, 0, rox, roy, 0.5f, 0.5f);
    }

    // ── Shared view options ──────────────────────────────────────────────────
    void DrawViewOptions()
    {
        EditorGUILayout.Space(4);
        previewScale = EditorGUILayout.Slider("Preview Scale", previewScale, 0.5f, 3f);
        showGrid     = EditorGUILayout.Toggle("Show Grid Lines", showGrid);
        showAnchor   = EditorGUILayout.Toggle("Show Anchor Line", showAnchor);
    }

    // ── Preview renderer ──────────────────────────────────────────────────────
    // footprintI/J = tile grid dimensions; anchorI/J = anchor in footprint (0=top-left).
    // spriteOffsetX/Y = world-pixel shifts applied to the sprite (positive Y = up in game).
    // pivotX/Y = for sprites that use a non-centre pivot (e.g. trees).
    void DrawPreview(Sprite spr, int fpI, int fpJ, int ancI, int ancJ,
                     float sprOX, float sprOY, float pivotX, float pivotY)
    {
        float scale  = previewScale;
        float tilePx = TILE_PX * scale;

        float gridW  = fpI * tilePx;
        float gridH  = fpJ * tilePx;

        float sprW = spr != null ? spr.rect.width  * scale : tilePx;
        float sprH = spr != null ? spr.rect.height * scale : tilePx;
        float oX   = sprOX * scale;
        float oY   = sprOY * scale;    // positive = up in game = upward (smaller) in editor

        // Extra padding so the sprite doesn't clip
        float pad       = tilePx;
        float previewW  = Mathf.Max(gridW, sprW) + pad * 2f;
        float previewH  = gridH + sprH + pad * 2f;

        scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(Mathf.Min(previewH + 20f, 550f)));
        Rect pr = GUILayoutUtility.GetRect(previewW, previewH, GUILayout.ExpandWidth(false));

        EditorGUI.DrawRect(pr, new Color(0.1f, 0.1f, 0.1f));

        // Footprint centre in EDITOR coords.
        // Footprint tiles sit in the lower part of the preview; sprite extends upward from there.
        float fpCX = pr.x + pr.width  * 0.5f;
        float fpCY = pr.y + pad + sprH + Mathf.Max(0f, oY);   // anchor tile centre

        // ── Footprint tile grid ──────────────────────────────────────────
        // Footprint cell (di, dj):
        //   di=0 is left column  → ancI tiles left of anchor in game → game dx = di - ancI
        //   dj=0 is TOP row      → ancJ rows above anchor in game    → game dy = ancJ - dj
        //   editor offset: edX = fpCX + (di-ancI)*tilePx,  edY = fpCY - (ancJ-dj)*tilePx
        for (int di = 0; di < fpI; di++)
        for (int dj = 0; dj < fpJ; dj++)
        {
            float edX = fpCX + (di - ancI) * tilePx;
            float edY = fpCY - (ancJ - dj) * tilePx;

            bool isAnchor  = (di == ancI && dj == ancJ);
            bool isBottom  = (dj == fpJ - 1);

            Color fill = isAnchor  ? new Color(0.3f, 0.6f, 0.3f, 0.55f)
                       : isBottom  ? new Color(0.15f, 0.35f, 0.55f, 0.6f)
                       :             new Color(0.20f, 0.48f, 0.75f, 0.40f);

            Rect tileRect = new Rect(edX - tilePx * 0.5f, edY - tilePx * 0.5f, tilePx - 1f, tilePx - 1f);
            EditorGUI.DrawRect(tileRect, fill);
            DrawRectOutline(tileRect, new Color(0.4f, 0.7f, 1.0f, 0.9f), 1f);

            var lStyle = new GUIStyle(EditorStyles.miniLabel)
                { alignment = TextAnchor.MiddleCenter };
            lStyle.normal.textColor = new Color(1f, 1f, 1f, isAnchor ? 1f : 0.55f);
            string lbl = isAnchor ? $"({di},{dj})\nanchor" : $"({di},{dj})";
            GUI.Label(tileRect, lbl, lStyle);
        }

        // ── Grid lines ───────────────────────────────────────────────────
        if (showGrid)
        {
            Color gc = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            float ext = tilePx * 0.5f;
            for (int di = 0; di <= fpI; di++)
            {
                float lx = fpCX + (di - ancI - 0.5f) * tilePx;
                if (lx >= pr.xMin && lx <= pr.xMax)
                    EditorGUI.DrawRect(new Rect(lx, pr.yMin, 1f, pr.height), gc);
            }
            for (int dj = 0; dj <= fpJ; dj++)
            {
                float ly = fpCY - (ancJ - dj + 0.5f) * tilePx;
                if (ly >= pr.yMin && ly <= pr.yMax)
                    EditorGUI.DrawRect(new Rect(pr.xMin, ly, pr.width, 1f), gc);
            }
        }

        // ── Anchor line (sprite base) ────────────────────────────────────
        if (showAnchor)
        {
            // Sprite base in game = fpCY position shifted by oY (up in game = smaller editor Y).
            // For pivot-based sprites, "base" is the bottom edge:
            //   sprite height in editor = sprH; pivot at pivotY from bottom.
            //   sprite centre editor Y  = fpCY - oY - (sprH * (1f - pivotY) - sprH * pivotY) / ... hmm.
            // Simplify: base = centre - sprH*pivotY; centre = fpCY - oY
            // But for offset-based: base edge = fpCY - oY + sprH * 0.5f (when pivot=0.5)
            // We'll just draw a line at fpCY - oY (the anchor point in editor coords)
            float ancEdY = fpCY - oY;
            float lx0 = Mathf.Max(fpCX - gridW, pr.xMin + 2);
            float lx1 = Mathf.Min(fpCX + gridW, pr.xMax - 2);
            EditorGUI.DrawRect(new Rect(lx0, ancEdY - 1f, lx1 - lx0, 2f), new Color(1f, 0.9f, 0.1f, 0.9f));
            var ls = new GUIStyle(EditorStyles.miniLabel);
            ls.normal.textColor = new Color(1f, 0.9f, 0.1f, 1f);
            GUI.Label(new Rect(lx1 + 2f, ancEdY - 8f, 90f, 16f), "anchor pt", ls);
        }

        // ── Sprite ───────────────────────────────────────────────────────
        if (spr != null)
        {
            // In game: sprite anchor = (worldX + sprOX,  worldY + sprOY).
            //   worldY is the anchor tile Y → fpCY in editor.
            //   Positive oY (up in game) = smaller editor Y.
            // Sprite centre in editor:
            //   edSprCX = fpCX + oX
            //   edSprCY = fpCY - oY - sprH*(0.5f - pivotY)
            //             (pivot at pivotY from BOTTOM; sprH*(0.5-pivotY) = distance from centre to bottom offset)
            //   i.e. when pivotY=0.5 (centre pivot), edSprCY = fpCY - oY.
            //        when pivotY=0 (bottom pivot), edSprCY = fpCY - oY - sprH*0.5 (centre is sprH/2 above base).
            float edSprCX = fpCX + oX;
            float edSprCY = fpCY - oY - sprH * (0.5f - pivotY);

            Rect sprRect = new Rect(edSprCX - sprW * 0.5f, edSprCY - sprH * 0.5f, sprW, sprH);

            Texture2D tex = spr.texture;
            if (tex != null)
            {
                var sr = spr.rect;
                var uv = new Rect(sr.x / tex.width,  sr.y / tex.height,
                                  sr.width / tex.width, sr.height / tex.height);
                GUI.DrawTextureWithTexCoords(sprRect, tex, uv);
            }
            DrawRectOutline(sprRect, new Color(1f, 1f, 0.3f, 0.5f), 1f);
        }

        // ── Crosshair at anchor tile centre ──────────────────────────────
        EditorGUI.DrawRect(new Rect(fpCX - 5f, fpCY - 0.5f, 10f, 1f), Color.white);
        EditorGUI.DrawRect(new Rect(fpCX - 0.5f, fpCY - 5f, 1f, 10f), Color.white);

        EditorGUILayout.EndScrollView();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    void LoadTerrainCfg()
    {
        if (terrainCfg == null)
            terrainCfg = Resources.Load<TerrainConfig>("TerrainConfig");
    }

    void EnsureTreeArrays()
    {
        if (terrainCfg.treePivotY == null || terrainCfg.treePivotY.Length < 4)
        {
            terrainCfg.treePivotY = new float[] { 0.125f, 0.125f, 0.1667f, 0.1667f };
            EditorUtility.SetDirty(terrainCfg);
        }
        if (terrainCfg.treePivotX == null || terrainCfg.treePivotX.Length < 4)
        {
            terrainCfg.treePivotX = new float[] { 0.5f, 0.5f, 0.5f, 0.5f };
            EditorUtility.SetDirty(terrainCfg);
        }
    }

    void EnsureRockArrays(int count)
    {
        if (terrainCfg.rockOffsetX == null || terrainCfg.rockOffsetX.Length < count)
        {
            var arr = new float[count];
            if (terrainCfg.rockOffsetX != null)
                System.Array.Copy(terrainCfg.rockOffsetX, arr, Mathf.Min(terrainCfg.rockOffsetX.Length, count));
            terrainCfg.rockOffsetX = arr;
            EditorUtility.SetDirty(terrainCfg);
        }
        if (terrainCfg.rockOffsetY == null || terrainCfg.rockOffsetY.Length < count)
        {
            var arr = new float[count];
            if (terrainCfg.rockOffsetY != null)
                System.Array.Copy(terrainCfg.rockOffsetY, arr, Mathf.Min(terrainCfg.rockOffsetY.Length, count));
            terrainCfg.rockOffsetY = arr;
            EditorUtility.SetDirty(terrainCfg);
        }
    }

    static void DrawRectOutline(Rect r, Color c, float t)
    {
        EditorGUI.DrawRect(new Rect(r.xMin,     r.yMin,     r.width, t), c);
        EditorGUI.DrawRect(new Rect(r.xMin,     r.yMax - t, r.width, t), c);
        EditorGUI.DrawRect(new Rect(r.xMin,     r.yMin,     t, r.height), c);
        EditorGUI.DrawRect(new Rect(r.xMax - t, r.yMin,     t, r.height), c);
    }

    void OnInspectorUpdate() => Repaint();
}
