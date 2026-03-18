using NUnit.Framework;

[TestFixture]
public class UnitStateLogicTests
{
    // ================================================================
    //  ComputeNextState — basic transitions
    // ================================================================

    [Test]
    public void ComputeNextState_IsDead_ReturnsDying()
    {
        var result = UnitStateLogic.ComputeNextState(UnitState.Idle, isDead: true, isMoving: false, isWaitingForPath: false);
        Assert.AreEqual(UnitState.Dying, result);
    }

    [Test]
    public void ComputeNextState_IsDead_OverridesFighting()
    {
        var result = UnitStateLogic.ComputeNextState(UnitState.Fighting, isDead: true, isMoving: false, isWaitingForPath: false);
        Assert.AreEqual(UnitState.Dying, result);
    }

    [Test]
    public void ComputeNextState_CurrentDying_StaysDying()
    {
        var result = UnitStateLogic.ComputeNextState(UnitState.Dying, isDead: false, isMoving: false, isWaitingForPath: false);
        Assert.AreEqual(UnitState.Dying, result);
    }

    [Test]
    public void ComputeNextState_CurrentFighting_StaysFighting()
    {
        var result = UnitStateLogic.ComputeNextState(UnitState.Fighting, isDead: false, isMoving: true, isWaitingForPath: false);
        Assert.AreEqual(UnitState.Fighting, result);
    }

    [Test]
    public void ComputeNextState_IsMoving_ReturnsMoving()
    {
        var result = UnitStateLogic.ComputeNextState(UnitState.Idle, isDead: false, isMoving: true, isWaitingForPath: false);
        Assert.AreEqual(UnitState.Moving, result);
    }

    [Test]
    public void ComputeNextState_IsWaitingForPath_ReturnsMoving()
    {
        var result = UnitStateLogic.ComputeNextState(UnitState.Idle, isDead: false, isMoving: false, isWaitingForPath: true);
        Assert.AreEqual(UnitState.Moving, result);
    }

    [Test]
    public void ComputeNextState_AllFalse_ReturnsIdle()
    {
        var result = UnitStateLogic.ComputeNextState(UnitState.Idle, isDead: false, isMoving: false, isWaitingForPath: false);
        Assert.AreEqual(UnitState.Idle, result);
    }

    [Test]
    public void ComputeNextState_MovingToIdle_WhenNotMoving()
    {
        var result = UnitStateLogic.ComputeNextState(UnitState.Moving, isDead: false, isMoving: false, isWaitingForPath: false);
        Assert.AreEqual(UnitState.Idle, result);
    }

    // ================================================================
    //  ComputeNextState — priority edge cases
    // ================================================================

    [Test]
    public void ComputeNextState_IsDead_OverridesMoving()
    {
        var result = UnitStateLogic.ComputeNextState(UnitState.Moving, isDead: true, isMoving: true, isWaitingForPath: true);
        Assert.AreEqual(UnitState.Dying, result);
    }

    [Test]
    public void ComputeNextState_DyingAbsorbsDead()
    {
        // Already Dying + isDead=true should stay Dying (isDead checked first, returns Dying)
        var result = UnitStateLogic.ComputeNextState(UnitState.Dying, isDead: true, isMoving: true, isWaitingForPath: true);
        Assert.AreEqual(UnitState.Dying, result);
    }

    [Test]
    public void ComputeNextState_DyingAbsorbsAllInputs()
    {
        // Dying state ignores all other flags
        var result = UnitStateLogic.ComputeNextState(UnitState.Dying, isDead: false, isMoving: true, isWaitingForPath: true);
        Assert.AreEqual(UnitState.Dying, result);
    }

    [Test]
    public void ComputeNextState_FightingIgnoresWaitingForPath()
    {
        var result = UnitStateLogic.ComputeNextState(UnitState.Fighting, isDead: false, isMoving: false, isWaitingForPath: true);
        Assert.AreEqual(UnitState.Fighting, result);
    }

