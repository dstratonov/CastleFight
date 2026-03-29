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

    private float pendingTimeout;
    private const float PENDING_MAX_TIME = 15f;

    private void OnDestroy()
    {
        if (ghostObject != null)
        {
            Destroy(ghostObject);
            ghostObject = null;
        }
    }

    private void Update()
    {
        if (!isLocalPlayer) return;

        if (pendingPosition.HasValue)
        {
            pendingTimeout -= Time.deltaTime;
            if (pendingTimeout <= 0f)
            {
                pendingPosition = null;
                pendingBuildingId = null;
            }
            else
            {
                CheckPendingPlacement();
            }
        }

        if (!isPlacing) return;

        var mouse = Mouse.current;
        var keyboard = Keyboard.current;
        if (mouse == null) return;

        UpdateGhostPosition(mouse);
        HandleGhostRotation(keyboard);

        if (mouse.leftButton.wasPressedThisFrame)
            TryConfirmPlacement();

        if (mouse.rightButton.wasPressedThisFrame || (keyboard != null && keyboard.escapeKey.wasPressedThisFrame))
            CancelPlacement();
    }

    private void HandleGhostRotation(Keyboard keyboard)
    {
        if (ghostObject == null || keyboard == null) return;

        if (keyboard.qKey.wasPressedThisFrame)
            ghostObject.transform.Rotate(0f, -90f, 0f);
        if (keyboard.eKey.wasPressedThisFrame)
            ghostObject.transform.Rotate(0f, 90f, 0f);
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
        if (mainCamera == null) mainCamera = Camera.main;
        if (mainCamera == null) return;

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
        if (grid == null) return false;

        if (!grid.CanPlaceBuilding(position, player.TeamId)) return false;

        if (ghostObject != null)
        {
            Bounds bounds = BuildingManager.ComputeBuildingBounds(ghostObject);
            var cells = grid.GetCellsOverlappingBounds(bounds);
            if (!grid.AreCellsEmpty(cells)) return false;

            var unitPresence = UnitGridPresence.Instance;
            if (unitPresence != null)
            {
                foreach (var cell in cells)
                {
                    if (unitPresence.IsOccupied(cell)) return false;
                }
            }
        }

        return true;
    }

    private float invalidFeedbackTimer;

    private void TryConfirmPlacement()
    {
        if (ghostObject == null) return;

        Vector3 position = ghostObject.transform.position;

        if (!IsInValidZone(position))
        {
            PlayInvalidFeedback();
            return;
        }

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
            pendingTimeout = PENDING_MAX_TIME;

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

    private void PlayInvalidFeedback()
    {
        if (invalidFeedbackTimer > 0f) return;
        invalidFeedbackTimer = 0.3f;

        if (ghostObject != null)
            StartCoroutine(ShakeGhost());
    }

    private System.Collections.IEnumerator ShakeGhost()
    {
        if (ghostObject == null) yield break;

        Vector3 origin = ghostObject.transform.position;
        float elapsed = 0f;
        const float duration = 0.25f;
        const float magnitude = 0.15f;

        while (elapsed < duration && ghostObject != null)
        {
            float x = origin.x + Random.Range(-magnitude, magnitude);
            float z = origin.z + Random.Range(-magnitude, magnitude);
            ghostObject.transform.position = new Vector3(x, origin.y, z);
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (ghostObject != null)
            ghostObject.transform.position = origin;

        invalidFeedbackTimer = 0f;
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

        if (BuildingManager.Instance == null) return;
        if (ResourceManager.Instance == null) return;
        if (!ResourceManager.Instance.CanAfford(player, data.cost)) return;

        var grid = GridSystem.Instance;
        if (grid != null && !grid.CanPlaceBuilding(position, player.TeamId)) return;

        if (!ResourceManager.Instance.TrySpendGold(player, data.cost)) return;

        var buildingObj = BuildingManager.Instance.PlaceBuilding(data, position, rotation, player.TeamId, player.PlayerId);
        if (buildingObj == null)
        {
            ResourceManager.Instance.AwardGold(player, data.cost);
            return;
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
