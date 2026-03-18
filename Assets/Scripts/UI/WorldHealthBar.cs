using UnityEngine;

public class WorldHealthBar : MonoBehaviour
{
    private Health health;
    private Transform barRoot;
    private Transform fillTransform;
    private float barWidth;
    private float yOffset;
    private Vector3 boundsOffset;

    private const float BAR_HEIGHT = 0.15f;
    private const float BORDER = 0.02f;
    private const float SCREEN_SCALE_FACTOR = 0.025f;
    private const float MIN_VISIBLE_HEIGHT = 0.5f;
    private const float MAX_VISIBLE_HEIGHT = 120f;

    private static Mesh sharedQuad;
    private static Material borderMat;
    private static Material bgMat;
    private static Material allyFillMat;
    private static Material enemyFillMat;

    private Camera cachedCamera;
    private float cameraCacheTime;
    private bool wasFullHealth = true;

    private void Start()
    {
        health = GetComponent<Health>();
        if (health == null) { Destroy(this); return; }

        var localPlayer = NetworkPlayer.Local;
        bool isAlly = localPlayer != null && health.TeamId == localPlayer.TeamId;

        ComputeLayout();
        CreateBar(isAlly);
    }

    private void OnDisable()
    {
        if (barRoot != null)
        {
            Destroy(barRoot.gameObject);
            barRoot = null;
        }
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
            boundsOffset = bounds.center - transform.position;
            boundsOffset.y = 0f;

            float entitySize = Mathf.Max(bounds.size.x, bounds.size.z);
            barWidth = Mathf.Clamp(entitySize * 0.7f, 0.8f, 4f);
        }
        else
        {
            yOffset = 2f;
            boundsOffset = Vector3.zero;
            barWidth = 1f;
        }
    }

    private void CreateBar(bool isAlly)
    {
        EnsureSharedResources();

        barRoot = new GameObject("WorldHP").transform;

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

    private Camera GetCamera()
    {
        if (cachedCamera != null) return cachedCamera;
        if (Time.time - cameraCacheTime < 1f) return null;
        cameraCacheTime = Time.time;
        cachedCamera = Camera.main;
        return cachedCamera;
    }

    private void LateUpdate()
    {
        if (fillTransform == null || barRoot == null || health == null) return;

        if (health.IsDead)
        {
            barRoot.gameObject.SetActive(false);
            return;
        }

        float pct = health.HealthPercent;
        bool isFullHealth = pct >= 0.999f;

        if (isFullHealth && wasFullHealth)
        {
            barRoot.gameObject.SetActive(false);
            return;
        }
        wasFullHealth = isFullHealth;

        var cam = GetCamera();
        if (cam == null) return;

        Vector3 worldPos = transform.position + boundsOffset + Vector3.up * yOffset;
        float camDist = Vector3.Distance(cam.transform.position, worldPos);

        if (camDist < MIN_VISIBLE_HEIGHT || camDist > MAX_VISIBLE_HEIGHT)
        {
            barRoot.gameObject.SetActive(false);
            return;
        }

        barRoot.gameObject.SetActive(true);
        barRoot.position = worldPos;
        barRoot.rotation = cam.transform.rotation;

        float scale = Mathf.Max(camDist * SCREEN_SCALE_FACTOR, 1f);
        barRoot.localScale = Vector3.one * scale;

        float innerW = barWidth - BORDER * 2f;
        float innerH = BAR_HEIGHT - BORDER * 2f;
        float fw = innerW * pct;
        fillTransform.localScale = new Vector3(fw, innerH, 1f);
        fillTransform.localPosition = new Vector3((fw - innerW) * 0.5f, 0f, -0.002f);

        UpdateFillColor(pct);
    }

    private MeshRenderer fillRenderer;
    private MaterialPropertyBlock fillPropBlock;
    private static readonly int ColorPropId = Shader.PropertyToID("_BaseColor");
    private static readonly int FallbackColorPropId = Shader.PropertyToID("_Color");

    private void UpdateFillColor(float pct)
    {
        if (fillRenderer == null)
            fillRenderer = fillTransform.GetComponent<MeshRenderer>();
        if (fillPropBlock == null)
            fillPropBlock = new MaterialPropertyBlock();
        if (fillRenderer == null) return;

        Color c = GetWorldHealthColor(pct);
        fillRenderer.GetPropertyBlock(fillPropBlock);
        fillPropBlock.SetColor(ColorPropId, c);
        fillPropBlock.SetColor(FallbackColorPropId, c);
        fillRenderer.SetPropertyBlock(fillPropBlock);
    }

    private static Color GetWorldHealthColor(float pct)
    {
        if (pct > 0.6f)
            return Color.Lerp(new Color(0.85f, 0.85f, 0.15f), new Color(0.1f, 0.75f, 0.1f), (pct - 0.6f) / 0.4f);
        if (pct > 0.25f)
            return Color.Lerp(new Color(0.9f, 0.35f, 0.1f), new Color(0.85f, 0.85f, 0.15f), (pct - 0.25f) / 0.35f);
        return Color.Lerp(new Color(0.8f, 0.1f, 0.1f), new Color(0.9f, 0.35f, 0.1f), pct / 0.25f);
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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void CleanupStatics()
    {
        if (borderMat != null) { Destroy(borderMat); borderMat = null; }
        if (bgMat != null) { Destroy(bgMat); bgMat = null; }
        if (allyFillMat != null) { Destroy(allyFillMat); allyFillMat = null; }
        if (enemyFillMat != null) { Destroy(enemyFillMat); enemyFillMat = null; }
        if (sharedQuad != null) { Destroy(sharedQuad); sharedQuad = null; }
    }
}
