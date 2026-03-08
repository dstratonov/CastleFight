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

    private const float BlendTime = 0.15f;
    private const float MaxOneShotDuration = 5f;

    private int animLogThrottle;
    private const int AnimLogInterval = 30;

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

        if (!hasIdle)
            Debug.LogWarning($"[Anim:{gameObject.name}] No Idle state");
        if (!hasWalk)
            Debug.LogWarning($"[Anim:{gameObject.name}] No Walk state");
        if (!hasAttack)
            Debug.LogWarning($"[Anim:{gameObject.name}] No Attack state");
    }

    #region Public API

    public void PlayIdle()
    {
        if (hasIdle) desiredLoop = idleHash;
    }

    public void CancelOneShot()
    {
        if (!oneShotActive) return;
        if (GameDebug.Animation)
            Debug.Log($"[Anim:{gameObject.name}] CancelOneShot (was hash={oneShotHash})");
        oneShotActive = false;
        oneShotHash = 0;
    }

    public void PlayWalk()
    {
        if (hasWalk) desiredLoop = walkHash;
        else if (hasIdle) desiredLoop = idleHash;
    }

    public void PlayAttack()
    {
        if (!hasAttack || isDead) return;

        if (oneShotActive && oneShotHash == HashHit)
        {
            if (GameDebug.Animation)
                Debug.Log($"[Anim:{gameObject.name}] PlayAttack overriding hit anim");
        }

        if (GameDebug.Animation && !oneShotActive)
            Debug.Log($"[Anim:{gameObject.name}] PlayAttack");

        oneShotHash = HashAttack;
        oneShotActive = true;
        oneShotFallbackTimer = MaxOneShotDuration;
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
        animator.Play(HashHit, 0, 0f);
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
                if (GameDebug.Animation)
                    Debug.Log($"[Anim:{gameObject.name}] OneShot completed normally t={info.normalizedTime:F2}");
                FinishOneShot();
                return;
            }
            if (info.shortNameHash != oneShotHash && oneShotFallbackTimer < MaxOneShotDuration - 0.1f)
            {
                if (GameDebug.Animation)
                    Debug.Log($"[Anim:{gameObject.name}] OneShot state mismatch (expected hash={oneShotHash}, got={info.shortNameHash}) -> finishing");
                FinishOneShot();
                return;
            }
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
            return;
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
