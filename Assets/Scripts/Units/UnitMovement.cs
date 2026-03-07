using UnityEngine;
using Mirror;
using System.Collections.Generic;

public class UnitMovement : NetworkBehaviour
{
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float repathInterval = 1f;
    [SerializeField] private float waypointThreshold = 0.5f;
    [SerializeField] private float separationRadius = 1.5f;
    [SerializeField] private float separationWeight = 1.2f;
    [SerializeField] private float rotationSpeed = 10f;

    private Unit unit;
    private GridSystem grid;
    private List<Vector3> waypoints;
    private int waypointIndex;
    private Vector3? worldTarget;
    private float repathTimer;
    private bool isStopped;

    public bool IsMoving => !isStopped && waypoints != null && waypointIndex < waypoints.Count;
    public bool HasPath => waypoints != null && waypointIndex < waypoints.Count;
    public Vector3? WorldTarget => worldTarget;
    public IReadOnlyList<Vector3> Waypoints => waypoints;
    public int WaypointIndex => waypointIndex;
    public float SeparationRadius => separationRadius;

    public event System.Action OnReachedDestination;

    private void Awake()
    {
        unit = GetComponent<Unit>();
    }

    public override void OnStartServer()
    {
        grid = GridSystem.Instance;
        if (grid == null)
            Debug.LogError($"[UnitMovement] GridSystem.Instance is NULL on {gameObject.name}!");
    }

    private void Update()
    {
        if (!isServer || grid == null) return;
        if (unit != null && unit.IsDead) return;
        if (isStopped) return;

        if (waypoints != null && waypointIndex < waypoints.Count)
        {
            MoveTowardWaypoint();
        }
        else if (worldTarget.HasValue)
        {
            repathTimer -= Time.deltaTime;
            if (repathTimer <= 0f)
            {
                repathTimer = repathInterval;
                CalculatePath();
            }
        }
    }

    private void MoveTowardWaypoint()
    {
        Vector3 target = waypoints[waypointIndex];
        Vector3 pos = transform.position;
        Vector3 toTarget = target - pos;
        toTarget.y = 0;

        float distToWaypoint = toTarget.magnitude;
        if (distToWaypoint < waypointThreshold)
        {
            waypointIndex++;
            if (waypointIndex >= waypoints.Count)
            {
                waypoints = null;
                worldTarget = null;
                OnReachedDestination?.Invoke();
                return;
            }
            toTarget = waypoints[waypointIndex] - pos;
            toTarget.y = 0;
        }

        float speed = (unit != null && unit.Data != null) ? unit.Data.moveSpeed : moveSpeed;

        Vector3 moveDir = toTarget.normalized;
        Vector3 separation = CalculateSeparation();
        moveDir = (moveDir + separation * separationWeight).normalized;

        Vector3 newPos = pos + moveDir * speed * Time.deltaTime;
        newPos.y = grid.GridOrigin.y;
        transform.position = newPos;

        if (moveDir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }

        if (Time.frameCount % 10 == 0)
            RpcSyncPosition(transform.position);

        repathTimer -= Time.deltaTime;
        if (repathTimer <= 0f)
        {
            repathTimer = repathInterval;
            CalculatePath();
        }
    }

    private Vector3 CalculateSeparation()
    {
        if (UnitManager.Instance == null) return Vector3.zero;

        Vector3 force = Vector3.zero;
        var nearby = UnitManager.Instance.GetUnitsInRadius(transform.position, separationRadius);

        foreach (var other in nearby)
        {
            if (other == null || other.gameObject == gameObject) continue;

            Vector3 offset = transform.position - other.transform.position;
            offset.y = 0;
            float dist = offset.magnitude;
            if (dist < 0.01f)
            {
                offset = new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f));
                dist = 0.1f;
            }

            force += offset.normalized * (1f - dist / separationRadius);
        }

        return force;
    }

    [Server]
    public void SetDestinationWorld(Vector3 target)
    {
        worldTarget = target;
        isStopped = false;
        CalculatePath();
    }

    [Server]
    public void SetDestinationToEnemyCastle()
    {
        if (unit == null || grid == null) return;

        int enemyTeam = TeamManager.Instance != null
            ? TeamManager.Instance.GetEnemyTeamId(unit.TeamId)
            : (unit.TeamId == 0 ? 1 : 0);

        Castle[] castles = FindObjectsByType<Castle>(FindObjectsSortMode.None);
        foreach (var c in castles)
        {
            if (c.TeamId == enemyTeam)
            {
                SetDestinationWorld(c.transform.position);
                return;
            }
        }
    }

    [Server]
    public void Stop()
    {
        waypoints = null;
        waypointIndex = 0;
        worldTarget = null;
        isStopped = true;
    }

    [Server]
    public void Resume()
    {
        isStopped = false;
        if (worldTarget.HasValue)
            CalculatePath();
    }

    private void CalculatePath()
    {
        if (!worldTarget.HasValue || grid == null) return;

        Vector2Int startCell = grid.WorldToCell(transform.position);
        Vector2Int goalCell = grid.WorldToCell(worldTarget.Value);

        if (!grid.IsInBounds(startCell))
            startCell = ClampToGrid(startCell);
        if (!grid.IsInBounds(goalCell))
            goalCell = ClampToGrid(goalCell);

        var result = GridPathfinding.FindPath(startCell, goalCell, grid);

        if (result.HasPath)
        {
            waypoints = GridPathfinding.SmoothPath(result.Path, grid);
            if (waypoints.Count > 1)
                waypoints[waypoints.Count - 1] = worldTarget.Value;

            waypointIndex = 0;
            SkipPassedWaypoints();
        }
        else
        {
            waypoints = null;
        }
    }

    private void SkipPassedWaypoints()
    {
        if (waypoints == null || waypoints.Count == 0) return;

        float bestDist = float.MaxValue;
        int bestIdx = 0;
        for (int i = 0; i < waypoints.Count; i++)
        {
            float d = (waypoints[i] - transform.position).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                bestIdx = i;
            }
        }

        if (bestIdx < waypoints.Count - 1)
            waypointIndex = bestIdx + 1;
        else
            waypointIndex = bestIdx;
    }

    private Vector2Int ClampToGrid(Vector2Int cell)
    {
        return new Vector2Int(
            Mathf.Clamp(cell.x, 0, grid.Width - 1),
            Mathf.Clamp(cell.y, 0, grid.Height - 1)
        );
    }

    [ClientRpc]
    private void RpcSyncPosition(Vector3 pos)
    {
        if (isServer) return;
        transform.position = Vector3.Lerp(transform.position, pos, 0.5f);
    }
}
