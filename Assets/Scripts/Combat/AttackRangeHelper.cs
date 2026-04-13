using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// World-space attack range + attack-position helper.
///
/// All geometry is in world units on the XZ plane (Y is ignored). A target's
/// shape is given by its <see cref="IAttackable.WorldBounds"/> — a point for
/// units (tiny AABB) or a rectangle for buildings/castles.
///
/// An attacker is "in range" when:
///     distance(attackerPos, closestPointOnTargetBoundsXZ) &lt;= attackerRadius + attackRange
/// where attackRange is the unit's <see cref="UnitData.attackRange"/>.
/// </summary>
public static class AttackRangeHelper
{
    private const float BandExtraReachStep = 1.5f;
    private const float MinSlotWidth = 0.8f;
    private const float MeleePreferredExtraReach = 0.15f;
    private const float RangedPreferredExtraReachFactor = 0.75f;
    private const float ExtendedMeleePreferredExtraReachFactor = 0.45f;
    private const float QueueOverflowBands = 2f;
    private const float SlotPaddingWorld = 0.05f;
    private const float ShapeEpsilon = 0.001f;

    // ================================================================
    //  RANGE CHECKS
    // ================================================================

    /// <summary>
    /// XZ distance from <paramref name="attackerPos"/> to the closest point
    /// on the target's body surface. Uses sphere math when the target
    /// exposes a non-zero <see cref="IAttackable.TargetRadius"/> (units,
    /// which are round bodies) and the cube-closest-point fallback for
    /// axis-aligned structures (buildings, castles — their collider is
    /// a real box).
    ///
    /// Using sphere math for round targets is essential: the old cube
    /// fallback returned corner distances for diagonal approaches, which
    /// over-reported the attacker's reach by up to √2·r. Attackers would
    /// "in-range-lock" far from the correct kissing distance and clump
    /// on one side of the target instead of distributing around the ring.
    ///
    /// Ignores Y so vertical differences between large buildings and
    /// ground units don't skew the check.
    /// </summary>
    public static float DistanceToTarget(Vector3 attackerPos, IAttackable target)
    {
        if (target.TargetRadius > 0.01f)
        {
            // Round body — sphere distance.
            Vector3 centerToAttacker = target.Position - attackerPos;
            centerToAttacker.y = 0f;
            float centerDist = centerToAttacker.magnitude;
            return Mathf.Max(0f, centerDist - target.TargetRadius);
        }

        // Extended body — cube closest-point.
        var b = target.WorldBounds;
        Vector3 sample = new(attackerPos.x, b.center.y, attackerPos.z);
        Vector3 closest = b.ClosestPoint(sample);
        float dx = closest.x - attackerPos.x;
        float dz = closest.z - attackerPos.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// Returns true when the attacker can hit the target right now.
    /// A small tolerance (+0.05) is added so frame-to-frame jitter doesn't
    /// bounce the unit out of range while it's actively attacking.
    /// </summary>
    public static bool IsTargetInRange(
        Vector3 attackerPos, float attackerRadius, float attackRange, IAttackable target)
    {
        if (target == null || target.gameObject == null) return false;
        float dist = DistanceToTarget(attackerPos, target);
        return dist <= attackerRadius + attackRange + 0.05f;
    }

    // ================================================================
    //  ATTACKER CAPACITY — arc-based hexagonal kissing packing
    // ================================================================

    /// <summary>
    /// Padding multiplier applied to each attacker's effective body
    /// radius when computing ring capacity and ring positions.
    ///
    /// Serves TWO functions — the second is the important one:
    ///
    ///   1. A small safety margin for RVO solver drift so locked
    ///      attackers don't end up with tiny physical overlap at the
    ///      exact kissing distance (minor — the unified-radius fix
    ///      mostly handles this).
    ///
    ///   2. WALKABILITY. With pad = 1.0 the ring is a solid wall of
    ///      touching locked bodies, and an attacker assigned to slot
    ///      N cannot walk around the ring to reach slot N if slots
    ///      0..N-1 are already locked between it and the empty spot.
    ///      It gets pinned against the back of the wall and the ring
    ///      never fills uniformly. A small gap between adjacent slots
    ///      lets late arrivers squeeze past locked attackers to reach
    ///      the open side.
    ///
    /// The cost is one capacity slot in the equal-size case: 10%
    /// padding gives 5 equal-size attackers instead of the geometric
    /// max of 6, but the ring actually forms uniformly. For unequal
    /// sizes (e.g. footman vs goblin), the ratio of lost slots is
    /// typically 1 in 12-13. Trade-off is worth it — "5 clean" beats
    /// "6 broken".
    /// </summary>
    public const float RingPaddingMultiplier = 1.10f;

    /// <summary>
    /// Angular arc (in radians) that an attacker of the given body radius
    /// occupies on the target's kissing ring. Computed geometrically with
    /// a <see cref="RingPaddingMultiplier"/> buffer: the attacker is
    /// treated as slightly larger than its visual body so adjacent
    /// attackers in a tight ring still have a small gap between them
    /// after RVO precision drift.
    ///
    /// Summing these arcs over the current attackers on a target and
    /// comparing against <c>2π</c> tells us whether one more fits —
    /// correctly handling MIXED sizes (small units take small arcs, big
    /// units take big arcs).
    /// </summary>
    public static float GetArcRequiredFor(IAttackable target, float attackerRadius)
    {
        if (target == null || attackerRadius < 0.01f) return 0f;

        float paddedAttackerRadius = attackerRadius * RingPaddingMultiplier;

        // Prefer TargetRadius (sphere) when the target is a round body.
        // Fall back to the larger horizontal half-extent for buildings
        // whose round "equivalent radius" isn't set.
        float targetRadius = target.TargetRadius;
        if (targetRadius < 0.01f)
        {
            var bounds = target.WorldBounds;
            targetRadius = Mathf.Max(bounds.extents.x, bounds.extents.z);
        }

        float ringRadius = targetRadius + paddedAttackerRadius;
        if (ringRadius < 0.01f) return 2f * Mathf.PI;

        float sinHalfAngle = paddedAttackerRadius / ringRadius;
        if (sinHalfAngle >= 1f) return 2f * Mathf.PI;

        return 2f * Mathf.Asin(sinHalfAngle);
    }

    /// <summary>
    /// Total angular arc currently occupied on <paramref name="target"/>
    /// by team-<paramref name="attackerTeam"/> units whose combat target
    /// is this target. Sums each committed attacker's own
    /// <see cref="GetArcRequiredFor"/> so mixed sizes are accounted for
    /// correctly. O(teamSize) per call, negligible for our unit counts.
    /// </summary>
    public static float GetArcOccupiedOn(IAttackable target, int attackerTeam)
    {
        if (target == null || target.gameObject == null) return 0f;
        if (UnitManager.Instance == null) return 0f;

        float total = 0f;
        var teamUnits = UnitManager.Instance.GetTeamUnits(attackerTeam);
        for (int i = 0; i < teamUnits.Count; i++)
        {
            var u = teamUnits[i];
            if (u == null || u.IsDead) continue;
            var c = u.Combat;
            if (c == null) continue;
            var t = c.CurrentTarget;
            if (t == null || t.gameObject == null) continue;
            if (t.gameObject != target.gameObject) continue;
            total += GetArcRequiredFor(target, u.EffectiveRadius);
        }
        return total;
    }

    /// <summary>
    /// Returns a world position on the target's kissing ring at the
    /// given angle (radians, 0 = +X, π/2 = +Z). UnitCombat assigns each
    /// committer a distinct angle at Scan time and uses this helper to
    /// compute the destination point it walks toward, so each locked
    /// attacker ends up at a unique ring slot and bodies don't overlap.
    /// </summary>
    public static Vector3 GetRingPosition(IAttackable target, float attackerRadius, float angleRadians)
    {
        if (target == null) return Vector3.zero;
        float targetRadius = target.TargetRadius;
        if (targetRadius < 0.01f)
        {
            var bounds = target.WorldBounds;
            targetRadius = Mathf.Max(bounds.extents.x, bounds.extents.z);
        }
        // Match GetArcRequiredFor's padded radius so the arc math and the
        // actual ring position agree — without this, adjacent attackers at
        // their assigned angles would be exactly kissing (chord = 2r) and
        // any RVO precision error would push them into overlap.
        float ringRadius = targetRadius + attackerRadius * RingPaddingMultiplier;
        Vector3 center = target.Position;
        float cx = center.x + Mathf.Cos(angleRadians) * ringRadius;
        float cz = center.z + Mathf.Sin(angleRadians) * ringRadius;
        return new Vector3(cx, center.y, cz);
    }

    // ================================================================
    //  PERIMETER WALK — rectangle & circle positioning
    // ================================================================

    /// <summary>
    /// Returns a world position on the perimeter of a rectangle defined
    /// by <paramref name="bounds"/>, at arc-length parameter
    /// <paramref name="t"/> ∈ [0, perimeter). Walks the edges
    /// clockwise from the SW corner: south → east → north → west.
    ///
    /// Then steps outward by <paramref name="buffer"/> so the position
    /// sits just outside the building footprint / navmesh cut.
    /// </summary>
    public static Vector3 RectPerimeterPoint(Bounds bounds, float t, float buffer)
    {
        float w = bounds.extents.x * 2f; // full width
        float h = bounds.extents.z * 2f; // full height
        float perim = 2f * (w + h);
        t = ((t % perim) + perim) % perim; // wrap to [0, perim)

        float cx = bounds.center.x;
        float cy = bounds.center.y;
        float cz = bounds.center.z;
        float hw = bounds.extents.x;
        float hh = bounds.extents.z;

        float px, pz, nx, nz; // position on edge + outward normal

        if (t < w)
        {
            // South edge: SW → SE (walking +X)
            px = cx - hw + t;
            pz = cz - hh;
            nx = 0f; nz = -1f;
        }
        else if (t < w + h)
        {
            // East edge: SE → NE (walking +Z)
            float d = t - w;
            px = cx + hw;
            pz = cz - hh + d;
            nx = 1f; nz = 0f;
        }
        else if (t < 2f * w + h)
        {
            // North edge: NE → NW (walking -X)
            float d = t - w - h;
            px = cx + hw - d;
            pz = cz + hh;
            nx = 0f; nz = 1f;
        }
        else
        {
            // West edge: NW → SW (walking -Z)
            float d = t - 2f * w - h;
            px = cx - hw;
            pz = cz + hh - d;
            nx = -1f; nz = 0f;
        }

        return new Vector3(px + nx * buffer, cy, pz + nz * buffer);
    }

    /// <summary>
    /// Perimeter of the target's shape in world units.
    /// Circle (TargetRadius > 0): 2π(R + attackerRadius).
    /// Rectangle (TargetRadius == 0): 2(w+h) of WorldBounds.
    /// </summary>
    public static float GetPerimeter(IAttackable target, float attackerRadius)
    {
        if (target.TargetRadius > 0.01f)
            return 2f * Mathf.PI * (target.TargetRadius + attackerRadius * RingPaddingMultiplier);
        var b = target.WorldBounds;
        return 2f * (b.extents.x * 2f + b.extents.z * 2f);
    }

    public static bool CanCommitToTarget(IAttackable target, Unit attacker)
    {
        if (target == null || attacker == null || attacker.Data == null)
            return false;

        if (target.Priority == TargetPriority.Default)
            return true;

        return TryResolveAttackSlot(target, attacker, allowQueueOutsideRange: false, out _);
    }

    public static bool ShouldHardLock(Unit attacker)
    {
        if (attacker == null || attacker.Data == null)
            return true;

        return !IsRangedAttacker(attacker, attacker.Data.attackRange) && attacker.Data.attackRange <= 1.25f;
    }

    public static float GetAttackSlotTolerance(Unit attacker)
    {
        if (attacker == null || attacker.Data == null)
            return 0.35f;

        if (ShouldHardLock(attacker))
        {
            float tolerance = attacker.EffectiveRadius * 0.35f;
            return Mathf.Clamp(tolerance, 0.10f, 0.60f);
        }

        float softTolerance = attacker.EffectiveRadius * 0.8f + attacker.Data.attackRange * 0.05f;
        return Mathf.Clamp(softTolerance, 0.25f, 1.5f);
    }

    // ================================================================
    //  ATTACK POSITION — unified for units, buildings, castles
    // ================================================================

    /// <summary>
    /// Returns a general-purpose attack approach point.
    /// Round targets use their center and rely on the unit-vs-unit
    /// ring/arc logic elsewhere. Extended targets (buildings/castles)
    /// use the closest point on their physical bounds plus a pathing
    /// stand-off, so units approach the perimeter instead of aiming at
    /// an unreachable center point.
    /// </summary>
    public static Vector3 FindAttackPosition(
        Vector3 attackerPos, float attackerRadius, float attackRange,
        IAttackable target, int attackerUnitId = -1, Unit attacker = null,
        bool allowQueueOutsideRange = false,
        int searchDirection = 0)
    {
        if (target == null) return attackerPos;

        bool canQueue = allowQueueOutsideRange || target.Priority == TargetPriority.Default;
        if (attacker != null && TryFindAdaptiveAttackSlot(target, attacker, canQueue, searchDirection, out Vector3 slotted))
            return slotted;

        if (attacker != null && TryResolveAttackSlot(target, attacker, canQueue, out slotted))
            return slotted;

        return FindFallbackAttackPosition(
            attackerPos, attackerRadius, attackRange, target, attackerUnitId, attacker);
    }

    private static bool TryFindAdaptiveAttackSlot(
        IAttackable target,
        Unit attacker,
        bool allowQueueOutsideRange,
        int searchDirection,
        out Vector3 destination)
    {
        destination = Vector3.zero;
        if (attacker == null || attacker.Data == null)
            return false;

        LayoutParticipant participant = CreateParticipant(target, attacker);
        List<OccupiedReservation> occupied = CollectOccupiedReservations(target, attacker);
        Vector3 referencePosition = attacker.transform.position;
        float bestScore = float.PositiveInfinity;
        bool found = false;
        int effectiveSearchDirection = target.TargetRadius > 0.01f ? searchDirection : 0;

        if (TryUseCurrentPositionAsSlot(target, participant, attacker, occupied, out destination))
            return true;

        if (attacker.Combat != null
            && attacker.Combat.AttackPosition.HasValue
            && IsExistingSlotViable(target, participant, attacker.transform.position, attacker.Combat.AttackPosition.Value, occupied))
        {
            destination = attacker.Combat.AttackPosition.Value;
            return true;
        }

        foreach (int bandIndex in EnumerateCandidateBands(participant, allowQueueOutsideRange))
        {
            float extraReach = ComputeExtraReachForBand(participant, bandIndex);
            float centerSurfaceOffset = participant.Radius + extraReach;
            float perimeter = GetShapePerimeter(target, centerSurfaceOffset);
            if (perimeter <= ShapeEpsilon)
                continue;

            float sampleSpacing = Mathf.Max((participant.SlotWidth + SlotPaddingWorld) * 0.5f, 0.35f);
            int sampleCount = Mathf.Clamp(Mathf.CeilToInt(perimeter / sampleSpacing), 12, 128);

            for (int offset = 0; offset < sampleCount; offset++)
            {
                float parameter = GetSampleParameter(participant.PreferredParameter, sampleCount, offset, effectiveSearchDirection);
                Vector3 candidate = EvaluateAttackPosition(
                    target,
                    parameter,
                    centerSurfaceOffset,
                    attacker.transform.position.y);

                if (!IsCandidateReachableForParticipant(target, participant, attacker.transform.position, candidate))
                    continue;

                if (!IsCandidateOpen(candidate, participant.Radius, occupied))
                    continue;

                float angularPenalty = GetSearchParameterDistance(parameter, participant.PreferredParameter, effectiveSearchDirection) * perimeter * 0.35f;
                float bandPenalty = Mathf.Abs(bandIndex - participant.PreferredBand) * 0.6f;
                float score = Vector3.Distance(referencePosition, candidate) + angularPenalty + bandPenalty;

                if (score < bestScore)
                {
                    bestScore = score;
                    destination = candidate;
                    found = true;
                }
            }

            if (found && bandIndex == participant.PreferredBand)
                break;
        }

        return found;
    }

    private static bool TryResolveAttackSlot(
        IAttackable target,
        Unit attacker,
        bool allowQueueOutsideRange,
        out Vector3 destination)
    {
        destination = Vector3.zero;
        if (attacker == null || attacker.Data == null || UnitManager.Instance == null)
            return false;

        List<LayoutParticipant> participants = CollectParticipants(target, attacker);
        if (participants.Count == 0)
            return false;

        if (!TryAssignParticipantsToBands(target, participants, allowQueueOutsideRange, out var assignments))
            return false;

        if (!assignments.TryGetValue(attacker.GetInstanceID(), out SlotAssignment assignment))
            return false;

        destination = EvaluateAttackPosition(
            target,
            assignment.Parameter,
            assignment.CenterSurfaceOffset,
            attacker.transform.position.y);
        return true;
    }

    private static List<LayoutParticipant> CollectParticipants(IAttackable target, Unit attacker)
    {
        var participants = new List<LayoutParticipant>();
        if (attacker == null || attacker.Data == null || UnitManager.Instance == null)
            return participants;

        int attackerId = attacker.GetInstanceID();
        foreach (Unit candidate in UnitManager.Instance.GetTeamUnits(attacker.TeamId))
        {
            if (!ShouldIncludeParticipant(candidate, target, attackerId))
                continue;

            participants.Add(CreateParticipant(target, candidate));
        }

        bool alreadyIncluded = false;
        for (int i = 0; i < participants.Count; i++)
        {
            if (participants[i].UnitId == attackerId)
            {
                alreadyIncluded = true;
                break;
            }
        }

        if (!alreadyIncluded)
            participants.Add(CreateParticipant(target, attacker));

        return participants;
    }

    private static bool ShouldIncludeParticipant(Unit candidate, IAttackable target, int attackerId)
    {
        if (candidate == null || candidate.IsDead || candidate.Data == null || candidate.Combat == null)
            return false;

        if (candidate.GetInstanceID() == attackerId)
            return true;

        IAttackable currentTarget = candidate.Combat.CurrentTarget;
        return currentTarget != null
            && currentTarget.gameObject != null
            && target.gameObject != null
            && currentTarget.gameObject == target.gameObject;
    }

    private static LayoutParticipant CreateParticipant(IAttackable target, Unit unit)
    {
        float attackRange = Mathf.Max(0f, unit.Data.attackRange);
        bool isRanged = IsRangedAttacker(unit, attackRange);
        float slotWidth = Mathf.Max(
            MinSlotWidth,
            unit.EffectiveRadius * 2f * RingPaddingMultiplier + (isRanged ? 0.35f : 0.1f));
        float preferredExtraReach = Mathf.Min(
            attackRange,
            ComputePreferredExtraReach(unit, attackRange));
        float maxExtraReach = Mathf.Max(0f, attackRange - 0.05f);
        int preferredBand = Mathf.Max(0, Mathf.RoundToInt(preferredExtraReach / BandExtraReachStep));
        int maxBand = Mathf.Max(preferredBand, Mathf.FloorToInt(maxExtraReach / BandExtraReachStep + 0.001f));
        Vector3 referencePosition = unit.transform.position;
        if (unit.Combat != null
            && unit.Combat.CurrentTarget != null
            && target.gameObject != null
            && unit.Combat.CurrentTarget.gameObject == target.gameObject
            && unit.Combat.AttackPosition.HasValue)
        {
            Vector3 existingSlot = unit.Combat.AttackPosition.Value;
            if (ShouldKeepExistingSlotReference(target, unit, slotWidth, existingSlot))
                referencePosition = existingSlot;
        }

        float preferredParameter = GetPreferredShapeParameter(
            target,
            referencePosition,
            unit.GetInstanceID(),
            unit.EffectiveRadius + preferredExtraReach);

        return new LayoutParticipant(
            unit,
            unit.GetInstanceID(),
            unit.EffectiveRadius,
            attackRange,
            isRanged,
            preferredExtraReach,
            maxExtraReach,
            preferredBand,
            maxBand,
            preferredParameter,
            slotWidth);
    }

    private static bool TryAssignParticipantsToBands(
        IAttackable target,
        List<LayoutParticipant> participants,
        bool allowQueueOutsideRange,
        out Dictionary<int, SlotAssignment> assignments)
    {
        assignments = new Dictionary<int, SlotAssignment>(participants.Count);
        participants.Sort(LayoutParticipantComparer.Instance);

        var bands = new Dictionary<int, List<LayoutParticipant>>();
        var bandAssignments = new Dictionary<int, Dictionary<int, SlotAssignment>>();

        for (int i = 0; i < participants.Count; i++)
        {
            LayoutParticipant participant = participants[i];
            bool placed = false;

            foreach (int bandIndex in EnumerateCandidateBands(participant, allowQueueOutsideRange))
            {
                bands.TryGetValue(bandIndex, out List<LayoutParticipant> currentBand);
                var candidateBand = currentBand != null
                    ? new List<LayoutParticipant>(currentBand)
                    : new List<LayoutParticipant>();
                candidateBand.Add(participant);

                if (!TryAssignBand(target, candidateBand, bandIndex, out Dictionary<int, SlotAssignment> candidateAssignments))
                    continue;

                bands[bandIndex] = candidateBand;
                bandAssignments[bandIndex] = candidateAssignments;
                placed = true;
                break;
            }

            if (!placed)
            {
                assignments = null;
                return false;
            }
        }

        foreach (var band in bandAssignments)
        {
            foreach (var assignment in band.Value)
                assignments[assignment.Key] = assignment.Value;
        }

        return true;
    }

    private static IEnumerable<int> EnumerateCandidateBands(LayoutParticipant participant, bool allowQueueOutsideRange)
    {
        int maxBand = participant.MaxBand + (allowQueueOutsideRange ? Mathf.RoundToInt(QueueOverflowBands) : 0);
        bool outwardFirst = participant.IsRanged;

        for (int offset = 0; offset <= maxBand; offset++)
        {
            int primary = participant.PreferredBand + (outwardFirst ? offset : -offset);
            if (primary >= 0 && primary <= maxBand)
                yield return primary;

            if (offset == 0)
                continue;

            int secondary = participant.PreferredBand + (outwardFirst ? -offset : offset);
            if (secondary >= 0 && secondary <= maxBand)
                yield return secondary;
        }
    }

    private static bool TryAssignBand(
        IAttackable target,
        List<LayoutParticipant> participants,
        int bandIndex,
        out Dictionary<int, SlotAssignment> assignments)
    {
        assignments = new Dictionary<int, SlotAssignment>(participants.Count);
        if (participants.Count == 0)
            return true;

        var placements = new List<BandPlacement>(participants.Count);
        for (int i = 0; i < participants.Count; i++)
        {
            LayoutParticipant participant = participants[i];
            float extraReach = ComputeExtraReachForBand(participant, bandIndex);
            float centerSurfaceOffset = participant.Radius + extraReach;
            float perimeter = GetShapePerimeter(target, centerSurfaceOffset);
            if (perimeter <= ShapeEpsilon)
                return false;

            float span = Mathf.Clamp01((participant.SlotWidth + SlotPaddingWorld) / perimeter);
            if (span >= 1f)
                return false;

            placements.Add(new BandPlacement(
                participant,
                participant.PreferredParameter,
                centerSurfaceOffset,
                span));
        }

        if (placements.Count == 1)
        {
            BandPlacement only = placements[0];
            assignments[only.Participant.UnitId] = new SlotAssignment(only.PreferredParameter, only.CenterSurfaceOffset);
            return true;
        }

        placements.Sort(BandPlacementComparer.Instance);
        int startIndex = FindLargestGapStartIndex(placements);

        var rotated = new List<BandPlacement>(placements.Count);
        for (int i = 0; i < placements.Count; i++)
            rotated.Add(placements[(startIndex + i) % placements.Count]);

        float[] preferred = new float[rotated.Count];
        float[] assigned = new float[rotated.Count];

        preferred[0] = rotated[0].PreferredParameter;
        assigned[0] = preferred[0];

        for (int i = 1; i < rotated.Count; i++)
        {
            float value = rotated[i].PreferredParameter;
            while (value < preferred[i - 1])
                value += 1f;
            preferred[i] = value;

            float minSpacing = (rotated[i - 1].Span + rotated[i].Span) * 0.5f;
            assigned[i] = Mathf.Max(value, assigned[i - 1] + minSpacing);
        }

        float minEdge = assigned[0] - rotated[0].Span * 0.5f;
        float maxEdge = assigned[rotated.Count - 1] + rotated[rotated.Count - 1].Span * 0.5f;
        if (maxEdge - minEdge > 1f + 0.0001f)
            return false;

        float preferredCenter = 0f;
        float assignedCenter = 0f;
        for (int i = 0; i < rotated.Count; i++)
        {
            preferredCenter += preferred[i];
            assignedCenter += assigned[i];
        }
        preferredCenter /= rotated.Count;
        assignedCenter /= rotated.Count;

        float minShift = -minEdge;
        float maxShift = 1f - maxEdge;
        float shift = Mathf.Clamp(preferredCenter - assignedCenter, minShift, maxShift);

        for (int i = 0; i < rotated.Count; i++)
        {
            float parameter = Mathf.Repeat(assigned[i] + shift, 1f);
            assignments[rotated[i].Participant.UnitId] = new SlotAssignment(
                parameter,
                rotated[i].CenterSurfaceOffset);
        }

        return true;
    }

    private static List<OccupiedReservation> CollectOccupiedReservations(IAttackable target, Unit attacker)
    {
        var reservations = new List<OccupiedReservation>();
        if (attacker == null || target == null || UnitManager.Instance == null)
            return reservations;

        foreach (Unit candidate in UnitManager.Instance.GetTeamUnits(attacker.TeamId))
        {
            if (candidate == null || candidate.IsDead || candidate == attacker || candidate.Combat == null)
                continue;

            IAttackable currentTarget = candidate.Combat.CurrentTarget;
            if (currentTarget == null || currentTarget.gameObject == null || target.gameObject == null || currentTarget.gameObject != target.gameObject)
                continue;

            if (!ShouldReserveSlot(candidate))
                continue;

            Vector3 reservationPosition = candidate.Combat.AttackPosition.HasValue
                ? candidate.Combat.AttackPosition.Value
                : candidate.transform.position;

            reservations.Add(new OccupiedReservation(reservationPosition, candidate.EffectiveRadius));
        }

        return reservations;
    }

    private static bool ShouldReserveSlot(Unit candidate)
    {
        if (candidate == null || candidate.Combat == null)
            return false;

        if (candidate.Combat.IsAttacking)
            return true;

        if (!candidate.Combat.AttackPosition.HasValue)
            return false;

        IAttackable currentTarget = candidate.Combat.CurrentTarget;
        if (currentTarget != null && currentTarget.TargetRadius <= 0.01f)
            return true;

        float reserveTolerance = GetAttackSlotTolerance(candidate) * 1.5f;
        return Vector3.Distance(candidate.transform.position, candidate.Combat.AttackPosition.Value) <= reserveTolerance;
    }

    private static float GetSampleParameter(float preferredParameter, int sampleCount, int offset, int searchDirection)
    {
        if (sampleCount <= 0)
            return preferredParameter;

        if (offset == 0)
            return Mathf.Repeat(preferredParameter, 1f);

        if (searchDirection > 0)
            return Mathf.Repeat(preferredParameter + offset / (float)sampleCount, 1f);

        if (searchDirection < 0)
            return Mathf.Repeat(preferredParameter - offset / (float)sampleCount, 1f);

        int step = (offset + 1) / 2;
        int direction = (offset % 2 == 1) ? 1 : -1;
        float delta = step / (float)sampleCount;
        return Mathf.Repeat(preferredParameter + delta * direction, 1f);
    }

    private static bool IsCandidateOpen(
        Vector3 candidate,
        float attackerRadius,
        List<OccupiedReservation> occupied,
        float extraPadding = SlotPaddingWorld)
    {
        for (int i = 0; i < occupied.Count; i++)
        {
            float required = attackerRadius + occupied[i].Radius + extraPadding;
            float dx = candidate.x - occupied[i].Position.x;
            float dz = candidate.z - occupied[i].Position.z;
            if (dx * dx + dz * dz < required * required)
                return false;
        }

        return true;
    }

    private static bool IsExistingSlotViable(
        IAttackable target,
        LayoutParticipant participant,
        Vector3 attackerPosition,
        Vector3 existingSlot,
        List<OccupiedReservation> occupied)
    {
        if (!IsCandidateOpen(existingSlot, participant.Radius, occupied))
            return false;

        float surfaceDistance = DistanceToTarget(existingSlot, target);
        float maxDistance = participant.Radius + participant.AttackRange + BandExtraReachStep * QueueOverflowBands + 0.25f;
        if (surfaceDistance < participant.Radius - 0.1f || surfaceDistance > maxDistance)
            return false;

        if (!RequiresTightSlotStickiness(target, participant.Unit, participant.IsRanged))
            return true;

        return Vector3.Distance(attackerPosition, existingSlot)
            <= GetExistingSlotStickDistance(participant.Unit, participant.SlotWidth);
    }

    private static bool TryUseCurrentPositionAsSlot(
        IAttackable target,
        LayoutParticipant participant,
        Unit attacker,
        List<OccupiedReservation> occupied,
        out Vector3 destination)
    {
        destination = Vector3.zero;
        if (target == null || attacker == null)
            return false;

        // Buildings and castles are wide footprints, so an attacker that is
        // already standing at an open in-range spot should settle there
        // instead of orbiting toward a sampled perimeter coordinate.
        if (target.TargetRadius > 0.01f)
            return false;

        Vector3 candidate = attacker.transform.position;
        if (!IsCandidateReachableForParticipant(target, participant, attacker.transform.position, candidate))
            return false;

        if (!IsCandidateOpen(candidate, participant.Radius, occupied, extraPadding: 0f))
            return false;

        float surfaceDistance = DistanceToTarget(candidate, target);
        float minDistance = Mathf.Max(0f, participant.Radius - 0.1f);
        float maxDistance = participant.Radius + participant.AttackRange + 0.1f;
        if (surfaceDistance < minDistance || surfaceDistance > maxDistance)
            return false;

        destination = candidate;
        return true;
    }

    private static bool ShouldKeepExistingSlotReference(
        IAttackable target,
        Unit unit,
        float slotWidth,
        Vector3 existingSlot)
    {
        if (target == null || unit == null)
            return false;

        if (!RequiresTightSlotStickiness(target, unit, IsRangedAttacker(unit, unit.Data.attackRange)))
            return true;

        return Vector3.Distance(unit.transform.position, existingSlot)
            <= GetExistingSlotStickDistance(unit, slotWidth);
    }

    private static bool RequiresTightSlotStickiness(IAttackable target, Unit unit, bool isRanged)
    {
        if (target == null || unit == null)
            return false;

        if (target.TargetRadius <= 0.01f)
            return true;

        return !isRanged && unit.Data != null && unit.Data.attackRange <= 1.25f;
    }

    private static float GetExistingSlotStickDistance(Unit unit, float slotWidth)
    {
        float tolerance = GetAttackSlotTolerance(unit) * 4f;
        return Mathf.Max(2f, Mathf.Max(slotWidth * 1.5f, tolerance));
    }

    private static bool IsCandidateReachableForParticipant(
        IAttackable target,
        LayoutParticipant participant,
        Vector3 attackerPosition,
        Vector3 candidate)
    {
        if (target.TargetRadius <= 0.01f || participant.IsRanged || participant.AttackRange > 1.5f)
            return true;

        Vector3 approachDirection = attackerPosition - target.Position;
        approachDirection.y = 0f;
        if (approachDirection.sqrMagnitude <= 0.01f)
            return true;

        Vector3 slotDirection = candidate - target.Position;
        slotDirection.y = 0f;
        if (slotDirection.sqrMagnitude <= 0.01f)
            return true;

        approachDirection.Normalize();
        slotDirection.Normalize();
        return Vector3.Dot(approachDirection, slotDirection) >= 0f;
    }

    private static float CircularParameterDistance(float a, float b)
    {
        float delta = Mathf.Abs(a - b);
        return Mathf.Min(delta, 1f - delta);
    }

    private static float GetSearchParameterDistance(float parameter, float preferredParameter, int searchDirection)
    {
        if (searchDirection > 0)
            return Mathf.Repeat(parameter - preferredParameter, 1f);

        if (searchDirection < 0)
            return Mathf.Repeat(preferredParameter - parameter, 1f);

        return CircularParameterDistance(parameter, preferredParameter);
    }

    private static int FindLargestGapStartIndex(List<BandPlacement> placements)
    {
        float largestGap = -1f;
        int startIndex = 0;

        for (int i = 0; i < placements.Count; i++)
        {
            float current = placements[i].PreferredParameter;
            float next = placements[(i + 1) % placements.Count].PreferredParameter;
            float gap = i == placements.Count - 1
                ? (next + 1f) - current
                : next - current;

            if (gap > largestGap)
            {
                largestGap = gap;
                startIndex = (i + 1) % placements.Count;
            }
        }

        return startIndex;
    }

    private static Vector3 FindFallbackAttackPosition(
        Vector3 attackerPos,
        float attackerRadius,
        float attackRange,
        IAttackable target,
        int attackerUnitId,
        Unit attacker)
    {
        float preferredExtraReach = ComputePreferredExtraReach(attacker, attackRange);
        float centerSurfaceOffset = attackerRadius + preferredExtraReach;
        float parameter = GetPreferredShapeParameter(target, attackerPos, attackerUnitId, centerSurfaceOffset);
        return EvaluateAttackPosition(target, parameter, centerSurfaceOffset, attackerPos.y);
    }

    private static float ComputePreferredExtraReach(Unit attacker, float attackRange)
    {
        if (attackRange <= 0f)
            return 0f;

        bool isRanged = IsRangedAttacker(attacker, attackRange);
        if (isRanged)
        {
            float minimum = Mathf.Min(1.5f, attackRange);
            float maximum = Mathf.Max(minimum, attackRange - 0.25f);
            return Mathf.Clamp(attackRange * RangedPreferredExtraReachFactor, minimum, maximum);
        }

        if (attackRange > 1.25f)
        {
            float maximum = Mathf.Max(0.25f, attackRange - 0.15f);
            return Mathf.Clamp(attackRange * ExtendedMeleePreferredExtraReachFactor, 0.25f, maximum);
        }

        return Mathf.Min(MeleePreferredExtraReach, attackRange);
    }

    private static bool IsRangedAttacker(Unit attacker, float attackRange)
    {
        if (attacker != null && attacker.Data != null)
        {
            if (attacker.Data.isRanged || attacker.Data.projectilePrefab != null)
                return true;
        }

        return attackRange > 1.5f;
    }

    private static float ComputeExtraReachForBand(LayoutParticipant participant, int bandIndex)
    {
        float desired = bandIndex * BandExtraReachStep;
        if (bandIndex <= participant.MaxBand)
            return Mathf.Min(participant.MaxExtraReach, desired);

        return participant.MaxExtraReach + (bandIndex - participant.MaxBand) * BandExtraReachStep;
    }

    private static float GetShapePerimeter(IAttackable target, float centerSurfaceOffset)
    {
        if (target.TargetRadius > 0.01f)
            return 2f * Mathf.PI * (target.TargetRadius + centerSurfaceOffset);

        Bounds bounds = target.WorldBounds;
        return GetRoundedRectPerimeter(bounds, centerSurfaceOffset);
    }

    private static Vector3 EvaluateAttackPosition(
        IAttackable target,
        float parameter,
        float centerSurfaceOffset,
        float y)
    {
        if (target.TargetRadius > 0.01f)
        {
            float angle = parameter * Mathf.PI * 2f;
            float radius = target.TargetRadius + centerSurfaceOffset;
            Vector3 center = target.Position;
            return new Vector3(
                center.x + Mathf.Cos(angle) * radius,
                y,
                center.z + Mathf.Sin(angle) * radius);
        }

        Bounds bounds = target.WorldBounds;
        float perimeter = GetShapePerimeter(target, centerSurfaceOffset);
        Vector3 point = GetRoundedRectPerimeterPoint(bounds, parameter * perimeter, centerSurfaceOffset);
        point.y = y;
        return point;
    }

    private static float GetPreferredShapeParameter(
        IAttackable target,
        Vector3 sample,
        int attackerUnitId,
        float centerSurfaceOffset = 0f)
    {
        if (target.TargetRadius > 0.01f)
        {
            Vector3 direction = sample - target.Position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 0.0001f)
            {
                float fallbackAngle = attackerUnitId >= 0
                    ? Mathf.Repeat(attackerUnitId * 0.6180339f, 1f) * Mathf.PI * 2f
                    : 0f;
                return fallbackAngle / (Mathf.PI * 2f);
            }

            float angle = Mathf.Atan2(direction.z, direction.x);
            if (angle < 0f)
                angle += Mathf.PI * 2f;
            return angle / (Mathf.PI * 2f);
        }

        Bounds bounds = target.WorldBounds;
        float perimeter = centerSurfaceOffset > 0.001f
            ? GetRoundedRectPerimeter(bounds, centerSurfaceOffset)
            : GetBaseRectPerimeter(bounds);
        if (perimeter <= ShapeEpsilon)
            return attackerUnitId >= 0 ? Mathf.Repeat(attackerUnitId * 0.6180339f, 1f) : 0f;

        Vector3 perimeterPoint = centerSurfaceOffset > 0.001f
            ? ClosestPointOnRoundedRectPerimeter(bounds, sample, centerSurfaceOffset)
            : ClosestPointOnRectPerimeter(bounds, sample);
        float distance = centerSurfaceOffset > 0.001f
            ? GetRoundedRectPerimeterDistance(bounds, perimeterPoint, centerSurfaceOffset)
            : GetRectPerimeterDistance(bounds, perimeterPoint);
        return Mathf.Repeat(distance / perimeter, 1f);
    }

