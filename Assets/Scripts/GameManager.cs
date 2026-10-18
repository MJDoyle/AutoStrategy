using System;
using System.Collections.Generic;
using UnityEngine;

public enum GamePhase { Shop, Combat }

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Display")]
    [Tooltip("Uniform pixel scale for all unit sprites.")]
    public float spriteScale = 1f;

    [Header("Combat")]
    [Tooltip("Global combat speed multiplier — 1 = normal, 2 = double speed, etc.")]
    [Range(0.25f, 8f)]
    public float combatSpeed = 1f;

    public GamePhase CurrentPhase { get; private set; } = GamePhase.Shop;
    public PlayerState CombatOpponent { get; private set; }
    public PlayerState NextOpponent   { get; private set; }
    public PlayerState ViewedPlayer   { get; private set; }
    public int Round { get; private set; } = 1;

    public PlayerState[] Players { get; private set; } = new PlayerState[8];
    public PlayerState HumanPlayer => Players[0];

    public ShopManager Shop { get; private set; }
    public BoardManager Board { get; private set; }

    public CampTerrain MiddleTerrain { get; private set; }

    public event Action OnShopChanged;
    public event Action OnGoldChanged;
    public event Action OnPlayersChanged;
    public event Action<GamePhase> OnPhaseChanged;
    public event Action<PlayerState> OnViewedPlayerChanged;
    public event Action OnMatchupChanged;
    public event Action OnBuildingsChanged;
    public event Action OnPlacementChanged;

    public int                PendingUnitSlot { get; private set; } = -1;
    public BuildingDefinition PendingBuilding { get; private set; }
    public bool               IsPendingPlacement => PendingUnitSlot >= 0 || PendingBuilding != null;

    public Sprite PendingSprite =>
        PendingBuilding != null    ? PendingBuilding.sprite :
        PendingUnitSlot >= 0       ? Shop?.slots[PendingUnitSlot]?.idleSprite :
        null;

    private GlobalUnitPool globalPool = new GlobalUnitPool();
    static readonly string[] AiNames = { "Alistair", "Morgan", "Drake", "Lyra", "Finn", "Sera", "Cael" };

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;

        var defs = new List<UnitDefinition>(Resources.LoadAll<UnitDefinition>("UnitDefinitions"));
        if (defs.Count == 0)
        {
            Debug.LogError("No UnitDefinitions found. Run Tools > AutoStrategy > Initialize Assets first.");
            return;
        }

        globalPool.Initialize(defs);

        Players[0] = new PlayerState { playerName = "You", gold = 10, isHuman = true, avatarIndex = 0 };
        for (int i = 1; i < 8; i++)
            Players[i] = new PlayerState
            {
                playerName  = AiNames[i - 1],
                gold        = UnityEngine.Random.Range(2, 8),
                health      = UnityEngine.Random.Range(60, 100),
                isHuman     = false,
                avatarIndex = i
            };

        // Generate unique terrain for every player, then place the starting monastery.
        var terrainCfg = Resources.Load<TerrainConfig>("TerrainConfig");
        int baseSeed = UnityEngine.Random.Range(0, 100000);
        for (int i = 0; i < 8; i++)
        {
            Players[i].terrain = new CampTerrain();
            Players[i].terrain.Generate(baseSeed + i, cfg: terrainCfg);
            PlaceMonastery(Players[i]);
        }

        ViewedPlayer = Players[0];

        Shop = new ShopManager(globalPool);
        Board = new BoardManager(HumanPlayer);
        Shop.GenerateShop();
        GenerateMatchup();
    }

    void GenerateMatchup()
    {
        var alive = new List<PlayerState>();
        for (int i = 1; i < 8; i++)
            if (Players[i].health > 0) alive.Add(Players[i]);
        NextOpponent = alive.Count > 0
            ? alive[UnityEngine.Random.Range(0, alive.Count)]
            : Players[1];
        OnMatchupChanged?.Invoke();
    }

    public void ViewPlayer(PlayerState p)
    {
        if (p == null || ViewedPlayer == p) return;
        ViewedPlayer = p;
        OnViewedPlayerChanged?.Invoke(p);
    }

    // ── Buildings ─────────────────────────────────────────────────────────────

    void PlaceMonastery(PlayerState player)
    {
        var defs = Resources.LoadAll<BuildingDefinition>("BuildingDefinitions");
        BuildingDefinition def = null;
        foreach (var d in defs)
            if (string.Equals(d.buildingName, "Monastery", System.StringComparison.OrdinalIgnoreCase))
            { def = d; break; }
        if (def == null) return;

        var (cx, cy) = CampTerrain.ClearingCenter();
        int htx = Mathf.Clamp(Mathf.FloorToInt(cx), 0, CampTerrain.SIZE - 1);
        int hty = Mathf.Clamp(Mathf.FloorToInt(cy), 0, CampTerrain.SIZE - 1);
        float elev = player.terrain != null
            ? Mathf.Max(0f, player.terrain.heightMap[htx, hty] - 1) * CampLayout.TILE_PX
            : 0f;
        player.buildings.Add(new PlacedBuilding
        {
            def    = def,
            worldX = CampLayout.TileCenterX(htx),
            worldY = CampLayout.TileCenterY(hty) + elev,
            tileX  = htx,
            tileY  = hty,
        });
    }

    public bool TryBuyBuilding(BuildingDefinition def)
    {
        if (def == null || HumanPlayer.gold < def.cost) return false;
        HumanPlayer.gold -= def.cost;
        var (wx, wy, tx, ty) = HumanPlayer.FindBuildingSpot(def);
        HumanPlayer.buildings.Add(new PlacedBuilding { def = def, worldX = wx, worldY = wy, tileX = tx, tileY = ty });
        OnGoldChanged?.Invoke();
        OnBuildingsChanged?.Invoke();
        return true;
    }

    // ── Placement ─────────────────────────────────────────────────────────────

    public void StartUnitPlacement(int shopSlot)
    {
        var def = Shop?.slots[shopSlot];
        if (def == null || HumanPlayer.gold < def.cost) return;
        PendingUnitSlot = shopSlot;
        PendingBuilding = null;
        OnPlacementChanged?.Invoke();
    }

    public void StartBuildingPlacement(BuildingDefinition def)
    {
        if (def == null || HumanPlayer.gold < def.cost) return;
        PendingBuilding  = def;
        PendingUnitSlot  = -1;
        OnPlacementChanged?.Invoke();
    }

    public void CancelPlacement()
    {
        PendingUnitSlot = -1;
        PendingBuilding = null;
        OnPlacementChanged?.Invoke();
    }

    public bool ConfirmUnitPlacement(int col, int row)
    {
        if (PendingUnitSlot < 0) return false;
        if (HumanPlayer.board[col, row] != null) return false;
        if (HumanPlayer.terrain?.IsCellBlocked(col, row) == true) return false;

        int slot = PendingUnitSlot;
        CancelPlacement();

        var unit = Shop.BuySlot(slot, HumanPlayer);
        if (unit == null) return false;

        Board.PlaceAt(unit, col, row);
        OnShopChanged?.Invoke();
        OnGoldChanged?.Invoke();
        return true;
    }

    public bool ConfirmBuildingPlacement(int tx, int ty)
    {
        if (PendingBuilding == null) return false;
        if (HumanPlayer.gold < PendingBuilding.cost) return false;

        // Centre tile must be inside the clearing.
        float tcx   = tx + 0.5f;
        float tcy   = ty + 0.5f;
        float nearX = Mathf.Clamp(tcx, CampTerrain.SEG_X0, CampTerrain.SEG_X1);
        float dist  = Mathf.Sqrt((tcx - nearX) * (tcx - nearX)
                                + (tcy - CampTerrain.SEG_Y) * (tcy - CampTerrain.SEG_Y));
        if (dist >= CampTerrain.CLEAR_R - 1f) return false;

        // Footprint must not overlap any existing building's footprint.
        foreach (var b in HumanPlayer.buildings)
        {
            if (b.def == null) continue;
            if (PendingBuilding.OverlapsWith(tx, ty, b.def, b.tileX, b.tileY))
                return false;
        }

        var def = PendingBuilding;
        CancelPlacement();

        float elev = HumanPlayer.terrain != null
            ? Mathf.Max(0f, HumanPlayer.terrain.heightMap[tx, ty] - 1) * CampLayout.TILE_PX
            : 0f;

        HumanPlayer.gold -= def.cost;
        HumanPlayer.buildings.Add(new PlacedBuilding
        {
            def    = def,
            worldX = CampLayout.TileCenterX(tx),
            worldY = CampLayout.TileCenterY(ty) + elev,
            tileX  = tx,
            tileY  = ty,
        });
        OnGoldChanged?.Invoke();
        OnBuildingsChanged?.Invoke();
        return true;
    }

    // ── Shop ──────────────────────────────────────────────────────────────────

    public bool TryBuyUnit(int shopSlot)
    {
        var unit = Shop.BuySlot(shopSlot, HumanPlayer);
        if (unit == null) return false;
        if (!Board.AutoPlace(unit))
        {
            HumanPlayer.gold += unit.definition.cost;
            return false;
        }
        OnShopChanged?.Invoke();
        OnGoldChanged?.Invoke();
        return true;
    }

    public bool TryReroll()
    {
        if (!Shop.TryReroll(HumanPlayer)) return false;
        OnShopChanged?.Invoke();
        OnGoldChanged?.Invoke();
        return true;
    }

    public bool TryMoveUnit(int fx, int fy, int tx, int ty) => Board.MoveUnit(fx, fy, tx, ty);

    public bool TrySellUnit(int x, int y)
    {
        if (!Board.SellUnit(x, y)) return false;
        OnGoldChanged?.Invoke();
        return true;
    }

    // ── Combat ────────────────────────────────────────────────────────────────

    public void StartFight()
    {
        if (CurrentPhase != GamePhase.Shop) return;
        CancelPlacement();

        // All AIs spend available gold then form up
        for (int i = 1; i < 8; i++)
            BuyAndFormAi(Players[i]);

        CombatOpponent = NextOpponent ?? Players[1];

        // Generate middle terrain that blends from the player's right edge into the enemy's right edge
        var terrainCfgMid = Resources.Load<TerrainConfig>("TerrainConfig");
        MiddleTerrain = new CampTerrain();
        MiddleTerrain.GenerateConnected(UnityEngine.Random.Range(0, 100000),
                                        leftCamp:  HumanPlayer.terrain,
                                        rightCamp: CombatOpponent.terrain,
                                        cfg:       terrainCfgMid);

        CurrentPhase = GamePhase.Combat;
        OnPhaseChanged?.Invoke(CurrentPhase);
    }

    public void EndCombat(int playerSurvivors, int enemySurvivors)
    {
        if (enemySurvivors > 0) HumanPlayer.health -= enemySurvivors * 2;
        if (playerSurvivors > 0) CombatOpponent.health -= playerSurvivors * 2;

        Round++;
        int income = Mathf.Min(10, 3 + Round);
        HumanPlayer.gold += income;

        // AIs earn income too (they'll spend it next fight)
        for (int i = 1; i < 8; i++)
            Players[i].gold += income;

        Time.timeScale = 1f; // reset speed when returning to shop

        CurrentPhase = GamePhase.Shop;
        OnPhaseChanged?.Invoke(CurrentPhase);

        Shop.GenerateShop();
        GenerateMatchup();
        OnShopChanged?.Invoke();
        OnGoldChanged?.Invoke();
        OnPlayersChanged?.Invoke();
    }

    // ── AI buying & formation ─────────────────────────────────────────────────

    // Formation slots: front (col 8) to back (col 4), spreading outward from centre row
    private static readonly (int col, int row)[] FormationSlots =
    {
        (8,4),(8,3),(8,5),(8,2),(8,6),(8,1),(8,7),(8,0),(8,8),
        (7,4),(7,3),(7,5),(7,2),(7,6),(7,1),(7,7),(7,0),(7,8),
        (6,4),(6,3),(6,5),(6,2),(6,6),(6,1),(6,7),(6,0),(6,8),
        (5,4),(5,3),(5,5),(5,2),(5,6),(5,1),(5,7),(5,0),(5,8),
        (4,4),(4,3),(4,5),(4,2),(4,6),(4,1),(4,7),(4,0),(4,8),
    };

    private void BuyAndFormAi(PlayerState ai)
    {
        var defs = Resources.LoadAll<UnitDefinition>("UnitDefinitions");
        if (defs.Length == 0) return;

        // Spend all available gold on random units
        int attempts = 0;
        while (ai.gold > 0 && attempts < 60)
        {
            attempts++;
            var def = defs[UnityEngine.Random.Range(0, defs.Length)];
            if (def.cost > ai.gold) continue;
            var (x, y) = ai.FindFirstEmpty();
            if (x < 0) break;
            ai.board[x, y] = new UnitInstance { definition = def };
            ai.gold -= def.cost;
        }

        // Rearrange into formation: melee (low range) at front, ranged at back
        var units = new List<UnitInstance>();
        for (int x = 0; x < 9; x++)
            for (int y = 0; y < 9; y++)
                if (ai.board[x, y] != null) { units.Add(ai.board[x, y]); ai.board[x, y] = null; }

        units.Sort((a, b) => a.definition.range.CompareTo(b.definition.range));

        for (int i = 0; i < units.Count && i < FormationSlots.Length; i++)
        {
            var (col, row) = FormationSlots[i];
            ai.board[col, row] = units[i];
        }
    }
}
