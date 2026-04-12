using UnityEngine;
using Mirror;
using Pathfinding;
using Pathfinding.RVO;

/// <summary>
/// Unit movement using A* Pathfinding Project Pro.
/// Delegates pathfinding to RichAI + Seeker, local avoidance to RVOController.
/// Server-authoritative: AI components only run on the server.
/// </summary>
public class UnitMovement : NetworkBehaviour
{
    [SerializeField] private float rotationSpeed = 10f;

    private Unit unit;
    private UnitStateMachine stateMachine;
    private IAstarAI ai;
    private RVOController rvo;
    private Seeker seeker;

    private bool isStopped;

    // Strategic destination persists through combat stops.
    // Resume() restores it so the unit continues marching after combat.
    private Vector3? strategicDestination;

    // Arrival tracking
    private int arriveCount;
    private bool wasAtDestination;

    // Previous position for velocity inference
    private Vector3 previousPosition;
    public Vector3 PreviousPosition => previousPosition;

    // Public state
    public bool IsMoving => !isStopped && ai != null && ai.hasPath && !ai.reachedDestination;
    public bool HasPath => ai != null && ai.hasPath;
    public bool IsDestinationUnreachable { get; private set; }
    public Vector3? WorldTarget { get; private set; }
    public int ArriveCount => arriveCount;
    public bool IsWaitingForPath => ai != null && ai.pathPending;
    public bool HasStrategicDestination => strategicDestination.HasValue;
    public Vector3? StrategicDestination => strategicDestination;

    /// <summary>
    /// True when this unit has been hard-stopped via <see cref="Stop"/> or
    /// <see cref="ArriveAtDestination"/>. A hard-stopped unit will not move
    /// until <see cref="SetDestinationWorld"/>, <see cref="Resume"/>, or the
    /// auto-resume path in <see cref="Update"/> wakes it up. Combat reads
    /// this so it can force a destination refresh on units that were hard-
    /// stopped at a castle approach and need to re-engage.
    /// </summary>
    public bool IsHardStopped => isStopped;

    public event System.Action OnReachedDestination;

    private static readonly System.Collections.Generic.Dictionary<int, Castle> cachedEnemyCastles = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetMovementStatics()
    {
        cachedEnemyCastles.Clear();
    }

    private void Awake()
    {
        unit = GetComponent<Unit>();
        stateMachine = GetComponent<UnitStateMachine>();
    }

    public override void OnStartServer()
    {
        previousPosition = transform.position;
        EnsureAIComponents();

        if (ai != null)
        {
            ai.isStopped = true;
            ai.canSearch = true;
        }
    }

    public override void OnStartClient()
    {
        if (!isServer)
        {
            // Disable AI components on client — server drives movement
            var richAI = GetComponent<RichAI>();
            if (richAI != null) richAI.enabled = false;
            var seekerComp = GetComponent<Seeker>();
            if (seekerComp != null) seekerComp.enabled = false;
            var rvoComp = GetComponent<RVOController>();
            if (rvoComp != null) rvoComp.enabled = false;
        }
    }

