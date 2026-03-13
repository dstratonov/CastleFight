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
    private Health health;
    private UnitAnimator unitAnimator;

    public UnitState CurrentState => currentState;
    public UnitAnimator Animator => unitAnimator;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        movement = GetComponent<UnitMovement>();
        health = GetComponent<Health>();

        DisableRootMotionEarly();
    }

    private void DisableRootMotionEarly()
    {
        var animator = GetComponentInChildren<Animator>();
        if (animator != null)
            animator.applyRootMotion = false;
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
        if (!isServer) return;
        if (currentState == UnitState.Dying) return;
        UpdateState();
    }

    [Server]
    private void UpdateState()
    {
        if (unit.IsDead)
        {
            SetState(UnitState.Dying);
            return;
        }

        if (currentState == UnitState.Fighting)
            return;

        if (movement != null && (movement.IsMoving || movement.IsWaitingForPath))
        {
            SetState(UnitState.Moving);
        }
        else
        {
            SetState(UnitState.Idle);
        }
    }

    [Server]
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

        switch (state)
        {
            case UnitState.Idle:
                unitAnimator.CancelOneShot();
                unitAnimator.PlayIdle();
                break;
            case UnitState.Fighting:
                unitAnimator.PlayIdle();
                break;
            case UnitState.Moving:
                unitAnimator.CancelOneShot();
                unitAnimator.PlayWalk();
                break;
            case UnitState.Dying:
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
    [Server]
    public void TriggerAttackAnimation(float attackCooldown)
    {
        if (unitAnimator == null || currentState == UnitState.Dying) return;
        if (GameDebug.Animation)
            Debug.Log($"[State:{gameObject.name}] TriggerAttack cooldown={attackCooldown:F2}s state={currentState} oneShot={unitAnimator.IsPlayingOneShot}");
        unitAnimator.PlayAttack(attackCooldown);
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
