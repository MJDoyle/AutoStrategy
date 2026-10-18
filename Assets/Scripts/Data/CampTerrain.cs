using UnityEngine;

public class CampTerrain
{
    public const int SIZE = 30;

    // Clear zone: capsule defined by a horizontal segment + radius (all in tile coords)
    // The segment runs from (SEG_X0, SEG_Y) to (SEG_X1, SEG_Y); radius = CLEAR_R tiles
    // This creates the "moving circle" shape described in the design:
    // a circle sweeps from the right edge (SEG_X1) leftward (to SEG_X0) and off the
    // left edge, leaving a stadium/capsule clear zone in the right portion of the camp.
    public const int SEG_X0  = 13;
    public const int SEG_X1  = 29;  // SIZE - 1
    public const int SEG_Y   = 15;  // SIZE / 2
    public const int CLEAR_R = 5;

    // Centre of the clearing in tile coordinates.
    public static (float x, float y) ClearingCenter() =>
        ((SEG_X0 + SEG_X1) * 0.5f, SEG_Y);

    // Unit board (9×9) mapped into tile space — one tile per cell, centred in the clearing.
    // Clearing centre is tile (21, 15); 9 cells centred there → X0=17, Y0=11.
    public const int UNIT_TILE_X0 = 17;
    public const int UNIT_TILE_Y0 = 11;
    public const int UNIT_TILE_STRIDE = 1;  // tiles per board slot

    public TerrainCell[,] grid     = new TerrainCell[SIZE, SIZE];
    public byte[,]         variant  = new byte[SIZE, SIZE];
    public float[,]        phase    = new float[SIZE, SIZE];
    public byte[,]         heightMap = new byte[SIZE, SIZE];

    public bool IsMiddleTerrain;

    public void Generate(int seed, bool isMiddle = false, TerrainConfig cfg = null)
    {
        IsMiddleTerrain = isMiddle;
        var rng = new System.Random(seed);

        for (int tx = 0; tx < SIZE; tx++)
        for (int ty = 0; ty < SIZE; ty++)
        {
            grid[tx, ty]      = TerrainCell.Clear;
            variant[tx, ty]   = 0;
            phase[tx, ty]     = 0f;
            heightMap[tx, ty] = 1;
        }

        GenerateHeightMap(rng, cfg);

        if (isMiddle)
            GenerateMiddle(rng);
        else
            GenerateCamp(rng);
    }

    void GenerateHeightMap(System.Random rng, TerrainConfig cfg)
    {
        float ox = (float)(rng.NextDouble() * 997.3f);
        float oy = (float)(rng.NextDouble() * 997.3f);

        float freq    = cfg != null ? cfg.noiseFrequency : 0.04f;
        int   octaves = cfg != null ? cfg.noiseOctaves   : 6;
        float hWater  = cfg != null ? cfg.heightWater    : 0.12f;
        float hRaised = cfg != null ? cfg.heightRaised   : 0.65f;
        float hPeak   = cfg != null ? cfg.heightPeak     : 2.0f;

        // Pass 1: collect raw FBM values and find range
        var raw = new float[SIZE, SIZE];
        float minH = float.MaxValue, maxH = float.MinValue;
        for (int tx = 0; tx < SIZE; tx++)
        for (int ty = 0; ty < SIZE; ty++)
        {
            float h = FBM(ox + tx * freq, oy + ty * freq, octaves, 2f, 0.5f);
            raw[tx, ty] = h;
            if (h < minH) minH = h;
            if (h > maxH) maxH = h;
        }

        // Pass 2: normalise to [0,1] then apply thresholds
        float range = Mathf.Max(maxH - minH, 0.001f);
        for (int tx = 0; tx < SIZE; tx++)
        for (int ty = 0; ty < SIZE; ty++)
        {
            float h = (raw[tx, ty] - minH) / range;
            heightMap[tx, ty] = h < hWater  ? (byte)0
                              : h < hRaised ? (byte)1
                              : h < hPeak   ? (byte)2
                              : (byte)3;
        }

        // Pass 3: remove isolated elevated tiles (no same-or-higher neighbour → reduce by 1).
        // Three passes catch chain-reactions without forcing slope gradients.
        // Unlike slope-limiting this keeps flat elevated platforms with sharp cliff edges;
        // slope-limiting was producing multi-row "mountain" walls instead.
        for (int pass = 0; pass < 3; pass++)
        for (int tx = 0; tx < SIZE; tx++)
        for (int ty = 0; ty < SIZE; ty++)
        {
            byte h = heightMap[tx, ty];
            if (h <= 1) continue;
            bool hasNeighbour =
                (tx > 0      && heightMap[tx-1, ty] >= h) ||
                (tx < SIZE-1 && heightMap[tx+1, ty] >= h) ||
                (ty > 0      && heightMap[tx, ty-1] >= h) ||
                (ty < SIZE-1 && heightMap[tx, ty+1] >= h);
            if (!hasNeighbour) heightMap[tx, ty]--;
        }
    }