    private void EnsureAIComponents()
    {
        seeker = GetComponent<Seeker>();
        if (seeker == null) seeker = gameObject.AddComponent<Seeker>();

        var richAI = GetComponent<RichAI>();
        if (richAI == null) richAI = gameObject.AddComponent<RichAI>();
        ai = richAI;

        rvo = GetComponent<RVOController>();
        if (rvo == null) rvo = gameObject.AddComponent<RVOController>();

        // Configure from UnitData.
        //
        // Unified radius model: rvo.radius == ai.radius == visual body
        // radius (Unit.EffectiveRadius). There is no way to decouple the
        // two — A* Pro's RVOController.radius is just a proxy for
        // ai.radius when an IAstarAI component is attached (see the
        // RVOController.radius getter/setter, lines 70–79). Any attempt
        // to set rvo.radius to a different value than ai.radius is
        // silently discarded by the next frame's proxy read.
        //
        // The consequence: big units (troll, dragon, hydra) plan paths
        // as if they are their full visual size, so they may refuse to
        // enter gaps narrower than 2*body-radius in dense maps. We accept
        // this trade-off because the alternative (clamping the shared
        // radius to 2.0 for pathfinding) caused RVO to think big units
        // were smaller than they looked, which let them interpenetrate
        // smaller units and produced visible overlap during combat.
        //
        // Floor at 0.3 so extremely tiny units (bees, sprites) still
        // have a usable avoidance radius and don't tunnel through walls
        // at high speed.
        if (unit != null && unit.Data != null)
        {
            ai.maxSpeed = unit.Data.moveSpeed;
            float dataRadius = unit.Data.unitRadius > 0 ? unit.Data.unitRadius : 0.5f;
            rvo.radius = Mathf.Max(0.3f, dataRadius);
        }
        else
        {
            ai.maxSpeed = 3.5f;
            rvo.radius = 0.3f;
        }

        // Crowd-friendly RVO tuning.
        //
        // lockWhenNotMoving: DISABLED. We delegate all "stop when crowded"
        // logic to A* Pro's built-in RVODestinationCrowdedBehavior (on
        // AIBase.rvoDensityBehavior) — the SC2-inspired density module
        // that automatically halts agents when the area around their
        // destination is > 60% packed. lockWhenNotMoving would fight it
        // by hard-locking stationary agents so new arrivals couldn't
        // push past, and we'd lose the smooth crowd jostle.
        rvo.lockWhenNotMoving = false;

        // More neighbours considered per agent — the default 10 is too few
        // for dense RTS crowds at chokes; agents pop through each other
        // when the quadtree query's nearest-N cutoff excludes relevant
        // nearby agents. 50 handles 20-30 agent clusters comfortably.
        rvo.maxNeighbours = 50;

        // Disable A* Pro's rvoDensityBehavior entirely.
        //
        // That module is SC2-style crowd stopping: when the area around an
        // agent's destination is packed above a threshold, it halts the
        // agent at its current position and drops its priority to 0.1.
        // It also contains a hardcoded 3-second wait in its "try to return
        // after being pushed" path (`if (timer1 > 3 && ...)` in
        // RVODestinationCrowdedBehavior.Update). That 3-second constant is
        // a magic number with no corresponding design rationale in our
        // combat logic — and any system that relies on it inherits the
        // magic.
        //
        // Our combat uses a CLEAN alternative: capacity-limited commitment.
        // When UnitCombat.Scan finds a target, it counts existing attackers
        // and refuses to commit if the target is already at its hexagonal
        // kissing max. Excess units keep walking to their strategic
        // destination (the enemy castle) without needing density to halt
        // them short. Once committed, attackers walk freely to the target
        // and lock in place via LockForAttack when they reach attack range.
        // Physical packing at the ring is handled by hardCollisions, not
        // by density.
        richAI.rvoDensityBehavior.enabled = false;

        // Movement plane: standard 3D (Y-up, move on XZ)
        richAI.orientation = Pathfinding.OrientationMode.ZAxisForward;
        richAI.gravity = new Vector3(0, -9.81f, 0);
        richAI.groundMask = LayerMask.GetMask("Ground");
        richAI.enableRotation = true;
        richAI.rotationSpeed = rotationSpeed * 60f; // RichAI uses degrees/sec
        richAI.updatePosition = true;
        richAI.updateRotation = true;
    }

    /// <summary>
    /// Configure AI speed and radius after UnitData is available.
    /// Called from Unit.Initialize(). Sets the single unified radius
    /// used for both pathfinding and RVO collision — see the big
    /// explanation in <see cref="EnsureAIComponents"/> for why these
    /// cannot be decoupled.
    /// </summary>
    public void ConfigureFromData(UnitData data)
    {
        if (ai != null)
        {
            ai.maxSpeed = data.moveSpeed;
        }
        if (rvo != null)
        {
            float raw = data.unitRadius > 0 ? data.unitRadius : 0.3f;
            rvo.radius = Mathf.Max(0.3f, raw);
        }
    }

    private void LateUpdate()
    {
        // Capture position AFTER A* Pro has moved the unit this frame.
        // DebugOverlay uses (currentPos - PreviousPosition) for velocity arrows.
        if (isServer)
            previousPosition = transform.position;
    }

