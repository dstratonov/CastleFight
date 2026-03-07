using UnityEngine;

[CreateAssetMenu(menuName = "CastleFight/Hero Data")]
public class HeroData : ScriptableObject
{
    public string heroName;
    public GameObject prefab;

    [Header("Movement")]
    public float moveSpeed = 8f;

    [Header("Auto Attack")]
    public float attackRange = 8f;
    public float attackCooldown = 1f;
    public float attackDamage = 15f;
    public AttackType attackType = AttackType.Hero;
    public GameObject projectilePrefab;
    public float projectileSpeed = 15f;

    [Header("Survivability")]
    public int maxHealth = 500;
    public float respawnTime = 10f;
    public ArmorType armorType = ArmorType.Hero;
}
