using UnityEngine;

public class WorldHealthBar : MonoBehaviour
{
    private Health health;
    private Transform barRoot;
    private Transform fillTransform;
    private MeshRenderer fillRenderer;
    private MaterialPropertyBlock propBlock;
    private float barWidth;
    private float yOffset;

    private const float BAR_HEIGHT = 0.12f;
    private const float FILL_INSET = 0.8f;

    private static Mesh sharedQuad;
    private static Material bgMaterial;
    private static Material fillMaterial;

    private void Start()
    {
        health = GetComponent<Health>();
        if (health == null) { Destroy(this); return; }

        ComputeLayout();
        CreateBar();
        propBlock = new MaterialPropertyBlock();
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
            yOffset = bounds.max.y - transform.position.y + 0.3f;
            barWidth = Mathf.Clamp(Mathf.Max(bounds.size.x, bounds.size.z) * 0.8f, 0.6f, 4f);
        }
        else
        {
            yOffset = 2f;
            barWidth = 1f;
        }
    }

    private void CreateBar()
    {
        EnsureSharedResources();

        barRoot = new GameObject("WorldHP").transform;
        barRoot.SetParent(transform, false);
        barRoot.localPosition = Vector3.up * yOffset;

        var bgObj = CreateQuadObj("BG", barRoot, bgMaterial);
        bgObj.transform.localScale = new Vector3(barWidth, BAR_HEIGHT, 1f);

        var fillObj = CreateQuadObj("Fill", barRoot, fillMaterial);
        fillObj.transform.localScale = new Vector3(barWidth * FILL_INSET, BAR_HEIGHT * 0.7f, 1f);
        fillObj.transform.localPosition = new Vector3(0f, 0f, -0.001f);
        fillTransform = fillObj.transform;
        fillRenderer = fillObj.GetComponent<MeshRenderer>();
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
        float fillWidth = barWidth * FILL_INSET * pct;
        fillTransform.localScale = new Vector3(fillWidth, BAR_HEIGHT * 0.7f, 1f);
        fillTransform.localPosition = new Vector3((fillWidth - barWidth * FILL_INSET) * 0.5f, 0f, -0.001f);

        Color c = pct > 0.5f
            ? Color.Lerp(new Color(1f, 0.9f, 0f), new Color(0.15f, 0.85f, 0.1f), (pct - 0.5f) * 2f)
            : Color.Lerp(new Color(0.9f, 0.1f, 0.05f), new Color(1f, 0.9f, 0f), pct * 2f);

        fillRenderer.GetPropertyBlock(propBlock);
        propBlock.SetColor("_Color", c);
        fillRenderer.SetPropertyBlock(propBlock);
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

        if (bgMaterial == null)
        {
            bgMaterial = CreateBarMaterial(new Color(0.05f, 0.05f, 0.05f, 0.7f));
        }

        if (fillMaterial == null)
        {
            fillMaterial = CreateBarMaterial(Color.green);
        }
    }

    private static Material CreateBarMaterial(Color color)
    {
        var shader = Shader.Find("Sprites/Default");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        var mat = new Material(shader);
        mat.color = color;
        mat.renderQueue = 3500;
        return mat;
    }
}