    private static float GetBaseRectPerimeter(Bounds bounds)
    {
        return 2f * (bounds.size.x + bounds.size.z);
    }

    private static Vector3 ClosestPointOnRectPerimeter(Bounds bounds, Vector3 sample)
    {
        float minX = bounds.min.x;
        float maxX = bounds.max.x;
        float minZ = bounds.min.z;
        float maxZ = bounds.max.z;

        float x = Mathf.Clamp(sample.x, minX, maxX);
        float z = Mathf.Clamp(sample.z, minZ, maxZ);

        bool insideX = sample.x > minX && sample.x < maxX;
        bool insideZ = sample.z > minZ && sample.z < maxZ;
        if (!(insideX && insideZ))
            return new Vector3(x, bounds.center.y, z);

        float distWest = Mathf.Abs(sample.x - minX);
        float distEast = Mathf.Abs(maxX - sample.x);
        float distSouth = Mathf.Abs(sample.z - minZ);
        float distNorth = Mathf.Abs(maxZ - sample.z);

        float minDist = Mathf.Min(distWest, distEast, distSouth, distNorth);
        if (minDist == distSouth) z = minZ;
        else if (minDist == distEast) x = maxX;
        else if (minDist == distNorth) z = maxZ;
        else x = minX;

        return new Vector3(x, bounds.center.y, z);
    }

