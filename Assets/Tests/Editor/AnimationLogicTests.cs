using NUnit.Framework;

[TestFixture]
public class AnimationLogicTests
{
    // ================================================================
    //  ComputeAttackSpeed
    // ================================================================

    [Test]
    public void ComputeAttackSpeed_ClipMatchesCooldown_Returns1()
    {
        float result = AnimationLogic.ComputeAttackSpeed(1f, 1f);
        Assert.AreEqual(1f, result, 0.001f);
    }

    [Test]
    public void ComputeAttackSpeed_ClipLongerThanCooldown_SpeedsUp()
    {
        // clip=2, cooldown=1 => ratio=2 (speed up to fit)
        float result = AnimationLogic.ComputeAttackSpeed(2f, 1f);
        Assert.AreEqual(2f, result, 0.001f);
    }

    [Test]
    public void ComputeAttackSpeed_ClipShorterThanCooldown_SlowsDown()
    {
        // clip=1, cooldown=2 => ratio=0.5 (at min clamp)
        float result = AnimationLogic.ComputeAttackSpeed(1f, 2f);
        Assert.AreEqual(0.5f, result, 0.001f);
    }

    [Test]
    public void ComputeAttackSpeed_ClampsToMax()
    {
        // clip=10, cooldown=1 => ratio=10, clamped to 3
        float result = AnimationLogic.ComputeAttackSpeed(10f, 1f);
        Assert.AreEqual(AnimationLogic.MaxAnimSpeed, result, 0.001f);
    }

    [Test]
    public void ComputeAttackSpeed_ClampsToMin()
    {
        // clip=0.1, cooldown=10 => ratio=0.01, clamped to 0.5
        float result = AnimationLogic.ComputeAttackSpeed(0.1f, 10f);
        Assert.AreEqual(AnimationLogic.MinAnimSpeed, result, 0.001f);
    }

    [Test]
    public void ComputeAttackSpeed_ZeroCooldown_Returns1()
    {
        float result = AnimationLogic.ComputeAttackSpeed(1f, 0f);
        Assert.AreEqual(1f, result);
    }

    [Test]
    public void ComputeAttackSpeed_NearZeroCooldown_Returns1()
    {
        float result = AnimationLogic.ComputeAttackSpeed(1f, 0.005f);
        Assert.AreEqual(1f, result);
    }

    [Test]
    public void ComputeAttackSpeed_ZeroClipLength_Returns1()
    {
        float result = AnimationLogic.ComputeAttackSpeed(0f, 1f);
        Assert.AreEqual(1f, result);
    }

    [Test]
    public void ComputeAttackSpeed_BothZero_Returns1()
    {
        float result = AnimationLogic.ComputeAttackSpeed(0f, 0f);
        Assert.AreEqual(1f, result);
    }

    [Test]
    public void ComputeAttackSpeed_NegativeValues_Returns1()
    {
        float result = AnimationLogic.ComputeAttackSpeed(-1f, 1f);
        Assert.AreEqual(1f, result);
    }

    // ================================================================
    //  ComputeWalkSpeedRatio
    // ================================================================

    [Test]
    public void ComputeWalkSpeedRatio_MatchesBase_Returns1()
    {
        float result = AnimationLogic.ComputeWalkSpeedRatio(3.5f, 3.5f);
        Assert.AreEqual(1f, result, 0.001f);
    }

    [Test]
    public void ComputeWalkSpeedRatio_DoubleMoveSpeed_Returns2()
    {
        float result = AnimationLogic.ComputeWalkSpeedRatio(7f, 3.5f);
        Assert.AreEqual(2f, result, 0.001f);
    }

    [Test]
    public void ComputeWalkSpeedRatio_HalfMoveSpeed_ReturnsHalf()
    {
        float result = AnimationLogic.ComputeWalkSpeedRatio(1.75f, 3.5f);
        Assert.AreEqual(0.5f, result, 0.001f);
    }

    [Test]
    public void ComputeWalkSpeedRatio_ClampsToMax()
    {
        float result = AnimationLogic.ComputeWalkSpeedRatio(100f, 3.5f);
        Assert.AreEqual(AnimationLogic.MaxWalkSpeedRatio, result, 0.001f);
    }

