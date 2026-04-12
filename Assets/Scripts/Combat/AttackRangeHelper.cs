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

    // ================================================================
    //  ATTACK POSITION — unified for units, buildings, castles
    // ================================================================

    /// <summary>
    /// Returns the target's position. That's it.
    ///
    /// Units walk toward the target. A* routes to the nearest walkable
    /// point. RVO prevents body overlap. Units attack the moment they're
    /// in range — they don't need to reach a specific slot.
    ///
    /// Pre-computing unique positions per attacker was tried extensively
    /// and failed: hash-based positions become invalid by the time the
    /// unit arrives (another unit already took that spot), perimeter
    /// walks send units to the far side of buildings, and approach-biased
    /// angles oscillate as the unit moves. The simple "aim at target,
    /// let physics sort it out" is what every shipping RTS does.
    /// </summary>
    public static Vector3 FindAttackPosition(
        Vector3 attackerPos, float attackerRadius, float attackRange,
        IAttackable target, int attackerUnitId = -1, Unit attacker = null)
    {
        if (target == null) return attackerPos;
        Vector3 pos = target.Position;
        pos.y = attackerPos.y;
        return pos;
    }
}
