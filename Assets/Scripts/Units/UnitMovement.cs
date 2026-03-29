using UnityEngine;
using Mirror;
using System.Collections.Generic;

/// <summary>
/// Unit movement using grid-based A* pathfinding.
/// Follows waypoints from A*, wall-slides around obstacles.
/// No boids, no steering, no density stop — just simple path following.
/// </summary>
public class UnitMovement : NetworkBehaviour
{
    [SerializeField] private float waypointThreshold = 0.5f;
    [SerializeField] private float rotationSpeed = 10f;

    private Unit unit;
    private UnitStateMachine stateMachine;
    private GridSystem grid;

    // Current path
    private List<Vector3> waypoints;
    private int waypointIndex;
    private Vector3? worldTarget;
    private bool isStopped;

    // Strategic destination persists through combat stops.
    // Resume() restores it so the unit continues marching after combat.
    private Vector3? strategicDestination;

    // Replan scheduling
    private int replanFrameSlot;
    private const int ReplanStaggerFrames = 15;
    private bool needsReplan;

    // Stuck detection
    private Vector3 lastProgressPos;
    private float stallTime;
    private const float StuckThresholdTime = 2f;
    private int arriveCount;

    // Near-destination arrival timer
    private float nearDestTimer;
    private const float NearDestArriveTime = 1.5f;

    // Network position sync
    private Vector3 serverPosition;
    private bool hasServerPos;

    // Previous position for velocity inference
    private Vector3 previousPosition;
    public Vector3 PreviousPosition => previousPosition;

    // Public state
    public bool IsMoving => !isStopped && HasPath;
    public bool HasPath => waypoints != null && waypointIndex < waypoints.Count;
    public bool IsDestinationUnreachable { get; private set; }
    public Vector3? WorldTarget => worldTarget;
    public IReadOnlyList<Vector3> Waypoints => waypoints;
    public int WaypointIndex => waypointIndex;
    public int ArriveCount => arriveCount;
    public bool IsWaitingForPath => needsReplan && !HasPath;
    public bool HasStrategicDestination => strategicDestination.HasValue;
    public Vector3? StrategicDestination => strategicDestination;

    public Bounds PathBounds { get; private set; }

    // Debug: raw A* cell path for visualization
    private readonly List<Vector2Int> debugCellPath = new();
    public IReadOnlyList<Vector2Int> DebugCellPath => debugCellPath;

    public event System.Action OnReachedDestination;

    private static readonly Dictionary<int, Castle> cachedEnemyCastles = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetMovementStatics()
    {
        cachedEnemyCastles.Clear();
    }

    private void Awake()
    {
        unit = GetComponent<Unit>();
        stateMachine = GetComponent<UnitStateMachine>();
        replanFrameSlot = Mathf.Abs(GetInstanceID()) % ReplanStaggerFrames;
    }

    public override void OnStartServer()
    {
        grid = GridSystem.Instance;
        previousPosition = transform.position;
        lastProgressPos = transform.position;
    }

    private void Update()
    {
        if (!isServer || !NetworkServer.active || unit == null || grid == null || unit.IsDead)
            return;

        previousPosition = transform.position;

        // Client-side interpolation
        if (!isServer)
        {
            if (hasServerPos)
            {
                float speed = (unit != null && unit.Data != null) ? unit.Data.moveSpeed : 3.5f;
                transform.position = Vector3.MoveTowards(transform.position, serverPosition, speed * 2f * Time.deltaTime);
            }
            return;
        }

        if (isStopped)
            return;

        if (!HasPath && !worldTarget.HasValue)
            return;

        // Check arrival at final destination
        if (worldTarget.HasValue)
        {
            float effectiveRadius = unit != null ? unit.EffectiveRadius : 0f;
            float arrDist = Vector3.Distance(transform.position, worldTarget.Value);
            float arrThresh = Mathf.Max(waypointThreshold, effectiveRadius * 1.3f);
            if (arrDist < arrThresh)
            {
                ArriveAtDestination();
                return;
            }
        }

        // Advance through waypoints
        if (HasPath)
        {
            float effectiveRadius = unit != null ? unit.EffectiveRadius : 0f;
            while (waypointIndex < waypoints.Count)
            {
                if (MovementLogic.ShouldAdvanceWaypoint(transform.position, waypoints[waypointIndex], effectiveRadius, waypointThreshold))
                {
                    waypointIndex++;
                    if (waypointIndex >= waypoints.Count)
                    {
                        ArriveAtDestination();
                        return;
                    }
                }
                else break;
            }
        }

        // Move along path
        if (HasPath)
            MoveAlongPath();

        // Staggered replan check
        if ((Time.frameCount % ReplanStaggerFrames) == replanFrameSlot)
            CheckReplan();

        // Periodic network sync
        if ((Time.frameCount + (GetInstanceID() & 0xFF)) % 10 == 0)
            RpcSyncPositionAndRotation(transform.position, transform.rotation);
    }

