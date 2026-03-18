using NUnit.Framework;
using UnityEngine;
using System.Collections.Generic;

[TestFixture]
public class CombatTargetingTests
{
    // ================================================================
    //  GetAttackRange
    // ================================================================

    [Test]
    public void GetAttackRange_Melee_ClampsLow()
    {
        Assert.AreEqual(0.3f, CombatTargeting.GetAttackRange(0f, false), 0.001f);
    }

    [Test]
    public void GetAttackRange_Melee_ClampsHigh()
    {
        Assert.AreEqual(2f, CombatTargeting.GetAttackRange(5f, false), 0.001f);
    }

    [Test]
    public void GetAttackRange_Melee_MidValue()
    {
        Assert.AreEqual(1f, CombatTargeting.GetAttackRange(1f, false), 0.001f);
    }

    [Test]
    public void GetAttackRange_Ranged_ClampsLow()
    {
        Assert.AreEqual(1f, CombatTargeting.GetAttackRange(0.5f, true), 0.001f);
    }

    [Test]
    public void GetAttackRange_Ranged_ClampsHigh()
    {
        Assert.AreEqual(8f, CombatTargeting.GetAttackRange(20f, true), 0.001f);
    }

    [Test]
    public void GetAttackRange_Ranged_MidValue()
    {
        Assert.AreEqual(5f, CombatTargeting.GetAttackRange(5f, true), 0.001f);
    }

    // ================================================================
    //  GetAggroRange
    // ================================================================

    [Test]
    public void GetAggroRange_ClampsToMinimum()
    {
        float result = CombatTargeting.GetAggroRange(0f, 0f);
        Assert.AreEqual(5f, result, 0.001f);
    }

    [Test]
    public void GetAggroRange_ClampsToMaximum()
    {
        float result = CombatTargeting.GetAggroRange(8f, 5f);
        Assert.AreEqual(12f, result, 0.001f);
    }

    [Test]
    public void GetAggroRange_MidValue()
    {
        float result = CombatTargeting.GetAggroRange(2f, 1f);
        Assert.AreEqual(7f, result, 0.001f);
    }


    // ================================================================
    //  IsInRange
    // ================================================================

    [Test]
    public void IsInRange_WithinRange_ReturnsTrue()
    {
        Assert.IsTrue(CombatTargeting.IsInRange(1f, 2f, false));
    }

    [Test]
    public void IsInRange_ExactlyAtRange_ReturnsTrue()
    {
        Assert.IsTrue(CombatTargeting.IsInRange(2f, 2f, false));
    }

    [Test]
    public void IsInRange_OutsideRange_ReturnsFalse()
    {
        Assert.IsFalse(CombatTargeting.IsInRange(3f, 2f, false));
    }

    [Test]
    public void IsInRange_Hysteresis_WasInRange_ExtendsBy15Percent()
    {
        float range = 2f;
        float disengageDist = range + range * 0.15f; // 2.3
        Assert.IsTrue(CombatTargeting.IsInRange(disengageDist, range, true));
        Assert.IsFalse(CombatTargeting.IsInRange(disengageDist + 0.01f, range, true));
    }

    [Test]
    public void IsInRange_NoHysteresis_WasNotInRange()
    {
        Assert.IsFalse(CombatTargeting.IsInRange(2.1f, 2f, false));
    }

    // ================================================================
    //  IsInRangeWithTolerance
    // ================================================================

    [Test]
    public void IsInRangeWithTolerance_AlreadyInRange_ReturnsTrue()
    {
        Assert.IsTrue(CombatTargeting.IsInRangeWithTolerance(1f, 2f, false, false, 0.5f, 0));
    }

    [Test]
    public void IsInRangeWithTolerance_PathNotDone_OutOfRange_ReturnsFalse()
    {
        Assert.IsFalse(CombatTargeting.IsInRangeWithTolerance(3f, 2f, false, false, 0.5f, 0));
    }