    // Generates the middle battlefield so its height map blends from the player camp's
    // right edge (leftCamp tx=SIZE-1) to the enemy camp's right edge (rightCamp tx=SIZE-1,
    // which faces the middle because the enemy section is rendered mirrored).
    public void GenerateConnected(int seed, CampTerrain leftCamp, CampTerrain rightCamp, TerrainConfig cfg = null)
    {
        IsMiddleTerrain = true;
        var rng = new System.Random(seed);

        for (int tx = 0; tx < SIZE; tx++)
        for (int ty = 0; ty < SIZE; ty++)
        {
            grid[tx, ty]      = TerrainCell.Clear;
            variant[tx, ty]   = 0;
            phase[tx, ty]     = 0f;
            heightMap[tx, ty] = 1;
        }

        GenerateConnectedHeightMap(rng, cfg, leftCamp, rightCamp);
        GenerateConnectedDecorations(rng, leftCamp, rightCamp);
    }

    void GenerateConnectedHeightMap(System.Random rng, TerrainConfig cfg,
                                    CampTerrain leftCamp, CampTerrain rightCamp)
    {
        float ox = (float)(rng.NextDouble() * 997.3f);
        float oy = (float)(rng.NextDouble() * 997.3f);

        float freq    = cfg != null ? cfg.noiseFrequency : 0.04f;
        int   octaves = cfg != null ? cfg.noiseOctaves   : 6;
        float hWater  = cfg != null ? cfg.heightWater    : 0.12f;
        float hRaised = cfg != null ? cfg.heightRaised   : 0.65f;
        float hPeak   = cfg != null ? cfg.heightPeak     : 2.0f;

        // Raw FBM, normalised to [0,1]
        var raw = new float[SIZE, SIZE];
        float minH = float.MaxValue, maxH = float.MinValue;
        for (int tx = 0; tx < SIZE; tx++)
        for (int ty = 0; ty < SIZE; ty++)
        {
            float h = FBM(ox + tx * freq, oy + ty * freq, octaves, 2f, 0.5f);
            raw[tx, ty] = h;
            if (h < minH) minH = h;
            if (h > maxH) maxH = h;
        }
        float range = Mathf.Max(maxH - minH, 0.001f);
        for (int tx = 0; tx < SIZE; tx++)
        for (int ty = 0; ty < SIZE; ty++)
            raw[tx, ty] = (raw[tx, ty] - minH) / range;

        // Map discrete height levels to continuous band midpoints so we can
        // blend between the adjacent camp edges and the FBM value.
        float hPeakC = Mathf.Min(hPeak, 1f);
        float[] hVal =
        {
            hWater  * 0.5f,
            (hWater  + hRaised) * 0.5f,
            (hRaised + hPeakC)  * 0.5f,
            (hPeakC  + 1.0f)    * 0.5f,
        };

        // Over this many columns from each edge the height blends fully to the boundary.
        const int BLEND = 12;

        for (int tx = 0; tx < SIZE; tx++)
        for (int ty = 0; ty < SIZE; ty++)
        {
            // smoothstep weights: 0 at the boundary column, 1 at BLEND columns in
            float tL = Mathf.Clamp01(tx / (float)BLEND);
            float tR = Mathf.Clamp01((SIZE - 1 - tx) / (float)BLEND);
            tL = tL * tL * (3f - 2f * tL);
            tR = tR * tR * (3f - 2f * tR);

            float wL = 1f - tL;
            float wR = 1f - tR;
            float wN = Mathf.Max(0f, 1f - wL - wR);
            float wSum = wN + wL + wR;

            float vL = hVal[Mathf.Clamp(leftCamp.heightMap[SIZE - 1, ty],  0, 3)];
            float vR = hVal[Mathf.Clamp(rightCamp.heightMap[SIZE - 1, ty], 0, 3)];
            float vN = raw[tx, ty];

            float v = (vN * wN + vL * wL + vR * wR) / wSum;

            heightMap[tx, ty] = v < hWater  ? (byte)0
                               : v < hRaised ? (byte)1
                               : v < hPeak   ? (byte)2
                               : (byte)3;
        }

        // Snapshot boundary columns before isolation removal so they are never changed.
        var bL = new byte[SIZE];
        var bR = new byte[SIZE];
        for (int ty = 0; ty < SIZE; ty++)
        {
            bL[ty] = heightMap[0,        ty];
            bR[ty] = heightMap[SIZE - 1, ty];
        }

        for (int pass = 0; pass < 3; pass++)
        for (int tx = 0; tx < SIZE; tx++)
        for (int ty = 0; ty < SIZE; ty++)
        {
            byte h = heightMap[tx, ty];
            if (h <= 1) continue;
            bool hasNeighbour =
                (tx > 0      && heightMap[tx-1, ty] >= h) ||
                (tx < SIZE-1 && heightMap[tx+1, ty] >= h) ||
                (ty > 0      && heightMap[tx, ty-1] >= h) ||
                (ty < SIZE-1 && heightMap[tx, ty+1] >= h);
            if (!hasNeighbour) heightMap[tx, ty]--;
        }

        for (int ty = 0; ty < SIZE; ty++)
        {
            heightMap[0,        ty] = bL[ty];
            heightMap[SIZE - 1, ty] = bR[ty];
        }
    }

