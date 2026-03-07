using UnityEngine;

namespace CastleFight
{
public class Projectile : MonoBehaviour
{
    private Transform target;
    private float speed;
    private Vector3 lastTargetPos;

    public void Initialize(Transform targetTransform, float projectileSpeed)
    {
        target = targetTransform;
        speed = projectileSpeed;
        if (target != null)
            lastTargetPos = target.position;
    }

    private void Update()
    {
        if (target != null)
            lastTargetPos = target.position;

        Vector3 direction = (lastTargetPos - transform.position);
        if (direction.magnitude < 0.3f)
        {
            Destroy(gameObject);
            return;
        }

        transform.position += direction.normalized * (speed * Time.deltaTime);
        transform.forward = direction.normalized;
    }
}
}
