using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using System.IO;
using System.Collections.Generic;
using System.Linq;

public static class GameSetupEditor
{
    private const string DefsPath = "Assets/Resources/UnitDefinitions";

    [MenuItem("Tools/AutoStrategy/Initialize Assets & Scene")]
    public static void Initialize()
    {
        if (!Directory.Exists(DefsPath))
            Directory.CreateDirectory(DefsPath);

        // moveSpeed is in canvas px/sec — scaled up for the larger 3200px combat world
        const string B = "Tiny Swords/Units/Blue Units";
        CreateDefinition(UnitType.Pawn,    "Pawn",    1,  80,  15, 1.2f, 1, 500f,
            idle:          $"{B}/Pawn/Pawn_Idle.png",
            run:           $"{B}/Pawn/Pawn_Run.png");
        CreateDefinition(UnitType.Warrior, "Warrior", 2, 200, 30, 0.8f, 1, 550f,
            idle:          $"{B}/Warrior/Warrior_Idle.png",
            run:           $"{B}/Warrior/Warrior_Run.png",
            attack:        $"{B}/Warrior/Warrior_Attack1.png");
        CreateDefinition(UnitType.Archer,  "Archer",  2,  80, 45, 1.3f, 4, 575f,
            idle:          $"{B}/Archer/Archer_Idle.png",
            run:           $"{B}/Archer/Archer_Run.png",
            attack:        $"{B}/Archer/Archer_Shoot.png",
            projectile:    $"{B}/Archer/Arrow.png");
        CreateDefinition(UnitType.Lancer,  "Lancer",  3, 150, 55, 0.7f, 2, 700f,
            idle:          $"{B}/Lancer/Lancer_Idle.png",
            run:           $"{B}/Lancer/Lancer_Run.png",
            attack:        $"{B}/Lancer/Lancer_Right_Attack.png",
            animFrameSize: 320);
        CreateDefinition(UnitType.Monk,    "Monk",    3, 120, 20, 1.0f, 3, 475f,
            idle:          $"{B}/Monk/Idle.png",
            run:           $"{B}/Monk/Run.png",
            attack:        $"{B}/Monk/Heal.png");

        SetupTerrainConfig();
        SetupBuildings();
        SetupUISprites();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        SetupScene();

        Debug.Log("AutoStrategy initialized! Press Play to run the game.");
    }

    static void CreateDefinition(UnitType type, string unitName, int cost,
        int hp, int atk, float attackSpeed, int range, float moveSpeed,
        string idle, string run = null, string attack = null, string projectile = null,
        int animFrameSize = 192)
    {
        string assetPath = $"{DefsPath}/{unitName}.asset";

        var idleSprite  = LoadFirstSprite($"Assets/{idle}");
        AssetDatabase.Refresh();
        var idleFrames  = LoadAllFrames($"Assets/{idle}");
        var runFrames   = run     != null ? LoadFramesSafe(run,    animFrameSize) : null;
        var atkFrames   = attack  != null ? LoadFramesSafe(attack, animFrameSize) : null;
        var projSprite  = projectile != null ? LoadFirstSprite($"Assets/{projectile}") : null;

        var existing = AssetDatabase.LoadAssetAtPath<UnitDefinition>(assetPath);
        if (existing != null)
        {
            existing.unitType        = type;
            existing.unitName        = unitName;
            existing.cost            = cost;
            existing.baseHealth      = hp;
            existing.baseAttack      = atk;
            existing.attackSpeed     = attackSpeed;
            existing.range           = range;
            existing.moveSpeed       = moveSpeed;
            existing.idleSprite      = idleSprite;
            existing.idleFrames      = idleFrames;
            existing.runFrames       = runFrames;
            existing.attackFrames    = atkFrames;
            existing.projectileSprite = projSprite;
            EditorUtility.SetDirty(existing);
            return;
        }

        var def = ScriptableObject.CreateInstance<UnitDefinition>();
        def.unitType         = type;
        def.unitName         = unitName;
        def.cost             = cost;
        def.baseHealth       = hp;
        def.baseAttack       = atk;
        def.attackSpeed      = attackSpeed;
        def.range            = range;
        def.moveSpeed        = moveSpeed;
        def.idleSprite       = idleSprite;
        def.idleFrames       = idleFrames;
        def.runFrames        = runFrames;
        def.attackFrames     = atkFrames;
        def.projectileSprite = projSprite;
        AssetDatabase.CreateAsset(def, assetPath);
    }

