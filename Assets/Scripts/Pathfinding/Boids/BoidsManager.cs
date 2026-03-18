using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Immutable snapshot of a neighbor unit for Boids force calculations.
/// Enables pure-function testing without Unity dependencies.
/// </summary>
public struct BoidsNeighbor
{
    public Vector3 Position;
    public Vector3 Velocity;
    public float Radius;
    public int TeamId;
    public int InstanceId;
    public UnitState State;
}

/// <summary>
/// Output of a Boids force calculation.
/// </summary>
public struct BoidsForces
{
    public Vector3 Separation;
    public Vector3 Avoidance;
    public Vector3 Alignment;
    public Vector3 Cohesion;
    public int SeparationCount;
    public bool HasOverlap;
    /// <summary>
    /// Avoidance side chosen this frame: -1 = left, +1 = right, 0 = none.
    /// Persisted across frames by the caller to prevent side-flickering.
    /// </summary>
    public int AvoidanceSide;
    /// <summary>
    /// SC2-style surround dampening: 0 = no dampening, 1 = full stop.
    /// Reduces forward drive when close neighbors block the desired direction,
    /// creating natural surrounding without a formal slot system.
    /// </summary>
    public float SurroundDampen;
}

/// <summary>
/// Layer 2 of SC2 pathfinding: Boids steering for unit-to-unit avoidance.
/// Completely independent from Layer 1 (NavMesh). Units are NEVER in the NavMesh.
/// Runs every frame for every unit.
///
/// Three forces: Separation (dominant), Alignment, Cohesion.
/// Side-steering avoidance prevents backward bouncing.
/// Density stop prevents pile-on at destinations.
/// </summary>
public class BoidsManager
{
    private readonly SpatialHashGrid spatialHash;

    // Tuning weights
    private const float SeparationWeight = 2.5f;
    private const float AlignmentWeight = 0.3f;
    private const float CohesionWeight = 0.2f;
    private const float AvoidanceStrength = 3.0f;
    private const float DanceBreakWeight = 1.5f;
    private const float DensityFraction = 0.6f;
    private const float DensityMinDist = 0.5f;
    private const float DensityMaxDist = 15f;
    private const float PushSpeed = 125f;
    private const float CollisionTierMinScale = 0.05f;
    private const float MaxLateralDeflectionAngle = 75f; // degrees — caps sideways drift

    // Statistics for PathfindingDiagnostic
    public int StatBoidsCallCount;
    public int StatOverriddenByBoids;
    public int StatDensityStopCount;
    public float StatMaxSteeringMagnitude;

    public void ResetStats()
    {
        StatBoidsCallCount = 0;
        StatOverriddenByBoids = 0;
        StatDensityStopCount = 0;
        StatMaxSteeringMagnitude = 0f;
    }

    private readonly List<BoidsNeighbor> neighborBuffer = new(32);
    // Per-unit avoidance side lock: persists across frames to prevent flickering
    private readonly Dictionary<int, int> avoidanceSideLocks = new();

    public BoidsManager(SpatialHashGrid hash)
    {
        spatialHash = hash;
    }

    /// <summary>
    /// Snapshot live Unit objects into immutable BoidsNeighbor structs for the
    /// pure-logic core methods. Filters out null, dead, and self entries.
    /// </summary>
    private static void GatherNeighbors(List<Unit> nearby, int excludeId, float deltaTime, List<BoidsNeighbor> output)
    {
        output.Clear();
        for (int i = 0; i < nearby.Count; i++)
        {
            var other = nearby[i];
            if (other == null || other.GetInstanceID() == excludeId || other.IsDead) continue;

            Vector3 posDelta = other.transform.position -
                (other.Movement != null ? other.Movement.PreviousPosition : other.transform.position);
            // Convert from units/frame to units/second so alignment and
            // dance-prevention thresholds are framerate-independent.
            Vector3 velocity = deltaTime > 1e-6f ? posDelta / deltaTime : Vector3.zero;

            output.Add(new BoidsNeighbor
            {
                Position = other.transform.position,
                Velocity = velocity,
                Radius = other.EffectiveRadius,
                TeamId = other.TeamId,
                InstanceId = other.GetInstanceID(),
                State = ((IPathfindingAgent)other).CurrentState
            });
        }
    }

