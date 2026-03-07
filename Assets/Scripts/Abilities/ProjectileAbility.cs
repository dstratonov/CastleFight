using UnityEngine;
using Mirror;
using CastleFight;

public class ProjectileAbility : Ability
{
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform firePoint;
    [SerializeField] private float projectileSpeed = 12f;

    public override void Activate(GameObject caster, Vector3 targetPosition, GameObject targetObject = null)
    {
        if (projectilePrefab == null) return;

        Vector3 spawnPos = firePoint != null ? firePoint.position : caster.transform.position + Vector3.up;
        var projObj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
        var proj = projObj.GetComponent<Projectile>();

        if (proj != null && targetObject != null)
            proj.Initialize(targetObject.transform, projectileSpeed);
    }

    public override void Deactivate() { }
}
