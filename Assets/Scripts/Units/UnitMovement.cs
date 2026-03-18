using UnityEngine;
using Mirror;
using System.Collections.Generic;

/// <summary>
/// Unit movement using SC2-style two-layer pathfinding.
/// Layer 1 (NavMesh + A* + Funnel) produces waypoints.
/// Layer 2 (Boids) handles unit-to-unit avoidance each frame.
/// Wall collision is handled by ValidatePosition (physics layer, not Boids).
/// </summary>
public class UnitMovement : NetworkBehaviour
{
    [SerializeField] private float moveSpeed = 3.5f;
    [SerializeField] private float waypointThreshold = 0.5f;
    [SerializeField] private float rotationSpeed = 10f;

    private Unit unit;
    private UnitStateMachine stateMachine;
    private GridSystem grid;
    private Animator cachedAnimator;

    // Current path (Layer 1 output: vertex-expanded waypoints)
    private List<Vector3> waypoints;
    private int waypointIndex;
    private Vector3? worldTarget;
    private bool isStopped;

    // SC2-style underlying order: the strategic destination persists through
    // combat stops. When Stop() is called for combat, worldTarget is cleared
    // but strategicDestination is preserved. Resume() restores it so the unit
    // continues marching after the engagement ends.
    private Vector3? strategicDestination;

    // Replan scheduling (staggered across units, not every frame)
    private int replanFrameSlot;
    private const int ReplanStaggerFrames = 15;
    private bool needsReplan;

    // Stuck detection
    private Vector3 lastProgressPos;
    private float stallTime;
    private const float StuckThresholdTime = 1.5f;
    private int arriveCount;

    // Near-destination arrival timer: tracks time spent close to destination without arriving.
    // Independent of stallTime — fires even if the unit is sliding/oscillating.
    private float nearDestTimer;
    private const float NearDestArriveTime = 1.5f;

    // Velocity smoothing
    private Vector3 smoothedVelocity;

    // Network position sync
    private Vector3 serverPosition;
    private bool hasServerPos;

    // Previous position (used by Boids for velocity inference)
    private Vector3 previousPosition;
    public Vector3 PreviousPosition => previousPosition;

    // Push/yield for anti-ghosting
    private float yieldTimer;
    private Vector3 yieldDirection;
    private const float YieldDuration = 0.4f;

    // Smooth lerp to walkable position (replaces instant teleport)
    private Vector3? walkableLerpTarget;
    private float walkableLerpTimer;
    private const float WalkableLerpDuration = 0.5f;

    // Debug log throttle
    private float debugLogTimer;

    // Public state
    public bool IsMoving => !isStopped && HasPath && !isDensityStopped;
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

    public event System.Action OnReachedDestination;

    private bool isDensityStopped;
    private float densityCheckTimer;

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