    [Test]
    public void IsInRangeWithTolerance_PathDone_WithinTolerance_ReturnsTrue()
    {
        float range = 2f;
        float unitRadius = 0.5f;
        float distWithTolerance = range + unitRadius;
        Assert.IsTrue(CombatTargeting.IsInRangeWithTolerance(distWithTolerance, range, false, true, unitRadius, 0));
    }

    [Test]
    public void IsInRangeWithTolerance_PathDone_StuckRetry_ExpandsTolerance()
    {
        float range = 2f;
        float unitRadius = 0.5f;
        int retries = 1;
        // tolerance = min(0.5 + 1*0.5, 0.15*2+0.5) = min(1.0, 0.8) = 0.8
        float distWithRetryTolerance = range + 0.8f;
        Assert.IsTrue(CombatTargeting.IsInRangeWithTolerance(distWithRetryTolerance, range, false, true, unitRadius, retries));
    }

    [Test]
    public void IsInRangeWithTolerance_PathDone_HighRetryCount_ToleranceCapped()
    {
        float range = 2f;
        float unitRadius = 0.5f;
        int retries = 10;
        // maxTolerance = 0.15*2+0.5 = 0.8
        float distAtCappedTolerance = range + 0.8f;
        Assert.IsTrue(CombatTargeting.IsInRangeWithTolerance(distAtCappedTolerance, range, false, true, unitRadius, retries));
        // beyond the cap should fail
        float distBeyondCap = range + 0.8f + 0.1f;
        Assert.IsFalse(CombatTargeting.IsInRangeWithTolerance(distBeyondCap, range, false, true, unitRadius, retries));
    }

    [Test]
    public void IsInRangeWithTolerance_PathDone_FarBeyondTolerance_ReturnsFalse()
    {
        Assert.IsFalse(CombatTargeting.IsInRangeWithTolerance(10f, 2f, false, true, 0.5f, 0));
    }

    [Test]
    public void IsInRangeWithTolerance_Hysteresis_PathDone()
    {
        float range = 2f;
        float unitRadius = 0.5f;
        float hysteresisRange = range + range * 0.15f; // 2.3
        // tolerance = min(0.5, 0.15*2+0.5) = min(0.5, 0.8) = 0.5
        float dist = hysteresisRange + 0.5f; // 2.8
        Assert.IsTrue(CombatTargeting.IsInRangeWithTolerance(dist, range, true, true, unitRadius, 0));
    }

    [Test]
    public void IsInRangeWithTolerance_LargeUnit_ToleranceCapped()
    {
        float range = 2f;
        float largeRadius = 2.58f;
        float effectiveRange = range + largeRadius; // 4.58
        // maxTolerance = 0.15*4.58+0.5 = 1.187
        // Old formula gave up to unitRadius*2 = 5.16 — way too generous
        float distBeyondNewTolerance = effectiveRange + 2.0f;
        Assert.IsFalse(CombatTargeting.IsInRangeWithTolerance(distBeyondNewTolerance, effectiveRange, false, true, largeRadius, 0),
            "Large melee unit tolerance should be capped well below old unitRadius*2");
    }

    [Test]
    public void IsInRangeWithTolerance_ConsistentWithGetMaxAttackDistance()
    {
        float attackRange = 2f;
        float unitRadius = 2.28f;
        float effectiveRange = CombatTargeting.GetAttackRange(attackRange, false) + unitRadius;
        float maxAttackDist = CombatTargeting.GetMaxAttackDistance(
            CombatTargeting.GetAttackRange(attackRange, false), unitRadius, false);
        // At max retries before blacklist (3), tolerance should not exceed damage validation range
        float maxTolerance = 0.15f * effectiveRange + 0.5f;
        float maxCombatEntry = effectiveRange + maxTolerance;
        Assert.LessOrEqual(maxCombatEntry, maxAttackDist,
            "Max tolerance combat entry must not exceed GetMaxAttackDistance");
    }

