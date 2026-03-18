using UnityEngine;

public class Projectile : MonoBehaviour
{
    private Transform target;
    private float speed;
    private float damage;
    private GameObject attacker;
    private bool dealsDamage;

    private static Mesh sphereMesh;
    private static Material[] materials;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        sphereMesh = null;
        materials = null;
    }

    public void Initialize(Transform tgt, float spd)
    {
        target = tgt;
        speed = spd;
        dealsDamage = false;
    }

    public static Projectile Spawn(Vector3 start, Transform target, float speed,
        float damage, GameObject attacker, bool dealsDamage, AttackType attackType)
    {
        EnsureResources();

        var go = new GameObject("Projectile");
        go.transform.position = start;

        go.transform.localScale = Vector3.one * 0.15f;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = sphereMesh;

        int matIdx = MaterialIndex(attackType);
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = materials[matIdx];
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        Color col = materials[matIdx].color;
        var trail = go.AddComponent<TrailRenderer>();
        trail.startWidth = 0.1f;
        trail.endWidth = 0f;
        trail.time = 0.25f;
        trail.material = materials[matIdx];
        trail.startColor = col;
        trail.endColor = new Color(col.r, col.g, col.b, 0f);
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.minVertexDistance = 0.1f;

        var proj = go.AddComponent<Projectile>();
        proj.target = target;
        proj.speed = speed;
        proj.damage = damage;
        proj.attacker = attacker;
        proj.dealsDamage = dealsDamage;

        Object.Destroy(go, 5f);
        return proj;
    }

    private void Update()
    {
        if (target == null)
        {
            if (GameDebug.Combat)
                Debug.Log($"[Projectile] Target lost, destroying");
            Destroy(gameObject);
            return;
        }

        Vector3 targetPos = BoundsHelper.GetCenter(target.gameObject);
        transform.position = Vector3.MoveTowards(transform.position, targetPos, speed * Time.deltaTime);

        Vector3 dir = targetPos - transform.position;
        if (dir.sqrMagnitude > 0.01f)
            transform.forward = dir.normalized;

        if (Vector3.Distance(transform.position, targetPos) < 0.3f)
        {
            if (dealsDamage)
            {
                var health = target.GetComponent<Health>();
                if (health != null && !health.IsDead)
                {
                    if (GameDebug.Combat)
                        Debug.Log($"[Projectile] HIT {target.name} for {damage:F1} dmg");
                    health.TakeDamage(damage, attacker);
                }
            }
            Destroy(gameObject);
        }
    }

    private static int MaterialIndex(AttackType type)
    {
        return type switch
        {
            AttackType.Pierce => 0,
            AttackType.Magic => 1,
            AttackType.Chaos => 2,
            _ => 3
        };
    }

    private static void EnsureResources()
    {
        if (sphereMesh != null && materials != null) return;

        if (sphereMesh == null)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphereMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
            var col = tmp.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);
            Object.DestroyImmediate(tmp);
        }

        if (materials == null)
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");

            Color[] colors =
            {
                new Color(0.85f, 0.8f, 0.5f),
                new Color(0.55f, 0.3f, 1f),
                new Color(1f, 0.35f, 0.1f),
                new Color(0.9f, 0.85f, 0.3f)
            };

            materials = new Material[colors.Length];
            for (int i = 0; i < colors.Length; i++)
            {
                materials[i] = new Material(shader) { color = colors[i], renderQueue = 3500 };
            }
        }
    }
}