    // ================================================================
    //  CORE MOVEMENT — simple waypoint following
    // ================================================================

    private void MoveAlongPath()
    {
        float speed = unit.Data.moveSpeed;
        Vector3 nextWp = waypoints[waypointIndex];
        Vector3 dir = nextWp - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.0001f)
        {
            waypointIndex++;
            return;
        }

        Vector3 velocity = dir.normalized * speed;

        Vector3 oldPos = transform.position;
        Vector3 newPos = oldPos + velocity * Time.deltaTime;
        newPos.y = grid.GridOrigin.y;

        // Hard lock (chasing specific enemy) → avoid ALL units
        // Soft lock (marching) → avoid same-team only, walk through enemies
        bool hardLock = unit != null && unit.Combat != null && unit.Combat.HasTarget;
        grid.WalkableTeamContext = hardLock ? -2 : (unit != null ? unit.TeamId : 0);

        // Temporarily unmark self so our own cells don't block checks
        UnmarkSelf();

        int fp = unit != null ? unit.FootprintSize : 1;

        // Check if the next waypoint cell is blocked — recompute path
        Vector2Int wpCell = grid.WorldToCell(nextWp);
        if (!FootprintHelper.IsWalkable(grid, wpCell, fp))
        {
            ComputePathInternal();
            RemarkSelf();
            grid.WalkableTeamContext = -1;
            return;
        }

        // Check if moving to a new cell that's occupied by another unit
        // But always allow movement if we're ALREADY overlapping (let units separate)
        int myId = unit != null ? unit.GetInstanceID() : 0;
        var presence = UnitGridPresence.Instance;
        Vector2Int newCell = grid.WorldToCell(newPos);
        Vector2Int oldCell = grid.WorldToCell(oldPos);
        if (newCell != oldCell && presence != null)
        {
            FootprintHelper.GetHalfExtents(fp, out int hl, out int hh);

            // Check if we're currently overlapping at old position
            bool alreadyOverlapping = false;
            for (int dx = -hl; dx <= hh && !alreadyOverlapping; dx++)
                for (int dy = -hl; dy <= hh && !alreadyOverlapping; dy++)
                    if (presence.IsOccupiedByOther(new Vector2Int(oldCell.x + dx, oldCell.y + dy), myId))
                        alreadyOverlapping = true;

            // Only block if we'd create a NEW overlap (not already overlapping)
            if (!alreadyOverlapping)
            {
                for (int dx = -hl; dx <= hh; dx++)
                {
                    for (int dy = -hl; dy <= hh; dy++)
                    {
                        if (presence.IsOccupiedByOther(new Vector2Int(newCell.x + dx, newCell.y + dy), myId))
                        {
                            ComputePathInternal();
                            RemarkSelf();
                            grid.WalkableTeamContext = -1;
                            return;
                        }
                    }
                }
            }
        }

        RemarkSelf();
        grid.WalkableTeamContext = -1;

        transform.position = newPos;

