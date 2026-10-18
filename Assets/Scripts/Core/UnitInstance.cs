public class UnitInstance
{
    public UnitDefinition definition;
    public int starLevel = 1;

    public int MaxHealth => definition.HealthAtStar(starLevel);
    public int Attack => definition.AttackAtStar(starLevel);
}