    // ================================================================
    //  SelectBestTarget
    // ================================================================

    [Test]
    public void SelectBestTarget_NullList_ReturnsNegOne()
    {
        Assert.AreEqual(-1, CombatTargeting.SelectBestTarget(null));
    }

    [Test]
    public void SelectBestTarget_EmptyList_ReturnsNegOne()
    {
        Assert.AreEqual(-1, CombatTargeting.SelectBestTarget(new List<CombatTargeting.TargetCandidate>()));
    }

    [Test]
    public void SelectBestTarget_SingleValidTarget()
    {
        var list = new List<CombatTargeting.TargetCandidate>
        {
            new CombatTargeting.TargetCandidate { Id = 42, EngageCount = 0 }
        };
        Assert.AreEqual(42, CombatTargeting.SelectBestTarget(list));
    }

    [Test]
    public void SelectBestTarget_PrefersUnderCap()
    {
        var list = new List<CombatTargeting.TargetCandidate>
        {
            new CombatTargeting.TargetCandidate { Id = 1, EngageCount = CombatTargeting.MaxEngagersPerUnit },
            new CombatTargeting.TargetCandidate { Id = 2, EngageCount = 1 }
        };
        Assert.AreEqual(2, CombatTargeting.SelectBestTarget(list));
    }

    [Test]
    public void SelectBestTarget_SkipsDead()
    {
        var list = new List<CombatTargeting.TargetCandidate>
        {
            new CombatTargeting.TargetCandidate { Id = 1, IsDead = true, EngageCount = 0 },
            new CombatTargeting.TargetCandidate { Id = 2, EngageCount = 0 }
        };
        Assert.AreEqual(2, CombatTargeting.SelectBestTarget(list));
    }

    [Test]
    public void SelectBestTarget_SkipsBlacklisted()
    {
        var list = new List<CombatTargeting.TargetCandidate>
        {
            new CombatTargeting.TargetCandidate { Id = 1, IsBlacklisted = true, EngageCount = 0 },
            new CombatTargeting.TargetCandidate { Id = 2, EngageCount = 0 }
        };
        Assert.AreEqual(2, CombatTargeting.SelectBestTarget(list));
    }

    [Test]
    public void SelectBestTarget_AllOverCap_FallsBackToFirst()
    {
        var list = new List<CombatTargeting.TargetCandidate>
        {
            new CombatTargeting.TargetCandidate { Id = 10, EngageCount = CombatTargeting.MaxEngagersPerUnit },
            new CombatTargeting.TargetCandidate { Id = 20, EngageCount = CombatTargeting.MaxEngagersPerUnit + 2 }
        };
        Assert.AreEqual(10, CombatTargeting.SelectBestTarget(list));
    }

    [Test]
    public void SelectBestTarget_AllDeadOrBlacklisted_ReturnsNegOne()
    {
        var list = new List<CombatTargeting.TargetCandidate>
        {
            new CombatTargeting.TargetCandidate { Id = 1, IsDead = true },
            new CombatTargeting.TargetCandidate { Id = 2, IsBlacklisted = true }
        };
        Assert.AreEqual(-1, CombatTargeting.SelectBestTarget(list));
    }

    // ================================================================
    //  EvaluateApproachStall
    // ================================================================

    [Test]
    public void EvaluateApproachStall_LowTimer_Continue()
    {
        Assert.AreEqual(ApproachAction.Continue,
            CombatTargeting.EvaluateApproachStall(1f, 0, false));
    }

    [Test]
    public void EvaluateApproachStall_ExceedsRetryTime_Unit_RetryApproach()
    {
        Assert.AreEqual(ApproachAction.RetryApproach,
            CombatTargeting.EvaluateApproachStall(3.5f, 0, false));
    }

    [Test]
    public void EvaluateApproachStall_ExceedsRetryTime_Structure_RetryApproach()
    {
        Assert.AreEqual(ApproachAction.RetryApproach,
            CombatTargeting.EvaluateApproachStall(2.5f, 0, true));
    }