    [Test]
    public void ComputeNextState_FightingIgnoresBothMovementFlags()
    {
        var result = UnitStateLogic.ComputeNextState(UnitState.Fighting, isDead: false, isMoving: true, isWaitingForPath: true);
        Assert.AreEqual(UnitState.Fighting, result);
    }

    // ================================================================
    //  State transition sequences
    // ================================================================

    [Test]
    public void Sequence_IdleToMovingToFightingToDying()
    {
        var state = UnitState.Idle;

        // Start moving
        state = UnitStateLogic.ComputeNextState(state, isDead: false, isMoving: true, isWaitingForPath: false);
        Assert.AreEqual(UnitState.Moving, state, "Idle -> Moving");

        // Enter combat (state externally set to Fighting by UnitStateMachine)
        state = UnitState.Fighting;

        // Fighting is sticky
        state = UnitStateLogic.ComputeNextState(state, isDead: false, isMoving: false, isWaitingForPath: false);
        Assert.AreEqual(UnitState.Fighting, state, "Fighting stays Fighting");

        // Die during combat
        state = UnitStateLogic.ComputeNextState(state, isDead: true, isMoving: false, isWaitingForPath: false);
        Assert.AreEqual(UnitState.Dying, state, "Fighting -> Dying on death");

        // Dying is absorbing
        state = UnitStateLogic.ComputeNextState(state, isDead: false, isMoving: true, isWaitingForPath: true);
        Assert.AreEqual(UnitState.Dying, state, "Dying stays Dying regardless of inputs");
    }

    [Test]
    public void Sequence_MovingToIdleToMoving()
    {
        var state = UnitState.Moving;

        // Stop moving
        state = UnitStateLogic.ComputeNextState(state, isDead: false, isMoving: false, isWaitingForPath: false);
        Assert.AreEqual(UnitState.Idle, state, "Moving -> Idle");

        // Start moving again
        state = UnitStateLogic.ComputeNextState(state, isDead: false, isMoving: true, isWaitingForPath: false);
        Assert.AreEqual(UnitState.Moving, state, "Idle -> Moving again");
    }

    [Test]
    public void Sequence_RapidOscillation_StableResults()
    {
        // Rapid toggling of isMoving should produce deterministic results
        var state = UnitState.Idle;

        for (int i = 0; i < 100; i++)
        {
            bool moving = (i % 2 == 0);
            state = UnitStateLogic.ComputeNextState(state, isDead: false, isMoving: moving, isWaitingForPath: false);
            var expected = moving ? UnitState.Moving : UnitState.Idle;
            Assert.AreEqual(expected, state, $"Iteration {i}: expected {expected}");
        }
    }

    [Test]
    public void Sequence_WaitingForPathThenMoving()
    {
        var state = UnitState.Idle;

        // Waiting for path
        state = UnitStateLogic.ComputeNextState(state, isDead: false, isMoving: false, isWaitingForPath: true);
        Assert.AreEqual(UnitState.Moving, state, "WaitingForPath -> Moving");

        // Path arrives, now actually moving
        state = UnitStateLogic.ComputeNextState(state, isDead: false, isMoving: true, isWaitingForPath: false);
        Assert.AreEqual(UnitState.Moving, state, "Still Moving with path");

        // Arrive at destination
        state = UnitStateLogic.ComputeNextState(state, isDead: false, isMoving: false, isWaitingForPath: false);
        Assert.AreEqual(UnitState.Idle, state, "Arrived -> Idle");
    }

    // ================================================================
    //  ShouldTransition
    // ================================================================

    [Test]
    public void ShouldTransition_SameState_ReturnsFalse()
    {
        Assert.IsFalse(UnitStateLogic.ShouldTransition(UnitState.Idle, UnitState.Idle));
        Assert.IsFalse(UnitStateLogic.ShouldTransition(UnitState.Moving, UnitState.Moving));
        Assert.IsFalse(UnitStateLogic.ShouldTransition(UnitState.Fighting, UnitState.Fighting));
        Assert.IsFalse(UnitStateLogic.ShouldTransition(UnitState.Dying, UnitState.Dying));
    }

