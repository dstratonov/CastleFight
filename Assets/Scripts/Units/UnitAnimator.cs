using UnityEngine;

/// <summary>
/// Controls unit animations through Animator states.
/// Manages looping animations (Idle/Walk) and one-shot animations (Attack/Hit/Death).
/// Attack speed is controlled via a per-state speed parameter, not global animator.speed.
/// </summary>
public class UnitAnimator : MonoBehaviour
{
    private Animator animator;

    private static readonly int HashIdle = Animator.StringToHash("Idle");
    private static readonly int HashWalk = Animator.StringToHash("Walk");
    private static readonly int HashAttack = Animator.StringToHash("Attack");
    private static readonly int HashDeath = Animator.StringToHash("Death");
    private static readonly int HashHit = Animator.StringToHash("Hit");
    private static readonly int HashSpeedMultiplier = Animator.StringToHash("SpeedMultiplier");

    private int idleHash;
    private int walkHash;

    private int desiredLoop;
    private int currentLoopHash;
    private int oneShotHash;
    private bool oneShotActive;
    private float oneShotFallbackTimer;
    private bool isDead;

    private bool hasIdle, hasWalk, hasAttack, hasDeath, hasHit;
    private bool hasSpeedParam;
    private float attackClipLength = 1f;
    private float deathClipLength = 2f;

    private const float DefaultBlendTime = 0.15f;
    private const float OneShotBlendTime = 0.075f;

    private float baseWalkSpeed = AnimationLogic.DefaultBaseWalkSpeed;
    private float currentMoveSpeed;
    private float currentSpeedMultiplier = 1f;

    public void Initialize(Animator anim)
    {
        animator = anim;
        if (animator == null) return;

        animator.applyRootMotion = false;
        animator.speed = 1f;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        EnsureRootMotionBlocked();

        if (animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning($"[Anim:{gameObject.name}] No AnimatorController - animations disabled");
            return;
        }

        hasIdle = animator.HasState(0, HashIdle);
        hasWalk = animator.HasState(0, HashWalk);
        hasAttack = animator.HasState(0, HashAttack);
        hasDeath = animator.HasState(0, HashDeath);
        hasHit = animator.HasState(0, HashHit);
        hasSpeedParam = HasParameter(HashSpeedMultiplier, AnimatorControllerParameterType.Float);

        idleHash = hasIdle ? HashIdle : 0;
        walkHash = hasWalk ? HashWalk : 0;

        var resolved = AnimationLogic.ResolveLoopStates(hasIdle, hasWalk, idleHash, walkHash);
        idleHash = resolved.idleHash;
        walkHash = resolved.walkHash;
        hasIdle = resolved.hasIdle;
        hasWalk = resolved.hasWalk;

        desiredLoop = hasIdle ? idleHash : 0;
        currentLoopHash = 0;
        oneShotActive = false;
        isDead = false;
        currentSpeedMultiplier = 1f;

        if (GameDebug.Animation)
            Debug.Log($"[Anim:{gameObject.name}] Init idle={hasIdle} walk={hasWalk} atk={hasAttack} death={hasDeath} hit={hasHit} speedParam={hasSpeedParam}");

        CacheClipLengths();

        if (!hasIdle)
            Debug.LogWarning($"[Anim:{gameObject.name}] No Idle state");
        if (!hasWalk)
            Debug.LogWarning($"[Anim:{gameObject.name}] No Walk state");
        if (!hasAttack)
            Debug.LogWarning($"[Anim:{gameObject.name}] No Attack state");
    }

    private void EnsureRootMotionBlocked()
    {
        var blocker = animator.GetComponent<RootMotionBlocker>();
        if (blocker == null)
            blocker = animator.gameObject.AddComponent<RootMotionBlocker>();
        blocker.Lock();
    }

    private bool HasParameter(int nameHash, AnimatorControllerParameterType type)
    {
        foreach (var p in animator.parameters)
            if (p.nameHash == nameHash && p.type == type)
                return true;
        return false;
    }

    private void CacheClipLengths()
    {
        if (animator.runtimeAnimatorController == null) return;

        var overrideCtrl = animator.runtimeAnimatorController as AnimatorOverrideController;
        if (overrideCtrl != null)
        {
            CacheClipLengthsFromOverride(overrideCtrl);
            return;
        }

        CacheClipLengthsByName();
    }