        // Rotate toward movement direction
        Vector3 moveDelta = newPos - oldPos;
        moveDelta.y = 0f;
        Vector3 faceDir = moveDelta.sqrMagnitude > 0.0001f ? moveDelta : dir;
        ApplyRotation(faceDir);

        TrackProgress();
    }

    // ================================================================
    //  PATH REQUESTS
    // ================================================================

    [Server]
    public void SetDestinationWorld(Vector3 target)
    {
        if (grid == null) return;

        // Validate destination using unit's full footprint, not just center cell
        int footprint = unit != null ? unit.FootprintSize : 1;
        Vector2Int cell = grid.WorldToCell(target);
        if (!FootprintHelper.IsWalkable(grid, cell, footprint))
        {
            Vector2Int nearest = FootprintHelper.FindNearestWalkable(grid, cell, footprint);
            target = grid.CellToWorld(nearest);
        }

        if (worldTarget.HasValue && HasPath)
        {
            float threshold = grid.CellSize;
            if (MovementLogic.IsDuplicateDestination(target, worldTarget, threshold))
                return;
        }

        // Already at destination
        float effRadius = unit != null ? unit.EffectiveRadius : 0f;
        if (MovementLogic.HasArrivedAtDestination(transform.position, target, effRadius, waypointThreshold))
        {
            worldTarget = target;
            ArriveAtDestination();
            return;
        }

        worldTarget = target;
        isStopped = false;
        stallTime = 0f;
        nearDestTimer = 0f;
        IsDestinationUnreachable = false;
        lastProgressPos = transform.position;
        needsReplan = false;

        ComputePath();
    }

    [Server]
    public void ForceSetDestinationWorld(Vector3 target)
    {
        worldTarget = null;
        waypoints = null;
        waypointIndex = 0;
        SetDestinationWorld(target);
    }

    [Server]
    public void SetDestinationToEnemyCastle()
    {
        if (unit == null || grid == null) return;

        int enemyTeam = TeamManager.Instance.GetEnemyTeamId(unit.TeamId);

        if (!cachedEnemyCastles.TryGetValue(enemyTeam, out var castle) || castle == null)
        {
            castle = GameRegistry.GetEnemyCastle(unit.TeamId);
            if (castle != null)
                cachedEnemyCastles[enemyTeam] = castle;
        }

        if (castle == null) return;

        Vector3 castlePos = castle.transform.position;
        float spreadRadius = 2f;
        Vector3 offset = MovementLogic.ComputeCastleSpreadOffset(GetInstanceID(), spreadRadius);
        Vector3 spreadTarget = castlePos + offset;

        var pfm = PathfindingManager.Instance;
        if (pfm == null) return;
        Vector3 target = pfm.FindNearestWalkable(spreadTarget);

        Vector2Int targetCell = grid.WorldToCell(target);
        if (!grid.IsWalkable(targetCell))
            target = pfm.FindNearestWalkable(castlePos);

        strategicDestination = target;
        ForceSetDestinationWorld(target);
    }

    private void ComputePath()
    {
        bool hardLock = unit != null && unit.Combat != null && unit.Combat.HasTarget;
        grid.WalkableTeamContext = hardLock ? -2 : (unit != null ? unit.TeamId : 0);
        UnmarkSelf();
        ComputePathInternal();
        RemarkSelf();
        grid.WalkableTeamContext = -1;
    }

    /// <summary>A* path computation. Caller must unmark/remark self.</summary>
    private void ComputePathInternal()
    {
        if (!worldTarget.HasValue || grid == null) return;

        int footprint = unit != null ? unit.FootprintSize : 1;

        debugCellPath.Clear();
        var path = GridAStar.FindPath(grid, transform.position, worldTarget.Value, debugCellPath, footprint);

        if (path != null && path.Count > 0)
        {
            waypoints = path;
            waypointIndex = 0;
            IsDestinationUnreachable = false;
            float radius = unit != null ? unit.EffectiveRadius : 0.5f;
            PathBounds = PathInvalidation.ComputePathBounds(waypoints, radius);
        }
        else
        {
            IsDestinationUnreachable = true;
            waypoints = null;
            waypointIndex = 0;
        }
    }

    public void FlagForReplan() { needsReplan = true; }

    public void InvalidatePath()
    {
        if (waypoints == null) return;
        waypoints = null;
        waypointIndex = 0;
    }

    private void CheckReplan()
    {
        if (!worldTarget.HasValue) return;

        if (needsReplan)
        {
            needsReplan = false;
            ComputePath();
            return;
        }

        if (stallTime > StuckThresholdTime && HasPath && !IsDestinationUnreachable)
        {
            stallTime = 0f;
            var pfm = PathfindingManager.Instance;
            if (pfm != null && pfm.TryConsumePathRequest())
                ComputePath();
        }
    }

    // ================================================================
    //  CONTROL METHODS
    // ================================================================

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
        stallTime = 0f;
        nearDestTimer = 0f;
        IsDestinationUnreachable = false;

        if (!worldTarget.HasValue && strategicDestination.HasValue)
            worldTarget = strategicDestination;

        if (worldTarget.HasValue)
            ComputePath();
    }

    private void ArriveAtDestination()
    {
        arriveCount++;
        waypoints = null;
        waypointIndex = 0;
        worldTarget = null;
        strategicDestination = null;
        stallTime = 0f;
        nearDestTimer = 0f;
        OnReachedDestination?.Invoke();
    }

    // ================================================================
    //  STUCK DETECTION
    // ================================================================

    private void TrackProgress()
    {
        float moved = Vector3.Distance(transform.position, lastProgressPos);
        float effectiveRadius = unit != null ? unit.EffectiveRadius : 0.5f;
        bool isStalled = moved < 0.1f * Time.deltaTime;

        if (isStalled)
        {
            stallTime += Time.deltaTime;

            if (stallTime > 3f && worldTarget.HasValue)
            {
                float distToDest = Vector3.Distance(transform.position, worldTarget.Value);
                float nearThreshold = effectiveRadius * 3f;

                if (distToDest < nearThreshold)
                {
                    ArriveAtDestination();
                    return;
                }
                else
                {
                    IsDestinationUnreachable = true;
                    waypoints = null;
                    waypointIndex = 0;
                    worldTarget = null;
                    stallTime = 0f;
                    return;
                }
            }
        }
        else
        {
            stallTime = Mathf.Max(0f, stallTime - Time.deltaTime * 0.5f);
        }

        lastProgressPos = transform.position;

        if (worldTarget.HasValue)
        {
            float distToGoal = Vector3.Distance(transform.position, worldTarget.Value);
            float nearThreshold = Mathf.Max(1.5f, effectiveRadius * 1.5f);
            if (distToGoal < nearThreshold)
            {
                nearDestTimer += Time.deltaTime;
                if (nearDestTimer > NearDestArriveTime)
                {
                    ArriveAtDestination();
                    return;
                }
            }
            else
            {
                nearDestTimer = 0f;
            }
        }
    }


    // ================================================================
    //  SELF UNMARK/REMARK
    // ================================================================

    private void UnmarkSelf()
    {
        var presence = UnitGridPresence.Instance;
        if (presence != null && unit != null)
            presence.UnmarkUnit(unit.GetInstanceID());
    }

    private void RemarkSelf()
    {
        var presence = UnitGridPresence.Instance;
        if (presence != null && unit != null)
            presence.RemarkUnit(unit.GetInstanceID());
    }

    // ================================================================
    //  UTILITIES
    // ================================================================

    private void ApplyRotation(Vector3 moveDelta)
    {
        moveDelta.y = 0f;
        if (moveDelta.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDelta);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
    }

    [ClientRpc]
    private void RpcSyncPositionAndRotation(Vector3 pos, Quaternion rot)
    {
        if (isServer) return;
        serverPosition = pos;
        hasServerPos = true;
        transform.rotation = Quaternion.Slerp(transform.rotation, rot, 15f * Time.deltaTime);
    }
}