    static Sprite[] LoadFramesSafe(string relativePath, int frameW, int frameH = 0,
                                    Vector2? pivot = null)
    {
        string path = $"Assets/{relativePath}";
        ForceSliceAtSize(path, frameW, frameH, pivot);
        AssetDatabase.Refresh();
        return LoadAllFrames(path);
    }

    // Always re-slices a filmstrip at the given frame dimensions.
    // frameH=0 means square (frameH = frameW).  Handles non-square sheets like Tree1/2 (192×256).
    static void ForceSliceAtSize(string path, int frameW, int frameH = 0,
                                  Vector2? pivot = null)
    {
        if (frameH <= 0) frameH = frameW;
        var spritePivot = pivot ?? new Vector2(0.5f, 0.5f);

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning($"No TextureImporter at: {path}");
            return;
        }

        EnsureMaxTextureSize(importer, path);

        // Read the PNG width directly so the column count is based on the real file,
        // not whatever Unity may have cached.
        int pngW = ReadPngDimension(path, widthNotHeight: true);
        if (pngW <= 0)
        {
            Debug.LogWarning($"Could not read PNG width for: {path}");
            return;
        }

        int cols = Mathf.Max(1, pngW / frameW);
        string bn = Path.GetFileNameWithoutExtension(path);

        importer.filterMode = FilterMode.Point;
        importer.textureType = TextureImporterType.Sprite;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.SaveAndReimport();

        var rects = new SpriteRect[cols];
        for (int i = 0; i < cols; i++)
            rects[i] = new SpriteRect
            {
                name      = $"{bn}_{i}",
                rect      = new Rect(i * frameW, 0, frameW, frameH),
                alignment = SpriteAlignment.Custom,
                pivot     = spritePivot,
                spriteID  = GUID.Generate()
            };

