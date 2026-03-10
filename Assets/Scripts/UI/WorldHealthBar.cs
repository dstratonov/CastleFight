using UnityEngine;

public class WorldHealthBar : MonoBehaviour
{
    private Health health;
    private Transform barRoot;
    private Transform fillTransform;
    private float barWidth;
    private float yOffset;

    private const float BAR_HEIGHT = 0.07f;
    private const float BORDER = 0.012f;

    private static Mesh sharedQuad;
    private static Material borderMat;
    private static Material bgMat;
    private static Material allyFillMat;
    private static Material enemyFillMat;

    private void Start()
    {
        health = GetComponent<Health>();
        if (health == null) { Destroy(this); return; }

        var localPlayer = NetworkPlayer.Local;
        bool isAlly = localPlayer != null && health.TeamId == localPlayer.TeamId;

        ComputeLayout();
        CreateBar(isAlly);
    }

    private void OnDestroy()
    {
        if (barRoot != null)
            Destroy(barRoot.gameObject);
    }

    private void ComputeLayout()
    {
        if (BoundsHelper.TryGetCombinedBounds(gameObject, out var bounds))
        {
            yOffset = bounds.max.y - transform.position.y + 0.2f;
            float entitySize = Mathf.Max(bounds.size.x, bounds.size.z);
            barWidth = Mathf.Clamp(entitySize * 0.8f, 0.6f, 3f);
        }
        else
        {
            yOffset = 2f;
            barWidth = 0.8f;
        }
    }

    private void CreateBar(bool isAlly)
    {
        EnsureSharedResources();

        barRoot = new GameObject("WorldHP").transform;
        barRoot.SetParent(transform, false);
        barRoot.localPosition = Vector3.up * yOffset;

        float innerW = barWidth - BORDER * 2f;
        float innerH = BAR_HEIGHT - BORDER * 2f;

        var borderObj = CreateQuadObj("Border", barRoot, borderMat);
        borderObj.transform.localScale = new Vector3(barWidth, BAR_HEIGHT, 1f);

        var bgObj = CreateQuadObj("BG", barRoot, bgMat);
        bgObj.transform.localScale = new Vector3(innerW, innerH, 1f);
        bgObj.transform.localPosition = new Vector3(0f, 0f, -0.001f);

        Material fill = isAlly ? allyFillMat : enemyFillMat;
        var fillObj = CreateQuadObj("Fill", barRoot, fill);
        fillObj.transform.localScale = new Vector3(innerW, innerH, 1f);
        fillObj.transform.localPosition = new Vector3(0f, 0f, -0.002f);
        fillTransform = fillObj.transform;
    }

    private static GameObject CreateQuadObj(string name, Transform parent, Material mat)
    {
        var obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.layer = 0;
        var mf = obj.AddComponent<MeshFilter>();
        mf.sharedMesh = sharedQuad;
        var mr = obj.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return obj;
    }

    private void LateUpdate()
    {
        if (fillTransform == null || barRoot == null) return;

        var cam = Camera.main;
        if (cam == null || health == null) return;

        if (health.IsDead)
        {
            barRoot.gameObject.SetActive(false);
            return;
        }

        barRoot.position = transform.position + Vector3.up * yOffset;
        barRoot.rotation = cam.transform.rotation;

        float pct = health.HealthPercent;
        float innerW = barWidth - BORDER * 2f;
        float innerH = BAR_HEIGHT - BORDER * 2f;
        float fw = innerW * pct;
        fillTransform.localScale = new Vector3(fw, innerH, 1f);
        fillTransform.localPosition = new Vector3((fw - innerW) * 0.5f, 0f, -0.002f);
    }

    private static void EnsureSharedResources()
    {
        if (sharedQuad == null)
        {
            sharedQuad = new Mesh { name = "HPBarQuad" };
            sharedQuad.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3( 0.5f, -0.5f, 0f),
                new Vector3( 0.5f,  0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f)
            };
            sharedQuad.uv = new[]
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f),
                new Vector2(1f, 1f), new Vector2(0f, 1f)
            };
            sharedQuad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            sharedQuad.RecalculateNormals();
            sharedQuad.RecalculateBounds();
        }

        if (borderMat == null)
            borderMat = CreateUnlitMaterial(new Color(0f, 0f, 0f, 1f));

        if (bgMat == null)
            bgMat = CreateUnlitMaterial(new Color(0.08f, 0.08f, 0.08f, 1f));

        if (allyFillMat == null)
            allyFillMat = CreateUnlitMaterial(new Color(0.1f, 0.75f, 0.1f, 1f));

        if (enemyFillMat == null)
            enemyFillMat = CreateUnlitMaterial(new Color(0.85f, 0.12f, 0.1f, 1f));
    }

    private static Material CreateUnlitMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        if (shader == null) shader = Shader.Find("Sprites/Default");

        var mat = new Material(shader);
        mat.SetColor("_BaseColor", color);
        mat.color = color;
        mat.renderQueue = 3500;
        return mat;
    }
}