    [Test]
    public void ComputeWalkSpeedRatio_ClampsToMin()
    {
        float result = AnimationLogic.ComputeWalkSpeedRatio(0.1f, 3.5f);
        Assert.AreEqual(AnimationLogic.MinWalkSpeedRatio, result, 0.001f);
    }

    [Test]
    public void ComputeWalkSpeedRatio_ZeroMoveSpeed_Returns1()
    {
        float result = AnimationLogic.ComputeWalkSpeedRatio(0f, 3.5f);
        Assert.AreEqual(1f, result);
    }

    [Test]
    public void ComputeWalkSpeedRatio_ZeroBaseSpeed_Returns1()
    {
        float result = AnimationLogic.ComputeWalkSpeedRatio(3.5f, 0f);
        Assert.AreEqual(1f, result);
    }

    [Test]
    public void ComputeWalkSpeedRatio_BothZero_Returns1()
    {
        float result = AnimationLogic.ComputeWalkSpeedRatio(0f, 0f);
        Assert.AreEqual(1f, result);
    }

    // ================================================================
    //  GetDeathDuration
    // ================================================================

    [Test]
    public void GetDeathDuration_NoDeathAnim_ReturnsFallback()
    {
        float result = AnimationLogic.GetDeathDuration(false, 2f, 3f);
        Assert.AreEqual(3f, result);
    }

    [Test]
    public void GetDeathDuration_HasDeathAnim_ReturnsClampedLength()
    {
        float result = AnimationLogic.GetDeathDuration(true, 2f, 3f);
        Assert.AreEqual(2f, result, 0.001f);
    }

    [Test]
    public void GetDeathDuration_ClipTooShort_ClampsToMin()
    {
        float result = AnimationLogic.GetDeathDuration(true, 0.1f, 3f);
        Assert.AreEqual(AnimationLogic.MinDeathDuration, result, 0.001f);
    }

    [Test]
    public void GetDeathDuration_ClipTooLong_ClampsToMax()
    {
        float result = AnimationLogic.GetDeathDuration(true, 20f, 3f);
        Assert.AreEqual(AnimationLogic.MaxDeathDuration, result, 0.001f);
    }

    [Test]
    public void GetDeathDuration_ZeroClip_ClampsToMin()
    {
        float result = AnimationLogic.GetDeathDuration(true, 0f, 3f);
        Assert.AreEqual(AnimationLogic.MinDeathDuration, result, 0.001f);
    }

    // ================================================================
    //  ClassifyClip
    // ================================================================

    [Test]
    public void ClassifyClip_AllAttackKeywords_ReturnAttack()
    {
        string[] attackClips = { "HumanAttack01", "spider_bite", "Bear_Claw_Swipe",
            "Slash_Heavy", "golem_punch", "Sword_Strike", "axe_swing_01",
            "Giant_Stomp", "AcidSpit", "scorpion_sting", "hammer_smash" };

        foreach (var clip in attackClips)
            Assert.AreEqual(ClipCategory.Attack, AnimationLogic.ClassifyClip(clip), $"'{clip}' should be Attack");
    }

    [Test]
    public void ClassifyClip_DeathKeywords_ReturnDeath()
    {
        Assert.AreEqual(ClipCategory.Death, AnimationLogic.ClassifyClip("UnitDeath_01"));
        Assert.AreEqual(ClipCategory.Death, AnimationLogic.ClassifyClip("Creature_Die"));
    }

    [Test]
    public void ClassifyClip_NonCombat_ReturnsUnknown()
    {
        Assert.AreEqual(ClipCategory.Unknown, AnimationLogic.ClassifyClip("Idle_Relax"));
        Assert.AreEqual(ClipCategory.Unknown, AnimationLogic.ClassifyClip("Walk_Forward"));
        Assert.AreEqual(ClipCategory.Unknown, AnimationLogic.ClassifyClip(""));
    }

    [Test]
    public void ClassifyClip_CaseInsensitive_AttackPriorityOverDeath()
    {
        Assert.AreEqual(ClipCategory.Attack, AnimationLogic.ClassifyClip("HEAVY_ATTACK"));
        Assert.AreEqual(ClipCategory.Death, AnimationLogic.ClassifyClip("DEATH_Explosion"));
        Assert.AreEqual(ClipCategory.Attack, AnimationLogic.ClassifyClip("attack_death_combo"));
    }

    // ================================================================
    //  ResolveLoopStates
    // ================================================================

