using UnityEngine;

[CreateAssetMenu(fileName = "New Building", menuName = "AutoStrategy/Building Definition")]
public class BuildingDefinition : ScriptableObject
{
    public string buildingName;
    public Sprite sprite;
    public int    cost = 3;
    public float  fogRevealRadius = 4f;

    // Legacy symmetric radius — still used as the fallback when explicit footprint dims are 0.
    public int footprintRadius = 0;   // 0=1×1 tile, 1=3×3 tiles, 2=5×5 tiles

    [Header("Explicit Footprint (overrides footprintRadius when footprintI > 0)")]
    [Tooltip("Footprint width in tiles (i columns). 0 = derive from footprintRadius.")]
    public int footprintI = 0;
    [Tooltip("Footprint height in tiles (j rows). 0 = derive from footprintRadius.")]
    public int footprintJ = 0;
    [Tooltip("Anchor column inside the footprint (0 = left). Anchor tile maps to (tileX, tileY).")]
    public int anchorI = 0;
    [Tooltip("Anchor row inside the footprint (0 = top row, j increases downward).")]
    public int anchorJ = 0;

    // Effective footprint dimensions — use explicit values when set, else derive from footprintRadius.
    public int EffI  => footprintI > 0 ? footprintI : 2 * footprintRadius + 1;
    public int EffJ  => footprintJ > 0 ? footprintJ : 2 * footprintRadius + 1;
    public int EffAI => footprintI > 0 ? anchorI : footprintRadius;   // anchor col
    public int EffAJ => footprintJ > 0 ? anchorJ : footprintRadius;   // anchor row (0=top)

    // Extents from anchor tile in GAME tile coords (game Y = up; j=0 in footprint = top = highest gameY).
    public int ExtLeft  => EffAI;
    public int ExtRight => EffI - 1 - EffAI;
    public int ExtAbove => EffAJ;                  // tiles above anchor = higher gameY
    public int ExtBelow => EffJ - 1 - EffAJ;       // tiles below anchor = lower gameY

    // Returns true if tile (tileX, tileY) falls within this building's footprint
    // when the building's anchor is placed at (atX, atY).
    public bool ContainsTile(int atX, int atY, int tileX, int tileY)
        => tileX >= atX - ExtLeft  && tileX <= atX + ExtRight
        && tileY >= atY - ExtBelow && tileY <= atY + ExtAbove;

    // Returns true if this building's footprint (anchor at ax,ay) overlaps another's (anchor at bx,by).
    public bool OverlapsWith(int ax, int ay, BuildingDefinition other, int bx, int by)
    {
        int alx = ax - ExtLeft,  arx = ax + ExtRight;
        int aly = ay - ExtBelow, ary = ay + ExtAbove;
        int blx = bx - other.ExtLeft,  brx = bx + other.ExtRight;
        int bly = by - other.ExtBelow, bry = by + other.ExtAbove;
        return alx <= brx && arx >= blx && aly <= bry && ary >= bly;
    }

    [Header("Sprite Alignment (world pixels, relative to anchor tile centre)")]
    [Tooltip("Shifts the sprite left/right relative to the footprint anchor.")]
    public float spriteOffsetX = 0f;
    [Tooltip("Shifts the sprite base up(+)/down(-) relative to TileCenterY of the anchor tile.")]
    public float spriteOffsetY = 0f;

    [TextArea] public string description;
}
