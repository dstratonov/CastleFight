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
    private GridMovement movement;
    private Health health;
    private UnitAnimator unitAnimator;

    public UnitState CurrentState => currentState;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        movement = GetComponent<GridMovement>();
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
        if (animator != null)
        {
            unitAnimator = gameObject.AddComponent<UnitAnimator>();
            unitAnimator.Initialize(animator);
        }
    }

    private void OnEnable()
    {
        if (health != null)
            health.OnDeath += OnDeath;
    }

    private void OnDisable()
    {
        if (health != null)
            health.OnDeath -= OnDeath;
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
        else
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
                unitAnimator.PlayIdle();
                break;
            case UnitState.Moving:
                unitAnimator.PlayWalk();
                break;
            case UnitState.Fighting:
                unitAnimator.PlayAttack();
                break;
            case UnitState.Dying:
                unitAnimator.PlayDeath();
                break;
        }
    }

    private void OnDeath(GameObject killer)
    {
        if (isServer)
            SetState(UnitState.Dying);
    }
}
