/// <summary>
/// Pure unit state transition logic extracted from UnitStateMachine for testability.
/// </summary>
public static class UnitStateLogic
{
    /// <summary>
    /// Compute the next state based on current conditions.
    /// This mirrors UnitStateMachine.UpdateState() logic.
    /// </summary>
    public static UnitState ComputeNextState(UnitState currentState, bool isDead, bool isMoving, bool isWaitingForPath)
    {
        if (isDead) return UnitState.Dying;
        if (currentState == UnitState.Dying) return UnitState.Dying;
        if (currentState == UnitState.Fighting) return UnitState.Fighting;
        if (isMoving || isWaitingForPath) return UnitState.Moving;
        return UnitState.Idle;
    }

    /// <summary>
    /// Check if a state transition should actually happen (same state = no transition).
    /// </summary>
    public static bool ShouldTransition(UnitState currentState, UnitState newState)
    {
        return currentState != newState;
    }

    /// <summary>
    /// Determine which animation should play for a given state.
    /// </summary>
    public static AnimAction GetAnimationForState(UnitState state)
    {
        return state switch
        {
            UnitState.Idle => AnimAction.Idle,
            UnitState.Moving => AnimAction.Walk,
            UnitState.Fighting => AnimAction.IdleReady,
            UnitState.Dying => AnimAction.Death,
            _ => AnimAction.Idle
        };
    }

    /// <summary>
    /// Check if one-shot animations (attack, hit) should be cancelled on state transition.
    /// </summary>
    public static bool ShouldCancelOneShot(UnitState newState)
    {
        return newState == UnitState.Idle || newState == UnitState.Moving;
    }
}

public enum AnimAction
{
    Idle,
    IdleReady,
    Walk,
    Death
}