    /// <summary>
    /// Uses AnimatorOverrideController API to get clips assigned to each state
    /// by looking up the placeholder clip names. No fragile string matching needed.
    /// </summary>
    private void CacheClipLengthsFromOverride(AnimatorOverrideController overrideCtrl)
    {
        var overrides = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<AnimationClip, AnimationClip>>();
        overrideCtrl.GetOverrides(overrides);

        foreach (var pair in overrides)
        {
            if (pair.Value == null) continue;
            string originalName = pair.Key.name.ToLowerInvariant();
            if (originalName.Contains("attack"))
                attackClipLength = pair.Value.length;
            else if (originalName.Contains("death"))
                deathClipLength = pair.Value.length;
        }
    }

    private void CacheClipLengthsByName()
    {
        foreach (var clip in animator.runtimeAnimatorController.animationClips)
        {
            var category = AnimationLogic.ClassifyClip(clip.name);
            if (category == ClipCategory.Attack)
                attackClipLength = clip.length;
            else if (category == ClipCategory.Death)
                deathClipLength = clip.length;
        }
    }

    #region Public API

    public void PlayIdle()
    {
        if (hasIdle) desiredLoop = idleHash;
    }

    public void HoldPose()
    {
        desiredLoop = 0;
        currentLoopHash = 0;
    }

    public void CancelOneShot()
    {
        if (!oneShotActive) return;
        if (GameDebug.Animation)
            Debug.Log($"[Anim:{gameObject.name}] CancelOneShot (was hash={oneShotHash})");
        oneShotActive = false;
        oneShotHash = 0;
        currentLoopHash = 0;
        SetSpeedMultiplier(1f);
    }

    public void PlayWalk()
    {
        if (hasWalk) desiredLoop = walkHash;
        else if (hasIdle) desiredLoop = idleHash;
    }

    /// <summary>Set the unit's configured move speed for walk animation scaling.</summary>
    public void SetMoveSpeed(float speed)
    {
        currentMoveSpeed = speed;
    }

    public void PlayAttack(float attackCooldown = 0f)
    {
        if (!hasAttack || isDead) return;

        if (GameDebug.Animation && !oneShotActive)
            Debug.Log($"[Anim:{gameObject.name}] PlayAttack cooldown={attackCooldown:F2}");

        oneShotHash = HashAttack;
        oneShotActive = true;
        oneShotFallbackTimer = AnimationLogic.MaxOneShotDuration;

        float targetSpeed = AnimationLogic.ComputeAttackSpeed(attackClipLength, attackCooldown);

        SetSpeedMultiplier(targetSpeed);
        animator.Play(HashAttack, 0, 0f);
    }

    public void PlayHit()
    {
        if (!hasHit || isDead || oneShotActive) return;
        if (GameDebug.Animation)
            Debug.Log($"[Anim:{gameObject.name}] PlayHit");
        oneShotHash = HashHit;
        oneShotActive = true;
        oneShotFallbackTimer = AnimationLogic.MaxOneShotDuration;
        SetSpeedMultiplier(1f);
        animator.CrossFadeInFixedTime(HashHit, OneShotBlendTime, 0, 0f);
    }

    public void PlayDeath()
    {
        if (!hasDeath) return;
        if (GameDebug.Animation)
            Debug.Log($"[Anim:{gameObject.name}] PlayDeath");
        isDead = true;
        oneShotActive = false;
        desiredLoop = 0;
        currentLoopHash = 0;
        SetSpeedMultiplier(1f);
        animator.CrossFadeInFixedTime(HashDeath, DefaultBlendTime, 0, 0f);
    }

    #endregion

    #region Speed Control

    /// <summary>
    /// Sets animation speed using the per-state SpeedMultiplier parameter if available,
    /// falling back to global animator.speed otherwise.
    /// Per-state speed parameters only affect the current state, not all animations.
    /// </summary>
    private void SetSpeedMultiplier(float speed)
    {
        currentSpeedMultiplier = speed;
        if (hasSpeedParam)
            animator.SetFloat(HashSpeedMultiplier, speed);
        else
            animator.speed = speed;
    }

