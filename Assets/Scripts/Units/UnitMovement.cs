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
    private Animator cachedAnimator;
    private int consecutivePathFails;
    private bool isPathComplete;

    public bool IsMoving => !isStopped && waypoints != null && waypointIndex < waypoints.Count;
    public bool HasPath => waypoints != null && waypointIndex < waypoints.Count;
    public bool IsPathComplete => isPathComplete;
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

        cachedAnimator = GetComponentInChildren<Animator>();
        if (cachedAnimator != null)
            cachedAnimator.applyRootMotion = false;
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
        if (cachedAnimator != null && cachedAnimator.applyRootMotion)
            cachedAnimator.applyRootMotion = false;

        Vector3 target = waypoints[waypointIndex];
        Vector3 pos = transform.position;
        Vector3 toTarget = target - pos;
        toTarget.y = 0;

        float effectiveThreshold = waypointThreshold;
        if (unit != null)
            effectiveThreshold = Mathf.Max(waypointThreshold, unit.EffectiveRadius * 0.8f);

        float distToWaypoint = toTarget.magnitude;
        if (distToWaypoint < effectiveThreshold)
        {
            waypointIndex++;
            if (waypointIndex >= waypoints.Count)
            {
                if (GameDebug.Movement)
                    Debug.Log($"[Move:{gameObject.name}] reached destination at {transform.position:F1}");
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
        Vector3 wallRepulsion = CalculateWallRepulsion();
        moveDir = (moveDir + separation * separationWeight + wallRepulsion).normalized;

        Vector3 newPos = pos + moveDir * speed * Time.deltaTime;
        newPos.y = grid.GridOrigin.y;

        newPos = ValidatePosition(pos, newPos);

        transform.position = newPos;

        Vector3 actualDir = newPos - pos;
        actualDir.y = 0;
        if (actualDir.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(actualDir);
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

    private Vector3 ValidatePosition(Vector3 oldPos, Vector3 newPos)
    {
        Vector2Int newCell = grid.WorldToCell(newPos);
        if (grid.IsInBounds(newCell) && grid.IsWalkable(newCell))
            return newPos;

        Vector3 slideX = new Vector3(newPos.x, newPos.y, oldPos.z);
        Vector2Int cellX = grid.WorldToCell(slideX);
        if (grid.IsInBounds(cellX) && grid.IsWalkable(cellX))
            return slideX;

        Vector3 slideZ = new Vector3(oldPos.x, newPos.y, newPos.z);
        Vector2Int cellZ = grid.WorldToCell(slideZ);
        if (grid.IsInBounds(cellZ) && grid.IsWalkable(cellZ))
            return slideZ;

        return oldPos;
    }

    private Vector3 CalculateWallRepulsion()
    {
        if (grid == null) return Vector3.zero;

        Vector3 pos = transform.position;
        Vector2Int myCell = grid.WorldToCell(pos);
        float cellSize = grid.CellSize;
        Vector3 force = Vector3.zero;
        float checkRadius = cellSize * 1.5f;

        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dz = -2; dz <= 2; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                Vector2Int neighbor = new(myCell.x + dx, myCell.y + dz);
                if (!grid.IsInBounds(neighbor) || grid.IsWalkable(neighbor)) continue;

                Vector3 wallPos = grid.CellToWorld(neighbor);
                Vector3 offset = pos - wallPos;
                offset.y = 0;
                float dist = offset.magnitude;

                if (dist < checkRadius && dist > 0.01f)
                {
                    float strength = 1f - (dist / checkRadius);
                    force += offset.normalized * strength * 2f;
                }
            }
        }

        return force;
    }

    private float GetEffectiveSeparationRadius()
    {
        if (unit != null)
            return Mathf.Max(separationRadius, unit.EffectiveRadius * 3f);
        return separationRadius;
    }

    private Vector3 CalculateSeparation()
    {
        if (UnitManager.Instance == null) return Vector3.zero;

        float myRadius = unit != null ? unit.EffectiveRadius : 0.5f;
        float effectiveRadius = GetEffectiveSeparationRadius();
        Vector3 force = Vector3.zero;
        var nearby = UnitManager.Instance.GetUnitsInRadius(transform.position, effectiveRadius);

        foreach (var other in nearby)
        {
            if (other == null || other.gameObject == gameObject) continue;

            float otherRadius = 0.5f;
            var otherUnit = other.GetComponent<Unit>();
            if (otherUnit != null)
                otherRadius = otherUnit.EffectiveRadius;

            float combinedRadius = myRadius + otherRadius;

            Vector3 offset = transform.position - other.transform.position;
            offset.y = 0;
            float dist = offset.magnitude;
            if (dist < 0.01f)
            {
                offset = new Vector3(Random.Range(-1f, 1f), 0, Random.Range(-1f, 1f));
                dist = 0.1f;
            }

            if (dist < combinedRadius * 1.5f)
            {
                float sizeRatio = Mathf.Clamp(otherRadius / myRadius, 0.1f, 3f);
                force += offset.normalized * (1f - dist / effectiveRadius) * sizeRatio;
            }
        }

        return force;
    }

    [Server]
    public void SetDestinationWorld(Vector3 target)
    {
        if (grid != null)
        {
            Vector2Int cell = grid.WorldToCell(target);
            if (!grid.IsInBounds(cell) || !grid.IsWalkable(cell))
            {
                Vector3 corrected = grid.FindNearestWalkablePosition(target, transform.position);
                if (GameDebug.Movement)
                    Debug.Log($"[Move:{gameObject.name}] dest {target:F1} not walkable, corrected to {corrected:F1}");
                target = corrected;
            }
        }

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
                Vector3 target = grid.FindNearestWalkablePosition(c.transform.position, transform.position);
                SetDestinationWorld(target);
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

        if (grid != null)
        {
            Vector2Int cell = grid.WorldToCell(transform.position);
            if (!grid.IsInBounds(cell) || !grid.IsWalkable(cell))
            {
                Vector3 safe = grid.FindNearestWalkablePosition(transform.position, transform.position);
                transform.position = safe;
            }
        }
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

        if (!grid.IsWalkable(startCell))
        {
            Vector3 nearest = grid.FindNearestWalkablePosition(transform.position, transform.position);
            if (GameDebug.Movement)
                Debug.Log($"[Move:{gameObject.name}] start cell {startCell} non-walkable, relocating to {nearest:F1}");
            transform.position = nearest;
            startCell = grid.WorldToCell(nearest);
        }

        var result = GridPathfinding.FindPath(startCell, goalCell, grid);

        if (result.HasPath)
        {
            consecutivePathFails = 0;
            isPathComplete = result.IsComplete;
            waypoints = GridPathfinding.SmoothPath(result.Path, grid);

            if (result.IsComplete && waypoints.Count > 1)
                waypoints[waypoints.Count - 1] = worldTarget.Value;

            waypointIndex = 0;
            SkipPassedWaypoints();

            if (GameDebug.Movement)
                Debug.Log($"[Move:{gameObject.name}] path {startCell}->{goalCell} cells={result.Path.Count} wp={waypoints.Count} complete={result.IsComplete}");
        }
        else
        {
            consecutivePathFails++;
            isPathComplete = false;
            if (GameDebug.Movement)
                Debug.Log($"[Move:{gameObject.name}] path FAILED {startCell}->{goalCell} (fails={consecutivePathFails})");

            if (consecutivePathFails >= 5)
            {
                if (GameDebug.Movement)
                    Debug.Log($"[Move:{gameObject.name}] path blocked, giving up on target");
                worldTarget = null;
                consecutivePathFails = 0;
            }

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