        ApplySpriteRects(importer, rects);
        Debug.Log($"[AutoStrategy] Sliced {bn} into {cols} frames of {frameW}x{frameH}");
    }

    // Slices a 2-D tilemap into cellW×cellH tiles (all rows and columns).
    // ppu=1 so each tile renders at exactly cellW×cellH canvas pixels when Image.Type.Tiled.
    static void ForceSliceGrid(string path, int cellW, int cellH, float ppu = 1f)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning($"No TextureImporter at: {path}");
            return;
        }

        EnsureMaxTextureSize(importer, path);

        int pngW = ReadPngDimension(path, widthNotHeight: true);
        int pngH = ReadPngDimension(path, widthNotHeight: false);
        if (pngW <= 0 || pngH <= 0)
        {
            Debug.LogWarning($"Could not read PNG dimensions for: {path}");
            return;
        }

        int cols = Mathf.Max(1, pngW / cellW);
        int rows = Mathf.Max(1, pngH / cellH);
        string bn = Path.GetFileNameWithoutExtension(path);

        importer.filterMode          = FilterMode.Point;
        importer.textureType         = TextureImporterType.Sprite;
        importer.textureCompression  = TextureImporterCompression.Uncompressed;
        importer.spritePixelsPerUnit = ppu;
        importer.spriteImportMode    = SpriteImportMode.Multiple;
        importer.isReadable          = true;   // required so runtime can extract sub-tile pixels
        importer.SaveAndReimport();

        int idx = 0;
        var rects = new SpriteRect[cols * rows];
        for (int row = 0; row < rows; row++)
        for (int col = 0; col < cols; col++)
        {
            rects[idx] = new SpriteRect
            {
                name      = $"{bn}_{idx}",
                rect      = new Rect(col * cellW, row * cellH, cellW, cellH),
                alignment = SpriteAlignment.Center,
                pivot     = new Vector2(0.5f, 0.5f),
                spriteID  = GUID.Generate()
            };
            idx++;
        }

        ApplySpriteRects(importer, rects);
        Debug.Log($"[AutoStrategy] Sliced {bn} into {cols}×{rows} tiles of {cellW}×{cellH} (PPU={ppu})");
    }

    // ── Sprite setup ─────────────────────────────────────────────────────────

    static Sprite LoadFirstSprite(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning($"No TextureImporter at: {path}");
            return null;
        }

        bool changed = false;
        if (importer.filterMode != FilterMode.Point)
        { importer.filterMode = FilterMode.Point; changed = true; }
        if (importer.textureType != TextureImporterType.Sprite)
        { importer.textureType = TextureImporterType.Sprite; changed = true; }

        // Use ISpriteEditorDataProvider to get current sprite rects — avoids the
        // obsolete TextureImporter.spritesheet API which silently returns nothing.
        if (importer.spriteImportMode == SpriteImportMode.Multiple)
        {
            // Ensure the texture isn't downscaled — sheets wider than maxTextureSize
            // (e.g. Lancer 3840px with limit 2048) produce sub-assets whose Sprite.rect
            // is in the resized pixel space, making every frame appear smaller than intended.
            EnsureMaxTextureSize(importer, path);

            // Always reimport so the Library cache reflects the current .meta rects.
            // Without this, LoadAllAssetsAtPath returns stale cached sprites.
            importer.SaveAndReimport();
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            var currentRects = GetSpriteRects(importer);

            // If the meta has more entries than we have rects, add the missing frames.
            int metaCount    = CountSpritesInMeta(path);
            int currentCount = currentRects?.Length ?? 0;

            if (currentCount == 0)
            {
                // Provider found nothing — fall through to re-slice below.
            }
            else
            {
                if (metaCount > currentCount)
                {
                    var first = currentRects[0];
                    int fw = (int)first.rect.width;
                    int fh = (int)first.rect.height;
                    string bn = Path.GetFileNameWithoutExtension(path);
                    var extended = new SpriteRect[metaCount];
                    for (int i = 0; i < currentCount; i++) extended[i] = currentRects[i];
                    for (int i = currentCount; i < metaCount; i++)
                        extended[i] = new SpriteRect
                        {
                            name      = $"{bn}_{i}",
                            rect      = new Rect(i * fw, 0, fw, fh),
                            alignment = SpriteAlignment.Center,
                            pivot     = new Vector2(0.5f, 0.5f),
                            spriteID  = GUID.Generate()
                        };
                    ApplySpriteRects(importer, extended);
                }

                var frames = LoadAllFrames(path);
                return frames?.Length > 0 ? frames[0] : null;
            }
        }

        // ── Need to slice this sprite from scratch ────────────────────────────
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.spriteImportMode   = SpriteImportMode.Single;
        EnsureMaxTextureSize(importer, path);
        importer.SaveAndReimport();

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (tex == null)
        {
            Debug.LogWarning($"Texture2D not found: {path}");
            return null;
        }

        // Horizontal filmstrip: frame size = sheet height (assumes square frames)
        if (tex.width > tex.height)
        {
            int frameH = tex.height;
            int cols   = Mathf.Max(1, tex.width / frameH);
            string baseName = Path.GetFileNameWithoutExtension(path);

            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.SaveAndReimport();

            var rects = new SpriteRect[cols];
            for (int i = 0; i < cols; i++)
                rects[i] = new SpriteRect
                {
                    name      = $"{baseName}_{i}",
                    rect      = new Rect(i * frameH, 0, frameH, frameH),
                    alignment = SpriteAlignment.Center,
                    pivot     = new Vector2(0.5f, 0.5f),
                    spriteID  = GUID.Generate()
                };

            ApplySpriteRects(importer, rects);

            var frames = LoadAllFrames(path);
            return frames?.Length > 0 ? frames[0] : null;
        }

        // Square or portrait — use as a single sprite
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ── Texture size helpers ──────────────────────────────────────────────────

    static void EnsureMaxTextureSize(TextureImporter importer, string path)
    {
        // Read the PNG dimensions from the file header (8-byte sig + 4-byte length + "IHDR" + W + H).
        // We read the file directly so we don't need a prior import to get width/height.
        int pngW = 0, pngH = 0;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            fs.Seek(16, SeekOrigin.Begin); // skip PNG sig (8) + chunk length (4) + "IHDR" (4)
            var buf = new byte[8];
            fs.Read(buf, 0, 8);
            pngW = (buf[0] << 24) | (buf[1] << 16) | (buf[2] << 8) | buf[3];
            pngH = (buf[4] << 24) | (buf[5] << 16) | (buf[6] << 8) | buf[7];
        }
        catch { return; }

        int maxDim = Mathf.Max(pngW, pngH);
        // Unity accepts: 32,64,128,256,512,1024,2048,4096,8192,16384
        int needed = 32;
        while (needed < maxDim) needed *= 2;
        needed = Mathf.Min(needed, 16384);

        if (importer.maxTextureSize < needed)
        {
            importer.maxTextureSize = needed;
            Debug.Log($"[AutoStrategy] Raised maxTextureSize to {needed} for {Path.GetFileName(path)} ({pngW}x{pngH})");
        }
    }

    static int ReadPngDimension(string path, bool widthNotHeight)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
            fs.Seek(16, SeekOrigin.Begin); // skip PNG sig (8) + chunk length (4) + "IHDR" (4)
            var buf = new byte[8];
            fs.Read(buf, 0, 8);
            int w = (buf[0] << 24) | (buf[1] << 16) | (buf[2] << 8) | buf[3];
            int h = (buf[4] << 24) | (buf[5] << 16) | (buf[6] << 8) | buf[7];
            return widthNotHeight ? w : h;
        }
        catch { return 0; }
    }

    // ── ISpriteEditorDataProvider helpers ────────────────────────────────────

    static SpriteRect[] GetSpriteRects(TextureImporter importer)
    {
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var dp = factory.GetSpriteEditorDataProviderFromObject(importer);
        if (dp == null) return null;
        dp.InitSpriteEditorDataProvider();
        return dp.GetSpriteRects();
    }

    static void ApplySpriteRects(TextureImporter importer, SpriteRect[] rects)
    {
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        var dp = factory.GetSpriteEditorDataProviderFromObject(importer);
        if (dp == null) return;
        dp.InitSpriteEditorDataProvider();
        dp.SetSpriteRects(rects);
        dp.Apply();
        importer.SaveAndReimport();
        AssetDatabase.Refresh();
    }

    // ── Frame loading ─────────────────────────────────────────────────────────

    static Sprite[] LoadAllFrames(string path)
    {
        var all     = AssetDatabase.LoadAllAssetsAtPath(path);
        var sprites = new List<Sprite>();
        foreach (var a in all)
            if (a is Sprite s) sprites.Add(s);

        if (sprites.Count == 0) return null;

        sprites.Sort((a, b) => ExtractFrameIndex(a.name).CompareTo(ExtractFrameIndex(b.name)));
        return sprites.ToArray();
    }

    static int ExtractFrameIndex(string name)
    {
        int u = name.LastIndexOf('_');
        if (u >= 0 && int.TryParse(name.Substring(u + 1), out int idx)) return idx;
        return 0;
    }

    // ── Meta helpers ──────────────────────────────────────────────────────────

    static int CountSpritesInMeta(string texturePath)
    {
        string metaPath = texturePath + ".meta";
        if (!File.Exists(metaPath)) return 0;

        var lines    = File.ReadAllLines(metaPath);
        int tableIdx = -1;
        for (int i = 0; i < lines.Length; i++)
            if (lines[i].TrimEnd().EndsWith("nameFileIdTable:")) { tableIdx = i; break; }
        if (tableIdx < 0) return 0;

        int tableIndent = lines[tableIdx].Length - lines[tableIdx].TrimStart().Length;
        int count = 0;
        for (int i = tableIdx + 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            int indent = lines[i].Length - lines[i].TrimStart().Length;
            if (indent <= tableIndent) break;
            count++;
        }
        return count;
    }

    // ── Terrain Config ────────────────────────────────────────────────────────

    // Flat-ground autotile index lookup: bitmask (N=8,E=4,S=2,W=1) → sprite index in Tilemap_color4.
    // Bit SET = that neighbour also has h>=1 (ground/elevated); bit clear = neighbour is water.
    // Derived by measuring per-edge alpha on the 16 tiles specified by the asset author
    // (rows 2-5, cols 0-3 of the 9×6 tileset = sprite indices 18-21, 27-30, 36-39, 45-48).
    // Bitmask 0-15: N=8, E=4, S=2, W=1. Bit SET = that neighbour is at the same or higher level.
    // Flat ground (color4, cols 0-3, rows 2-5): border between h=0 and h>=1.
    static readonly int[] FlatGroundIndices =
    {
        21, 20, 48, 47, 18, 19, 45, 46,  // bitmask  0– 7: ---- / ---W / --S- / --SW / -E-- / -E-W / -ES- / -ESW
        30, 29, 39, 38, 27, 28, 36, 37   // bitmask  8–15: N--- / N--W / N-S- / N-SW / NE-- / NE-W / NES- / NESW
    };

    // Elevated ground (color2, cols 5-8, rows 2-5): border between h=1 and h>=2.
    // Same bitmask mapping as FlatGroundIndices but +5 per sprite (right half of same tileset layout).
    static readonly int[] ElevatedGroundIndices =
    {
        26, 25, 53, 52, 23, 24, 50, 51,  // bitmask  0– 7
        35, 34, 44, 43, 32, 33, 41, 42   // bitmask  8–15
    };

    // Cliff face tiles will be configured in a later step once flat-ground layer is verified.

    static void SetupTerrainConfig()
    {
        const string configPath = "Assets/Resources/TerrainConfig.asset";
        var cfg = AssetDatabase.LoadAssetAtPath<TerrainConfig>(configPath);
        if (cfg == null)
        {
            cfg = ScriptableObject.CreateInstance<TerrainConfig>();
            AssetDatabase.CreateAsset(cfg, configPath);
        }

        const string T = "Tiny Swords/Terrain";

        // Trees (4 variants)
        // Tree1/2: 1536×256 → 8 frames of 192×256 (tall sprites)
        // Tree3/4: square filmstrip → 192×192 frames
        cfg.treeSets = new SpriteAnim[4];
        var treePivot12 = new Vector2(0.5f, 0.125f);           // 192×256: 32 px from bottom
        var treePivot34 = new Vector2(0.5f, 1f / 6f);          // 192×192: 32 px from bottom
        cfg.treeSets[0] = new SpriteAnim { frames = LoadFramesSafe($"{T}/Resources/Wood/Trees/Tree1.png", 192, 256, treePivot12) };
        cfg.treeSets[1] = new SpriteAnim { frames = LoadFramesSafe($"{T}/Resources/Wood/Trees/Tree2.png", 192, 256, treePivot12) };
        cfg.treeSets[2] = new SpriteAnim { frames = LoadFramesSafe($"{T}/Resources/Wood/Trees/Tree3.png", 192, pivot: treePivot34) };
        cfg.treeSets[3] = new SpriteAnim { frames = LoadFramesSafe($"{T}/Resources/Wood/Trees/Tree4.png", 192, pivot: treePivot34) };

        // Rocks (4 variants, static 64px sprites)
        cfg.rockSprites = new Sprite[4];
        for (int i = 0; i < 4; i++)
            cfg.rockSprites[i] = LoadFirstSprite($"Assets/{T}/Decorations/Rocks/Rock{i + 1}.png");

        // Water (4 animated variants, 64px × N frames each)
        cfg.waterSets = new SpriteAnim[4];
        for (int i = 0; i < 4; i++)
        {
            int idx = i + 1;
            string wPath = $"{T}/Decorations/Rocks in the Water/Water Rocks_0{idx}.png";
            cfg.waterSets[i] = new SpriteAnim { frames = LoadFramesSafe(wPath, 64) };
        }

        // Terrain tilesets: slice all three color variants and populate autotile arrays.
        // Tilemap_color1 = level 3 peaks, color2 = level 2 raised, color4 = level 1 ground.
        string tm1 = $"Assets/{T}/Tileset/Tilemap_color1.png";
        string tm2 = $"Assets/{T}/Tileset/Tilemap_color2.png";
        string tm4 = $"Assets/{T}/Tileset/Tilemap_color4.png";

        ForceSliceGrid(tm1, 64, 64, ppu: 1f);
        ForceSliceGrid(tm2, 64, 64, ppu: 1f);
        ForceSliceGrid(tm4, 64, 64, ppu: 1f);
        AssetDatabase.Refresh();

        cfg.tilesLevel1 = LoadAutotileSet(tm4, FlatGroundIndices);      // flat ground border (h=0 → h>=1)
        cfg.tilesLevel2 = LoadAutotileSet(tm2, ElevatedGroundIndices);  // elevated ground border (h=1 → h>=2)
        cfg.tilesLevel3 = LoadAutotileSet(tm1, ElevatedGroundIndices);  // peak ground border (h=2 → h>=3)

        // Cliff face tiles from color4 (right-half columns).
        // Array order: [left-low/right-high, both-high, left-high/right-low, both-low]
        cfg.cliffFaceWater  = LoadAutotileSet(tm4, new[] { 5, 6, 7, 8 });
        cfg.cliffFaceGround = LoadAutotileSet(tm4, new[] { 14, 15, 16, 17 });

        // Ground tile for the camp background: interior tile (bitmask 15 = idx 37) from color4.
        cfg.spawnDustFrames = LoadFramesSafe("Tiny Swords/Particle FX/Dust_02.png", 64);

        // Shadow_0 is the 80×80 terrain shadow — already custom-sliced in the meta.
        string shadowPath = $"Assets/{T}/Tileset/Shadow.png";
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(shadowPath))
            if (a is Sprite s && s.name == "Shadow_0") { cfg.shadowSprite = s; break; }

        if (cfg.groundTile == null && cfg.tilesLevel1 != null && cfg.tilesLevel1.Length > 15)
            cfg.groundTile = cfg.tilesLevel1[15];

        // Water background tile (64×64, single sprite).
        string waterBg = $"Assets/{T}/Tileset/Water Background color.png";
        cfg.waterTile = LoadFirstSprite(waterBg);

        EditorUtility.SetDirty(cfg);
        Debug.Log("[AutoStrategy] TerrainConfig populated with autotile sets.");
    }

    // Loads a 16-element autotile sprite array from a sliced tilemap, picking each sprite
    // by its sequential tile index (as assigned by ForceSliceGrid row-by-row from bottom).
    static Sprite[] LoadAutotileSet(string path, int[] indices)
    {
        var all = AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<Sprite>()
            .ToDictionary(
                s => { int n; return int.TryParse(s.name.Split('_').Last(), out n) ? n : -1; },
                s => s);

        var result = new Sprite[indices.Length];
        for (int bm = 0; bm < indices.Length; bm++)
        {
            int tileIdx = indices[bm];
            if (all.TryGetValue(tileIdx, out var sprite))
                result[bm] = sprite;
            else
                Debug.LogWarning($"[AutoStrategy] Autotile idx {tileIdx} not found in {path}");
        }
        return result;
    }

    // ── UI sprites ────────────────────────────────────────────────────────────

    static void SetupUISprites()
    {
        foreach (var path in new[]
        {
            "Assets/Resources/selection_square_64x64.png",
            "Assets/Resources/selection_square_64x64_thick.png",
            "Assets/Resources/tree_overlay.png",
            "Assets/Resources/rock_overlay.png",
            "Assets/Resources/green_overlay.png",
        })
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;
            bool changed = false;
            if (importer.textureType != TextureImporterType.Sprite)
            { importer.textureType = TextureImporterType.Sprite; changed = true; }
            if (importer.spriteImportMode != SpriteImportMode.Single)
            { importer.spriteImportMode = SpriteImportMode.Single; changed = true; }
            if (importer.filterMode != FilterMode.Point)
            { importer.filterMode = FilterMode.Point; changed = true; }
            if (importer.spritePivot != new Vector2(0.5f, 0.5f))
            { importer.spritePivot = new Vector2(0.5f, 0.5f); changed = true; }
            if (changed) importer.SaveAndReimport();
        }
    }

    // ── Buildings ─────────────────────────────────────────────────────────────

    private const string BuildDefsPath = "Assets/Resources/BuildingDefinitions";

    static void SetupBuildings()
    {
        if (!Directory.Exists(BuildDefsPath))
            Directory.CreateDirectory(BuildDefsPath);

        const string B = "Tiny Swords/Buildings/Blue Buildings";
        //                    name        cost  fogR  fpR  sprite path                    description
        CreateBuildingDef("Monastery", 5,    8f,   1,   $"{B}/Monastery.png", "A place of worship and healing.");
        CreateBuildingDef("Castle",    8,    6f,   1,   $"{B}/Castle.png",    "A mighty stronghold.");
        CreateBuildingDef("Tower",     4,    6f,   0,   $"{B}/Tower.png",     "A tall watchtower for defense.");
        CreateBuildingDef("Barracks",  4,    4f,   0,   $"{B}/Barracks.png",  "Trains soldiers for battle.");
        CreateBuildingDef("Archery",   3,    4f,   0,   $"{B}/Archery.png",   "Range training for archers.");
        CreateBuildingDef("House1",    2,    3f,   0,   $"{B}/House1.png",    "A modest dwelling.");
        CreateBuildingDef("House2",    2,    3f,   0,   $"{B}/House2.png",    "A sturdy home.");
        CreateBuildingDef("House3",    2,    3f,   0,   $"{B}/House3.png",    "A comfortable residence.");
    }

    static void CreateBuildingDef(string buildingName, int cost, float fogRadius, int footprintRadius, string spritePath, string description)
    {
        string assetPath = $"{BuildDefsPath}/{buildingName}.asset";
        string fullPath  = $"Assets/{spritePath}";

        // Ensure the PNG is imported as a single sprite (portrait/square, no filmstrip).
        var importer = AssetImporter.GetAtPath(fullPath) as TextureImporter;
        if (importer != null)
        {
            bool changed = false;
            if (importer.filterMode != FilterMode.Point)
            { importer.filterMode = FilterMode.Point; changed = true; }
            if (importer.textureType != TextureImporterType.Sprite)
            { importer.textureType = TextureImporterType.Sprite; changed = true; }
            if (importer.spriteImportMode != SpriteImportMode.Single)
            { importer.spriteImportMode = SpriteImportMode.Single; changed = true; }
            if (importer.spritePivot != new Vector2(0.5f, 0f))
            { importer.spritePivot = new Vector2(0.5f, 0f); changed = true; }
            EnsureMaxTextureSize(importer, fullPath);
            if (changed) importer.SaveAndReimport();
        }

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(fullPath);

        var existing = AssetDatabase.LoadAssetAtPath<BuildingDefinition>(assetPath);
        if (existing != null)
        {
            existing.buildingName    = buildingName;
            existing.cost            = cost;
            existing.fogRevealRadius = fogRadius;
            existing.footprintRadius = footprintRadius;
            existing.sprite          = sprite;
            existing.description     = description;
            EditorUtility.SetDirty(existing);
            return;
        }

        var def = ScriptableObject.CreateInstance<BuildingDefinition>();
        def.buildingName    = buildingName;
        def.cost            = cost;
        def.fogRevealRadius = fogRadius;
        def.footprintRadius = footprintRadius;
        def.sprite          = sprite;
        def.description     = description;
        AssetDatabase.CreateAsset(def, assetPath);
        Debug.Log($"[AutoStrategy] Created building: {buildingName}");
    }

    // ── Scene setup ───────────────────────────────────────────────────────────

    static void SetupScene()
    {
        var scene = EditorSceneManager.GetActiveScene();
        foreach (var go in scene.GetRootGameObjects())
            if (go.name == "AutoStrategy")
                Object.DestroyImmediate(go);

        var root = new GameObject("AutoStrategy");
        root.AddComponent<GameManager>();
        root.AddComponent<GameUI>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Scene '{scene.name}' configured. Hit Play!");
    }
}