    static float FBM(float x, float y, int octaves, float lacunarity, float gain)
    {
        float v = 0f, amp = 0.5f, freq = 1f, max = 0f;
        for (int i = 0; i < octaves; i++)
        {
            v    += Mathf.PerlinNoise(x * freq, y * freq) * amp;
            max  += amp;
            amp  *= gain;
            freq *= lacunarity;
        }
        return v / max;
    }

    void GenerateCamp(System.Random rng)
    {
        for (int tx = 0; tx < SIZE; tx++)
        for (int ty = 0; ty < SIZE; ty++)
        {
            float cx   = tx + 0.5f;
            float cy   = ty + 0.5f;
            float px   = Mathf.Clamp(cx, SEG_X0, SEG_X1);
            float dist = Mathf.Sqrt((cx - px) * (cx - px) + (cy - SEG_Y) * (cy - SEG_Y));

            if (dist > CLEAR_R)
            {
                if (rng.NextDouble() < 0.88)
                    SetTree(tx, ty, rng);
            }
            else
            {
                double r = rng.NextDouble();
                if      (r < 0.022) SetRock(tx, ty, rng);
                else if (r < 0.042) SetWater(tx, ty, rng);
            }
        }
    }

    // FBM-based decoration for the connected middle section.
    // Density blends from leftCamp's right edge to rightCamp's right edge using the
    // same smoothstep technique as the height map, so tree clusters continue naturally
    // across the seams rather than cutting off sharply.
    void GenerateConnectedDecorations(System.Random rng, CampTerrain leftCamp, CampTerrain rightCamp)
    {
        float ox = (float)(rng.NextDouble() * 997.3f);
        float oy = (float)(rng.NextDouble() * 997.3f);

        // Higher frequency than terrain noise → finer-grained cluster detail.
        const float FREQ    = 0.09f;
        const int   OCTAVES = 5;
        const int   BLEND   = 12;

        // Raw FBM, normalised to [0,1].
        var raw = new float[SIZE, SIZE];
        float minH = float.MaxValue, maxH = float.MinValue;
        for (int tx = 0; tx < SIZE; tx++)
        for (int ty = 0; ty < SIZE; ty++)
        {
            float h = FBM(ox + tx * FREQ, oy + ty * FREQ, OCTAVES, 2f, 0.5f);
            raw[tx, ty] = h;
            if (h < minH) minH = h;
            if (h > maxH) maxH = h;
        }
        float range = Mathf.Max(maxH - minH, 0.001f);
        for (int tx = 0; tx < SIZE; tx++)
        for (int ty = 0; ty < SIZE; ty++)
            raw[tx, ty] = (raw[tx, ty] - minH) / range;

        // Encode each cell type as a continuous density so we can lerp between them.
        // Tree → dense (0.85), Rock → medium (0.55), Water → sparse (0.30), Clear → open (0.05).
        static float Encode(TerrainCell c) => c switch
        {
            TerrainCell.Tree  => 0.85f,
            TerrainCell.Rock  => 0.55f,
            TerrainCell.Water => 0.30f,
            _                 => 0.05f,
        };

        for (int tx = 0; tx < SIZE; tx++)
        for (int ty = 0; ty < SIZE; ty++)
        {
            float tL = Mathf.Clamp01(tx / (float)BLEND);
            float tR = Mathf.Clamp01((SIZE - 1 - tx) / (float)BLEND);
            tL = tL * tL * (3f - 2f * tL);
            tR = tR * tR * (3f - 2f * tR);

            float wL   = 1f - tL;
            float wR   = 1f - tR;
            float wN   = Mathf.Max(0f, 1f - wL - wR);
            float wSum = wN + wL + wR;

            float vL = Encode(leftCamp.grid[SIZE - 1, ty]);
            float vR = Encode(rightCamp.grid[SIZE - 1, ty]);
            float v  = (raw[tx, ty] * wN + vL * wL + vR * wR) / wSum;

            // Decode back to cell type. Thresholds chosen so that the open middle has
            // roughly 20% trees, 4% rocks, 4% water when boundaries are all Clear.
            if      (v > 0.65f) SetTree(tx, ty, rng);
            else if (v > 0.60f) SetRock(tx, ty, rng);
            else if (v > 0.25f) SetWater(tx, ty, rng);
            // else stays Clear
        }
    }

