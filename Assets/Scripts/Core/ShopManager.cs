using System.Collections.Generic;

public class ShopManager
{
    public const int ShopSize = 5;
    public const int RerollCost = 2;

    private GlobalUnitPool pool;
    public UnitDefinition[] slots = new UnitDefinition[ShopSize];
    private bool[] bought = new bool[ShopSize];

    public ShopManager(GlobalUnitPool pool) => this.pool = pool;

    public void GenerateShop()
    {
        // Return unsold units
        for (int i = 0; i < ShopSize; i++)
            if (slots[i] != null && !bought[i])
                pool.ReturnToPool(slots[i].unitType);

        var drawn = pool.DrawShop(ShopSize);
        for (int i = 0; i < ShopSize; i++)
        {
            slots[i] = drawn[i];
            bought[i] = false;
        }
    }

    public UnitInstance BuySlot(int slot, PlayerState player)
    {
        if (slot < 0 || slot >= ShopSize) return null;
        if (slots[slot] == null || bought[slot]) return null;
        if (player.gold < slots[slot].cost) return null;

        player.gold -= slots[slot].cost;
        var unit = new UnitInstance { definition = slots[slot] };
        bought[slot] = true;
        slots[slot] = null;
        return unit;
    }

    public bool TryReroll(PlayerState player)
    {
        if (player.gold < RerollCost) return false;
        player.gold -= RerollCost;
        GenerateShop();
        return true;
    }
}
