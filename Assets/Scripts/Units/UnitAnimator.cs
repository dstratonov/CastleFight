using UnityEngine;
using System.Collections.Generic;

public class UnitAnimator : MonoBehaviour
{
    private Animator animator;
    private int resolvedIdle = -1;
    private int resolvedWalk = -1;
    private int resolvedRun = -1;
    private int resolvedAttack = -1;
    private int resolvedDeath = -1;
    private int resolvedHit = -1;

    private int desiredLoop;
    private int currentLoopHash;
    private int oneShotHash;
    private bool oneShotActive;
    private float oneShotFallbackTimer;
    private bool isDead;

    private const float BlendTime = 0.15f;
    private const float MaxOneShotDuration = 5f;

    #region State Name Tables

    private static readonly string[][] IdleNames =
    {
        new[]
        {
            "Idle", "idle",
            "IdleBreathe", "idleBreathe",
            "IdleLookAround", "idleLookAround",
            "IdleNormal", "idleNormal",
            "IdleThreat", "IdleCombat", "idleCombat",
            "IdleAggressive",
            "IdleSpear", "IdleUnarmed", "IdleWeapon",
            "idleSwordShield", "idleDaggers", "idleSlingshot",
            "idleShield",
            "IdleDefenseWeapon", "IdleDefenseUnarmed",
            "idleProtectedSwordShield", "idleProtectedDaggers",
            "IdleLookAroundNormal", "IdleLookAroundThreat"
        },
        new[] { "Idle 0", "Idle 1", "Idle 2", "Idle 3", "Idle 4", "Idle 5", "Idle 6", "Idle 7" }
    };

    private static readonly string[][] WalkNames =
    {
        new[]
        {
            "Walk", "walk",
            "Walk Forward In Place", "Walk Forward W Root",
            "Move Forward In Place", "Move Forward W Root",
            "Fly Forward In Place",
            "Crawl", "CrawlNormal", "CrawlThreat",
            "walkNormalSwordShield", "walkNormalDaggers", "walkNormalSlingshot",
            "walkNormal",
            "WalkSpear", "WalkWeapon", "WalkUnarmed",
            "walkWeapon", "walkBareHands", "walkShield", "walkSlow",
            "Gallop"
        }
    };

    private static readonly string[][] RunNames =
    {
        new[]
        {
            "Run", "run",
            "Run Forward In Place", "Run Forward W Root",
            "Fly Forward W Root",
            "runSwordShield", "runDaggers", "runSlingshot",
            "runNormal",
            "RunSpear", "RunWeapon", "RunUnarmed"
        }
    };

    private static readonly string[][] AttackNames =
    {
        new[]
        {
            "attack1", "attack2", "attack3",
            "Attack1", "Attack2", "Attack3",
            "Bite", "BiteForward", "BiteAttack", "JumpBite",
            "JumpBiteNormal", "JumpBiteThreat",
            "snakeBiteAttack", "biteAttackBareHands",
            "Bite3HitCombo",
            "clawsAttackLeft", "clawsAttackRight",
            "ClawsAttackLeft", "ClawsAttackRight",
            "clawsAttackL", "clawsAttackR",
            "ClawsAttackL", "ClawsAttackR",
            "LeftClawsAttack", "RightClawsAttack",
            "clawsAttack2HitCombo",
            "ClawsLeftAttackCombat", "ClawsRightAttackCombat",
            "attack1SwordShield", "attack1Daggers",
            "attack1Weapon", "attack2Weapon", "attack3Weapon",
            "Attack1Spear", "Attack2Spear", "Attack3Spear",
            "throwSpear", "TongueAttackSpear",
            "2HitComboSwordShield", "2HitComboDaggers",
            "2HitComboAttack", "3HitComboAttack",
            "2HitComboA", "2HitComboB",
            "3HitComboA", "3HitComboB",
            "2HitComboForward",
            "Head Attack", "Projectile Attack", "Sting Projectile Attack",
            "Poison AOE Attack", "Cast Spell",
            "SpitFireBall", "SpitVenom", "3toxicSpitCombo",
            "shootSlingshot",
            "Slash Attack", "Dash Attack In Place",
            "Tentacles Attack", "Eyebeam Attack",
            "SpinningTailAttack", "StingerAttackCombat",
            "ramAttack", "StompAttack", "smashAttackForward",
            "ThrowRock", "ThrowSpiderWebNormal", "ThrowSpiderWebThreat",
            "jumpClawsAttack", "jumpBiteAttack",
            "leftHandAttackForward", "rightHandAttackForward",
            "2fistsSmashAttack", "2fistsCrushAttack",
            "CrawlBiteThreat",
            "BandageWhipAttack1",
            "Pounce Bite Attack In Place",
            "Roll Attack In Place"
        }
    };

    private static readonly string[][] DeathNames =
    {
        new[]
        {
            "Die", "Death", "death",
            "DeathNormal", "DeathThreat",
            "death1", "death2",
            "deathSwordShield", "deathDaggers", "deathSlingshot",
            "DeathSpear", "DeathWeapon", "DeathUnarmed",
            "deathHitTheGround"
        }
    };

    private static readonly string[][] HitNames =
    {
        new[]
        {
            "Take Damage",
            "getHit1", "getHit2",
            "GetHitFront", "GetHitLeft", "GetHitRight",
            "getHitSwordShield", "getHitDaggers", "getHitSlingshot",
            "flyGetHit"
        }
    };

