using UnityEngine;

public class UnitAnimator : MonoBehaviour
{
    private Animator animator;

    private static readonly int HashIdle = Animator.StringToHash("Idle");
    private static readonly int HashWalk = Animator.StringToHash("Walk");
    private static readonly int HashAttack = Animator.StringToHash("Attack");
    private static readonly int HashDeath = Animator.StringToHash("Death");
    private static readonly int HashHit = Animator.StringToHash("Hit");

    private int idleHash;
    private int walkHash;

    private int desiredLoop;
    private int currentLoopHash;
    private int oneShotHash;
    private bool oneShotActive;
    private float oneShotFallbackTimer;
    private bool isDead;

    private bool hasIdle, hasWalk, hasAttack, hasDeath, hasHit;
    private float attackClipLength = 1f;
    private float deathClipLength = 2f;

    private const float BlendTime = 0.15f;
    private const float MaxOneShotDuration = 5f;

    private int animLogThrottle;
    private const int AnimLogInterval = 30;

    private float baseWalkSpeed = 3.5f;
    private float currentMoveSpeed;

    public void Initialize(Animator anim)
    {
        animator = anim;
        if (animator == null) return;

        animator.applyRootMotion = false;
        animator.speed = 1f;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var blocker = animator.GetComponent<RootMotionBlocker>();
        if (blocker == null)
            blocker = animator.gameObject.AddComponent<RootMotionBlocker>();
        blocker.Lock();

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

        idleHash = hasIdle ? HashIdle : 0;
        walkHash = hasWalk ? HashWalk : 0;

        if (!hasWalk && hasIdle)
        {
            hasWalk = true;
            walkHash = HashIdle;
        }
        if (!hasIdle && hasWalk)
        {
            hasIdle = true;
            idleHash = HashWalk;
        }

        desiredLoop = hasIdle ? idleHash : 0;
        currentLoopHash = 0;
        oneShotActive = false;
        isDead = false;

        if (GameDebug.Animation)
            Debug.Log($"[Anim:{gameObject.name}] Init idle={hasIdle} walk={hasWalk} atk={hasAttack} death={hasDeath} hit={hasHit}" +
                $" idleHash={idleHash} walkHash={walkHash}");

        CacheClipLengths();

        if (!hasIdle)
            Debug.LogWarning($"[Anim:{gameObject.name}] No Idle state");
        if (!hasWalk)
            Debug.LogWarning($"[Anim:{gameObject.name}] No Walk state");
        if (!hasAttack)
            Debug.LogWarning($"[Anim:{gameObject.name}] No Attack state");
    }

