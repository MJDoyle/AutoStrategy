using UnityEngine;

[System.Serializable]
public class SpriteAnim
{
    public Sprite[] frames;
}

[CreateAssetMenu(menuName = "AutoStrategy/Terrain Config", fileName = "TerrainConfig")]
public class TerrainConfig : ScriptableObject
{
    [Tooltip("4 tree variants — all use 192px frame width; variants 0,1 are 192×256, 2,3 are 192×192")]
    public SpriteAnim[] treeSets = new SpriteAnim[4];
    public Sprite[]     rockSprites;
    [Tooltip("4 water variants, animated — each 64x64 frames")]
    public SpriteAnim[] waterSets = new SpriteAnim[4];
    [Tooltip("One 64×64 tile from Tilemap_color1 used as the tiling ground texture.")]
    public Sprite groundTile;
    [Tooltip("64×64 water background tile (Water Background color.png).")]
    public Sprite waterTile;

    // Autotile sprite arrays — 16 entries each, indexed by 4-neighbor bitmask (N=8, E=4, S=2, W=1).
    // Each entry is a 64×64 sprite from the corresponding Tilemap_color sheet.
    [Tooltip("Height level 1 terrain tiles (Tilemap_color4 — most common ground)")]
    public Sprite[] tilesLevel1 = new Sprite[16];
    [Tooltip("Height level 2 terrain tiles (Tilemap_color2 — raised areas)")]
    public Sprite[] tilesLevel2 = new Sprite[16];
    [Tooltip("Height level 3 terrain tiles (Tilemap_color1 — rare peaks)")]
    public Sprite[] tilesLevel3 = new Sprite[16];

    // Cliff face tiles (Tilemap_color4, right-half columns).
    // 4 entries indexed by lateral connectivity: [0]=left-low/right-high, [1]=both-high,
    // [2]=left-high/right-low, [3]=both-low.
    [Tooltip("Cliff face over water (color4 indices 5,6,7,8)")]
    public Sprite[] cliffFaceWater  = new Sprite[4];
    [Tooltip("Cliff face over ground (color4 indices 14,15,16,17)")]
    public Sprite[] cliffFaceGround = new Sprite[4];

    [Tooltip("Shadow_0 from Shadow.png (80×80) — placed under cliff face tiles.")]
    public Sprite shadowSprite;

    [Tooltip("Dust_02 spawn effect — 10 frames of 64×64, played once when a unit appears.")]
    public Sprite[] spawnDustFrames;

    [Header("Sprite Alignment")]
    [Tooltip("Pivot Y per tree variant (0=bottom, 1=top). Controls how the sprite anchors to its tile.")]
    public float[] treePivotY = { 0.125f, 0.125f, 0.1667f, 0.1667f };
    [Tooltip("Pivot X per tree variant (0=left, 1=right).")]
    public float[] treePivotX = { 0.5f, 0.5f, 0.5f, 0.5f };
    [Tooltip("Pixel X offset per rock sprite variant (positive = right).")]
    public float[] rockOffsetX;
    [Tooltip("Pixel Y offset per rock sprite variant (positive = up).")]
    public float[] rockOffsetY;

    [Header("Height Map Generation")]
    [Tooltip("FBM base frequency — lower = larger terrain blobs.")]
    public float noiseFrequency = 0.04f;
    [Tooltip("FBM octave count.")]
    public int   noiseOctaves   = 6;
    [Tooltip("Normalised height threshold below which tiles are water (h=0).")]
    public float heightWater    = 0.12f;
    [Tooltip("Normalised height threshold above which tiles become raised ground (h=2).")]
    public float heightRaised   = 0.65f;
    [Tooltip("Normalised height threshold above which tiles become peaks (h=3). Set > 1 to disable h=3.")]
    public float heightPeak     = 0.85f;

    public Sprite[] GetTreeFrames(int variant)
    {
        if (treeSets == null || variant >= treeSets.Length || treeSets[variant] == null)
            return null;
        return treeSets[variant].frames;
    }

    // Returns (frameWidth, frameHeight) for a tree variant.
    // Variants 0,1 are 192×256 (tall sprites); 2,3 are 192×192 (square).
    public (float w, float h) GetTreeDimensions(int variant)
        => variant < 2 ? (192f, 256f) : (192f, 192f);

    public Sprite[] GetWaterFrames(int variant)
    {
        if (waterSets == null || variant >= waterSets.Length || waterSets[variant] == null)
            return null;
        return waterSets[variant].frames;
    }

    // Returns the correct autotile sprite for a height level and 4-neighbor bitmask.
    // Returns null if not configured or bitmask out of range.
    public Sprite GetTerrainTile(byte level, int bitmask)
    {
        if (bitmask < 0 || bitmask > 15) return null;
        Sprite[] arr = level == 1 ? tilesLevel1
                     : level == 2 ? tilesLevel2
                     : level == 3 ? tilesLevel3
                     : null;
        return arr != null && arr.Length > bitmask ? arr[bitmask] : null;
    }

    // idx: 0=left-low/right-high, 1=both-high, 2=left-high/right-low, 3=both-low.
    public Sprite GetCliffFace(bool southIsWater, int idx)
    {
        if (idx < 0 || idx > 3) return null;
        var arr = southIsWater ? cliffFaceWater : cliffFaceGround;
        return arr != null && arr.Length > idx ? arr[idx] : null;
    }
}