    private void Update()
    {
        if (!isServer || !NetworkServer.active || unit == null || unit.IsDead)
            return;

        if (ai == null)
            return;

        // Auto-resume: if we were stopped (combat or prior arrival) but the unit
        // has a strategic destination and NO combat target at all, restart the march.
        // Use CurrentTarget (any lock) rather than HasTarget (hard only) so big units
        // engaged with a soft-lock castle don't oscillate between Resume and Stop.
        if (isStopped && !WorldTarget.HasValue && strategicDestination.HasValue)
        {
            bool hasAnyTarget = unit.Combat != null && unit.Combat.CurrentTarget != null
                                && unit.Combat.CurrentTarget.Health != null
                                && !unit.Combat.CurrentTarget.Health.IsDead;
            if (!hasAnyTarget)
            {
                Resume();
            }
        }

        if (isStopped)
            return;

        // Check arrival
        if (ai.reachedDestination && !wasAtDestination && WorldTarget.HasValue)
        {
            ArriveAtDestination();
            return;
        }
        wasAtDestination = ai.reachedDestination;

        // Periodic network sync
        if ((Time.frameCount + (GetInstanceID() & 0xFF)) % 10 == 0)
            RpcSyncPositionAndRotation(transform.position, transform.rotation);
    }

    // ================================================================
    //  PATH REQUESTS
    // ================================================================

    [Server]
    public void SetDestinationWorld(Vector3 target)
    {
        if (ai == null) return;

        // Wait for A* Pro graph to be available
        if (AstarPath.active == null || AstarPath.active.data.graphs == null || AstarPath.active.data.graphs.Length == 0)
            return;

        // Skip if already heading to same destination
        if (WorldTarget.HasValue && Vector3.Distance(target, WorldTarget.Value) < 0.5f)
            return;

        // Already at destination — use a small fixed threshold so big units
        // (dragons, hydras) don't spuriously report arrival from far away
        // just because their visual body is huge.
        if (Vector3.Distance(transform.position, target) < 0.5f)
        {
            WorldTarget = target;
            ArriveAtDestination();
            return;
        }

        WorldTarget = target;
        isStopped = false;
        IsDestinationUnreachable = false;
        wasAtDestination = false;

        ai.isStopped = false;
        ai.destination = target;
        // Hand rotation back to RichAI so it faces the next waypoint along
        // the path (this was disabled by Stop() while attacking).
        ai.updateRotation = true;

        // Unlock RVO — we're moving again. Without this a unit that fought,
        // its target died, and got a new destination would stay hard-locked.
        if (rvo != null)
        {
            rvo.locked = false;
            rvo.priority = 0.5f;
        }
    }

    [Server]
    public void ForceSetDestinationWorld(Vector3 target)
    {
        // Clear previous target so SetDestinationWorld doesn't skip as duplicate
        WorldTarget = null;
        SetDestinationWorld(target);
    }

    [Server]
    public void SetDestinationToEnemyCastle()
    {
        if (unit == null) return;

        int enemyTeam = TeamManager.Instance.GetEnemyTeamId(unit.TeamId);

        if (!cachedEnemyCastles.TryGetValue(enemyTeam, out var castle) || castle == null)
        {
            castle = GameRegistry.GetEnemyCastle(unit.TeamId);
            if (castle != null)
                cachedEnemyCastles[enemyTeam] = castle;
        }

        if (castle == null) return;

        // Just aim at the castle. A* routes to the nearest walkable
        // point, RVO prevents overlap, combat attacks when in range.
        Vector3 target = castle.transform.position;

        strategicDestination = target;
        ForceSetDestinationWorld(target);
    }

    // ================================================================
    //  CONTROL METHODS
    // ================================================================

    /// <summary>
    /// Hard-lock the RVO agent in place during an attack swing. While
    /// locked, the agent is an immovable RVO obstacle — other agents must
    /// path around it. This is the cornerstone of our stable combat ring:
    /// once an attacker reaches attack range it locks, and subsequent
    /// arrivals physically cannot push it out. They either density-stop
    /// behind the locked ring and overflow to the strategic destination,
    /// or find an empty slot on the ring and lock themselves in.
    ///
    /// Hexagonal kissing around a small target fits exactly 6 attackers;
    /// a 7th would require non-zero body overlap. Hard-locking guarantees
    /// that the 6 in the ring don't drift away from their kissing positions
    /// under incoming crowd pressure, so the ring stays stable.
    ///
    /// Does not touch <see cref="isStopped"/> or <see cref="IAstarAI.isStopped"/>
    /// — the AI layer still holds a destination and can resume normal
    /// movement the instant we unlock (Unlock → SetDestinationWorld works
    /// immediately, no auto-resume path needed).
    ///
    /// Rotation is handed off to combat (<see cref="UnitCombat"/> drives
    /// <c>transform.rotation</c> via FaceTarget/SnapFaceTarget while
    /// <c>ai.updateRotation</c> is false), so the unit faces its target
    /// instead of whatever waypoint was next on its path.
    /// </summary>
    [Server]
    public void LockForAttack()
    {
        if (rvo != null)
        {
            rvo.locked = true;
            rvo.priority = 1.0f;
        }
        if (ai != null)
            ai.updateRotation = false;
    }

