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

    public GameObject CurrentSelection => currentSelection;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        CleanupMaterial();
        if (Instance == this) Instance = null;
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
            var hitObj = hit.collider.gameObject;
            var unit = hitObj.GetComponent<Unit>() ?? hitObj.GetComponentInParent<Unit>();
            var building = hitObj.GetComponent<Building>() ?? hitObj.GetComponentInParent<Building>();
            var castle = hitObj.GetComponent<Castle>() ?? hitObj.GetComponentInParent<Castle>();

            if (unit != null)
                Select(unit.gameObject);
            else if (building != null)
                Select(building.gameObject);
            else if (castle != null)
                Select(castle.gameObject);
            else
                Deselect();
        }
        else
        {
            Deselect();
        }
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

        selectionIndicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        selectionIndicator.name = "SelectionRing";
        selectionIndicator.transform.SetParent(currentSelection.transform, false);
        selectionIndicator.transform.localPosition = new Vector3(0, 0.05f, 0);
        selectionIndicator.transform.localScale = new Vector3(2.5f, 0.02f, 2.5f);

        var col = selectionIndicator.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);

        var renderer = selectionIndicator.GetComponent<Renderer>();
        if (renderer != null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader != null)
            {
                if (selectionMaterial != null) Destroy(selectionMaterial);
                selectionMaterial = new Material(shader);
                selectionMaterial.color = new Color(0.2f, 1f, 0.3f, 0.5f);
                selectionMaterial.SetFloat("_Surface", 1);
                selectionMaterial.SetFloat("_Blend", 0);
                selectionMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                selectionMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                selectionMaterial.SetInt("_ZWrite", 0);
                selectionMaterial.renderQueue = 3000;
                renderer.material = selectionMaterial;
            }
        }
    }

    private void RemoveHighlight()
    {
        if (selectionIndicator != null)
        {
            Destroy(selectionIndicator);
            selectionIndicator = null;
        }
    }

    private void CleanupMaterial()
    {
        if (selectionMaterial != null)
        {
            Destroy(selectionMaterial);
            selectionMaterial = null;
        }
    }
}
