using System;
using System.Collections.Generic;

public class BoardManager
{
    private PlayerState player;
    public event Action<int, int> OnCellChanged;

    public BoardManager(PlayerState player) => this.player = player;

    public bool AutoPlace(UnitInstance unit)
    {
        var (x, y) = player.FindFirstEmpty();
        if (x < 0) return false;
        player.board[x, y] = unit;
        OnCellChanged?.Invoke(x, y);
        return true;
    }

    public bool PlaceAt(UnitInstance unit, int x, int y)
    {
        if (x < 0 || x >= 9 || y < 0 || y >= 9) return false;
        if (player.board[x, y] != null) return false;
        player.board[x, y] = unit;
        OnCellChanged?.Invoke(x, y);
        return true;
    }

    public bool MoveUnit(int fx, int fy, int tx, int ty)
    {
        if (player.board[fx, fy] == null) return false;
        if (tx < 0 || tx >= 9 || ty < 0 || ty >= 9) return false;

        // Swap if destination occupied
        var temp = player.board[tx, ty];
        player.board[tx, ty] = player.board[fx, fy];
        player.board[fx, fy] = temp;
        OnCellChanged?.Invoke(fx, fy);
        OnCellChanged?.Invoke(tx, ty);
        return true;
    }

    public bool SellUnit(int x, int y)
    {
        var unit = player.board[x, y];
        if (unit == null) return false;
        player.board[x, y] = null;
        player.gold += Math.Max(1, unit.definition.cost);
        OnCellChanged?.Invoke(x, y);
        return true;
    }

    private void CheckUpgrade(UnitType type, int star)
    {
        if (star >= 3) return;

        var positions = new List<(int x, int y)>();
        for (int x = 0; x < 9; x++)
            for (int y = 0; y < 9; y++)
            {
                var u = player.board[x, y];
                if (u != null && u.definition.unitType == type && u.starLevel == star)
                    positions.Add((x, y));
            }

        if (positions.Count < 3) return;

        // Keep the first position, clear the other two, upgrade
        var (kx, ky) = positions[0];
        for (int i = 1; i < 3; i++)
        {
            var (rx, ry) = positions[i];
            player.board[rx, ry] = null;
            OnCellChanged?.Invoke(rx, ry);
        }

        player.board[kx, ky] = new UnitInstance
        {
            definition = player.board[kx, ky].definition,
            starLevel = star + 1
        };
        OnCellChanged?.Invoke(kx, ky);
        CheckUpgrade(type, star + 1);
    }
}
