using UnityEngine;
using Mirror;

public class HeroAutoAttack : NetworkBehaviour
{
    [SerializeField] private float attackRange = 8f;
    [SerializeField] private float attackCooldown = 1f;
    [SerializeField] private float attackDamage = 15f;
    [SerializeField] private AttackType attackType = AttackType.Hero;
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private float projectileSpeed = 15f;
    [SerializeField] private LayerMask enemyLayer;

    public float AttackDamage => attackDamage;
    public float AttackRange => attackRange;
    public float AttackCooldown => attackCooldown;

    private float lastAttackTime;
    private readonly Collider[] scanResults = new Collider[32];

    private void Update()
    {
        if (!isServer) return;
        if (Time.time - lastAttackTime < attackCooldown) return;

        var target = FindNearestEnemy();
        if (target != null)
        {
            Attack(target);
            lastAttackTime = Time.time;
        }
    }

    private Health FindNearestEnemy()
    {
        int count = Physics.OverlapSphereNonAlloc(transform.position, attackRange, scanResults, enemyLayer);

        Health nearest = null;
        float nearestDist = float.MaxValue;
        var myPlayer = GetComponent<NetworkPlayer>();
        int myTeam = myPlayer != null ? myPlayer.TeamId : -1;

        for (int i = 0; i < count; i++)
        {
            var health = scanResults[i].GetComponent<Health>();
            if (health == null || health.IsDead) continue;

            int targetTeam = health.TeamId;
            if (targetTeam == myTeam || targetTeam < 0) continue;

            float dist = Vector3.Distance(transform.position, scanResults[i].transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = health;
            }
        }

        return nearest;
    }

    [Server]
    private void Attack(Health target)
    {
        Vector3 spawnPos = firePoint != null ? firePoint.position : transform.position + Vector3.up;

        var targetArmor = target.GetComponent<Unit>()?.Data?.armorType ?? ArmorType.Unarmored;
        float finalDamage = DamageSystem.CalculateDamage(attackDamage, attackType, targetArmor);

        // Server: spawn damage-dealing projectile — damage applies on impact, not instantly
        Projectile.Spawn(spawnPos, target.transform, projectileSpeed,
            finalDamage, gameObject, true, attackType);

        // Client: spawn visual-only projectile
        var targetNetId = target.GetComponent<NetworkIdentity>();
        if (targetNetId != null)
            RpcSpawnProjectile(targetNetId, spawnPos, (int)attackType);
    }

    [ClientRpc]
    private void RpcSpawnProjectile(NetworkIdentity targetId, Vector3 spawnPos, int atkType)
    {
        if (isServer) return;
        if (targetId == null) return;
        Projectile.Spawn(spawnPos, targetId.transform, projectileSpeed,
            0f, null, false, (AttackType)atkType);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
