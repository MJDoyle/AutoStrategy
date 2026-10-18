using System.Collections.Generic;
using UnityEngine;

public struct PlacedBuilding
{
    public BuildingDefinition def;
    public float              worldX, worldY;  // world-pixel bottom-centre of sprite
    public int                tileX,  tileY;   // logical tile grid position (ignores elevation)
}

public class PlayerState
{
    public string playerName;
    public int    health = 100;
    public int    gold   = 5;
    public bool   isHuman;
    public int    avatarIndex;

    public UnitInstance[,]     board     = new UnitInstance[9, 9];
    public CampTerrain         terrain;
    public List<PlacedBuilding> buildings = new List<PlacedBuilding>();

    public int UnitCount()
    {
        int count = 0;
        for (int x = 0; x < 9; x++)
            for (int y = 0; y < 9; y++)
                if (board[x, y] != null) count++;
        return count;
    }

    // Finds the empty board slot nearest to the centre of the clearing.
    // Skips terrain-blocked cells (rocks, trees, water); falls back to any empty cell
    // if every clear cell is occupied.
    public (int x, int y) FindFirstEmpty()
    {
        // Convert clearing tile-centre to fractional cell coordinates.
        var (cx, cy) = CampTerrain.ClearingCenter();
        float centerCol = (cx - CampTerrain.UNIT_TILE_X0 - CampTerrain.UNIT_TILE_STRIDE * 0.5f)
                          / CampTerrain.UNIT_TILE_STRIDE;
        float centerRow = (cy - CampTerrain.UNIT_TILE_Y0 - CampTerrain.UNIT_TILE_STRIDE * 0.5f)
                          / CampTerrain.UNIT_TILE_STRIDE;

        (int x, int y) best = (-1, -1);
        float bestDist = float.MaxValue;
        bool foundClear = false;

        for (int y = 0; y < 9; y++)
        for (int x = 0; x < 9; x++)
        {
            if (board[x, y] != null) continue;
            bool blocked = terrain != null && terrain.IsCellBlocked(x, y);
            if (blocked && foundClear) continue;  // ignore blocked once a clear slot exists

            float dx = x - centerCol, dy = y - centerRow;
            float dist = dx * dx + dy * dy;

            if (!blocked && !foundClear)
            {
                foundClear = true;  // first clear slot — discard any blocked candidates
                best = (x, y);
                bestDist = dist;
            }
            else if (dist < bestDist)
            {
                best = (x, y);
                bestDist = dist;
            }
        }
        return best;
    }

    // Returns world-pixel bottom-centre position + tile coords for placing a new building.
    // Searches the clearing for the nearest free spot whose footprint doesn't overlap
    // any already-placed building.
    public (float wx, float wy, int tx, int ty) FindBuildingSpot(BuildingDefinition def)
    {
        var (cx, cy) = CampTerrain.ClearingCenter();

        var candidates = new List<(int tx, int ty)>();
        for (int tx = 0; tx < CampTerrain.SIZE; tx++)
        for (int ty = 0; ty < CampTerrain.SIZE; ty++)
        {
            float tcx   = tx + 0.5f;
            float tcy   = ty + 0.5f;
            float nearX = Mathf.Clamp(tcx, CampTerrain.SEG_X0, CampTerrain.SEG_X1);
            float dist  = Mathf.Sqrt((tcx - nearX) * (tcx - nearX) + (tcy - CampTerrain.SEG_Y) * (tcy - CampTerrain.SEG_Y));
            if (dist < CampTerrain.CLEAR_R - 1f) candidates.Add((tx, ty));
        }

        candidates.Sort((a, b) =>
        {
            float da = (a.tx + 0.5f - cx) * (a.tx + 0.5f - cx) + (a.ty + 0.5f - cy) * (a.ty + 0.5f - cy);
            float db = (b.tx + 0.5f - cx) * (b.tx + 0.5f - cx) + (b.ty + 0.5f - cy) * (b.ty + 0.5f - cy);
            return da.CompareTo(db);
        });

        foreach (var (tx, ty) in candidates)
        {
            bool overlaps = false;
            foreach (var b in buildings)
            {
                if (b.def == null) continue;
                if (def.OverlapsWith(tx, ty, b.def, b.tileX, b.tileY))
                { overlaps = true; break; }
            }
            if (overlaps) continue;

            float elev = terrain != null
                ? Mathf.Max(0f, terrain.heightMap[tx, ty] - 1) * CampLayout.TILE_PX
                : 0f;
            return (CampLayout.TileCenterX(tx), CampLayout.TileCenterY(ty) + elev, tx, ty);
        }

        // Fallback: clearing centre
        int fcx = Mathf.FloorToInt(cx), fcy = Mathf.FloorToInt(cy);
        return (cx * CampLayout.TILE_PX, cy * CampLayout.TILE_PX, fcx, fcy);
    }
}