    #endregion

    #region LateUpdate -- Animation Reconciliation

    private void LateUpdate()
    {
        if (animator == null || isDead) return;
        if (oneShotActive) { UpdateOneShot(); return; }
        UpdateLoop();
    }

    private void UpdateOneShot()
    {
        oneShotFallbackTimer -= Time.deltaTime;

        bool isInTransition = animator.IsInTransition(0);
        var info = !isInTransition ? animator.GetCurrentAnimatorStateInfo(0) : default;
        int currentHash = !isInTransition ? info.shortNameHash : 0;
        float normalizedTime = !isInTransition ? info.normalizedTime : 0f;

        var result = AnimationLogic.EvaluateOneShot(
            oneShotFallbackTimer, isInTransition, currentHash, oneShotHash, normalizedTime);

        if (result == AnimationLogic.OneShotResult.Continue) return;

        if (GameDebug.Animation)
        {
            string reason = result switch
            {
                AnimationLogic.OneShotResult.Completed => $"completed normally t={normalizedTime:F2}",
                AnimationLogic.OneShotResult.StateMismatch => "state mismatch",
                AnimationLogic.OneShotResult.Timeout => $"TIMEOUT ({AnimationLogic.MaxOneShotDuration}s)",
                _ => "unknown"
            };
            Debug.Log($"[Anim:{gameObject.name}] OneShot {reason} -> finishing");
        }

        FinishOneShot();
    }

    private void FinishOneShot()
    {
        oneShotActive = false;
        oneShotHash = 0;
        SetSpeedMultiplier(1f);
        ApplyDesiredLoop();
    }

    private void UpdateLoop()
    {
        if (desiredLoop <= 0) return;

        if (currentLoopHash != desiredLoop)
        {
            ApplyDesiredLoop();
            return;
        }

        if (AnimationLogic.ShouldApplyWalkSpeed(currentLoopHash, walkHash, idleHash, currentMoveSpeed))
        {
            float ratio = AnimationLogic.ComputeWalkSpeedRatio(currentMoveSpeed, baseWalkSpeed);
            SetSpeedMultiplier(ratio);
        }
        else if (!oneShotActive && currentSpeedMultiplier != 1f)
        {
            SetSpeedMultiplier(1f);
        }
    }

    private void ApplyDesiredLoop()
    {
        if (desiredLoop <= 0) return;
        currentLoopHash = desiredLoop;
        animator.CrossFadeInFixedTime(desiredLoop, DefaultBlendTime, 0);
    }

    #endregion

    /// <summary>Resets animation state for object pooling.</summary>
    public void ResetState()
    {
        isDead = false;
        oneShotActive = false;
        oneShotHash = 0;
        desiredLoop = hasIdle ? idleHash : 0;
        currentLoopHash = 0;
        currentSpeedMultiplier = 1f;
        if (animator != null)
            SetSpeedMultiplier(1f);
    }

    #region Public State Queries

    public bool HasAnimator => animator != null && animator.runtimeAnimatorController != null;
    public bool HasIdleAnim => hasIdle;
    public bool HasWalkAnim => hasWalk;
    public bool HasAttackAnim => hasAttack;
    public bool HasDeathAnim => hasDeath;
    public bool IsPlayingOneShot => oneShotActive;

    /// <summary>Returns the cached death animation clip length, or fallback if no death clip.</summary>
    public float GetDeathDuration(float fallback = 2f)
    {
        return AnimationLogic.GetDeathDuration(hasDeath, deathClipLength, fallback);
    }

    #endregion
}

/// <summary>
/// Prevents root motion from displacing the animated model.
/// Placed on the same GameObject as the Animator. The empty OnAnimatorMove
/// override prevents Unity from applying root motion, and LateUpdate
/// locks the local position as a safety net.
/// </summary>
public class RootMotionBlocker : MonoBehaviour
{
    private Vector3 lockedLocalPos;
    private bool initialized;

    public void Lock()
    {
        lockedLocalPos = transform.localPosition;
        initialized = true;
    }

    private void OnAnimatorMove() { }

    private void LateUpdate()
    {
        if (initialized)
            transform.localPosition = lockedLocalPos;
    }
}