    [Test]
    public void EvaluateApproachStall_StructureShorterRetryTime()
    {
        Assert.AreEqual(ApproachAction.Continue,
            CombatTargeting.EvaluateApproachStall(1.5f, 0, true));
        Assert.AreEqual(ApproachAction.RetryApproach,
            CombatTargeting.EvaluateApproachStall(2.5f, 0, true));
    }

    [Test]
    public void EvaluateApproachStall_TooManyRetries_Blacklist()
    {
        Assert.AreEqual(ApproachAction.BlacklistAndRetreat,
            CombatTargeting.EvaluateApproachStall(5f, 3, false));
    }

    [Test]
    public void EvaluateApproachStall_CustomRetryTime()
    {
        Assert.AreEqual(ApproachAction.Continue,
            CombatTargeting.EvaluateApproachStall(0.9f, 0, false, 1f));
        Assert.AreEqual(ApproachAction.RetryApproach,
            CombatTargeting.EvaluateApproachStall(1.1f, 0, false, 1f));
    }

    [Test]
    public void EvaluateApproachStall_AtBoundary_RetryCount2_StillRetries()
    {
        Assert.AreEqual(ApproachAction.RetryApproach,
            CombatTargeting.EvaluateApproachStall(5f, 2, false));
    }

    // ================================================================
    //  GetMaxAttackDistance
    // ================================================================

    [Test]
    public void GetMaxAttackDistance_Melee()
    {
        float range = 1.5f;
        float radius = 0.5f;
        float expected = (range + radius) * 1.15f + 0.5f;
        Assert.AreEqual(expected, CombatTargeting.GetMaxAttackDistance(range, radius, false), 0.001f);
    }

    [Test]
    public void GetMaxAttackDistance_Ranged_HasLargerMargin()
    {
        float range = 5f;
        float radius = 0.5f;
        float expected = (range + radius) * 1.15f + 1.0f;
        Assert.AreEqual(expected, CombatTargeting.GetMaxAttackDistance(range, radius, true), 0.001f);
    }

    [Test]
    public void GetMaxAttackDistance_LargeMelee_NoDoubleRadius()
    {
        float range = 2f;
        float radius = 2.58f;
        float result = CombatTargeting.GetMaxAttackDistance(range, radius, false);
        float expected = (range + radius) * 1.15f + 0.5f;
        Assert.AreEqual(expected, result, 0.001f,
            "Large melee should not double-count unit radius in damage validation");
    }

    [Test]
    public void GetMaxAttackDistance_CoversHysteresisRange()
    {
        float range = 2f;
        float radius = 2.28f;
        float effectiveRange = range + radius;
        float hysteresisRange = effectiveRange * 1.15f;
        float maxDist = CombatTargeting.GetMaxAttackDistance(range, radius, false);
        Assert.Greater(maxDist, hysteresisRange,
            "MaxAttackDistance must exceed hysteresis range to prevent attack-block cycle");
    }

    // ================================================================
    //  IsApproachProgressing
    // ================================================================

    [Test]
    public void IsApproachProgressing_Unit_GoodProgress()
    {
        Assert.IsTrue(CombatTargeting.IsApproachProgressing(3f, 4f, false));
    }

    [Test]
    public void IsApproachProgressing_Unit_InsufficientProgress()
    {
        Assert.IsFalse(CombatTargeting.IsApproachProgressing(3.8f, 4f, false));
    }

    [Test]
    public void IsApproachProgressing_Structure_UsesSmallerThreshold()
    {
        // SC2-style: structures use 0.5 threshold (same as units),
        // not 2.0 — boids steering makes large thresholds unreachable
        Assert.IsTrue(CombatTargeting.IsApproachProgressing(3f, 4f, true),
            "1.0 units of progress should count for structures (threshold=0.5)");
        Assert.IsTrue(CombatTargeting.IsApproachProgressing(1.5f, 4f, true),
            "2.5 units of progress should count for structures");
        Assert.IsFalse(CombatTargeting.IsApproachProgressing(3.8f, 4f, true),
            "0.2 units of progress should NOT count for structures (threshold=0.5)");
    }

