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
    private UnitCombat combat;
    private Health health;
    private Animator animator;

    private static readonly int AnimIdle = Animator.StringToHash("Idle");
    private static readonly int AnimWalk = Animator.StringToHash("Walk");
    private static readonly int AnimAttack = Animator.StringToHash("Attack");
    private static readonly int AnimDeath = Animator.StringToHash("Death");
    private static readonly int AnimSpeed = Animator.StringToHash("Speed");

    public UnitState CurrentState => currentState;

    private void Awake()
    {
        unit = GetComponent<Unit>();
        movement = GetComponent<GridMovement>();
        combat = GetComponent<UnitCombat>();
        health = GetComponent<Health>();
        animator = GetComponentInChildren<Animator>();
    }

    public override void OnStartClient()
    {
        if (animator != null)
        {
            animator.applyRootMotion = false;
            SetAnimatorState(currentState);
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
    }

    private void OnStateChanged(UnitState oldState, UnitState newState)
    {
        SetAnimatorState(newState);
    }

    private void SetAnimatorState(UnitState state)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return;

        switch (state)
        {
            case UnitState.Idle:
                TrySetTrigger("Idle");
                TrySetFloat("Speed", 0f);
                break;
            case UnitState.Moving:
                TrySetTrigger("Walk");
                TrySetFloat("Speed", 1f);
                break;
            case UnitState.Fighting:
                TrySetTrigger("Attack");
                break;
            case UnitState.Dying:
                TrySetTrigger("Death");
                break;
        }
    }

    private void TrySetTrigger(string name)
    {
        if (animator == null) return;
        foreach (var param in animator.parameters)
        {
            if (param.name == name && param.type == AnimatorControllerParameterType.Trigger)
            {
                animator.SetTrigger(name);
                return;
            }
            if (param.name == name && param.type == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool(name, true);
                ResetOtherBools(name);
                return;
            }
        }
    }

    private void TrySetFloat(string name, float value)
    {
        if (animator == null) return;
        foreach (var param in animator.parameters)
        {
            if (param.name == name && param.type == AnimatorControllerParameterType.Float)
            {
                animator.SetFloat(name, value);
                return;
            }
        }
    }

    private void ResetOtherBools(string active)
    {
        string[] states = { "Idle", "Walk", "Attack", "Death" };
        foreach (var s in states)
        {
            if (s == active) continue;
            foreach (var param in animator.parameters)
            {
                if (param.name == s && param.type == AnimatorControllerParameterType.Bool)
                {
                    animator.SetBool(s, false);
                    break;
                }
            }
        }
    }

    private void OnDeath(GameObject killer)
    {
        if (isServer)
            SetState(UnitState.Dying);
    }
}
