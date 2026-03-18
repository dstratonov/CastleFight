using UnityEngine;
using Mirror;

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

        if (GameDebug.Combat)
            Debug.Log($"[ProjectileAbility] {caster.name} fired {data.abilityId} at {targetObject?.name ?? "pos"} speed={projectileSpeed}");

        // NetworkServer.Spawn makes the projectile visible on all clients.
        // The prefab must have a NetworkIdentity component for this to work.
        if (NetworkServer.active)
            NetworkServer.Spawn(projObj);
    }

    public override void Deactivate() { }
}
