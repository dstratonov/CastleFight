using UnityEngine;
using Mirror;
using System.Collections.Generic;
using CastleFight;

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

        RpcSpawnProjectile(spawnPos, target.gameObject);

        var targetArmor = target.GetComponent<Unit>()?.Data?.armorType ?? ArmorType.Unarmored;
        float finalDamage = DamageSystem.CalculateDamage(attackDamage, attackType, targetArmor);
        target.TakeDamage(finalDamage, gameObject);
    }

    [ClientRpc]
    private void RpcSpawnProjectile(Vector3 spawnPos, GameObject target)
    {
        if (projectilePrefab == null || target == null) return;
        var proj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
        var projScript = proj.GetComponent<Projectile>();
        if (projScript != null)
            projScript.Initialize(target.transform, projectileSpeed);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