    /// <summary>
    /// ZeroSpace-style collision tier scaling. Large units resist push from small ones.
    /// Returns a multiplier for separation/push force based on the neighbor-to-self
    /// radius ratio. When the neighbor is much smaller, the force on this unit is
    /// greatly reduced; when the neighbor is larger, full force applies.
    /// </summary>
    public static float CollisionTierScale(float myRadius, float otherRadius)
    {
        if (myRadius < 0.01f) return 1f;
        float ratio = otherRadius / myRadius;
        return Mathf.Clamp(ratio * ratio, CollisionTierMinScale, 1f);
    }

    /// <summary>
    /// Deterministic direction for units at identical positions, using Knuth multiplicative hash.
    /// Produces consistent results across frames unlike random displacement.
    /// </summary>
    public static Vector3 HashBasedDirection(int instanceId)
    {
        uint hash = (uint)Mathf.Abs(instanceId) * 2654435761u;
        float angle = ((hash & 0xFFFF) / (float)0xFFFF) * Mathf.PI * 2f;
        return new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
    }

    /// <summary>
    /// Pure-logic force calculation from a list of neighbor snapshots.
    /// No Unity dependencies — can be tested in EditMode.
    /// Includes all five Boids behaviors: separation, avoidance, dance prevention,
    /// alignment, and cohesion.
    /// </summary>
    public static BoidsForces ComputeForcesCore(
        Vector3 myPos, float myRadius, int myTeamId, int myInstanceId,
        Vector3 desiredVelocity,
        IReadOnlyList<BoidsNeighbor> neighbors,
        int previousAvoidanceSide = 0)
    {
        var result = new BoidsForces();
        int alignmentCount = 0;
        int cohesionCount = 0;
        // SC2-style side locking: once committed to dodging left or right,
        // stay on that side until no avoidance is needed. Prevents flickering.
        int avoidanceSideLocked = previousAvoidanceSide;
        bool avoidanceActive = false;

        for (int i = 0; i < neighbors.Count; i++)
        {
            var other = neighbors[i];
            if (other.InstanceId == myInstanceId) continue;

            Vector3 offset = myPos - other.Position;
            offset.y = 0f;
            float dist = offset.magnitude;
            if (dist < 0.01f)
            {
                offset = HashBasedDirection(myInstanceId);
                dist = 0.01f;
            }

            float combinedRadius = myRadius + other.Radius;

            if (dist < combinedRadius)
                result.HasOverlap = true;

            // SEPARATION: SC2-style push rules.
            // Push allied idle/moving units; reduced push on fighting allies;
            // no push on enemies (they collide but aren't pushed aside).
            if (dist < combinedRadius * 1.5f)
            {
                float strength = 1f - (dist / (combinedRadius * 1.5f));
                strength *= CollisionTierScale(myRadius, other.Radius);

                bool isAlly = other.TeamId == myTeamId;
                if (!isAlly)
                {
                    // Enemies: minimal separation to prevent full overlap,
                    // but no push-aside force
                    strength *= 0.1f;
                }
                else if (other.State == UnitState.Fighting)
                {
                    // Don't displace allies in combat
                    strength *= 0.15f;
                }

                result.Separation += (offset / dist) * strength;
                result.SeparationCount++;
            }

            // AVOIDANCE: side-steer to prevent backward bouncing
            float gap = dist - combinedRadius;
            float avoidanceRadius = myRadius * 4f;
            if (gap < avoidanceRadius && gap > -combinedRadius)
            {
                avoidanceActive = true;
                Vector3 awayDir = offset / dist;
                Vector3 perpendicular = Vector3.Cross(Vector3.up, awayDir).normalized;
                if (avoidanceSideLocked == 0)
                {
                    float dot = Vector3.Dot(perpendicular, desiredVelocity.normalized);
                    avoidanceSideLocked = dot >= 0 ? 1 : -1;
                }
                Vector3 avoidDir = perpendicular * avoidanceSideLocked;
                float weight = 1f - Mathf.Clamp01(gap / avoidanceRadius);
                result.Avoidance += avoidDir * weight * AvoidanceStrength;
            }

            // DANCE PREVENTION: when two units head toward each other, steer aside
            // to break the oscillation. Uses cross product for deterministic side choice.
            if (desiredVelocity.sqrMagnitude > 0.01f)
            {
                Vector3 otherVel = other.Velocity;
                otherVel.y = 0f;
                if (otherVel.sqrMagnitude > 0.01f)
                {
                    float headingDot = Vector3.Dot(desiredVelocity.normalized, otherVel.normalized);
                    if (headingDot < -0.8f)
                    {
                        float cross = NavMeshData.Cross2D(
                            new Vector2(desiredVelocity.x, desiredVelocity.z),
                            new Vector2(offset.x, offset.z));
                        float side = cross >= 0f ? 1f : -1f;
                        Vector3 dancePush = Vector3.Cross(Vector3.up, desiredVelocity.normalized) * side;
                        result.Avoidance += dancePush * DanceBreakWeight;
                    }
                }
            }

            // ALIGNMENT (allies only)
            if (other.TeamId == myTeamId)
            {
                Vector3 vel = other.Velocity;
                vel.y = 0f;
                if (vel.sqrMagnitude > 0.01f)
                {
                    result.Alignment += vel.normalized;
                    alignmentCount++;
                }
            }

            // COHESION (allies only, skip when overlapping)
            if (other.TeamId == myTeamId && dist > combinedRadius)
            {
                result.Cohesion += other.Position;
                cohesionCount++;
            }

            // SURROUND DAMPENING: reduce seek force when a close neighbor blocks
            // the desired direction. Enemy = strong (forces spreading around target),
            // ally = weaker (avoids pile-on at same position).
            if (desiredVelocity.sqrMagnitude > 0.01f && dist < combinedRadius * 2.5f)
            {
                Vector3 toNeighbor = -offset / dist; // direction toward neighbor
                float forwardDot = Vector3.Dot(desiredVelocity.normalized, toNeighbor);
                if (forwardDot > 0.5f) // neighbor is ahead of us
                {
                    float proximity = 1f - (dist / (combinedRadius * 2.5f));
                    bool isAlly = other.TeamId == myTeamId;
                    float dampen = proximity * forwardDot * (isAlly ? 0.3f : 0.6f);
                    result.SurroundDampen = Mathf.Max(result.SurroundDampen, dampen);
                }
            }
        }

        if (alignmentCount > 0)
            result.Alignment = (result.Alignment / alignmentCount).normalized;
        if (cohesionCount > 0)
        {
            result.Cohesion = (result.Cohesion / cohesionCount) - myPos;
            result.Cohesion = new Vector3(result.Cohesion.x, 0f, result.Cohesion.z);
        }

        // Persist side lock while avoidance is active; clear when resolved
        result.AvoidanceSide = avoidanceActive ? avoidanceSideLocked : 0;

        return result;
    }

