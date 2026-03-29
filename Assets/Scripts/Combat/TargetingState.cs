using UnityEngine;

/// <summary>
/// Per-unit targeting state. Manages current target, lock type, and scan decisions.
///
/// Hard lock: attacker is committed to this target. No scanning for upgrades.
///   → Units and buildings are hard targets.
///
/// Soft lock: attacker keeps scanning. If a higher-priority target appears, switch.
///   → Castle is a soft target (the default objective).
///
/// When target is lost and nothing else is in range, falls back to the default target.
/// </summary>
public class TargetingState
{
    public IAttackable Current { get; private set; }
    public TargetLock Lock { get; private set; }
    public bool HasTarget => Current != null && Current.Health != null && !Current.Health.IsDead && Current.gameObject != null;

    /// <summary>
    /// Try to assign a new target. Returns true if target was accepted.
    /// Respects lock rules: hard lock cannot be overridden by same or lower priority.
    /// </summary>
    public bool TrySetTarget(IAttackable target)
    {
        if (target == null) return false;

        // No current target — accept anything
        if (!HasTarget)
        {
            Apply(target);
            return true;
        }

        // Hard lock — reject unless new target has strictly higher priority
        if (Lock == TargetLock.Hard)
        {
            if (target.Priority > Current.Priority)
            {
                Apply(target);
                return true;
            }
            return false;
        }

        // Soft lock — accept if same or higher priority
        if (target.Priority >= Current.Priority)
        {
            Apply(target);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Force-set a target regardless of lock state (used for default target assignment).
    /// </summary>
    public void ForceSetTarget(IAttackable target)
    {
        Apply(target);
    }

    /// <summary>
    /// Clear the current target.
    /// </summary>
    public void Clear()
    {
        Current = null;
        Lock = TargetLock.Soft;
    }

    /// <summary>
    /// Validate the current target is still alive and in range.
    /// Returns false if target was invalidated.
    /// </summary>
    public bool Validate(Vector3 myPosition, float leashRange)
    {
        if (!HasTarget)
        {
            Clear();
            return false;
        }

        // Unit targets have a leash range
        if (Current.Priority == TargetPriority.Unit)
        {
            float dist = Vector3.Distance(myPosition, Current.gameObject.transform.position);
            if (dist > leashRange)
            {
                Clear();
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether the targeting state should scan for new targets.
    /// Hard lock = no scanning. Soft lock or no target = scan.
    /// </summary>
    public bool ShouldScan => !HasTarget || Lock == TargetLock.Soft;

    private void Apply(IAttackable target)
    {
        Current = target;
        Lock = target.Priority == TargetPriority.Default ? TargetLock.Soft : TargetLock.Hard;
    }
}
