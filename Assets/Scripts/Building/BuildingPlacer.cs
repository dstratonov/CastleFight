using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

public class BuildingPlacer : NetworkBehaviour
{
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private Material validPlacementMaterial;
    [SerializeField] private Material invalidPlacementMaterial;
    [SerializeField] private Material outOfRangePlacementMaterial;

    private BuildingData currentBuildingData;
    private GameObject ghostObject;
    private bool isPlacing;
    private Camera mainCamera;

    private Vector3? pendingPosition;
    private Quaternion pendingRotation;
    private string pendingBuildingId;

    private HeroBuilder heroBuilder;
    private HeroController heroController;

    public bool IsPlacing => isPlacing;
    public bool HasPendingPlacement => pendingPosition.HasValue;

    private void Awake()
    {
        heroBuilder = GetComponent<HeroBuilder>();
        heroController = GetComponent<HeroController>();
    }

    private void Start()
    {
        mainCamera = Camera.main;
    }

    private void Update()
    {
        if (!isLocalPlayer) return;

        if (pendingPosition.HasValue)
        {
            CheckPendingPlacement();
        }

        if (!isPlacing) return;

        var mouse = Mouse.current;
        var keyboard = Keyboard.current;
        if (mouse == null) return;

        UpdateGhostPosition(mouse);

        if (mouse.leftButton.wasPressedThisFrame)
            TryConfirmPlacement();

        if (mouse.rightButton.wasPressedThisFrame || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame))
            CancelPlacement();
    }

    public void StartPlacing(BuildingData data)
    {
        if (data == null || data.prefab == null) return;

        CancelPlacement();
        currentBuildingData = data;
        isPlacing = true;

        ghostObject = Instantiate(data.ghostPrefab != null ? data.ghostPrefab : data.prefab);
        DisableGhostFunctionality(ghostObject);
    }

    public void CancelPlacement()
    {
        isPlacing = false;
        currentBuildingData = null;
        pendingPosition = null;
        pendingBuildingId = null;

        if (ghostObject != null)
        {
            Destroy(ghostObject);
            ghostObject = null;
        }
    }

    private void UpdateGhostPosition(Mouse mouse)
    {
        if (ghostObject == null) return;

        Ray ray = mainCamera.ScreenPointToRay(mouse.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit, 200f, groundLayer)) return;

        var grid = GridSystem.Instance;
        Vector3 snapped = grid != null ? grid.SnapToGrid(hit.point) : hit.point;
        ghostObject.transform.position = snapped;

        bool zoneValid = IsInValidZone(snapped);
        bool inRange = heroBuilder != null && heroBuilder.IsInBuildRange(snapped);

        if (!zoneValid)
            SetGhostMaterial(invalidPlacementMaterial);
        else if (!inRange)
            SetGhostMaterial(outOfRangePlacementMaterial != null ? outOfRangePlacementMaterial : validPlacementMaterial);
        else
            SetGhostMaterial(validPlacementMaterial);
    }

    private bool IsInValidZone(Vector3 position)
    {
        var player = GetComponent<NetworkPlayer>();
        if (player == null) return false;

        var grid = GridSystem.Instance;
        if (grid != null && !grid.CanPlaceBuilding(position, player.TeamId))
            return false;

        return true;
    }

    private void TryConfirmPlacement()
    {
        if (ghostObject == null) return;

        Vector3 position = ghostObject.transform.position;
        if (!IsInValidZone(position)) return;

        bool inRange = heroBuilder != null && heroBuilder.IsInBuildRange(position);

        if (inRange)
        {
            CmdPlaceBuilding(currentBuildingData.buildingId, position, ghostObject.transform.rotation);
            CancelPlacement();
        }
        else
        {
            pendingPosition = position;
            pendingRotation = ghostObject.transform.rotation;
            pendingBuildingId = currentBuildingData.buildingId;

            if (heroController != null)
                heroController.MoveTo(position);

            if (ghostObject != null)
            {
                Destroy(ghostObject);
                ghostObject = null;
            }
            isPlacing = false;
            currentBuildingData = null;
        }
    }

    private void CheckPendingPlacement()
    {
        if (!pendingPosition.HasValue) return;

        bool inRange = heroBuilder != null && heroBuilder.IsInBuildRange(pendingPosition.Value);
        if (inRange)
        {
            CmdPlaceBuilding(pendingBuildingId, pendingPosition.Value, pendingRotation);
            pendingPosition = null;
            pendingBuildingId = null;
        }
    }

    [Command]
    private void CmdPlaceBuilding(string buildingId, Vector3 position, Quaternion rotation)
    {
        var player = GetComponent<NetworkPlayer>();
        if (player == null) return;

        var raceDb = RaceDatabase.Instance;
        if (raceDb == null) return;

        BuildingData data = raceDb.GetBuildingData(player.SelectedRaceId, buildingId);
        if (data == null) return;

        var builder = GetComponent<HeroBuilder>();
        if (builder == null) return;

        float dist = Vector3.Distance(transform.position, position);
        if (dist > builder.BuildRange) return;

        if (!ResourceManager.Instance.TrySpendGold(player, data.cost)) return;

        var grid = GridSystem.Instance;
        if (grid != null && !grid.CanPlaceBuilding(position, player.TeamId)) return;

        var buildingObj = BuildingManager.Instance?.PlaceBuilding(data, position, rotation, player.TeamId, player.PlayerId);

        if (grid != null && buildingObj != null)
        {
            Vector2Int cell = grid.WorldToCell(position);
            grid.PlaceBuilding(cell, buildingObj);
        }
    }

    private void DisableGhostFunctionality(GameObject ghost)
    {
        foreach (var collider in ghost.GetComponentsInChildren<Collider>())
            collider.enabled = false;
        foreach (var mb in ghost.GetComponentsInChildren<MonoBehaviour>())
            mb.enabled = false;
        foreach (var rb in ghost.GetComponentsInChildren<Rigidbody>())
            Destroy(rb);
    }

    private void SetGhostMaterial(Material mat)
    {
        if (mat == null || ghostObject == null) return;
        foreach (var renderer in ghostObject.GetComponentsInChildren<Renderer>())
            renderer.material = mat;
    }
}