    void GenerateMiddle(System.Random rng)
    {
        // Battlefield: some cover (trees & rocks) but mostly open
        for (int tx = 0; tx < SIZE; tx++)
        for (int ty = 0; ty < SIZE; ty++)
        {
            double r = rng.NextDouble();
            if      (r < 0.18) SetTree(tx, ty, rng);
            else if (r < 0.22) SetRock(tx, ty, rng);
            else if (r < 0.26) SetWater(tx, ty, rng);
        }
    }

    void SetTree(int tx, int ty, System.Random rng)
    {
        grid[tx, ty]    = TerrainCell.Tree;
        variant[tx, ty] = (byte)rng.Next(4);
        phase[tx, ty]   = (float)(rng.NextDouble() * 8.0);
    }

    void SetRock(int tx, int ty, System.Random rng)
    {
        grid[tx, ty]    = TerrainCell.Rock;
        variant[tx, ty] = (byte)rng.Next(4);
    }

    void SetWater(int tx, int ty, System.Random rng)
    {
        grid[tx, ty]    = TerrainCell.Water;
        variant[tx, ty] = (byte)rng.Next(4);
        phase[tx, ty]   = (float)(rng.NextDouble() * 4.0);
    }

    public bool IsBlocked(int tx, int ty)
    {
        if (tx < 0 || tx >= SIZE || ty < 0 || ty >= SIZE) return true;
        return grid[tx, ty] != TerrainCell.Clear;
    }

    // Is the centre tile of board cell (col, row) blocked?
    // Unit footprint is 1 tile (64×64 px), so only the centre matters.
    public bool IsCellBlocked(int col, int row)
    {
        int cx = UNIT_TILE_X0 + col * UNIT_TILE_STRIDE + UNIT_TILE_STRIDE / 2;
        int cy = UNIT_TILE_Y0 + row * UNIT_TILE_STRIDE + UNIT_TILE_STRIDE / 2;
        return IsBlocked(cx, cy);
    }
}