    [Test]
    public void ResolveLoopStates_BothExist_NoChange()
    {
        var result = AnimationLogic.ResolveLoopStates(true, true, 100, 200);
        Assert.AreEqual(100, result.idleHash);
        Assert.AreEqual(200, result.walkHash);
        Assert.IsTrue(result.hasIdle);
        Assert.IsTrue(result.hasWalk);
    }

    [Test]
    public void ResolveLoopStates_OnlyIdle_WalkFallsBackToIdle()
    {
        var result = AnimationLogic.ResolveLoopStates(true, false, 100, 0);
        Assert.AreEqual(100, result.idleHash);
        Assert.AreEqual(100, result.walkHash, "Walk should fall back to idle hash");
        Assert.IsTrue(result.hasIdle);
        Assert.IsTrue(result.hasWalk, "hasWalk should be true after fallback");
    }

    [Test]
    public void ResolveLoopStates_OnlyWalk_IdleFallsBackToWalk()
    {
        var result = AnimationLogic.ResolveLoopStates(false, true, 0, 200);
        Assert.AreEqual(200, result.idleHash, "Idle should fall back to walk hash");
        Assert.AreEqual(200, result.walkHash);
        Assert.IsTrue(result.hasIdle, "hasIdle should be true after fallback");
        Assert.IsTrue(result.hasWalk);
    }

    [Test]
    public void ResolveLoopStates_NeitherExist_BothStayFalse()
    {
        var result = AnimationLogic.ResolveLoopStates(false, false, 0, 0);
        Assert.AreEqual(0, result.idleHash);
        Assert.AreEqual(0, result.walkHash);
        Assert.IsFalse(result.hasIdle);
        Assert.IsFalse(result.hasWalk);
    }

    // ================================================================
    //  EvaluateOneShot
    // ================================================================

    [Test]
    public void EvaluateOneShot_Timeout_ReturnsTimeout()
    {
        var result = AnimationLogic.EvaluateOneShot(
            fallbackTimer: 0f, isInTransition: false,
            currentStateHash: 42, expectedHash: 42, normalizedTime: 0.5f);
        Assert.AreEqual(AnimationLogic.OneShotResult.Timeout, result);
    }

    [Test]
    public void EvaluateOneShot_NegativeTimer_ReturnsTimeout()
    {
        var result = AnimationLogic.EvaluateOneShot(
            fallbackTimer: -1f, isInTransition: false,
            currentStateHash: 42, expectedHash: 42, normalizedTime: 0.5f);
        Assert.AreEqual(AnimationLogic.OneShotResult.Timeout, result);
    }

    [Test]
    public void EvaluateOneShot_CompletedNormally_ReturnsCompleted()
    {
        var result = AnimationLogic.EvaluateOneShot(
            fallbackTimer: 3f, isInTransition: false,
            currentStateHash: 42, expectedHash: 42, normalizedTime: 1.0f);
        Assert.AreEqual(AnimationLogic.OneShotResult.Completed, result);
    }

    [Test]
    public void EvaluateOneShot_BeyondNormalized_ReturnsCompleted()
    {
        var result = AnimationLogic.EvaluateOneShot(
            fallbackTimer: 3f, isInTransition: false,
            currentStateHash: 42, expectedHash: 42, normalizedTime: 1.5f);
        Assert.AreEqual(AnimationLogic.OneShotResult.Completed, result);
    }

    [Test]
    public void EvaluateOneShot_StateMismatch_AfterGracePeriod_ReturnsMismatch()
    {
        // timer < MaxOneShotDuration - 0.1 means grace period has passed
        var result = AnimationLogic.EvaluateOneShot(
            fallbackTimer: 4.0f, isInTransition: false,
            currentStateHash: 99, expectedHash: 42, normalizedTime: 0f);
        Assert.AreEqual(AnimationLogic.OneShotResult.StateMismatch, result);
    }

    [Test]
    public void EvaluateOneShot_StateMismatch_DuringGracePeriod_ReturnsContinue()
    {
        // timer >= MaxOneShotDuration - 0.1 (4.9) means still in grace period
        var result = AnimationLogic.EvaluateOneShot(
            fallbackTimer: 4.95f, isInTransition: false,
            currentStateHash: 99, expectedHash: 42, normalizedTime: 0f);
        Assert.AreEqual(AnimationLogic.OneShotResult.Continue, result);
    }

