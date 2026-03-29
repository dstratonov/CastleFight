using UnityEngine;
using Mirror;

public enum UnitState
{
    Idle,
    Moving,
    Fighting,
    Dying
}

public class UnitStateMachine : NetworkBehaviour
{
    [SyncVar(hook = nameof(OnStateChanged))]
    private UnitState currentState = UnitState.Idle;

    private Unit unit;
    private UnitMovement movement;
    private UnitCombat combat;
    private Health health;
    private UnitAnimator unitAnimator;

    public UnitState CurrentState => currentState;
    public UnitAnimator Animator => unitAnimator;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        movement = GetComponent<UnitMovement>();
        combat = GetComponent<UnitCombat>();
        health = GetComponent<Health>();
    }

    public override void OnStartClient()
    {
        SetupAnimator();
        ApplyAnimation(currentState);
    }

    public override void OnStartServer()
    {
        SetupAnimator();
        ApplyAnimation(currentState);
    }

    private void SetupAnimator()
    {
        if (unitAnimator != null) return;

        var animator = GetComponentInChildren<Animator>();
        if (animator == null)
        {
            if (GameDebug.Animation)
                Debug.LogWarning($"[State:{gameObject.name}] No Animator found in children — animations disabled");
            return;
        }

        unitAnimator = gameObject.AddComponent<UnitAnimator>();
        unitAnimator.Initialize(animator);

        if (unit != null && unit.Data != null)
            unitAnimator.SetMoveSpeed(unit.Data.moveSpeed);

        if (GameDebug.Animation)
            Debug.Log($"[State:{gameObject.name}] Animator setup complete: hasAnim={unitAnimator.HasAnimator} " +
                $"idle={unitAnimator.HasIdleAnim} walk={unitAnimator.HasWalkAnim} atk={unitAnimator.HasAttackAnim} death={unitAnimator.HasDeathAnim}");
    }

    private void OnEnable()
    {
        if (health != null)
        {
            health.OnDeath += OnDeath;
            health.OnDamaged += OnDamaged;
        }
    }

    private void OnDisable()
    {
        if (health != null)
        {
            health.OnDeath -= OnDeath;
            health.OnDamaged -= OnDamaged;
        }
    }

    private void Update()
    {
        if (!isServer || !NetworkServer.active) return;
        if (currentState == UnitState.Dying) return;
        UpdateState();
    }

    private void UpdateState()
    {
        bool isMoving = movement != null && movement.IsMoving;
        bool isWaiting = movement != null && movement.IsWaitingForPath;
        bool hasTarget = combat != null && combat.HasTarget;
        var next = UnitStateLogic.ComputeNextState(currentState, unit.IsDead, isMoving, isWaiting, hasTarget);
        if (UnitStateLogic.ShouldTransition(currentState, next))
            SetState(next);
    }

    /// <summary>Transitions the state machine and applies the matching animation.</summary>
    public void SetState(UnitState newState)
    {
        if (currentState == newState) return;
        var oldState = currentState;
        if (GameDebug.StateMachine)
            Debug.Log($"[State:{gameObject.name} t{unit?.TeamId}] {oldState} -> {newState}" +
                (unitAnimator != null ? $" oneShot={unitAnimator.IsPlayingOneShot}" : ""));
        currentState = newState;
        ApplyAnimation(newState);
    }

    private void OnStateChanged(UnitState oldState, UnitState newState)
    {
        if (!isServer)
            ApplyAnimation(newState);
    }

    private void ApplyAnimation(UnitState state)
    {
        if (unitAnimator == null || !unitAnimator.HasAnimator) return;

        if (UnitStateLogic.ShouldCancelOneShot(state))
            unitAnimator.CancelOneShot();

        var action = UnitStateLogic.GetAnimationForState(state);
        switch (action)
        {
            case AnimAction.Idle:
            case AnimAction.IdleReady:
                unitAnimator.PlayIdle();
                break;
            case AnimAction.Walk:
                unitAnimator.PlayWalk();
                break;
            case AnimAction.Death:
                unitAnimator.PlayDeath();
                break;
        }
    }

    private void OnDamaged(float amount, GameObject attacker)
    {
        if (unitAnimator != null && currentState != UnitState.Dying)
        {
            if (GameDebug.Animation)
                Debug.Log($"[State:{gameObject.name}] OnDamaged {amount:F1} from {attacker?.name ?? "null"}, playing hit (state={currentState} oneShot={unitAnimator.IsPlayingOneShot})");
            unitAnimator.PlayHit();
            if (isServer)
                RpcPlayHit();
        }
    }

    /// <summary>Triggers the attack one-shot animation on server and syncs to clients.</summary>
    public void TriggerAttackAnimation(float attackCooldown)
    {
        if (unitAnimator == null || currentState == UnitState.Dying) return;
        if (GameDebug.Animation)
            Debug.Log($"[State:{gameObject.name}] TriggerAttack cooldown={attackCooldown:F2}s state={currentState} oneShot={unitAnimator.IsPlayingOneShot}");
        unitAnimator.PlayAttack(attackCooldown);
        if (NetworkServer.active)
            RpcPlayAttack(attackCooldown);
    }

    [ClientRpc]
    private void RpcPlayAttack(float attackCooldown)
    {
        if (isServer) return;
        if (unitAnimator != null && currentState != UnitState.Dying)
            unitAnimator.PlayAttack(attackCooldown);
    }

    [ClientRpc]
    private void RpcPlayHit()
    {
        if (isServer) return;
        if (unitAnimator != null && currentState != UnitState.Dying)
            unitAnimator.PlayHit();
    }

    private void OnDeath(GameObject killer)
    {
        if (GameDebug.StateMachine)
            Debug.Log($"[State:{gameObject.name}] OnDeath killer={killer?.name ?? "null"} isServer={isServer}");
        if (isServer)
            SetState(UnitState.Dying);
    }
}
