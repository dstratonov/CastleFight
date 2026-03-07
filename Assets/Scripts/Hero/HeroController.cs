using UnityEngine;
using UnityEngine.AI;
using Mirror;

[RequireComponent(typeof(NavMeshAgent))]
public class HeroController : NetworkBehaviour
{
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private GameObject moveIndicatorPrefab;

    private NavMeshAgent agent;
    private Camera mainCamera;

    public bool IsMoving => agent != null && agent.hasPath && agent.remainingDistance > agent.stoppingDistance;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
    }

    public override void OnStartLocalPlayer()
    {
        mainCamera = Camera.main;
        agent.speed = moveSpeed;
    }

    private void Update()
    {
        if (!isLocalPlayer) return;

        if (Input.GetMouseButtonDown(1))
        {
            HandleMoveInput();
        }
    }

    private void HandleMoveInput()
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 200f, groundLayer))
        {
            CmdMoveTo(hit.point);
            ShowMoveIndicator(hit.point);
        }
    }

    [Command]
    private void CmdMoveTo(Vector3 destination)
    {
        agent.SetDestination(destination);
        RpcMoveTo(destination);
    }

    [ClientRpc]
    private void RpcMoveTo(Vector3 destination)
    {
        if (isServer) return;
        agent.SetDestination(destination);
    }

    private void ShowMoveIndicator(Vector3 position)
    {
        if (moveIndicatorPrefab == null) return;
        var indicator = Instantiate(moveIndicatorPrefab, position + Vector3.up * 0.1f, Quaternion.identity);
        Destroy(indicator, 1f);
    }

    public void StopMoving()
    {
        if (agent.hasPath)
            agent.ResetPath();
    }
}