    private static float GetRectPerimeterDistance(Bounds bounds, Vector3 perimeterPoint)
    {
        float minX = bounds.min.x;
        float maxX = bounds.max.x;
        float minZ = bounds.min.z;
        float maxZ = bounds.max.z;
        float width = bounds.size.x;
        float depth = bounds.size.z;

        if (Mathf.Abs(perimeterPoint.z - minZ) <= 0.01f)
            return perimeterPoint.x - minX;

        if (Mathf.Abs(perimeterPoint.x - maxX) <= 0.01f)
            return width + (perimeterPoint.z - minZ);

        if (Mathf.Abs(perimeterPoint.z - maxZ) <= 0.01f)
            return width + depth + (maxX - perimeterPoint.x);

        return width + depth + width + (maxZ - perimeterPoint.z);
    }

    private static Vector3 ClosestPointOnRoundedRectPerimeter(Bounds bounds, Vector3 sample, float buffer)
    {
        if (buffer <= 0.001f)
            return ClosestPointOnRectPerimeter(bounds, sample);

        float minX = bounds.min.x;
        float maxX = bounds.max.x;
        float minZ = bounds.min.z;
        float maxZ = bounds.max.z;

        bool outsideX = sample.x < minX || sample.x > maxX;
        bool outsideZ = sample.z < minZ || sample.z > maxZ;

        if (outsideX && outsideZ)
        {
            float cornerX = sample.x > maxX ? maxX : minX;
            float cornerZ = sample.z > maxZ ? maxZ : minZ;
            Vector2 direction = new(sample.x - cornerX, sample.z - cornerZ);
            if (direction.sqrMagnitude <= 0.0001f)
            {
                float signX = cornerX > bounds.center.x ? 1f : -1f;
                float signZ = cornerZ > bounds.center.z ? 1f : -1f;
                direction = new Vector2(signX, signZ).normalized;
            }
            else
            {
                direction.Normalize();
            }

            return new Vector3(
                cornerX + direction.x * buffer,
                bounds.center.y,
                cornerZ + direction.y * buffer);
        }

        if (outsideX)
        {
            float x = sample.x > maxX ? maxX + buffer : minX - buffer;
            float z = Mathf.Clamp(sample.z, minZ, maxZ);
            return new Vector3(x, bounds.center.y, z);
        }

        if (outsideZ)
        {
            float x = Mathf.Clamp(sample.x, minX, maxX);
            float z = sample.z > maxZ ? maxZ + buffer : minZ - buffer;
            return new Vector3(x, bounds.center.y, z);
        }

        Vector3 perimeterPoint = ClosestPointOnRectPerimeter(bounds, sample);
        if (Mathf.Abs(perimeterPoint.z - minZ) <= 0.01f)
            return new Vector3(perimeterPoint.x, bounds.center.y, minZ - buffer);
        if (Mathf.Abs(perimeterPoint.x - maxX) <= 0.01f)
            return new Vector3(maxX + buffer, bounds.center.y, perimeterPoint.z);
        if (Mathf.Abs(perimeterPoint.z - maxZ) <= 0.01f)
            return new Vector3(perimeterPoint.x, bounds.center.y, maxZ + buffer);

        return new Vector3(minX - buffer, bounds.center.y, perimeterPoint.z);
    }

