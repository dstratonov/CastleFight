using UnityEngine;

public class WorldHealthBar : MonoBehaviour
{
    private Health health;
    private Transform barRoot;
    private Transform fillTransform;
    private MeshRenderer fillRenderer;
    private MaterialPropertyBlock propBlock;
    private float barWidth;
    private float barHeight;
    private float yOffset;
    private float innerWidth;
    private float innerHeight;
    private bool isAlly;

    private const float BORDER_THICKNESS = 0.018f;
    private const float PADDING = 0.012f;

    private static Mesh sharedQuad;
    private static Material borderMaterial;
    private static Material bgMaterial;
    private static Material fillMaterial;

    private void Start()
    {
        health = GetComponent<Health>();
        if (health == null) { Destroy(this); return; }

        var localPlayer = NetworkPlayer.Local;
        isAlly = localPlayer != null && health.TeamId == localPlayer.TeamId;

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
            yOffset = bounds.max.y - transform.position.y + 0.25f;
            float entitySize = Mathf.Max(bounds.size.x, bounds.size.z);
            barWidth = Mathf.Clamp(entitySize * 0.7f, 0.5f, 3.5f);
            barHeight = Mathf.Clamp(entitySize * 0.06f, 0.06f, 0.14f);
        }
        else
        {
            yOffset = 2f;
            barWidth = 0.8f;
            barHeight = 0.08f;
        }

        innerWidth = barWidth - BORDER_THICKNESS * 2f - PADDING * 2f;
        innerHeight = barHeight - BORDER_THICKNESS * 2f - PADDING * 2f;
    }

    private void CreateBar()
    {
        EnsureSharedResources();

        barRoot = new GameObject("WorldHP").transform;
        barRoot.SetParent(transform, false);
        barRoot.localPosition = Vector3.up * yOffset;

        var borderObj = CreateQuadObj("Border", barRoot, borderMaterial);
        borderObj.transform.localScale = new Vector3(barWidth, barHeight, 1f);

        var bgObj = CreateQuadObj("BG", barRoot, bgMaterial);
        bgObj.transform.localScale = new Vector3(barWidth - BORDER_THICKNESS * 2f, barHeight - BORDER_THICKNESS * 2f, 1f);
        bgObj.transform.localPosition = new Vector3(0f, 0f, -0.001f);

        var fillObj = CreateQuadObj("Fill", barRoot, fillMaterial);
        fillObj.transform.localScale = new Vector3(innerWidth, innerHeight, 1f);
        fillObj.transform.localPosition = new Vector3(0f, 0f, -0.002f);
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
        if (propBlock == null || fillRenderer == null || barRoot == null) return;

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
        float fw = innerWidth * pct;
        fillTransform.localScale = new Vector3(fw, innerHeight, 1f);
        fillTransform.localPosition = new Vector3((fw - innerWidth) * 0.5f, 0f, -0.002f);

        Color c;
        if (isAlly)
        {
            c = pct > 0.5f
                ? Color.Lerp(new Color(0.95f, 0.85f, 0f), new Color(0.1f, 0.8f, 0.15f), (pct - 0.5f) * 2f)
                : Color.Lerp(new Color(0.85f, 0.12f, 0.08f), new Color(0.95f, 0.85f, 0f), pct * 2f);
        }
        else
        {
            c = pct > 0.5f
                ? Color.Lerp(new Color(0.9f, 0.5f, 0.1f), new Color(0.85f, 0.15f, 0.1f), (pct - 0.5f) * 2f)
                : Color.Lerp(new Color(0.6f, 0.05f, 0.05f), new Color(0.9f, 0.5f, 0.1f), pct * 2f);
        }

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

        if (borderMaterial == null)
            borderMaterial = CreateBarMaterial(new Color(0f, 0f, 0f, 0.85f));

        if (bgMaterial == null)
            bgMaterial = CreateBarMaterial(new Color(0.15f, 0.15f, 0.15f, 0.75f));

        if (fillMaterial == null)
            fillMaterial = CreateBarMaterial(Color.green);
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
