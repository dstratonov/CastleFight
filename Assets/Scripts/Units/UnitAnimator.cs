using UnityEngine;

public class UnitAnimator : MonoBehaviour
{
    private Animator animator;

    private static readonly int HashIdle = Animator.StringToHash("Idle");
    private static readonly int HashWalk = Animator.StringToHash("Walk");
    private static readonly int HashAttack = Animator.StringToHash("Attack");
    private static readonly int HashDeath = Animator.StringToHash("Death");
    private static readonly int HashHit = Animator.StringToHash("Hit");

    private int desiredLoop;
    private int currentLoopHash;
    private int oneShotHash;
    private bool oneShotActive;
    private float oneShotFallbackTimer;
    private bool isDead;

    private bool hasIdle, hasWalk, hasAttack, hasDeath, hasHit;

    private const float BlendTime = 0.15f;
    private const float MaxOneShotDuration = 5f;

    public void Initialize(Animator anim)
    {
        animator = anim;
        if (animator == null) return;

        animator.applyRootMotion = false;
        animator.speed = 1f;

        var blocker = animator.GetComponent<RootMotionBlocker>();
        if (blocker == null)
            blocker = animator.gameObject.AddComponent<RootMotionBlocker>();
        blocker.Lock();

        if (animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning($"[UnitAnimator] No AnimatorController on {gameObject.name} - animations disabled");
            return;
        }

        hasIdle = animator.HasState(0, HashIdle);
        hasWalk = animator.HasState(0, HashWalk);
        hasAttack = animator.HasState(0, HashAttack);
        hasDeath = animator.HasState(0, HashDeath);
        hasHit = animator.HasState(0, HashHit);

        if (!hasWalk && hasIdle) hasWalk = true;
        if (!hasIdle && hasWalk) hasIdle = true;

        desiredLoop = hasIdle ? HashIdle : 0;
        currentLoopHash = 0;
        oneShotActive = false;
        isDead = false;

        if (!hasIdle)
            Debug.LogWarning($"[UnitAnimator] No Idle state on {gameObject.name}");
        if (!hasWalk)
            Debug.LogWarning($"[UnitAnimator] No Walk state on {gameObject.name}");
        if (!hasAttack)
            Debug.LogWarning($"[UnitAnimator] No Attack state on {gameObject.name}");
    }

    #region Public API

    public void PlayIdle()
    {
        if (hasIdle) desiredLoop = HashIdle;
    }

    public void PlayWalk()
    {
        if (hasWalk) desiredLoop = HashWalk;
        else if (hasIdle) desiredLoop = HashIdle;
    }

    public void PlayAttack()
    {
        if (!hasAttack || isDead || oneShotActive) return;
        oneShotHash = HashAttack;
        oneShotActive = true;
        oneShotFallbackTimer = MaxOneShotDuration;
        animator.Play(HashAttack, 0, 0f);
    }

    public void PlayHit()
    {
        if (!hasHit || isDead || oneShotActive) return;
        oneShotHash = HashHit;
        oneShotActive = true;
        oneShotFallbackTimer = MaxOneShotDuration;
        animator.Play(HashHit, 0, 0f);
    }

    public void PlayDeath()
    {
        if (!hasDeath) return;
        isDead = true;
        oneShotActive = false;
        desiredLoop = 0;
        currentLoopHash = 0;
        animator.Play(HashDeath, 0, 0f);
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
                FinishOneShot();
                return;
            }
            if (info.shortNameHash != oneShotHash && oneShotFallbackTimer < MaxOneShotDuration - 0.1f)
            {
                FinishOneShot();
                return;
            }
        }

        if (oneShotFallbackTimer <= 0f)
            FinishOneShot();
    }

    private void FinishOneShot()
    {
        oneShotActive = false;
        oneShotHash = 0;
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

        if (!animator.IsInTransition(0))
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.shortNameHash == currentLoopHash && info.normalizedTime >= 1f)
                animator.Play(currentLoopHash, 0, 0f);
        }
    }

    private void ApplyDesiredLoop()
    {
        if (desiredLoop <= 0) return;
        currentLoopHash = desiredLoop;
        animator.CrossFadeInFixedTime(desiredLoop, BlendTime, 0);
    }

    #endregion

    #region Public State Queries

    public bool HasAnimator => animator != null && animator.runtimeAnimatorController != null;
    public bool HasIdleAnim => hasIdle;
    public bool HasWalkAnim => hasWalk;
    public bool HasAttackAnim => hasAttack;
    public bool HasDeathAnim => hasDeath;
    public bool IsPlayingOneShot => oneShotActive;

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