    /// <summary>
    /// Combine forces into a final velocity with proper weighting and clamping.
    /// SC2-style direction preservation: steering forces are projected onto the
    /// plane perpendicular to the desired direction, so boids can deflect the
    /// unit laterally but never reverse it against its intended path.
    /// Pure-logic, testable in EditMode.
    /// </summary>
    public static Vector3 CombineForces(
        BoidsForces forces, Vector3 desiredVelocity, float maxSpeed, bool isMarching)
    {
        Vector3 totalSteering = Vector3.zero;

        if (forces.SeparationCount > 0)
            totalSteering += forces.Separation * SeparationWeight;

        if (isMarching)
        {
            if (forces.Alignment.sqrMagnitude > 0.01f)
                totalSteering += forces.Alignment * AlignmentWeight;
            if (forces.Cohesion.sqrMagnitude > 0.01f)
                totalSteering += forces.Cohesion.normalized * CohesionWeight;
        }

        totalSteering += forces.Avoidance;

        // SC2-style surround: dampen forward drive when blocked by close neighbors
        if (forces.SurroundDampen > 0f)
            desiredVelocity *= (1f - Mathf.Clamp01(forces.SurroundDampen));

        // SC2-style direction preservation: strip the backward component of
        // steering relative to the desired direction. This ensures boids can
        // only deflect the unit laterally, never push it backward against
        // its pathfinding direction.
        if (desiredVelocity.sqrMagnitude > 0.01f)
        {
            Vector3 desiredDir = desiredVelocity.normalized;
            float backwardComponent = Vector3.Dot(totalSteering, desiredDir);
            if (backwardComponent < 0f)
                totalSteering -= desiredDir * backwardComponent;

            // Cap lateral deflection: clamp the combined direction to a maximum
            // angle from the desired direction to prevent infinite sideways drift.
            Vector3 candidate = desiredVelocity + totalSteering;
            candidate.y = 0f;
            if (candidate.sqrMagnitude > 0.01f)
            {
                float angle = Vector3.Angle(desiredDir, candidate.normalized);
                if (angle > MaxLateralDeflectionAngle)
                {
                    // Rotate desired direction toward candidate by max angle
                    Vector3 cross = Vector3.Cross(desiredDir, candidate);
                    float sign = cross.y >= 0f ? 1f : -1f;
                    Quaternion maxRot = Quaternion.AngleAxis(MaxLateralDeflectionAngle * sign, Vector3.up);
                    Vector3 clampedDir = maxRot * desiredDir;
                    totalSteering = clampedDir * candidate.magnitude - desiredVelocity;
                }
            }
        }

        Vector3 combined = desiredVelocity + totalSteering;
        combined.y = 0f;

        float speedLimit = forces.HasOverlap ? maxSpeed * 1.5f : maxSpeed;
        if (combined.sqrMagnitude > speedLimit * speedLimit)
            combined = combined.normalized * speedLimit;

        return combined;
    }

