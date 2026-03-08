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
    private bool animatorNeedsRetry;

    public UnitState CurrentState => currentState;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        movement = GetComponent<UnitMovement>();
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
        if (unitAnimator != null && !animatorNeedsRetry) return;

        var animator = GetComponentInChildren<Animator>();
        if (animator == null) return;

        if (unitAnimator == null)
            unitAnimator = gameObject.AddComponent<UnitAnimator>();

        unitAnimator.Initialize(animator);
        animatorNeedsRetry = !unitAnimator.HasIdleAnim || !unitAnimator.HasWalkAnim;
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
        if (animatorNeedsRetry)
        {
            SetupAnimator();
            if (!animatorNeedsRetry)
                ApplyAnimation(currentState);
        }

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
        currentState = newState;
        ApplyAnimation(newState);
    }

    private void OnStateChanged(UnitState oldState, UnitState newState)
    {
        ApplyAnimation(newState);
    }

    private void ApplyAnimation(UnitState state)
    {
        if (unitAnimator == null || !unitAnimator.HasAnimator) return;

        switch (state)
        {
            case UnitState.Idle:
            case UnitState.Fighting:
                unitAnimator.PlayIdle();
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