    private static float GetRoundedRectPerimeterDistance(Bounds bounds, Vector3 perimeterPoint, float buffer)
    {
        if (buffer <= 0.001f)
            return GetRectPerimeterDistance(bounds, perimeterPoint);

        float minX = bounds.min.x;
        float maxX = bounds.max.x;
        float minZ = bounds.min.z;
        float maxZ = bounds.max.z;
        float width = bounds.size.x;
        float depth = bounds.size.z;
        float cornerArc = 0.5f * Mathf.PI * buffer;

        if (Mathf.Abs(perimeterPoint.z - (minZ - buffer)) <= 0.01f
            && perimeterPoint.x >= minX - 0.01f
            && perimeterPoint.x <= maxX + 0.01f)
        {
            return perimeterPoint.x - minX;
        }

        Vector2 southEast = new(maxX, minZ);
        if (IsPointOnRoundedCorner(perimeterPoint, southEast, buffer, -0.5f * Mathf.PI, 0f, out float southEastAngle))
            return width + (southEastAngle + 0.5f * Mathf.PI) * buffer;

        if (Mathf.Abs(perimeterPoint.x - (maxX + buffer)) <= 0.01f
            && perimeterPoint.z >= minZ - 0.01f
            && perimeterPoint.z <= maxZ + 0.01f)
        {
            return width + cornerArc + (perimeterPoint.z - minZ);
        }

        Vector2 northEast = new(maxX, maxZ);
        if (IsPointOnRoundedCorner(perimeterPoint, northEast, buffer, 0f, 0.5f * Mathf.PI, out float northEastAngle))
            return width + cornerArc + depth + northEastAngle * buffer;

        if (Mathf.Abs(perimeterPoint.z - (maxZ + buffer)) <= 0.01f
            && perimeterPoint.x >= minX - 0.01f
            && perimeterPoint.x <= maxX + 0.01f)
        {
            return width + cornerArc + depth + cornerArc + (maxX - perimeterPoint.x);
        }

        Vector2 northWest = new(minX, maxZ);
        if (IsPointOnRoundedCorner(perimeterPoint, northWest, buffer, 0.5f * Mathf.PI, Mathf.PI, out float northWestAngle))
            return width * 2f + depth + cornerArc * 2f + (northWestAngle - 0.5f * Mathf.PI) * buffer;

        if (Mathf.Abs(perimeterPoint.x - (minX - buffer)) <= 0.01f
            && perimeterPoint.z >= minZ - 0.01f
            && perimeterPoint.z <= maxZ + 0.01f)
        {
            return width * 2f + depth + cornerArc * 3f + (maxZ - perimeterPoint.z);
        }

        Vector2 southWest = new(minX, minZ);
        if (IsPointOnRoundedCorner(perimeterPoint, southWest, buffer, Mathf.PI, 1.5f * Mathf.PI, out float southWestAngle))
            return width * 2f + depth * 2f + cornerArc * 3f + (southWestAngle - Mathf.PI) * buffer;

        return 0f;
    }

