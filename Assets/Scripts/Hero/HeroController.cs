using UnityEngine;
using UnityEngine.InputSystem;
using Mirror;

public class HeroController : NetworkBehaviour
{
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float flyHeight = 1f;
    [SerializeField] private float stoppingDistance = 0.1f;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private GameObject moveIndicatorPrefab;

    private Camera mainCamera;
    private Vector3? targetPosition;
    private BuildingPlacer cachedPlacer;

    public bool IsMoving => targetPosition.HasValue;

    public override void OnStartLocalPlayer()
    {
        mainCamera = Camera.main;
        Debug.Log($"[Hero] OnStartLocalPlayer - camera: {(mainCamera != null ? mainCamera.name : "NULL")}, groundLayer: {groundLayer.value}");
    }

    private void Update()
    {
        if (!isLocalPlayer) return;

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            if (mainCamera == null) return;
        }

        var mouse = Mouse.current;
        if (mouse != null && mouse.rightButton.wasPressedThisFrame)
        {
            if (cachedPlacer == null) cachedPlacer = GetComponent<BuildingPlacer>();
            if (cachedPlacer != null && cachedPlacer.IsPlacing) return;

            if (cachedPlacer != null && cachedPlacer.HasPendingPlacement)
                cachedPlacer.CancelPlacement();

            HandleMoveInput();
        }

        if (targetPosition.HasValue)
        {
            MoveTowardsTarget();
        }
    }

    private void HandleMoveInput()
    {
        Vector2 mousePos = Mouse.current.position.ReadValue();
        Ray ray = mainCamera.ScreenPointToRay(mousePos);
        if (Physics.Raycast(ray, out RaycastHit hit, 200f, groundLayer))
        {
            Vector3 dest = new Vector3(hit.point.x, flyHeight, hit.point.z);
            Debug.Log($"[Hero] Moving to {dest}");
            CmdMoveTo(dest);
            ShowMoveIndicator(hit.point);
        }
        else
        {
            Debug.Log($"[Hero] Raycast missed - mouse: {mousePos}, layer: {groundLayer.value}");
        }
    }

    private void MoveTowardsTarget()
    {
        Vector3 target = targetPosition.Value;
        Vector3 direction = target - transform.position;
        float distance = direction.magnitude;

        if (distance <= stoppingDistance)
        {
            transform.position = target;
            targetPosition = null;
            return;
        }

        transform.position += direction.normalized * (moveSpeed * Time.deltaTime);

        if (direction.sqrMagnitude > 0.01f)
        {
            Quaternion look = Quaternion.LookRotation(new Vector3(direction.x, 0f, direction.z));
            transform.rotation = Quaternion.Slerp(transform.rotation, look, 10f * Time.deltaTime);
        }
    }

    [Command]
    private void CmdMoveTo(Vector3 destination)
    {
        targetPosition = destination;
        RpcMoveTo(destination);
    }

    [ClientRpc]
    private void RpcMoveTo(Vector3 destination)
    {
        if (isServer) return;
        targetPosition = destination;
    }

    private void ShowMoveIndicator(Vector3 position)
    {
        if (moveIndicatorPrefab == null) return;
        var indicator = Instantiate(moveIndicatorPrefab, position + Vector3.up * 0.1f, Quaternion.identity);
        Destroy(indicator, 1f);
    }

    public void MoveTo(Vector3 worldPosition)
    {
        Vector3 dest = new Vector3(worldPosition.x, flyHeight, worldPosition.z);
        CmdMoveTo(dest);
        ShowMoveIndicator(worldPosition);
    }

    public void StopMoving()
    {
        targetPosition = null;
    }
}
