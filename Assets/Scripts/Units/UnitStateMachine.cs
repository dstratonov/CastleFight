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
    [SyncVar] private UnitState currentState = UnitState.Idle;

    private Unit unit;
    private GridMovement movement;
    private UnitCombat combat;
    private Health health;

    public UnitState CurrentState => currentState;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        movement = GetComponent<GridMovement>();
        combat = GetComponent<UnitCombat>();
        health = GetComponent<Health>();
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
    }

    private void OnDeath(GameObject killer)
    {
        if (isServer)
            SetState(UnitState.Dying);
    }
}