    private void CacheClipLengths()
    {
        if (animator.runtimeAnimatorController == null) return;
        foreach (var clip in animator.runtimeAnimatorController.animationClips)
        {
            string name = clip.name.ToLowerInvariant();
            if (name.Contains("attack") || name.Contains("bite") || name.Contains("claw") ||
                name.Contains("slash") || name.Contains("spit") || name.Contains("sting") ||
                name.Contains("stomp") || name.Contains("smash"))
            {
                attackClipLength = clip.length;
            }
            else if (name.Contains("death") || name.Contains("die"))
            {
                deathClipLength = clip.length;
            }
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
            Debug.Log($"[Anim:{gameObject.name}] CancelOneShot (was hash={oneShotHash} speed={animator?.speed:F2})");
        oneShotActive = false;
        oneShotHash = 0;
        // Reset currentLoopHash so UpdateLoop re-applies the desired loop.
        // Without this, the Animator stays on the Attack clip because
        // UpdateLoop thinks the idle loop is already active.
        currentLoopHash = 0;
        if (animator != null)
            animator.speed = 1f;
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

        if (oneShotActive && oneShotHash == HashHit)
        {
            if (GameDebug.Animation)
                Debug.Log($"[Anim:{gameObject.name}] PlayAttack overriding hit anim");
        }

        if (GameDebug.Animation && !oneShotActive)
            Debug.Log($"[Anim:{gameObject.name}] PlayAttack cooldown={attackCooldown:F2}");

        oneShotHash = HashAttack;
        oneShotActive = true;
        oneShotFallbackTimer = MaxOneShotDuration;

        if (attackCooldown > 0.01f && attackClipLength > 0.01f)
        {
            float targetSpeed = Mathf.Clamp(attackClipLength / attackCooldown, 0.5f, 3f);
            animator.speed = targetSpeed;
        }
        else
        {
            animator.speed = 1f;
        }
        animator.Play(HashAttack, 0, 0f);
    }

    public void PlayHit()
    {
        if (!hasHit || isDead || oneShotActive) return;
        if (GameDebug.Animation)
            Debug.Log($"[Anim:{gameObject.name}] PlayHit");
        oneShotHash = HashHit;
        oneShotActive = true;
        oneShotFallbackTimer = MaxOneShotDuration;
        animator.CrossFadeInFixedTime(HashHit, BlendTime * 0.5f, 0, 0f);
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
        animator.speed = 1f;
        animator.CrossFadeInFixedTime(HashDeath, BlendTime, 0, 0f);
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

        if (!animator.IsInTransition(0))
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.shortNameHash == oneShotHash && info.normalizedTime >= 1f)
            {
                if (GameDebug.Animation)
                    Debug.Log($"[Anim:{gameObject.name}] OneShot completed normally t={info.normalizedTime:F2} speed={animator.speed:F2}");
                FinishOneShot();
                return;
            }
            if (info.shortNameHash != oneShotHash && oneShotFallbackTimer < MaxOneShotDuration - 0.1f)
            {
                if (GameDebug.Animation)
                    Debug.Log($"[Anim:{gameObject.name}] OneShot state mismatch (expected={oneShotHash}, got={info.shortNameHash} t={info.normalizedTime:F2}) -> finishing");
                FinishOneShot();
                return;
            }
        }
        else if (GameDebug.Animation && oneShotFallbackTimer < MaxOneShotDuration - 0.5f && Time.frameCount % 60 == 0)
        {
            Debug.Log($"[Anim:{gameObject.name}] OneShot in transition, timer={oneShotFallbackTimer:F1}s");
        }

        if (oneShotFallbackTimer <= 0f)
        {
            if (GameDebug.Animation)
                Debug.LogWarning($"[Anim:{gameObject.name}] OneShot TIMEOUT ({MaxOneShotDuration}s) -> forcing finish");
            FinishOneShot();
        }
    }

    private void FinishOneShot()
    {
        oneShotActive = false;
        oneShotHash = 0;
        animator.speed = 1f;
        ApplyDesiredLoop();
    }

    private void UpdateLoop()
    {
        if (desiredLoop <= 0) return;

        if (currentLoopHash != desiredLoop)
        {
            if (GameDebug.Animation && animLogThrottle % AnimLogInterval == 0)
                Debug.Log($"[Anim:{gameObject.name}] Loop switch {currentLoopHash} -> {desiredLoop}");
            ApplyDesiredLoop();
            animLogThrottle = 0;
            return;
        }

        if (currentLoopHash == walkHash && walkHash != idleHash && currentMoveSpeed > 0.01f)
        {
            float speedRatio = currentMoveSpeed / baseWalkSpeed;
            animator.speed = Mathf.Clamp(speedRatio, 0.5f, 2.5f);
        }
        else if (!oneShotActive)
        {
            animator.speed = 1f;
        }

        animLogThrottle++;
    }

    private void ApplyDesiredLoop()
    {
        if (desiredLoop <= 0) return;
        currentLoopHash = desiredLoop;
        animator.CrossFadeInFixedTime(desiredLoop, BlendTime, 0);
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
        if (animator != null)
            animator.speed = 1f;
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
        if (!hasDeath) return fallback;
        return Mathf.Clamp(deathClipLength, 0.5f, 5f);
    }

    #endregion
}

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
