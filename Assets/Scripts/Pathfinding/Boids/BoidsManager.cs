using UnityEngine;
using System.Collections.Generic;

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
    private const float DensityStopThreshold = 0.6f;

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

    public BoidsManager(SpatialHashGrid hash)
    {
        spatialHash = hash;
    }

    /// <summary>
    /// Compute the combined Boids steering force for a unit.
    /// desiredVelocity comes from Layer 1 (waypoint direction * speed).
    /// Returns the final velocity after applying all Boids forces.
    /// </summary>
    public Vector3 ComputeSteering(Unit unit, Vector3 desiredVelocity, float maxSpeed, bool isMarching)
    {
        if (unit == null || spatialHash == null)
            return desiredVelocity;

        float myRadius = unit.EffectiveRadius;
        float separationRadius = myRadius * 3f;
        float avoidanceRadius = myRadius * 4f;

        Vector3 myPos = unit.transform.position;
        var nearby = spatialHash.QueryRadius(myPos, avoidanceRadius);

        Vector3 separation = Vector3.zero;
        Vector3 alignment = Vector3.zero;
        Vector3 cohesion = Vector3.zero;
        Vector3 avoidance = Vector3.zero;

        int separationCount = 0;
        int alignmentCount = 0;
        int cohesionCount = 0;
        int avoidanceSideLocked = 0; // 0=not set, 1=positive, -1=negative

        foreach (var other in nearby)
        {
            if (other == null || other == unit || other.IsDead) continue;

            float otherRadius = other.EffectiveRadius;
            Vector3 offset = myPos - other.transform.position;
            offset.y = 0f;
            float dist = offset.magnitude;
            if (dist < 0.01f)
            {
                uint hash = (uint)Mathf.Abs(unit.GetInstanceID()) * 2654435761u;
                float angle = ((hash & 0xFFFF) / (float)0xFFFF) * Mathf.PI * 2f;
                offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                dist = 0.01f;
            }

            float combinedRadius = myRadius + otherRadius;
            float gap = dist - combinedRadius;

            // SEPARATION: push away from units too close
            if (dist < combinedRadius * 1.5f)
            {
                float strength = 1f - (dist / (combinedRadius * 1.5f));
                separation += (offset / dist) * strength;
                separationCount++;
            }

            // AVOIDANCE: steer to the side, not backward
            if (gap < avoidanceRadius && gap > -combinedRadius)
            {
                Vector3 awayDir = offset / dist;
                Vector3 perpendicular = Vector3.Cross(Vector3.up, awayDir).normalized;

                // Lock avoidance side per frame to prevent jitter
                if (avoidanceSideLocked == 0)
                {
                    float dot = Vector3.Dot(perpendicular, desiredVelocity.normalized);
                    avoidanceSideLocked = dot >= 0 ? 1 : -1;
                }

                Vector3 avoidDir = perpendicular * avoidanceSideLocked;
                float weight = 1f - Mathf.Clamp01(gap / avoidanceRadius);
                avoidance += avoidDir * weight * AvoidanceStrength;
            }

            // ALIGNMENT: steer toward average heading of neighbors (allies only)
            if (other.TeamId == unit.TeamId && other.Movement != null)
            {
                Vector3 otherVel = other.transform.position - other.Movement.PreviousPosition;
                otherVel.y = 0f;
                if (otherVel.sqrMagnitude > 0.01f)
                {
                    alignment += otherVel.normalized;
                    alignmentCount++;
                }
            }

            // COHESION: steer toward center of mass of nearby allies
            if (other.TeamId == unit.TeamId)
            {
                cohesion += other.transform.position;
                cohesionCount++;
            }

            // DANCE PREVENTION: when two units head toward each other, steer aside
            if (desiredVelocity.sqrMagnitude > 0.01f)
            {
                Vector3 otherVel2 = other.transform.position - (other.Movement != null ? other.Movement.PreviousPosition : other.transform.position);
                otherVel2.y = 0f;
                if (otherVel2.sqrMagnitude > 0.01f)
                {
                    float headingDot = Vector3.Dot(desiredVelocity.normalized, otherVel2.normalized);
                    if (headingDot < -0.8f) // heading at each other
                    {
                        Vector3 relPos = myPos - other.transform.position;
                        relPos.y = 0f;
                        float side = Mathf.Sign(NavMeshData.Cross2D(
                            new Vector2(desiredVelocity.x, desiredVelocity.z),
                            new Vector2(relPos.x, relPos.z)));

                        Vector3 dancePush = Vector3.Cross(Vector3.up, desiredVelocity.normalized) * side;
                        avoidance += dancePush * DanceBreakWeight;
                    }
                }
            }
        }

        // Normalize and weight forces
        Vector3 totalSteering = Vector3.zero;

        if (separationCount > 0)
            totalSteering += (separation / separationCount) * SeparationWeight;

        if (isMarching)
        {
            if (alignmentCount > 0)
            {
                alignment = (alignment / alignmentCount).normalized;
                totalSteering += alignment * AlignmentWeight;
            }
            if (cohesionCount > 0)
            {
                cohesion = (cohesion / cohesionCount) - myPos;
                cohesion.y = 0f;
                if (cohesion.sqrMagnitude > 0.01f)
                    totalSteering += cohesion.normalized * CohesionWeight;
            }
        }

        totalSteering += avoidance;

        Vector3 combined = desiredVelocity + totalSteering;
        combined.y = 0f;

        if (combined.sqrMagnitude > maxSpeed * maxSpeed)
            combined = combined.normalized * maxSpeed;

        StatBoidsCallCount++;
        float steeringMag = totalSteering.magnitude;
        if (steeringMag > StatMaxSteeringMagnitude)
            StatMaxSteeringMagnitude = steeringMag;

        // Track when Boids forces significantly override the desired direction
        if (desiredVelocity.sqrMagnitude > 0.01f && steeringMag > 0.01f)
        {
            float dot = Vector3.Dot(combined.normalized, desiredVelocity.normalized);
            if (dot < 0.3f) // Boids is pushing unit away from desired direction by > ~72deg
                StatOverriddenByBoids++;
        }

        if (GameDebug.Boids && separationCount > 0)
        {
            float dot = desiredVelocity.sqrMagnitude > 0.01f
                ? Vector3.Dot(combined.normalized, desiredVelocity.normalized)
                : 1f;
            Debug.Log($"[Boids] {unit.name}: sep={separation.magnitude:F2}({separationCount}) " +
                $"align={alignment.magnitude:F2}({alignmentCount}) " +
                $"cohes={cohesion.magnitude:F2}({cohesionCount}) " +
                $"avoid={avoidance.magnitude:F2} " +
                $"desired={desiredVelocity.magnitude:F2} combined={combined.magnitude:F2} " +
                $"steerMag={steeringMag:F2} dirDot={dot:F2}");
        }

        return combined;
    }

    /// <summary>
    /// Density stop check. If the area around the destination is too crowded,
    /// the unit should stop and wait rather than pushing into the pile.
    /// Returns true if the unit should stop.
    /// </summary>
    public bool ShouldDensityStop(Unit unit, Vector3 destination)
    {
        if (unit == null || spatialHash == null) return false;

        float distToDest = Vector3.Distance(unit.transform.position, destination);
        if (distToDest < 0.5f) return false;
        if (distToDest > 15f) return false;

        // Use a fixed probe radius around the destination so the density check
        // is meaningful regardless of how far the unit still is.
        float probeRadius = Mathf.Min(distToDest, 4f);
        float circleArea = Mathf.PI * probeRadius * probeRadius;
        float agentsArea = 0f;
        int nearbyCount = 0;

        var nearby = spatialHash.QueryRadius(destination, probeRadius);
        foreach (var other in nearby)
        {
            if (other == null || other == unit || other.IsDead) continue;
            float otherR = other.EffectiveRadius;
            agentsArea += Mathf.PI * otherR * otherR;
            nearbyCount++;
        }

        float density = agentsArea / circleArea;
        bool shouldStop = density > DensityStopThreshold;

        // Also stop if there are simply too many units crowding the destination
        if (!shouldStop && nearbyCount >= 6 && distToDest > 2f)
        {
            shouldStop = true;
        }

        if (shouldStop)
        {
            StatDensityStopCount++;
            if (GameDebug.Boids)
                Debug.Log($"[Boids] DensityStop: {unit.name} dist={distToDest:F1} probe={probeRadius:F1}" +
                    $" density={density:F2} nearby={nearbyCount}");
        }

        return shouldStop;
    }
}