    private static bool IsPointOnRoundedCorner(
        Vector3 perimeterPoint,
        Vector2 cornerCenter,
        float buffer,
        float minAngle,
        float maxAngle,
        out float angle)
    {
        Vector2 offset = new(perimeterPoint.x - cornerCenter.x, perimeterPoint.z - cornerCenter.y);
        float radiusDelta = Mathf.Abs(offset.magnitude - buffer);
        angle = Mathf.Atan2(offset.y, offset.x);
        if (angle < 0f)
            angle += Mathf.PI * 2f;

        float adjustedMin = minAngle < 0f ? minAngle + Mathf.PI * 2f : minAngle;
        float adjustedMax = maxAngle < 0f ? maxAngle + Mathf.PI * 2f : maxAngle;
        return radiusDelta <= 0.02f && angle >= adjustedMin - 0.01f && angle <= adjustedMax + 0.01f;
    }

    private static float GetRoundedRectPerimeter(Bounds bounds, float buffer)
    {
        if (buffer <= 0.001f)
            return GetBaseRectPerimeter(bounds);

        return 2f * (bounds.size.x + bounds.size.z) + 2f * Mathf.PI * buffer;
    }

    private static Vector3 GetRoundedRectPerimeterPoint(Bounds bounds, float t, float buffer)
    {
        if (buffer <= 0.001f)
            return GetExpandedRectPerimeterPoint(bounds, t, 0f);

        float minX = bounds.min.x;
        float maxX = bounds.max.x;
        float minZ = bounds.min.z;
        float maxZ = bounds.max.z;
        float width = bounds.size.x;
        float depth = bounds.size.z;
        float cornerArc = 0.5f * Mathf.PI * buffer;
        float perimeter = GetRoundedRectPerimeter(bounds, buffer);
        t = Mathf.Repeat(t, perimeter);

        float x;
        float z;

        if (t < width)
        {
            x = minX + t;
            z = minZ - buffer;
        }
        else if (t < width + cornerArc)
        {
            float angle = -0.5f * Mathf.PI + (t - width) / buffer;
            x = maxX + Mathf.Cos(angle) * buffer;
            z = minZ + Mathf.Sin(angle) * buffer;
        }
        else if (t < width + cornerArc + depth)
        {
            float d = t - width - cornerArc;
            x = maxX + buffer;
            z = minZ + d;
        }
        else if (t < width + cornerArc + depth + cornerArc)
        {
            float angle = (t - width - cornerArc - depth) / buffer;
            x = maxX + Mathf.Cos(angle) * buffer;
            z = maxZ + Mathf.Sin(angle) * buffer;
        }
        else if (t < width * 2f + depth + cornerArc * 2f)
        {
            float d = t - width - cornerArc - depth - cornerArc;
            x = maxX - d;
            z = maxZ + buffer;
        }
        else if (t < width * 2f + depth + cornerArc * 3f)
        {
            float angle = 0.5f * Mathf.PI + (t - width * 2f - depth - cornerArc * 2f) / buffer;
            x = minX + Mathf.Cos(angle) * buffer;
            z = maxZ + Mathf.Sin(angle) * buffer;
        }
        else if (t < width * 2f + depth * 2f + cornerArc * 3f)
        {
            float d = t - width * 2f - depth - cornerArc * 3f;
            x = minX - buffer;
            z = maxZ - d;
        }
        else
        {
            float angle = Mathf.PI + (t - width * 2f - depth * 2f - cornerArc * 3f) / buffer;
            x = minX + Mathf.Cos(angle) * buffer;
            z = minZ + Mathf.Sin(angle) * buffer;
        }

        return new Vector3(x, bounds.center.y, z);
    }

