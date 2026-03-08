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

    private int activeLoopHash;
    private bool isDead;
    private bool playingOneShot;
    private float oneShotTimer;

    private const float BlendTime = 0.15f;

    // --- Heroic Fantasy Creatures Pack naming conventions ---
    // Goblin/Orc: idleSwordShield, walkNormalSwordShield, attack1SwordShield, deathSwordShield
    // Troll/Harpy: idle, walk, attack1, death (all lowercase)
    // Werewolf/Vampire: idleBreathe, walk, clawsAttackLeft, death
    // Spider: IdleNormal, CrawlNormal, Bite, DeathNormal
    // Viper: Idle, Crawl, JumpBite, Death
    // Dragonide: IdleUnarmed, WalkUnarmed, DeathUnarmed
    // LizardWarrior: IdleSpear, WalkSpear, Attack1Spear, DeathSpear
    // Undead: idleNormal, walkWeapon, attack1Weapon, death1
    // Kobold: idleCombat, walkNormal, attack1, death
    // Cyclops: idleLookAround, walk, leftHandAttackForward, death
    // Griffin/MountainDragon: Idle, Walk, JumpBite/SpitFireBall, Death
    // OakTreeEnt: IdleBreathe, Walk, ClawsAttackL, Death
    // Chimera: idle, walk, ramAttack, death
    // Ghoul/Mummy: Idle, Walk, Attack1, Death
    // Hydra: IdleBreathe, Walk, Bite3HitCombo, Death
    //
    // --- Monsters Ultimate Pack 11 naming conventions ---
    // Mushroom: Idle/Idle 0-5, Walk Forward In Place, Head Attack, Die
    // Bee: Idle/Idle 0-6, Fly Forward In Place, Sting Projectile Attack, Die
    // Plant Monster: Idle/Idle 0-7, Move Forward In Place, Projectile Attack, Die
    // Dark Wizard: Idle/Idle 0-6, Fly Forward In Place, Slash Attack, Die
    // Eyeball Monster: Idle/Idle 0-6, Fly Forward In Place, Projectile Attack, Die

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
            // Generic simple names (lowercase + capitalized)
            "attack1", "attack2", "attack3",
            "Attack1", "Attack2", "Attack3",
            // Bite / jaw attacks
            "Bite", "BiteForward", "BiteAttack", "JumpBite",
            "JumpBiteNormal", "JumpBiteThreat",
            "snakeBiteAttack", "biteAttackBareHands",
            "Bite3HitCombo",
            // Claw attacks (all naming variants)
            "clawsAttackLeft", "clawsAttackRight",
            "ClawsAttackLeft", "ClawsAttackRight",
            "clawsAttackL", "clawsAttackR",
            "ClawsAttackL", "ClawsAttackR",
            "LeftClawsAttack", "RightClawsAttack",
            "clawsAttack2HitCombo",
            "ClawsLeftAttackCombat", "ClawsRightAttackCombat",
            // Weapon-specific attacks
            "attack1SwordShield", "attack1Daggers",
            "attack1Weapon", "attack2Weapon", "attack3Weapon",
            "Attack1Spear", "Attack2Spear", "Attack3Spear",
            "throwSpear", "TongueAttackSpear",
            // Combos
            "2HitComboSwordShield", "2HitComboDaggers",
            "2HitComboAttack", "3HitComboAttack",
            "2HitComboA", "2HitComboB",
            "3HitComboA", "3HitComboB",
            "2HitComboForward",
            // Ranged / spell / projectile
            "Head Attack", "Projectile Attack", "Sting Projectile Attack",
            "Poison AOE Attack", "Cast Spell",
            "SpitFireBall", "SpitVenom", "3toxicSpitCombo",
            "shootSlingshot",
            "Slash Attack", "Dash Attack In Place",
            "Tentacles Attack", "Eyebeam Attack",
            // Heavy / special attacks
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

    private static readonly string[] IdleKeywords = { "idle", "breathe", "lookaround", "rest", "stand" };
    private static readonly string[] WalkKeywords = { "walk", "move", "crawl", "fly forward", "gallop" };
    private static readonly string[] RunKeywords = { "run", "sprint" };
    private static readonly string[] AttackKeywords = { "attack", "bite", "claw", "slash", "hit combo", "spit", "sting", "stomp", "smash", "throw", "cast", "projectile" };
    private static readonly string[] DeathKeywords = { "die", "death" };
    private static readonly string[] HitKeywords = { "hit", "damage", "hurt" };

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

        // Fallback: try every clip name as a potential state name if it matches keywords
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

    private void LateUpdate()
    {
        if (animator == null || isDead) return;

        if (playingOneShot)
        {
            oneShotTimer -= Time.deltaTime;
            if (oneShotTimer <= 0f)
            {
                playingOneShot = false;
                if (activeLoopHash != 0)
                    animator.CrossFadeInFixedTime(activeLoopHash, BlendTime, 0);
                else if (resolvedIdle >= 0)
                    StartLoop(resolvedIdle);
            }
            return;
        }

        if (activeLoopHash != 0 && !animator.IsInTransition(0))
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            if (info.normalizedTime >= 1f)
                animator.Play(activeLoopHash, 0, 0f);
        }
    }

    public void PlayIdle()
    {
        if (resolvedIdle >= 0)
        {
            playingOneShot = false;
            StartLoop(resolvedIdle);
        }
    }

    public void PlayWalk()
    {
        int target = resolvedWalk >= 0 ? resolvedWalk : resolvedRun;
        if (target >= 0)
        {
            playingOneShot = false;
            StartLoop(target);
        }
    }

    public void PlayAttack()
    {
        if (resolvedAttack < 0) return;
        activeLoopHash = resolvedIdle >= 0 ? resolvedIdle : 0;
        playingOneShot = true;

        animator.CrossFadeInFixedTime(resolvedAttack, BlendTime, 0);
        animator.Update(0f);
        var info = animator.GetCurrentAnimatorStateInfo(0);
        float clipLen = info.length > 0f ? info.length : 1f;
        oneShotTimer = clipLen + BlendTime;
    }

    public void PlayDeath()
    {
        if (resolvedDeath < 0) return;
        isDead = true;
        playingOneShot = false;
        activeLoopHash = 0;
        animator.CrossFadeInFixedTime(resolvedDeath, BlendTime, 0);
    }

    public void PlayHit()
    {
        if (resolvedHit < 0 || isDead || playingOneShot) return;
        playingOneShot = true;

        animator.CrossFadeInFixedTime(resolvedHit, BlendTime * 0.5f, 0);
        animator.Update(0f);
        var info = animator.GetCurrentAnimatorStateInfo(0);
        float clipLen = info.length > 0f ? info.length : 0.5f;
        oneShotTimer = clipLen + BlendTime;
    }

    private void StartLoop(int stateHash)
    {
        if (activeLoopHash == stateHash && !playingOneShot) return;
        activeLoopHash = stateHash;
        animator.CrossFadeInFixedTime(stateHash, BlendTime, 0);
    }

    public bool HasAnimator => animator != null && animator.runtimeAnimatorController != null;
    public bool HasIdleAnim => resolvedIdle >= 0;
    public bool HasWalkAnim => resolvedWalk >= 0;
    public bool HasAttackAnim => resolvedAttack >= 0;
    public bool HasDeathAnim => resolvedDeath >= 0;
}