    [Test]
    public void ShouldTransition_DifferentState_ReturnsTrue()
    {
        Assert.IsTrue(UnitStateLogic.ShouldTransition(UnitState.Idle, UnitState.Moving));
        Assert.IsTrue(UnitStateLogic.ShouldTransition(UnitState.Moving, UnitState.Fighting));
        Assert.IsTrue(UnitStateLogic.ShouldTransition(UnitState.Fighting, UnitState.Dying));
        Assert.IsTrue(UnitStateLogic.ShouldTransition(UnitState.Moving, UnitState.Idle));
    }

    // ================================================================
    //  GetAnimationForState
    // ================================================================

    [Test]
    public void GetAnimationForState_AllStates()
    {
        Assert.AreEqual(AnimAction.Idle, UnitStateLogic.GetAnimationForState(UnitState.Idle));
        Assert.AreEqual(AnimAction.Walk, UnitStateLogic.GetAnimationForState(UnitState.Moving));
        Assert.AreEqual(AnimAction.IdleReady, UnitStateLogic.GetAnimationForState(UnitState.Fighting));
        Assert.AreEqual(AnimAction.Death, UnitStateLogic.GetAnimationForState(UnitState.Dying));
    }

    // ================================================================
    //  ShouldCancelOneShot
    // ================================================================

    [Test]
    public void ShouldCancelOneShot_IdleAndMoving_ReturnsTrue()
    {
        Assert.IsTrue(UnitStateLogic.ShouldCancelOneShot(UnitState.Idle));
        Assert.IsTrue(UnitStateLogic.ShouldCancelOneShot(UnitState.Moving));
    }

    [Test]
    public void ShouldCancelOneShot_FightingAndDying_ReturnsFalse()
    {
        Assert.IsFalse(UnitStateLogic.ShouldCancelOneShot(UnitState.Fighting));
        Assert.IsFalse(UnitStateLogic.ShouldCancelOneShot(UnitState.Dying));
    }

    // ================================================================
    //  All states x all input combos exhaustive coverage
    // ================================================================

    [Test]
    public void ComputeNextState_AllInputCombinations_NeverThrows()
    {
        var states = new[] { UnitState.Idle, UnitState.Moving, UnitState.Fighting, UnitState.Dying };
        var bools = new[] { false, true };

        foreach (var currentState in states)
            foreach (var isDead in bools)
                foreach (var isMoving in bools)
                    foreach (var isWaiting in bools)
                    {
                        var result = UnitStateLogic.ComputeNextState(currentState, isDead, isMoving, isWaiting);
                        // isDead always produces Dying
                        if (isDead)
                            Assert.AreEqual(UnitState.Dying, result,
                                $"isDead=true should always produce Dying (was {currentState})");
                        // Dying is absorbing
                        else if (currentState == UnitState.Dying)
                            Assert.AreEqual(UnitState.Dying, result,
                                "Dying state is absorbing");
                        // Fighting is sticky
                        else if (currentState == UnitState.Fighting)
                            Assert.AreEqual(UnitState.Fighting, result,
                                "Fighting state is sticky");
                    }
    }

    [Test]
    public void ComputeNextState_NonAbsorbingStates_MovementFlagsDecide()
    {
        // For Idle/Moving states (non-absorbing, non-sticky), movement flags decide
        var nonSticky = new[] { UnitState.Idle, UnitState.Moving };

        foreach (var state in nonSticky)
        {
            // isMoving -> Moving
            Assert.AreEqual(UnitState.Moving,
                UnitStateLogic.ComputeNextState(state, false, true, false),
                $"{state} + isMoving -> Moving");

            // isWaitingForPath -> Moving
            Assert.AreEqual(UnitState.Moving,
                UnitStateLogic.ComputeNextState(state, false, false, true),
                $"{state} + isWaitingForPath -> Moving");

            // neither -> Idle
            Assert.AreEqual(UnitState.Idle,
                UnitStateLogic.ComputeNextState(state, false, false, false),
                $"{state} + no movement -> Idle");
        }
    }
}