    /// <summary>
    /// Release the soft hold set by <see cref="LockForAttack"/>. Called
    /// when the unit leaves attack range, changes target, or loses its
    /// target (leash, death, overflow). Restores default RVO priority and
    /// returns rotation control to RichAI so the unit faces its next
    /// waypoint while walking.
    /// </summary>
    [Server]
    public void UnlockAfterAttack()
    {
        if (rvo != null)
        {
            rvo.locked = false;
            rvo.priority = 0.5f;
        }
        if (ai != null)
            ai.updateRotation = true;
    }

    [Server]
    public void Stop()
    {
        // Hard-stop — used for deliberate "hold this position as a rigid
        // obstacle" commands: GameOver freeze, scenario-pinned dummy
        // targets, explicit player hold orders. Combat positioning does
        // NOT call this; combat relies on A* Pro's rvoDensityBehavior to
        // stop agents when the area around their destination is crowded.
        // So locking the RVO agent here only affects units that truly
        // need to be unmovable, not arriving attackers in a melee crowd.
        WorldTarget = null;
        isStopped = true;
        if (ai != null)
        {
            ai.isStopped = true;
            ai.updateRotation = false;
        }
        if (rvo != null)
        {
            // Hard-lock: this agent cannot be pushed by other agents.
            // Required for S7's pinned dummy target to stay at origin
            // while attackers jostle around it.
            rvo.locked = true;
            rvo.priority = 1.0f;
        }
    }

    [Server]
    public void Resume()
    {
        isStopped = false;
        IsDestinationUnreachable = false;
        wasAtDestination = false;

        if (!WorldTarget.HasValue && strategicDestination.HasValue)
            WorldTarget = strategicDestination;

        if (WorldTarget.HasValue && ai != null)
        {
            ai.isStopped = false;
            ai.destination = WorldTarget.Value;
            ai.updateRotation = true;
        }

        // Unlock RVO and drop back to default priority while on the march
        if (rvo != null)
        {
            rvo.locked = false;
            rvo.priority = 0.5f;
        }
    }

    private void ArriveAtDestination()
    {
        arriveCount++;

        // Only clear strategicDestination if we actually arrived there.
        // Combat stop/resume cycles set intermediate WorldTargets (attack positions);
        // clearing the strategic destination on those arrivals would make the unit
        // forget where it was marching after combat ends.
        if (strategicDestination.HasValue && WorldTarget.HasValue)
        {
            float dist = Vector3.Distance(WorldTarget.Value, strategicDestination.Value);
            float threshold = unit != null ? unit.EffectiveRadius + 1f : 1.5f;
            if (dist < threshold)
                strategicDestination = null;
        }

        WorldTarget = null;
        isStopped = true;
        if (ai != null)
            ai.isStopped = true;
        OnReachedDestination?.Invoke();
    }

    public void FlagForReplan()
    {
        // A* Pro handles replanning automatically via graph updates and auto-repath.
        // Just ensure destination is set — RichAI will repath on its own.
    }

    public void InvalidatePath()
    {
        if (ai != null)
            ai.SetPath(null);
    }

    // ================================================================
    //  NETWORK SYNC
    // ================================================================

    [ClientRpc]
    private void RpcSyncPositionAndRotation(Vector3 pos, Quaternion rot)
    {
        if (isServer) return;
        // Both use frame-rate-independent interpolation with clamped alpha
        float dt = Time.deltaTime;
        transform.position = Vector3.Lerp(transform.position, pos, Mathf.Clamp01(10f * dt));
        transform.rotation = Quaternion.Slerp(transform.rotation, rot, Mathf.Clamp01(15f * dt));
    }
}