    private static Vector3 GetExpandedRectPerimeterPoint(Bounds bounds, float t, float buffer)
    {
        float minX = bounds.min.x - buffer;
        float maxX = bounds.max.x + buffer;
        float minZ = bounds.min.z - buffer;
        float maxZ = bounds.max.z + buffer;
        float width = maxX - minX;
        float depth = maxZ - minZ;
        float perimeter = 2f * (width + depth);
        t = Mathf.Repeat(t, perimeter);

        float x;
        float z;

        if (t < width)
        {
            x = minX + t;
            z = minZ;
        }
        else if (t < width + depth)
        {
            float d = t - width;
            x = maxX;
            z = minZ + d;
        }
        else if (t < width * 2f + depth)
        {
            float d = t - width - depth;
            x = maxX - d;
            z = maxZ;
        }
        else
        {
            float d = t - width * 2f - depth;
            x = minX;
            z = maxZ - d;
        }

        return new Vector3(x, bounds.center.y, z);
    }

    private readonly struct LayoutParticipant
    {
        public LayoutParticipant(
            Unit unit,
            int unitId,
            float radius,
            float attackRange,
            bool isRanged,
            float preferredExtraReach,
            float maxExtraReach,
            int preferredBand,
            int maxBand,
            float preferredParameter,
            float slotWidth)
        {
            Unit = unit;
            UnitId = unitId;
            Radius = radius;
            AttackRange = attackRange;
            IsRanged = isRanged;
            PreferredExtraReach = preferredExtraReach;
            MaxExtraReach = maxExtraReach;
            PreferredBand = preferredBand;
            MaxBand = maxBand;
            PreferredParameter = preferredParameter;
            SlotWidth = slotWidth;
        }

        public Unit Unit { get; }
        public int UnitId { get; }
        public float Radius { get; }
        public float AttackRange { get; }
        public bool IsRanged { get; }
        public float PreferredExtraReach { get; }
        public float MaxExtraReach { get; }
        public int PreferredBand { get; }
        public int MaxBand { get; }
        public float PreferredParameter { get; }
        public float SlotWidth { get; }
    }

