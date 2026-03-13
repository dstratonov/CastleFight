using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class SelectionManager : MonoBehaviour
{
    public static SelectionManager Instance { get; private set; }

    [SerializeField] private LayerMask selectableLayers = ~0;
    [SerializeField] private float maxRayDistance = 200f;

    private Camera mainCamera;
    private GameObject currentSelection;
    private GameObject selectionIndicator;
    private Material selectionMaterial;
    private float selectionGroundY;
    private Vector3 selectionCenterOffset;
    private bool currentSelectionIsEnemy;

    public GameObject CurrentSelection => currentSelection;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (selectionMaterial != null)
        {
            Destroy(selectionMaterial);
            selectionMaterial = null;
        }
        if (Instance == this) Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Instance = null;
    }

    private void Update()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null) return;
        }

        var mouse = Mouse.current;
        var keyboard = Keyboard.current;
        if (mouse == null) return;

        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            Deselect();
            return;
        }

        if (!mouse.leftButton.wasPressedThisFrame) return;
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (IsBuildingPlacerActive()) return;

        Ray ray = mainCamera.ScreenPointToRay(mouse.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, selectableLayers))
        {
            var selectable = FindSelectable(hit.collider.gameObject);
            if (selectable != null)
                Select(selectable.gameObject);
            else
                Deselect();
        }
        else
        {
            Deselect();
        }
    }

    private void LateUpdate()
    {
        if (currentSelection == null)
        {
            if (selectionIndicator != null)
            {
                RemoveHighlight();
                EventBus.Raise(new SelectionChangedEvent(null));
            }
            return;
        }

        var health = currentSelection.GetComponent<Health>();
        if (health != null && health.IsDead)
        {
            Deselect();
            return;
        }

        if (selectionIndicator == null) return;

        Vector3 pos = currentSelection.transform.position + selectionCenterOffset;
        pos.y = selectionGroundY;
        selectionIndicator.transform.position = pos;
    }

    public void Select(GameObject target)
    {
        if (currentSelection == target) return;

        RemoveHighlight();
        currentSelection = target;
        ApplyHighlight();
        EventBus.Raise(new SelectionChangedEvent(target));
    }

    public void Deselect()
    {
        if (currentSelection == null) return;

        RemoveHighlight();
        currentSelection = null;
        EventBus.Raise(new SelectionChangedEvent(null));
    }

    private bool IsBuildingPlacerActive()
    {
        var localPlayer = NetworkPlayer.Local;
        if (localPlayer == null) return false;
        var placer = localPlayer.GetComponent<BuildingPlacer>();
        return placer != null && (placer.IsPlacing || placer.HasPendingPlacement);
    }

    private void ApplyHighlight()
    {
        if (currentSelection == null) return;

        float diameter;
        Vector3 boundsCenter;
        ComputeSelectionBounds(currentSelection, out diameter, out selectionGroundY, out boundsCenter);

        selectionCenterOffset = boundsCenter - currentSelection.transform.position;
        selectionCenterOffset.y = 0f;

        currentSelectionIsEnemy = IsEnemy(currentSelection);
        selectionIndicator = CreateRingQuad(diameter, currentSelectionIsEnemy);

        Vector3 pos = currentSelection.transform.position + selectionCenterOffset;
        pos.y = selectionGroundY;
        selectionIndicator.transform.position = pos;
    }

    private static ISelectable FindSelectable(GameObject hitObj)
    {
        var selectable = hitObj.GetComponent<ISelectable>();
        if (selectable != null) return selectable;
        return hitObj.GetComponentInParent<ISelectable>();
    }

    private static bool IsEnemy(GameObject target)
    {
        var local = NetworkPlayer.Local;
        if (local == null) return false;

        var selectable = target.GetComponent<ISelectable>();
        return selectable != null && selectable.TeamId != local.TeamId;
    }

    private void ComputeSelectionBounds(GameObject target, out float diameter, out float groundY, out Vector3 boundsCenter)
    {
        diameter = 2.5f;
        groundY = target.transform.position.y + 0.05f;
        boundsCenter = target.transform.position;

        if (!BoundsHelper.TryGetCombinedBounds(target, out Bounds combined)) return;

        diameter = Mathf.Max(combined.size.x, combined.size.z) * 1.15f;
        diameter = Mathf.Clamp(diameter, 1.5f, 60f);
        groundY = Mathf.Max(combined.min.y + 0.25f, target.transform.position.y + 0.05f);
        boundsCenter = combined.center;
    }

    private static readonly Color COLOR_ALLY_RING = new(0.2f, 1f, 0.3f, 0.55f);
    private static readonly Color COLOR_ENEMY_RING = new(1f, 0.25f, 0.2f, 0.55f);

    private GameObject CreateRingQuad(float diameter, bool isEnemy = false)
    {
        var go = new GameObject("SelectionRing");

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = CreateQuadMesh();

        var mr = go.AddComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;

        if (selectionMaterial == null)
        {
            var shader = Shader.Find("Custom/SelectionRing");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
                Debug.LogWarning("[SelectionManager] Custom/SelectionRing shader not found, using fallback");
            }
            selectionMaterial = new Material(shader);
        }

        selectionMaterial.color = isEnemy ? COLOR_ENEMY_RING : COLOR_ALLY_RING;
        mr.sharedMaterial = selectionMaterial;

        go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        go.transform.localScale = new Vector3(diameter, diameter, 1f);

        return go;
    }

    private static Mesh _sharedQuadMesh;

    private static Mesh CreateQuadMesh()
    {
        if (_sharedQuadMesh != null) return _sharedQuadMesh;

        _sharedQuadMesh = new Mesh { name = "SelectionQuad" };
        _sharedQuadMesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3( 0.5f, -0.5f, 0f),
            new Vector3( 0.5f,  0.5f, 0f),
            new Vector3(-0.5f,  0.5f, 0f)
        };
        _sharedQuadMesh.uv = new[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f)
        };
        _sharedQuadMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        _sharedQuadMesh.RecalculateNormals();
        _sharedQuadMesh.RecalculateBounds();
        return _sharedQuadMesh;
    }

    private void RemoveHighlight()
    {
        if (selectionIndicator != null)
        {
            Destroy(selectionIndicator);
            selectionIndicator = null;
        }
    }
}
