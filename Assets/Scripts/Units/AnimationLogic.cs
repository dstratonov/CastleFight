using UnityEngine;

/// <summary>
/// Pure animation logic extracted from UnitAnimator for testability.
/// No MonoBehaviour, no Animator, no Unity runtime dependencies.
/// </summary>
public static class AnimationLogic
{
    public const float MinAnimSpeed = 0.5f;
    public const float MaxAnimSpeed = 3f;
    public const float MinWalkSpeedRatio = 0.5f;
    public const float MaxWalkSpeedRatio = 2.5f;
    public const float DefaultBaseWalkSpeed = 3.5f;
    public const float MaxOneShotDuration = 5f;
    public const float MinDeathDuration = 0.5f;
    public const float MaxDeathDuration = 5f;

    private static readonly string[] AttackClipKeywords = { "attack", "bite", "claw", "slash", "spit", "sting", "stomp", "smash", "punch", "strike", "swing" };
    private static readonly string[] DeathClipKeywords = { "death", "die" };

    /// <summary>
    /// Computes the animation speed multiplier to fit the attack clip within the cooldown.
    /// Returns 1 if either value is too small to compute a ratio.
    /// </summary>
    public static float ComputeAttackSpeed(float clipLength, float attackCooldown)
    {
        if (attackCooldown <= 0.01f || clipLength <= 0.01f)
            return 1f;
        return Mathf.Clamp(clipLength / attackCooldown, MinAnimSpeed, MaxAnimSpeed);
    }

    /// <summary>
    /// Computes the walk animation speed ratio based on movement speed.
    /// </summary>
    public static float ComputeWalkSpeedRatio(float moveSpeed, float baseWalkSpeed)
    {
        if (baseWalkSpeed <= 0.01f || moveSpeed <= 0.01f)
            return 1f;
        return Mathf.Clamp(moveSpeed / baseWalkSpeed, MinWalkSpeedRatio, MaxWalkSpeedRatio);
    }

    /// <summary>
    /// Returns the death animation duration, clamped to a safe range.
    /// Returns fallback if the unit has no death animation.
    /// </summary>
    public static float GetDeathDuration(bool hasDeathAnim, float clipLength, float fallback)
    {
        if (!hasDeathAnim) return fallback;
        return Mathf.Clamp(clipLength, MinDeathDuration, MaxDeathDuration);
    }

    /// <summary>
    /// Classifies a clip name as attack, death, or unknown based on keyword matching.
    /// </summary>
    public static ClipCategory ClassifyClip(string clipName)
    {
        string lower = clipName.ToLowerInvariant();
        if (MatchesAny(lower, AttackClipKeywords)) return ClipCategory.Attack;
        if (MatchesAny(lower, DeathClipKeywords)) return ClipCategory.Death;
        return ClipCategory.Unknown;
    }

    /// <summary>
    /// Determines the effective idle and walk hashes given which states exist.
    /// Handles fallback substitution (idle↔walk when one is missing).
    /// </summary>
    public static (int idleHash, int walkHash, bool hasIdle, bool hasWalk) ResolveLoopStates(
        bool rawHasIdle, bool rawHasWalk, int idleStateHash, int walkStateHash)
    {
        int resolvedIdle = rawHasIdle ? idleStateHash : 0;
        int resolvedWalk = rawHasWalk ? walkStateHash : 0;
        bool hasIdle = rawHasIdle;
        bool hasWalk = rawHasWalk;

        if (!hasWalk && hasIdle)
        {
            hasWalk = true;
            resolvedWalk = resolvedIdle;
        }
        if (!hasIdle && hasWalk)
        {
            hasIdle = true;
            resolvedIdle = resolvedWalk;
        }

        return (resolvedIdle, resolvedWalk, hasIdle, hasWalk);
    }

    public enum OneShotResult
    {
        Continue,
        Completed,
        StateMismatch,
        Timeout
    }

    /// <summary>
    /// Evaluates the one-shot animation state to determine if it should finish.
    /// </summary>
    public static OneShotResult EvaluateOneShot(
        float fallbackTimer, bool isInTransition,
        int currentStateHash, int expectedHash, float normalizedTime)
    {
        if (fallbackTimer <= 0f)
            return OneShotResult.Timeout;

        if (!isInTransition)
        {
            if (currentStateHash == expectedHash && normalizedTime >= 1f)
                return OneShotResult.Completed;

            if (currentStateHash != expectedHash && fallbackTimer < MaxOneShotDuration - 0.1f)
                return OneShotResult.StateMismatch;
        }

        return OneShotResult.Continue;
    }

    /// <summary>
    /// Determines if the walk speed should be applied to the animator.
    /// </summary>
    public static bool ShouldApplyWalkSpeed(int currentLoopHash, int walkHash, int idleHash, float moveSpeed)
    {
        return currentLoopHash == walkHash && walkHash != idleHash && moveSpeed > 0.01f;
    }

    private static bool MatchesAny(string name, string[] keywords)
    {
        foreach (var kw in keywords)
            if (name.Contains(kw)) return true;
        return false;
    }
}

public enum ClipCategory
{
    Unknown,
    Attack,
    Death
}