    [Test]
    public void IsApproachProgressing_SameDistance_NotProgressing()
    {
        Assert.IsFalse(CombatTargeting.IsApproachProgressing(5f, 5f, false));
        Assert.IsFalse(CombatTargeting.IsApproachProgressing(5f, 5f, true));
    }

    // ================================================================
    //  ShouldIncrementApproachStall
    // ================================================================

    [Test]
    public void ShouldIncrementApproachStall_Progressing_ReturnsFalse()
    {
        Assert.IsFalse(CombatTargeting.ShouldIncrementApproachStall(true, false));
    }

    [Test]
    public void ShouldIncrementApproachStall_ProgressingAndMoving_ReturnsFalse()
    {
        Assert.IsFalse(CombatTargeting.ShouldIncrementApproachStall(true, true));
    }

    [Test]
    public void ShouldIncrementApproachStall_NotProgressingButMoving_ReturnsFalse()
    {
        // Walking laterally to reach attack slot — should not penalize
        Assert.IsFalse(CombatTargeting.ShouldIncrementApproachStall(false, true));
    }

    [Test]
    public void ShouldIncrementApproachStall_NotProgressingAndStationary_ReturnsTrue()
    {
        // Stuck: not getting closer AND not physically moving
        Assert.IsTrue(CombatTargeting.ShouldIncrementApproachStall(false, false));
    }

    // ================================================================
    //  GetBlacklistDuration
    // ================================================================

    [Test]
    public void GetBlacklistDuration_NearTarget_ShortDuration()
    {
        float effectiveRange = 2f;
        float nearDist = effectiveRange * 2f; // within 3x
        Assert.AreEqual(2f, CombatTargeting.GetBlacklistDuration(nearDist, effectiveRange));
    }

    [Test]
    public void GetBlacklistDuration_FarTarget_LongDuration()
    {
        float effectiveRange = 2f;
        float farDist = effectiveRange * 5f; // beyond 3x
        Assert.AreEqual(8f, CombatTargeting.GetBlacklistDuration(farDist, effectiveRange));
    }

    [Test]
    public void GetBlacklistDuration_AtBoundary_ShortDuration()
    {
        float effectiveRange = 2f;
        float boundaryDist = effectiveRange * 3f - 0.01f; // just under 3x
        Assert.AreEqual(2f, CombatTargeting.GetBlacklistDuration(boundaryDist, effectiveRange));
    }

    [Test]
    public void GetBlacklistDuration_ExactlyAtBoundary_LongDuration()
    {
        float effectiveRange = 2f;
        float exactBoundary = effectiveRange * 3f; // exactly 3x — NOT less than
        Assert.AreEqual(8f, CombatTargeting.GetBlacklistDuration(exactBoundary, effectiveRange));
    }

    // ================================================================
    //  ShouldPrioritySwitchFromBuilding
    // ================================================================

    [Test]
    public void ShouldPrioritySwitchFromBuilding_AttackingBuilding_NearbyUnit_ReturnsTrue()
    {
        Assert.IsTrue(CombatTargeting.ShouldPrioritySwitchFromBuilding(true, true));
    }

    [Test]
    public void ShouldPrioritySwitchFromBuilding_AttackingUnit_ReturnsFalse()
    {
        Assert.IsFalse(CombatTargeting.ShouldPrioritySwitchFromBuilding(false, true));
    }

    [Test]
    public void ShouldPrioritySwitchFromBuilding_NoNearbyUnits_ReturnsFalse()
    {
        Assert.IsFalse(CombatTargeting.ShouldPrioritySwitchFromBuilding(true, false));
    }
}