    /// <summary>
    /// Pure-logic separation push for stopped units.
    /// Returns a position delta, not a velocity.
    /// </summary>
    public static Vector3 ComputeSeparationPushCore(
        Vector3 myPos, float myRadius, int myInstanceId,
        IReadOnlyList<BoidsNeighbor> neighbors, float deltaTime, int myTeamId = 0)
    {
        Vector3 push = Vector3.zero;

        for (int i = 0; i < neighbors.Count; i++)
        {
            var other = neighbors[i];
            if (other.InstanceId == myInstanceId) continue;

            Vector3 offset = myPos - other.Position;
            offset.y = 0f;
            float dist = offset.magnitude;

            if (dist < 0.01f)
            {
                offset = HashBasedDirection(myInstanceId);
                dist = 0.01f;
            }

            float combinedRadius = myRadius + other.Radius;
            if (dist < combinedRadius)
            {
                float penetration = combinedRadius - dist;
                float tierScale = CollisionTierScale(myRadius, other.Radius);

                // SC2-style: stopped units don't push enemies,
                // and barely push allies in combat
                bool isAlly = other.TeamId == myTeamId;
                float stateScale = 1f;
                if (!isAlly)
                    stateScale = 0.1f;
                else if (other.State == UnitState.Fighting)
                    stateScale = 0.15f;

                push += (offset / dist) * penetration * 0.5f * tierScale * stateScale;
            }
        }

        float maxPush = myRadius * PushSpeed * deltaTime;
        if (push.sqrMagnitude > maxPush * maxPush)
            push = push.normalized * maxPush;

        return push;
    }

    /// <summary>
    /// Compute the combined Boids steering force for a unit.
    /// desiredVelocity comes from Layer 1 (waypoint direction * speed).
    /// Returns the final velocity after applying all Boids forces.
    /// Delegates to ComputeForcesCore + CombineForces to keep a single code path.
    /// </summary>
    public Vector3 ComputeSteering(IPathfindingAgent agent, Vector3 desiredVelocity, float maxSpeed, bool isMarching)
    {
        Debug.Assert(agent != null, "[BoidsManager] ComputeSteering: agent is null");
        Debug.Assert(spatialHash != null, "[BoidsManager] ComputeSteering: spatialHash is null");
        if (agent == null || spatialHash == null)
            return desiredVelocity;

        float myRadius = agent.EffectiveRadius;
        Vector3 myPos = agent.Position;
        var nearby = spatialHash.QueryRadius(myPos, myRadius * 4f);
        GatherNeighbors(nearby, agent.InstanceId, Time.deltaTime, neighborBuffer);

        avoidanceSideLocks.TryGetValue(agent.InstanceId, out int prevSide);
        var forces = ComputeForcesCore(
            myPos, myRadius, agent.TeamId, agent.InstanceId,
            desiredVelocity, neighborBuffer, prevSide);
        avoidanceSideLocks[agent.InstanceId] = forces.AvoidanceSide;
        Vector3 combined = CombineForces(forces, desiredVelocity, maxSpeed, isMarching);

        StatBoidsCallCount++;
        float steeringMag = (combined - desiredVelocity).magnitude;
        if (steeringMag > StatMaxSteeringMagnitude)
            StatMaxSteeringMagnitude = steeringMag;

        if (desiredVelocity.sqrMagnitude > 0.01f && steeringMag > 0.01f)
        {
            float dot = Vector3.Dot(combined.normalized, desiredVelocity.normalized);
            if (dot < 0.3f)
                StatOverriddenByBoids++;
        }

        if (GameDebug.Boids && forces.SeparationCount > 0)
        {
            float dot = desiredVelocity.sqrMagnitude > 0.01f
                ? Vector3.Dot(combined.normalized, desiredVelocity.normalized)
                : 1f;
            Debug.Log($"[Boids] {agent.Name}: sep={forces.Separation.magnitude:F2}({forces.SeparationCount}) " +
                $"align={forces.Alignment.magnitude:F2} " +
                $"cohes={forces.Cohesion.magnitude:F2} " +
                $"avoid={forces.Avoidance.magnitude:F2} " +
                $"desired={desiredVelocity.magnitude:F2} combined={combined.magnitude:F2} " +
                $"steerMag={steeringMag:F2} dirDot={dot:F2}");
        }

        return combined;
    }

