using UnityEngine;
using Mirror;

namespace CastleFight
{
public class Projectile : MonoBehaviour
{
    private Transform target;
    private float speed;
    private float damage;
    private GameObject owner;
    private Vector3 lastTargetPos;
    private bool hasHit;

    public void Initialize(Transform targetTransform, float projectileSpeed, float projectileDamage = 0f, GameObject projectileOwner = null)
    {
        target = targetTransform;
        speed = projectileSpeed;
        damage = projectileDamage;
        owner = projectileOwner;
        if (target != null)
            lastTargetPos = target.position;
        else
            lastTargetPos = transform.position + transform.forward * 10f;
    }

    private void Update()
    {
        if (hasHit) return;

        if (target != null)
            lastTargetPos = target.position;

        Vector3 direction = lastTargetPos - transform.position;
        if (direction.magnitude < 0.5f)
        {
            OnHit();
            return;
        }

        transform.position += direction.normalized * (speed * Time.deltaTime);
        if (direction.sqrMagnitude > 0.01f)
            transform.forward = direction.normalized;
    }

    private void OnHit()
    {
        hasHit = true;

        if (damage > 0f && target != null)
        {
            var health = target.GetComponent<Health>();
            if (health != null && NetworkServer.active)
            {
                health.TakeDamage(damage, owner);
            }
        }

        Destroy(gameObject);
    }
}
}