    private readonly struct BandPlacement
    {
        public BandPlacement(
            LayoutParticipant participant,
            float preferredParameter,
            float centerSurfaceOffset,
            float span)
        {
            Participant = participant;
            PreferredParameter = preferredParameter;
            CenterSurfaceOffset = centerSurfaceOffset;
            Span = span;
        }

        public LayoutParticipant Participant { get; }
        public float PreferredParameter { get; }
        public float CenterSurfaceOffset { get; }
        public float Span { get; }
    }

    private readonly struct SlotAssignment
    {
        public SlotAssignment(float parameter, float centerSurfaceOffset)
        {
            Parameter = parameter;
            CenterSurfaceOffset = centerSurfaceOffset;
        }

        public float Parameter { get; }
        public float CenterSurfaceOffset { get; }
    }

    private readonly struct OccupiedReservation
    {
        public OccupiedReservation(Vector3 position, float radius)
        {
            Position = position;
            Radius = radius;
        }

        public Vector3 Position { get; }
        public float Radius { get; }
    }

    private sealed class LayoutParticipantComparer : IComparer<LayoutParticipant>
    {
        public static readonly LayoutParticipantComparer Instance = new();

        public int Compare(LayoutParticipant x, LayoutParticipant y)
        {
            int bandCompare = x.MaxBand.CompareTo(y.MaxBand);
            if (bandCompare != 0) return bandCompare;

            int rangedCompare = x.IsRanged.CompareTo(y.IsRanged);
            if (rangedCompare != 0) return rangedCompare;

            int radiusCompare = y.Radius.CompareTo(x.Radius);
            if (radiusCompare != 0) return radiusCompare;

            return x.UnitId.CompareTo(y.UnitId);
        }
    }

    private sealed class BandPlacementComparer : IComparer<BandPlacement>
    {
        public static readonly BandPlacementComparer Instance = new();

        public int Compare(BandPlacement x, BandPlacement y)
        {
            int parameterCompare = x.PreferredParameter.CompareTo(y.PreferredParameter);
            if (parameterCompare != 0) return parameterCompare;
            return x.Participant.UnitId.CompareTo(y.Participant.UnitId);
        }
    }
}
