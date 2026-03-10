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
        if (animator == null) return;

        unitAnimator = gameObject.AddComponent<UnitAnimator>();
        unitAnimator.Initialize(animator);
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

        if (movement != null && movement.IsMoving)
        {
            SetState(UnitState.Moving);
        }
        else if (currentState != UnitState.Fighting)
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
                unitAnimator.HoldPose();
                break;
            case UnitState.Moving:
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
            unitAnimator.PlayHit();
    }

    private void OnDeath(GameObject killer)
    {
        if (isServer)
            SetState(UnitState.Dying);
    }
}
