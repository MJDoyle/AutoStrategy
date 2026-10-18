using System.Collections.Generic;
using UnityEngine;

public class GlobalUnitPool
{
    private static readonly Dictionary<int, int> PoolSizeByTier = new Dictionary<int, int>
    {
        { 1, 29 }, { 2, 22 }, { 3, 18 }, { 4, 12 }, { 5, 10 }
    };

    private Dictionary<UnitType, int> pool = new Dictionary<UnitType, int>();
    private List<UnitDefinition> allDefinitions = new List<UnitDefinition>();

    public void Initialize(IList<UnitDefinition> definitions)
    {
        allDefinitions = new List<UnitDefinition>(definitions);
        pool.Clear();
        foreach (var def in definitions)
        {
            int size = PoolSizeByTier.TryGetValue(def.cost, out int s) ? s : 15;
            pool[def.unitType] = size;
        }
    }

    public List<UnitDefinition> DrawShop(int slots)
    {
        var available = new List<UnitDefinition>();
        foreach (var def in allDefinitions)
            if (pool.TryGetValue(def.unitType, out int count) && count > 0)
                available.Add(def);

        var result = new List<UnitDefinition>();
        for (int i = 0; i < slots; i++)
        {
            if (available.Count == 0) { result.Add(null); continue; }
            int idx = Random.Range(0, available.Count);
            var chosen = available[idx];
            result.Add(chosen);
            pool[chosen.unitType]--;
            if (pool[chosen.unitType] <= 0)
                available.Remove(chosen);
        }
        return result;
    }

    public void ReturnToPool(UnitType type, int count = 1)
    {
        if (pool.ContainsKey(type))
            pool[type] += count;
    }
}