        Debug.Assert(unit != null, $"[UnitMovement] {gameObject.name} missing Unit component", this);
        Debug.Assert(stateMachine != null, $"[UnitMovement] {gameObject.name} missing UnitStateMachine component", this);
    }

    public override void OnStartServer()
    {
        grid = GridSystem.Instance;
        Debug.Assert(grid != null, $"[UnitMovement] {gameObject.name} OnStartServer: GridSystem.Instance is null", this);

        cachedAnimator = GetComponentInChildren<Animator>();

        previousPosition = transform.position;
        lastProgressPos = transform.position;
    }

    private void Update()
    {
        if (!isServer)
        {
            if (hasServerPos)
            {
                float speed = (unit != null && unit.Data != null) ? unit.Data.moveSpeed : moveSpeed;
                transform.position = Vector3.MoveTowards(transform.position, serverPosition, speed * 2f * Time.deltaTime);
            }
            return;
        }

        if (!NetworkServer.active) return;

        if (unit == null || grid == null || unit.IsDead)
            return;

        previousPosition = transform.position;

        // Smooth lerp to walkable position if active
        if (walkableLerpTarget.HasValue)
        {
            walkableLerpTimer -= Time.deltaTime;
            float t = 1f - Mathf.Clamp01(walkableLerpTimer / WalkableLerpDuration);
            transform.position = Vector3.Lerp(transform.position, walkableLerpTarget.Value, t);
            if (walkableLerpTimer <= 0f)
            {
                transform.position = walkableLerpTarget.Value;
                walkableLerpTarget = null;
            }
            return;
        }

        if (isStopped)
        {
            UpdateYield();
            bool isFighting = stateMachine != null && stateMachine.CurrentState == UnitState.Fighting;
            if (!isFighting)
                ApplySeparationPush();
            return;
        }

        UpdateYield();

        if (!HasPath && !worldTarget.HasValue)
            return;

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

        // SC2-style: density stop only applies to marching, not combat approach.
        bool isCombatApproach = unit != null && unit.Combat != null && unit.Combat.AttackTarget != null;

        if (worldTarget.HasValue && HasPath && waypointIndex >= waypoints.Count - 2
            && MovementLogic.ShouldCheckDensity(isCombatApproach))
        {
            float distToDest = Vector3.Distance(transform.position, worldTarget.Value);
            float arriveThreshold = unit != null ? unit.EffectiveRadius * 2f : 1f;
            if (distToDest > arriveThreshold)
            {
                densityCheckTimer -= Time.deltaTime;
                if (densityCheckTimer <= 0f)
                {
                    densityCheckTimer = 0.3f;
                    var pfm = PathfindingManager.Instance;
                    isDensityStopped = pfm != null && pfm.ShouldDensityStop(unit, worldTarget.Value);
                }

                if (isDensityStopped)
                {
                    smoothedVelocity = Vector3.zero;
                    ApplySeparationPush();
                    TrackProgress();
                    return;
                }
            }
            else
            {
                isDensityStopped = false;
            }
        }
        else if (isCombatApproach)
        {
            isDensityStopped = false;
        }

        // Movement + Boids (every frame)
        if (HasPath)
            MoveAlongPath();
        else if (worldTarget.HasValue)
            MoveDirectToward(worldTarget.Value);

        // Staggered replan check
        if ((Time.frameCount % ReplanStaggerFrames) == replanFrameSlot)
            CheckReplan();

        // Periodic network sync
        if ((Time.frameCount + (GetInstanceID() & 0xFF)) % 10 == 0)
            RpcSyncPositionAndRotation(transform.position, transform.rotation);
    }

    // ================================================================
    //  CORE MOVEMENT (Layer 1 direction + Layer 2 Boids)
    // ================================================================

    private void MoveAlongPath()
    {
        Debug.Assert(unit != null && unit.Data != null, $"[UnitMovement] {gameObject.name} MoveAlongPath: unit or unit.Data is null", this);
        float speed = unit.Data.moveSpeed;
        Vector3 nextWp = waypoints[waypointIndex];
        Vector3 dir = nextWp - transform.position;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.0001f)
        {
            waypointIndex++;
            return;
        }

        Vector3 desiredVelocity = dir.normalized * speed;

        var pfm = PathfindingManager.Instance;
        Debug.Assert(pfm != null, $"[UnitMovement] {gameObject.name} MoveAlongPath: PathfindingManager.Instance is null", this);
        bool isMarching = stateMachine == null || stateMachine.CurrentState == UnitState.Moving;
        Vector3 finalVelocity = pfm.ComputeSteering(unit, desiredVelocity, speed, isMarching);

        // Deep log: detect when Boids significantly redirects the unit
        if (GameDebug.Movement && desiredVelocity.sqrMagnitude > 0.01f && finalVelocity.sqrMagnitude > 0.01f)
        {
            float dirDot = Vector3.Dot(finalVelocity.normalized, desiredVelocity.normalized);
            if (dirDot < 0f)
            {
                debugLogTimer = 0f; // force next log
                Debug.LogWarning($"[Move:{gameObject.name}] BOIDS REVERSED direction! " +
                    $"desired={desiredVelocity:F2} final={finalVelocity:F2} dot={dirDot:F2}");
            }
        }

        smoothedVelocity = SmoothDamp(smoothedVelocity, finalVelocity, 10f);
        smoothedVelocity = MovementLogic.PreventBackwardVelocity(smoothedVelocity, desiredVelocity);

        Vector3 oldPos = transform.position;
        Vector3 newPos = oldPos + smoothedVelocity * Time.deltaTime;
        newPos.y = grid.GridOrigin.y;

        newPos = ValidatePosition(oldPos, newPos);
        transform.position = newPos;

        if (GameDebug.Movement)
        {
            debugLogTimer -= Time.deltaTime;
            if (debugLogTimer <= 0f)
            {
                debugLogTimer = 2f;
                float distToWp = waypointIndex < waypoints.Count ? Vector3.Distance(transform.position, waypoints[waypointIndex]) : -1;
                float distToTarget = worldTarget.HasValue ? Vector3.Distance(transform.position, worldTarget.Value) : -1;
                Debug.Log($"[Move:{gameObject.name}] pos={transform.position:F1} wpIdx={waypointIndex}/{waypoints.Count} " +
                    $"distToWp={distToWp:F1} distToTarget={distToTarget:F1} " +
                    $"speed={smoothedVelocity.magnitude:F2}/{speed:F1} stall={stallTime:F1} " +
                    $"marching={isMarching} densStop={isDensityStopped}");
            }
        }

        ApplyRotation(newPos - oldPos);
        TrackProgress();
    }

    private void MoveDirectToward(Vector3 target)
    {
        Debug.Assert(unit != null && unit.Data != null, $"[UnitMovement] {gameObject.name} MoveDirectToward: unit or unit.Data is null", this);
        float speed = unit.Data.moveSpeed;
        Vector3 dir = target - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.01f)
        {
            ArriveAtDestination();
            return;
        }

        float dist = dir.magnitude;
        float threshold = unit != null ? Mathf.Max(waypointThreshold, unit.EffectiveRadius * 1.3f) : waypointThreshold;
        if (dist < threshold)
        {
            ArriveAtDestination();
            return;
        }

        Vector3 desiredVelocity = (dir / dist) * speed;
        var pfm = PathfindingManager.Instance;
        Debug.Assert(pfm != null, $"[UnitMovement] {gameObject.name} MoveDirectToward: PathfindingManager.Instance is null", this);
        bool isMarching = stateMachine == null || stateMachine.CurrentState == UnitState.Moving;
        Vector3 finalVelocity = pfm.ComputeSteering(unit, desiredVelocity, speed, isMarching);

        smoothedVelocity = SmoothDamp(smoothedVelocity, finalVelocity, 10f);
        smoothedVelocity = MovementLogic.PreventBackwardVelocity(smoothedVelocity, desiredVelocity);

        Vector3 oldPos = transform.position;
        Vector3 newPos = oldPos + smoothedVelocity * Time.deltaTime;
        newPos.y = grid.GridOrigin.y;
        newPos = ValidatePosition(oldPos, newPos);
        transform.position = newPos;

        ApplyRotation(newPos - oldPos);
        TrackProgress();
    }

    // ================================================================
    //  PATH REQUESTS
    // ================================================================

    [Server]
    public void SetDestinationWorld(Vector3 target)
    {
        Debug.Assert(grid != null, $"[UnitMovement] {gameObject.name} SetDestinationWorld: grid is null", this);
        {
            Vector2Int cell = grid.WorldToCell(target);
            if (!grid.IsInBounds(cell) || !grid.IsWalkable(cell))
            {
                var pfm = PathfindingManager.Instance;
                Debug.Assert(pfm != null, $"[UnitMovement] {gameObject.name} SetDestinationWorld: PathfindingManager.Instance is null", this);
                Vector3 corrected = pfm.FindNearestWalkable(target);
                Debug.Log($"[Move:{gameObject.name}] dest not walkable, corrected {target:F1} -> {corrected:F1}");
                target = corrected;
            }
        }

        if (worldTarget.HasValue && HasPath)
        {
            float threshold = grid.CellSize;
            if (MovementLogic.IsDuplicateDestination(target, worldTarget, threshold))
                return;
        }

        // Already within arrival range — arrive immediately without pathfinding.
        // Prevents arrive/reassign loops when combat keeps targeting a nearby building.
        float effRadius = unit != null ? unit.EffectiveRadius : 0f;
        if (MovementLogic.HasArrivedAtDestination(transform.position, target, effRadius, waypointThreshold))
        {
            worldTarget = target;
            ArriveAtDestination();
            return;
        }

        if (GameDebug.Movement)
            Debug.Log($"[Move:{gameObject.name}] SetDest pos={transform.position:F1} -> target={target:F1}");

        worldTarget = target;
        isStopped = false;
        stallTime = 0f;
        nearDestTimer = 0f;
        isDensityStopped = false;
        IsDestinationUnreachable = false;
        lastProgressPos = transform.position;
        smoothedVelocity = Vector3.zero;
        needsReplan = false;

        ComputePath();
    }

    [Server]
    public void ForceSetDestinationWorld(Vector3 target)
    {
        // Preserve nearDestTimer if the new target is close to the old one —
        // combat recalcs every 2s and shouldn't reset the "almost arrived" timer.
        float preservedNearDestTimer = 0f;
        if (worldTarget.HasValue)
        {
            float shift = Vector3.Distance(target, worldTarget.Value);
            if (shift < (grid != null ? grid.CellSize * 2f : 3f))
                preservedNearDestTimer = nearDestTimer;
        }

        if (GameDebug.Movement)
            Debug.Log($"[Move:{gameObject.name}] FORCE dest={target:F1}");
        worldTarget = null;
        waypoints = null;
        waypointIndex = 0;
        smoothedVelocity = Vector3.zero;
        SetDestinationWorld(target);

        nearDestTimer = preservedNearDestTimer;
    }

    [Server]
    public void SetDestinationToEnemyCastle()
    {
        Debug.Assert(unit != null, $"[UnitMovement] {gameObject.name} SetDestinationToEnemyCastle: unit is null", this);
        Debug.Assert(grid != null, $"[UnitMovement] {gameObject.name} SetDestinationToEnemyCastle: grid is null", this);

        Debug.Assert(TeamManager.Instance != null, $"[UnitMovement] {gameObject.name} SetDestinationToEnemyCastle: TeamManager.Instance is null", this);
        int enemyTeam = TeamManager.Instance.GetEnemyTeamId(unit.TeamId);

        if (!cachedEnemyCastles.TryGetValue(enemyTeam, out var castle) || castle == null)
        {
            castle = GameRegistry.GetEnemyCastle(unit.TeamId);
            if (castle != null)
                cachedEnemyCastles[enemyTeam] = castle;
        }

        if (castle == null)
        {
            Debug.LogError($"[UnitMovement] {gameObject.name} SetDestinationToEnemyCastle: no castle found for enemy team {enemyTeam}", this);
            return;
        }

        Vector3 castlePos = castle.transform.position;
        float spreadRadius = 2f;
        Vector3 offset = MovementLogic.ComputeCastleSpreadOffset(GetInstanceID(), spreadRadius);
        Vector3 spreadTarget = castlePos + offset;

        var pfm = PathfindingManager.Instance;
        Debug.Assert(pfm != null, $"[UnitMovement] {gameObject.name} SetDestinationToEnemyCastle: PathfindingManager.Instance is null", this);
        Vector3 target = pfm.FindNearestWalkable(spreadTarget);

        Vector2Int targetCell = grid.WorldToCell(target);
        if (!grid.IsWalkable(targetCell))
        {
            if (GameDebug.Movement)
                Debug.Log($"[Move:{gameObject.name}] spread target {target:F1} not walkable, using castle pos");
            target = pfm.FindNearestWalkable(castlePos);
        }

        Debug.Log($"[Move:{gameObject.name}] SetDestToEnemyCastle team={enemyTeam} target={target:F1}");
        strategicDestination = target;
        ForceSetDestinationWorld(target);
    }

    /// <summary>
    /// Compute a new path from current position to worldTarget using NavMesh A* + Funnel.
    /// </summary>
    private void ComputePath()
    {
        if (!worldTarget.HasValue) return;
        Debug.Assert(grid != null, $"[UnitMovement] {gameObject.name} ComputePath: grid is null", this);

        var pfm = PathfindingManager.Instance;
        if (pfm == null || !pfm.IsInitialized)
        {
            Debug.LogWarning($"[UnitMovement] {gameObject.name} ComputePath: PathfindingManager not ready (null={pfm == null}, init={pfm?.IsInitialized})");
            needsReplan = true;
            return;
        }

        float unitRadius = unit.EffectiveRadius;
        var path = pfm.RequestPath(transform.position, worldTarget.Value, unitRadius);

        // Large units may not fit through narrow passages. Retry with smaller
        // radius so they can still navigate (Boids handles the real-time avoidance).
        if (path == null && !pfm.LastRequestWasThrottled && unitRadius > 0.5f)
        {
            float reducedRadius = unitRadius * 0.5f;
            path = pfm.RequestPath(transform.position, worldTarget.Value, reducedRadius);
            if (path != null)
                Debug.Log($"[Move:{gameObject.name}] Path succeeded with reduced radius {reducedRadius:F2} (full radius {unitRadius:F2} failed)");
        }

        if (path != null && path.Count > 0)
        {
            waypoints = path;
            waypointIndex = 0;
            IsDestinationUnreachable = false;

            PathBounds = PathInvalidation.ComputePathBounds(waypoints, unitRadius);

            if (GameDebug.Movement)
            {
                float pathLen = 0f;
                for (int i = 1; i < path.Count; i++)
                    pathLen += Vector3.Distance(path[i - 1], path[i]);
                float directDist = Vector3.Distance(transform.position, worldTarget.Value);
                float ratio = directDist > 0.5f ? pathLen / directDist : 1f;
                Debug.Log($"[Move:{gameObject.name}] Path computed: {path.Count} waypoints ratio={ratio:F1}");
            }
        }
        else
        {
            if (pfm != null && pfm.LastRequestWasThrottled)
            {
                needsReplan = true;
                if (GameDebug.Movement)
                    Debug.Log($"[Move:{gameObject.name}] Path throttled, will retry next slot");
            }
            else
            {
                Debug.LogWarning($"[Move:{gameObject.name}] Path FAILED from {transform.position:F1} to {worldTarget.Value:F1} radius={unitRadius:F2} — destination unreachable");
                IsDestinationUnreachable = true;
                waypoints = null;
                waypointIndex = 0;
            }
        }
    }

    /// <summary>
    /// Called by PathfindingManager when the NavMesh changes (building placed/destroyed).
    /// Unit replans on its next stagger slot.
    /// </summary>
    public void FlagForReplan()
    {
        needsReplan = true;
    }

    /// <summary>
    /// Clears the current path so the unit stops immediately, but preserves
    /// the destination. Call FlagForReplan() later (after NavMesh rebuild)
    /// to resume pathfinding on the fresh mesh.
    /// Used when a building is placed on the unit's current path.
    /// </summary>
    public void InvalidatePath()
    {
        if (waypoints == null) return;

        if (GameDebug.Movement)
            Debug.Log($"[Move:{gameObject.name}] Path invalidated — stopping until NavMesh rebuild");

        waypoints = null;
        waypointIndex = 0;
        smoothedVelocity = Vector3.zero;
        isDensityStopped = false;
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

        // Stuck detection: if unit hasn't made progress in StuckThresholdTime.
        // Don't retry if already marked unreachable — wait for a NavMesh change to replan.
        if (stallTime > StuckThresholdTime && HasPath && !IsDestinationUnreachable)
        {
            stallTime = 0f;
            var pfm = PathfindingManager.Instance;
            if (pfm != null && pfm.TryConsumePathRequest())
            {
                if (GameDebug.Movement)
                    Debug.Log($"[Move:{gameObject.name}] Stuck replan (stallTime={stallTime:F1}s)");
                ComputePath();
            }
        }
    }

    // ================================================================
    //  CONTROL METHODS
    // ================================================================

    [Server]
    public void Stop()
    {
        if (GameDebug.Movement)
            Debug.Log($"[Move:{gameObject.name}] STOP at {transform.position:F1}" +
                $" (strategic={strategicDestination?.ToString("F1") ?? "none"})");
        waypoints = null;
        waypointIndex = 0;
        worldTarget = null;
        isStopped = true;
        smoothedVelocity = Vector3.zero;
        isDensityStopped = false;
        // strategicDestination is intentionally preserved — Resume() restores it

        if (grid != null)
        {
            Vector2Int cell = grid.WorldToCell(transform.position);
            if (!grid.IsInBounds(cell) || !grid.IsWalkable(cell))
            {
                Vector3 safe = grid.FindNearestWalkablePosition(transform.position, transform.position);
                walkableLerpTarget = safe;
                walkableLerpTimer = WalkableLerpDuration;
            }
        }
    }

    [Server]
    public void Resume()
    {
        isStopped = false;
        stallTime = 0f;
        nearDestTimer = 0f;
        isDensityStopped = false;
        IsDestinationUnreachable = false;

        if (!worldTarget.HasValue && strategicDestination.HasValue)
        {
            worldTarget = strategicDestination;
            if (GameDebug.Movement)
                Debug.Log($"[Move:{gameObject.name}] RESUME — restored strategic target={worldTarget.Value:F1}");
        }
        else if (GameDebug.Movement)
        {
            Debug.Log($"[Move:{gameObject.name}] RESUME target={worldTarget?.ToString("F1") ?? "none"}");
        }

        if (worldTarget.HasValue)
            ComputePath();
    }

    private void ArriveAtDestination()
    {
        if (GameDebug.Movement)
            Debug.Log($"[Move:{gameObject.name}] Arrived at {transform.position:F1}");
        arriveCount++;
        waypoints = null;
        waypointIndex = 0;
        worldTarget = null;
        strategicDestination = null;
        stallTime = 0f;
        nearDestTimer = 0f;
        smoothedVelocity = Vector3.zero;
        isDensityStopped = false;
        OnReachedDestination?.Invoke();
    }

    // ================================================================
    //  PUSH / YIELD (Anti-ghosting Layer 2 supplement)
    // ================================================================

    [Server]
    public void RequestYield(Vector3 requesterPosition, Vector3 requesterDirection)
    {
        if (yieldTimer > 0f) return;
        bool isIdle = stateMachine == null || stateMachine.CurrentState == UnitState.Idle;
        if (!isIdle || IsMoving) return;

        yieldDirection = MovementLogic.ComputeYieldDirection(transform.position, requesterPosition, requesterDirection);
        if (yieldDirection.sqrMagnitude < 0.01f) return;
        yieldTimer = MovementLogic.YieldDuration;
    }

    private void UpdateYield()
    {
        if (yieldTimer <= 0f) return;
        yieldTimer -= Time.deltaTime;

        Debug.Assert(unit != null && unit.Data != null, $"[UnitMovement] {gameObject.name} UpdateYield: unit or unit.Data is null", this);
        Debug.Assert(grid != null, $"[UnitMovement] {gameObject.name} UpdateYield: grid is null", this);
        float speed = unit.Data.moveSpeed;
        Vector3 move = yieldDirection * speed * 0.6f * Time.deltaTime;
        Vector3 newPos = transform.position + move;
        newPos.y = grid.GridOrigin.y;
        newPos = ValidatePosition(transform.position, newPos);
        transform.position = newPos;
    }

    // ================================================================
    //  STUCK DETECTION
    // ================================================================

    private void TrackProgress()
    {
        float moved = Vector3.Distance(transform.position, lastProgressPos);
        float effectiveRadius = unit != null ? unit.EffectiveRadius : 0.5f;

        float distToGoal = float.MaxValue;
        if (worldTarget.HasValue)
            distToGoal = Vector3.Distance(transform.position, worldTarget.Value);

        bool isStalled = moved < 0.1f * Time.deltaTime;

        if (isStalled)
        {
            stallTime += Time.deltaTime;

            // Tier 1 (0-1s): Continue trying current path, Boids resolves minor overlaps
            if (stallTime > 1f && stallTime <= 2f)
            {
                TryRequestYieldFromBlockers();
            }
            // Tier 2 (1-2s): Request replan via alternate route
            else if (stallTime > 2f && stallTime <= 3f)
            {
                if (!needsReplan)
                {
                    needsReplan = true;
                    if (GameDebug.Movement)
                        Debug.Log($"[Move:{gameObject.name}] Tier2 stuck ({stallTime:F1}s) — requesting replan");
                }
                TryRequestYieldFromBlockers();
            }
            // Tier 3 (2s+): Near dest = arrive, far = mark unreachable (or replan if combat)
            else if (stallTime > 3f && worldTarget.HasValue)
            {
                float distToDest = Vector3.Distance(transform.position, worldTarget.Value);
                float nearThreshold = unit != null ? unit.EffectiveRadius * 3f : 3f;

                if (distToDest < nearThreshold)
                {
                    if (GameDebug.Movement)
                        Debug.Log($"[Move:{gameObject.name}] Tier3 stuck near dest ({stallTime:F1}s, dist={distToDest:F1}) — arriving");
                    ArriveAtDestination();
                    return;
                }

                bool inCombat = unit != null && unit.Combat != null && unit.Combat.AttackTarget != null;
                if (inCombat)
                {
                    if (GameDebug.Movement)
                        Debug.Log($"[Move:{gameObject.name}] Tier3 stuck far COMBAT ({stallTime:F1}s, dist={distToDest:F1}) — replanning (SC2: never mark unreachable in combat)");
                    needsReplan = true;
                    stallTime = 0f;
                    return;
                }
                else
                {
                    if (GameDebug.Movement)
                        Debug.Log($"[Move:{gameObject.name}] Tier3 stuck far ({stallTime:F1}s, dist={distToDest:F1}) — marking unreachable");
                    IsDestinationUnreachable = true;
                    waypoints = null;
                    waypointIndex = 0;
                    worldTarget = null;
                    stallTime = 0f;
                    smoothedVelocity = Vector3.zero;
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
            float nearThreshold = Mathf.Max(1.5f, effectiveRadius * 1.5f);
            if (distToGoal < nearThreshold)
            {
                nearDestTimer += Time.deltaTime;
                if (nearDestTimer > NearDestArriveTime)
                {
                    if (GameDebug.Movement)
                        Debug.Log($"[Move:{gameObject.name}] Near-dest timer fired: dist={distToGoal:F1} radius={effectiveRadius:F1}");
                    ArriveAtDestination();
                    return;
                }
            }
            else
            {
                nearDestTimer = 0f;
            }
        }
        else
        {
            nearDestTimer = 0f;
        }
    }

    private void TryRequestYieldFromBlockers()
    {
        if (UnitManager.Instance == null || !worldTarget.HasValue) return;
        float myRadius = unit != null ? unit.EffectiveRadius : 0.5f;

        Vector3 moveDir = worldTarget.Value - transform.position;
        moveDir.y = 0;
        if (moveDir.sqrMagnitude < 0.01f) return;
        moveDir.Normalize();

        var nearby = UnitManager.Instance.GetUnitsInRadius(transform.position, myRadius * 4f);
        foreach (var other in nearby)
        {
            if (other == null || other == unit || other.IsDead) continue;
            if (other.TeamId != unit.TeamId) continue;

            var otherSm = other.StateMachine;
            if (otherSm != null && otherSm.CurrentState != UnitState.Idle) continue;

            Vector3 toOther = other.transform.position - transform.position;
            toOther.y = 0;
            if (Vector3.Dot(toOther.normalized, moveDir) < 0.3f) continue;

            float dist = toOther.magnitude;
            float combinedRadius = myRadius + other.EffectiveRadius;
            if (dist > combinedRadius * 2f) continue;

            other.Movement?.RequestYield(transform.position, moveDir);
        }
    }

    // ================================================================
    //  OVERLAP RESOLUTION (runs even when stopped/density-stopped)
    // ================================================================

    private void ApplySeparationPush()
    {
        var pfm = PathfindingManager.Instance;
        if (pfm == null) return;

        Vector3 push = pfm.ComputeSeparationPush(unit, Time.deltaTime);
        if (push.sqrMagnitude < 1e-6f) return;

        Vector3 newPos = transform.position + push;
        newPos.y = grid.GridOrigin.y;
        newPos = ValidatePosition(transform.position, newPos);
        transform.position = newPos;
    }

    // ================================================================
    //  WALL COLLISION (terrain boundary — NOT Boids)
    // ================================================================

    private Vector3 ValidatePosition(Vector3 oldPos, Vector3 newPos)
    {
        Vector2Int newCell = grid.WorldToCell(newPos);
        if (grid.IsInBounds(newCell) && grid.IsWalkable(newCell))
            return newPos;

        Vector3 slideX = new Vector3(newPos.x, newPos.y, oldPos.z);
        Vector2Int cellX = grid.WorldToCell(slideX);
        bool xOk = grid.IsInBounds(cellX) && grid.IsWalkable(cellX);

        Vector3 slideZ = new Vector3(oldPos.x, newPos.y, newPos.z);
        Vector2Int cellZ = grid.WorldToCell(slideZ);
        bool zOk = grid.IsInBounds(cellZ) && grid.IsWalkable(cellZ);

        if (xOk && zOk)
        {
            // Use actual movement delta for slide choice, not lagging smoothedVelocity
            Vector3 delta = newPos - oldPos;
            return Mathf.Abs(delta.x) >= Mathf.Abs(delta.z) ? slideX : slideZ;
        }
        if (xOk) return slideX;
        if (zOk) return slideZ;

        // Concave corner: both axis-aligned slides are unwalkable.
        // Push toward the nearest walkable neighbor cell instead of freezing.
        Vector2Int oldCell = grid.WorldToCell(oldPos);
        Vector3 pushDir = Vector3.zero;
        float bestDistSq = float.MaxValue;
        Vector3 bestWalkable = oldPos;
        bool foundWalkable = false;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                Vector2Int neighbor = new Vector2Int(oldCell.x + dx, oldCell.y + dz);
                if (!grid.IsInBounds(neighbor) || !grid.IsWalkable(neighbor)) continue;

                Vector3 neighborWorld = grid.CellToWorld(neighbor);
                float distSq = (neighborWorld - oldPos).sqrMagnitude;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    bestWalkable = neighborWorld;
                    foundWalkable = true;
                }
            }
        }

        if (foundWalkable)
        {
            pushDir = (bestWalkable - oldPos).normalized;
            float moveMag = (newPos - oldPos).magnitude;
            Vector3 pushed = oldPos + pushDir * moveMag;
            Vector2Int pushedCell = grid.WorldToCell(pushed);
            if (grid.IsInBounds(pushedCell) && grid.IsWalkable(pushedCell))
                return pushed;
            // Fractional step toward walkable cell center
            return Vector3.MoveTowards(oldPos, bestWalkable, moveMag);
        }

        return oldPos;
    }

    // ================================================================
    //  UTILITIES
    // ================================================================

    private void ApplyRotation(Vector3 moveDelta)
    {
        Vector3 dir = MovementLogic.GetRotationDirection(moveDelta, smoothedVelocity);
        if (dir.sqrMagnitude > 0.001f)
        {
            Quaternion targetRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
    }

    private static Vector3 SmoothDamp(Vector3 current, Vector3 target, float rate)
    {
        return MovementLogic.SmoothDamp(current, target, rate, Time.deltaTime);
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