    [Test]
    public void EvaluateOneShot_InTransition_ReturnsContinue()
    {
        var result = AnimationLogic.EvaluateOneShot(
            fallbackTimer: 3f, isInTransition: true,
            currentStateHash: 42, expectedHash: 42, normalizedTime: 1.0f);
        Assert.AreEqual(AnimationLogic.OneShotResult.Continue, result);
    }

    [Test]
    public void EvaluateOneShot_InTransition_TimerExpired_ReturnsTimeout()
    {
        // Timeout takes priority over transition
        var result = AnimationLogic.EvaluateOneShot(
            fallbackTimer: 0f, isInTransition: true,
            currentStateHash: 0, expectedHash: 42, normalizedTime: 0f);
        Assert.AreEqual(AnimationLogic.OneShotResult.Timeout, result);
    }

    [Test]
    public void EvaluateOneShot_StillPlaying_ReturnsContinue()
    {
        var result = AnimationLogic.EvaluateOneShot(
            fallbackTimer: 3f, isInTransition: false,
            currentStateHash: 42, expectedHash: 42, normalizedTime: 0.5f);
        Assert.AreEqual(AnimationLogic.OneShotResult.Continue, result);
    }

    [Test]
    public void EvaluateOneShot_JustStarted_ReturnsContinue()
    {
        var result = AnimationLogic.EvaluateOneShot(
            fallbackTimer: 5f, isInTransition: false,
            currentStateHash: 42, expectedHash: 42, normalizedTime: 0f);
        Assert.AreEqual(AnimationLogic.OneShotResult.Continue, result);
    }

    // ================================================================
    //  ShouldApplyWalkSpeed
    // ================================================================

    [Test]
    public void ShouldApplyWalkSpeed_WalkingWithSpeed_ReturnsTrue()
    {
        Assert.IsTrue(AnimationLogic.ShouldApplyWalkSpeed(
            currentLoopHash: 200, walkHash: 200, idleHash: 100, moveSpeed: 5f));
    }

    [Test]
    public void ShouldApplyWalkSpeed_WalkEqualsIdle_ReturnsFalse()
    {
        // When walk and idle share the same hash (fallback), don't scale
        Assert.IsFalse(AnimationLogic.ShouldApplyWalkSpeed(
            currentLoopHash: 100, walkHash: 100, idleHash: 100, moveSpeed: 5f));
    }

    [Test]
    public void ShouldApplyWalkSpeed_ZeroMoveSpeed_ReturnsFalse()
    {
        Assert.IsFalse(AnimationLogic.ShouldApplyWalkSpeed(
            currentLoopHash: 200, walkHash: 200, idleHash: 100, moveSpeed: 0f));
    }

    [Test]
    public void ShouldApplyWalkSpeed_NearZeroMoveSpeed_ReturnsFalse()
    {
        Assert.IsFalse(AnimationLogic.ShouldApplyWalkSpeed(
            currentLoopHash: 200, walkHash: 200, idleHash: 100, moveSpeed: 0.005f));
    }

    [Test]
    public void ShouldApplyWalkSpeed_NotWalking_ReturnsFalse()
    {
        Assert.IsFalse(AnimationLogic.ShouldApplyWalkSpeed(
            currentLoopHash: 100, walkHash: 200, idleHash: 100, moveSpeed: 5f));
    }

    [Test]
    public void ShouldApplyWalkSpeed_DifferentLoopHash_ReturnsFalse()
    {
        Assert.IsFalse(AnimationLogic.ShouldApplyWalkSpeed(
            currentLoopHash: 300, walkHash: 200, idleHash: 100, moveSpeed: 5f));
    }

    // ================================================================
    //  Grace period boundaries
    // ================================================================

    [Test]
    public void EvaluateOneShot_GracePeriodBoundary_ContinueThenMismatch()
    {
        float timer = AnimationLogic.MaxOneShotDuration - 0.1f;
        Assert.AreEqual(AnimationLogic.OneShotResult.Continue,
            AnimationLogic.EvaluateOneShot(timer, false, 99, 42, 0f),
            "At grace boundary, should continue");

        float belowGrace = timer - 0.001f;
        Assert.AreEqual(AnimationLogic.OneShotResult.StateMismatch,
            AnimationLogic.EvaluateOneShot(belowGrace, false, 99, 42, 0f),
            "Below grace boundary, should detect mismatch");
    }
}
