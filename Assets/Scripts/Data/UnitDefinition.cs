using UnityEngine;

[CreateAssetMenu(fileName = "New Unit", menuName = "AutoStrategy/Unit Definition")]
public class UnitDefinition : ScriptableObject
{
    public UnitType unitType;
    public string unitName;

    [Header("Sprites")]
    public Sprite idleSprite;
    public Sprite[] idleFrames;
    public Sprite[] runFrames;
    public Sprite[] attackFrames;
    public Sprite projectileSprite;  // e.g. arrow for ranged units

    [Header("Stats")]
    public int cost = 1;
    public int baseHealth = 100;
    public int baseAttack = 20;
    public float attackSpeed = 1f;
    public int range = 1;
    [Tooltip("Movement speed in canvas px/sec at 1x global combat speed")]
    public float moveSpeed = 120f;
    [TextArea] public string description;

    public int HealthAtStar(int star) => baseHealth * (int)Mathf.Pow(2, star - 1);
    public int AttackAtStar(int star) => baseAttack * (int)Mathf.Pow(2, star - 1);
}