    private static readonly string[] IdleKeywords = { "idle", "breathe", "lookaround", "rest", "stand" };
    private static readonly string[] WalkKeywords = { "walk", "move", "crawl", "fly forward", "gallop" };
    private static readonly string[] RunKeywords = { "run", "sprint" };
    private static readonly string[] AttackKeywords = { "attack", "bite", "claw", "slash", "hit combo", "spit", "sting", "stomp", "smash", "throw", "cast", "projectile" };
    private static readonly string[] DeathKeywords = { "die", "death" };
    private static readonly string[] HitKeywords = { "hit", "damage", "hurt" };

    #endregion

    #region Initialization

    public void Initialize(Animator anim)
    {
        animator = anim;
        if (animator == null) return;

        animator.applyRootMotion = false;
        animator.speed = 1f;

        CacheClipNames();

        resolvedIdle = ResolveState(IdleNames, IdleKeywords);
        resolvedWalk = ResolveState(WalkNames, WalkKeywords);
        resolvedRun = ResolveState(RunNames, RunKeywords);
        resolvedAttack = ResolveState(AttackNames, AttackKeywords);
        resolvedDeath = ResolveState(DeathNames, DeathKeywords);
        resolvedHit = ResolveState(HitNames, HitKeywords);

        if (resolvedWalk < 0 && resolvedRun >= 0) resolvedWalk = resolvedRun;
        if (resolvedRun < 0 && resolvedWalk >= 0) resolvedRun = resolvedWalk;

        desiredLoop = resolvedIdle;
        currentLoopHash = 0;
        oneShotActive = false;
        isDead = false;

        if (resolvedIdle < 0)
        {
            string available = clipNames != null ? string.Join(", ", clipNames) : "none";
            Debug.LogWarning($"[UnitAnimator] No idle animation found on {gameObject.name}. Available clips: {available}");
        }
        if (resolvedWalk < 0)
            Debug.LogWarning($"[UnitAnimator] No walk animation found on {gameObject.name}");
        if (resolvedAttack < 0)
            Debug.LogWarning($"[UnitAnimator] No attack animation found on {gameObject.name}");
    }

    private HashSet<string> clipNames;

    private void CacheClipNames()
    {
        clipNames = new HashSet<string>();
        if (animator.runtimeAnimatorController == null) return;
        foreach (var clip in animator.runtimeAnimatorController.animationClips)
        {
            if (clip != null)
                clipNames.Add(clip.name);
        }
    }

    private int ResolveState(string[][] nameGroups, string[] fallbackKeywords = null)
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

        if (clipNames != null && fallbackKeywords != null)
        {
            foreach (var clipName in clipNames)
            {
                string lower = clipName.ToLowerInvariant();
                foreach (var keyword in fallbackKeywords)
                {
                    if (lower.Contains(keyword))
                    {
                        int hash = Animator.StringToHash(clipName);
                        if (animator.HasState(0, hash))
                            return hash;
                        break;
                    }
                }
            }
        }

        return -1;
    }

    #endregion

    #region Public API

    /// <summary>
    /// Sets desired loop to idle. Safe to call any time -- will NOT interrupt one-shots.
    /// </summary>
    public void PlayIdle()
    {
        if (resolvedIdle >= 0)
            desiredLoop = resolvedIdle;
    }

    /// <summary>
    /// Sets desired loop to walk. Safe to call any time -- will NOT interrupt one-shots.
    /// </summary>
    public void PlayWalk()
    {
        int target = resolvedWalk >= 0 ? resolvedWalk : resolvedRun;
        if (target >= 0)
            desiredLoop = target;
    }

    /// <summary>
    /// Triggers a one-shot attack animation. Ignores call if another one-shot is active.
    /// </summary>
    public void PlayAttack()
    {
        if (resolvedAttack < 0 || isDead) return;
        if (oneShotActive) return;

        oneShotHash = resolvedAttack;
        oneShotActive = true;
        oneShotFallbackTimer = MaxOneShotDuration;
        animator.Play(resolvedAttack, 0, 0f);
    }

    /// <summary>
    /// Triggers a one-shot hit reaction. Ignores call if another one-shot is active.
    /// </summary>
    public void PlayHit()
    {
        if (resolvedHit < 0 || isDead) return;
        if (oneShotActive) return;

        oneShotHash = resolvedHit;
        oneShotActive = true;
        oneShotFallbackTimer = MaxOneShotDuration;
        animator.Play(resolvedHit, 0, 0f);
    }

    /// <summary>
    /// Plays death animation. Overrides everything.
    /// </summary>
    public void PlayDeath()
    {
        if (resolvedDeath < 0) return;
        isDead = true;
        oneShotActive = false;
        desiredLoop = 0;
        currentLoopHash = 0;
        animator.Play(resolvedDeath, 0, 0f);
    }

    #endregion

    #region LateUpdate -- Animation Reconciliation

    private void LateUpdate()
    {
        if (animator == null || isDead) return;

        if (oneShotActive)
        {
            UpdateOneShot();
            return;
        }

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

            // Animator may have transitioned away from our one-shot via controller logic.
            // If the current state doesn't match the one-shot hash and we're not in
            // transition, the controller overrode us -- treat the one-shot as done.
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

        // Re-loop non-looping clips when they finish
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
    public bool HasIdleAnim => resolvedIdle >= 0;
    public bool HasWalkAnim => resolvedWalk >= 0;
    public bool HasAttackAnim => resolvedAttack >= 0;
    public bool HasDeathAnim => resolvedDeath >= 0;
    public bool IsPlayingOneShot => oneShotActive;

    #endregion
}
