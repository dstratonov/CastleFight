using UnityEngine;

public class UnitAnimator : MonoBehaviour
{
    private Animator animator;
    private int resolvedIdle = -1;
    private int resolvedWalk = -1;
    private int resolvedRun = -1;
    private int resolvedAttack = -1;
    private int resolvedDeath = -1;
    private int resolvedHit = -1;

    private int activeLoopHash;
    private float loopTime;
    private float clipLength = 1f;
    private bool isDead;

    private static readonly string[][] IdleNames =
    {
        new[] { "Idle", "IdleBreathe", "IdleLookAround", "idleBreathe", "idleLookAround", "idleSwordShield", "idleDaggers" },
        new[] { "Idle 0", "Idle 1" }
    };

    private static readonly string[][] WalkNames =
    {
        new[] { "Walk", "walk", "Walk Forward In Place", "Walk Forward W Root", "walkNormalSwordShield", "walkNormalDaggers", "Fly Forward In Place" }
    };

    private static readonly string[][] RunNames =
    {
        new[] { "Run", "run", "Run Forward In Place", "Run Forward W Root", "runSwordShield", "runDaggers", "Fly Forward W Root" }
    };

    private static readonly string[][] AttackNames =
    {
        new[] { "Head Attack", "attack1SwordShield", "attack1Daggers", "clawsAttackLeft", "clawsAttackRight",
                "ClawsAttackLeft", "ClawsAttackRight", "JumpBite", "Sting Projectile Attack",
                "Projectile Attack", "Poison AOE Attack", "Cast Spell",
                "2HitComboSwordShield", "2HitComboDaggers", "clawsAttack2HitCombo",
                "SpitFireBall", "shootSlingshot" }
    };

    private static readonly string[][] DeathNames =
    {
        new[] { "Die", "Death", "death", "deathSwordShield", "deathDaggers", "deathSlingshot" }
    };

    private static readonly string[][] HitNames =
    {
        new[] { "Take Damage", "getHit1", "getHit2", "GetHitFront", "getHitSwordShield", "getHitDaggers", "getHitSlingshot" }
    };

    public void Initialize(Animator anim)
    {
        animator = anim;
        if (animator == null) return;

        animator.applyRootMotion = false;
        animator.speed = 1f;

        resolvedIdle = ResolveState(IdleNames);
        resolvedWalk = ResolveState(WalkNames);
        resolvedRun = ResolveState(RunNames);
        resolvedAttack = ResolveState(AttackNames);
        resolvedDeath = ResolveState(DeathNames);
        resolvedHit = ResolveState(HitNames);

        if (resolvedWalk < 0 && resolvedRun >= 0) resolvedWalk = resolvedRun;
        if (resolvedRun < 0 && resolvedWalk >= 0) resolvedRun = resolvedWalk;
    }

    private int ResolveState(string[][] nameGroups)
    {
        if (animator == null || animator.runtimeAnimatorController == null) return -1;

        foreach (var group in nameGroups)
        {
            foreach (var name in group)
            {
                int hash = Animator.StringToHash(name);
                if (animator.HasState(0, hash))
                    return hash;
            }
        }
        return -1;
    }

    private void LateUpdate()
    {
        if (animator == null || isDead) return;
        if (activeLoopHash == 0) return;

        loopTime += Time.deltaTime;
        float normalized = (clipLength > 0f) ? (loopTime / clipLength) % 1f : 0f;
        animator.Play(activeLoopHash, 0, normalized);
    }

    public void PlayIdle()
    {
        if (resolvedIdle >= 0)
            StartLoop(resolvedIdle);
    }

    public void PlayWalk()
    {
        int target = resolvedWalk >= 0 ? resolvedWalk : resolvedRun;
        if (target >= 0)
            StartLoop(target);
    }

    public void PlayAttack()
    {
        if (resolvedAttack < 0) return;
        StopLoop();
        animator.Play(resolvedAttack, 0, 0f);
    }

    public void PlayDeath()
    {
        if (resolvedDeath < 0) return;
        isDead = true;
        StopLoop();
        animator.Play(resolvedDeath, 0, 0f);
    }

    public void PlayHit()
    {
        if (resolvedHit < 0) return;
        animator.Play(resolvedHit, 0, 0f);
    }

    private void StartLoop(int stateHash)
    {
        if (activeLoopHash == stateHash) return;
        activeLoopHash = stateHash;
        loopTime = 0f;

        animator.Play(stateHash, 0, 0f);
        animator.Update(0f);

        var stateInfo = animator.GetCurrentAnimatorStateInfo(0);
        clipLength = stateInfo.length > 0f ? stateInfo.length : 1f;
    }

    private void StopLoop()
    {
        activeLoopHash = 0;
    }

    public bool HasAnimator => animator != null && animator.runtimeAnimatorController != null;
    public bool HasIdleAnim => resolvedIdle >= 0;
    public bool HasWalkAnim => resolvedWalk >= 0;
    public bool HasAttackAnim => resolvedAttack >= 0;
    public bool HasDeathAnim => resolvedDeath >= 0;
}