    /// <summary>
    /// Compute separation-only push for a stopped/idle unit.
    /// Returns a position delta to apply directly (not a velocity).
    /// Delegates to ComputeSeparationPushCore for a single code path.
    /// </summary>
    public Vector3 ComputeSeparationPush(IPathfindingAgent agent, float deltaTime)
    {
        Debug.Assert(agent != null, "[BoidsManager] ComputeSeparationPush: agent is null");
        Debug.Assert(spatialHash != null, "[BoidsManager] ComputeSeparationPush: spatialHash is null");
        if (agent == null || spatialHash == null) return Vector3.zero;

        float myRadius = agent.EffectiveRadius;
        Vector3 myPos = agent.Position;
        var nearby = spatialHash.QueryRadius(myPos, myRadius * 3f);
        GatherNeighbors(nearby, agent.InstanceId, Time.deltaTime, neighborBuffer);

        var push = ComputeSeparationPushCore(myPos, myRadius, agent.InstanceId, neighborBuffer, deltaTime, agent.TeamId);

        if (GameDebug.Boids && push.sqrMagnitude > 0.01f)
            Debug.Log($"[Boids] SepPush {agent.Name}: push={push.magnitude:F2} neighbors={neighborBuffer.Count}");

        return push;
    }

    /// <summary>
    /// Pure-logic density calculation for unit testing.
    /// Returns the density fraction and whether the unit should stop.
    /// </summary>
    public static (float density, bool shouldStop) ComputeDensityCore(
        float distToDest, float myRadius,
        IReadOnlyList<BoidsNeighbor> neighbors, int myInstanceId)
    {
        if (distToDest < DensityMinDist || distToDest > DensityMaxDist)
            return (0f, false);

        float probeRadius = distToDest + myRadius;
        float circleArea = Mathf.PI * probeRadius * probeRadius;
        float agentsArea = 0f;

        for (int i = 0; i < neighbors.Count; i++)
        {
            if (neighbors[i].InstanceId == myInstanceId) continue;
            float otherR = neighbors[i].Radius;
            agentsArea += Mathf.PI * otherR * otherR;
        }

        // SC2-style: density measures OTHER units' area occupancy only.
        // Self is excluded — we want to know if the destination is crowded,
        // not inflate density with our own footprint.
        float density = agentsArea / circleArea;
        return (density, density > DensityFraction);
    }

    /// <summary>
    /// SC2-style density stop: compute the fraction of a circle (centered at
    /// destination, radius = distance to agent) that is occupied by other agents.
    /// Stop if occupied fraction exceeds DensityFraction (default 0.5 per SC2).
    /// </summary>
    public bool ShouldDensityStop(IPathfindingAgent agent, Vector3 destination)
    {
        Debug.Assert(agent != null, "[BoidsManager] ShouldDensityStop: agent is null");
        Debug.Assert(spatialHash != null, "[BoidsManager] ShouldDensityStop: spatialHash is null");
        if (agent == null || spatialHash == null) return false;

        float myRadius = agent.EffectiveRadius;
        float distToDest = Vector3.Distance(agent.Position, destination);
        if (distToDest < DensityMinDist) return false;
        if (distToDest > DensityMaxDist) return false;

        float probeRadius = distToDest + myRadius;

        var nearby = spatialHash.QueryRadius(destination, probeRadius);
        GatherNeighbors(nearby, agent.InstanceId, Time.deltaTime, neighborBuffer);

        var (density, shouldStop) = ComputeDensityCore(distToDest, myRadius, neighborBuffer, agent.InstanceId);

        if (shouldStop)
        {
            StatDensityStopCount++;
            if (GameDebug.Boids)
                Debug.Log($"[Boids] DensityStop: {agent.Name} dist={distToDest:F1} density={density:F2}/{DensityFraction:F2}");
        }

        return shouldStop;
    }
}
