using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Pure-logic helpers for selective path invalidation.
/// Determines whether a unit's path is affected by a changed region (building placed/destroyed).
/// </summary>
public static class PathInvalidation
{
    /// <summary>
    /// Compute the axis-aligned bounding box of a list of waypoints, expanded by margin.
    /// Returns a zero-size Bounds at origin if waypoints is null or empty.
    /// </summary>
    public static Bounds ComputePathBounds(List<Vector3> waypoints, float margin = 0f)
    {
        if (waypoints == null || waypoints.Count == 0)
            return new Bounds(Vector3.zero, Vector3.zero);

        Vector3 min = waypoints[0];
        Vector3 max = waypoints[0];

        for (int i = 1; i < waypoints.Count; i++)
        {
            var wp = waypoints[i];
            if (wp.x < min.x) min.x = wp.x;
            if (wp.y < min.y) min.y = wp.y;
            if (wp.z < min.z) min.z = wp.z;
            if (wp.x > max.x) max.x = wp.x;
            if (wp.y > max.y) max.y = wp.y;
            if (wp.z > max.z) max.z = wp.z;
        }

        if (margin > 0f)
        {
            Vector3 m = new Vector3(margin, margin, margin);
            min -= m;
            max += m;
        }

        var bounds = new Bounds();
        bounds.SetMinMax(min, max);
        return bounds;
    }

    /// <summary>
    /// Check if a path (list of waypoints) intersects a changed region.
    /// Uses AABB overlap between the path's bounding box (expanded by margin) and the region.
    /// Returns false for null/empty waypoints.
    /// </summary>
    public static bool PathIntersectsRegion(List<Vector3> waypoints, Bounds region, float margin = 0f)
    {
        if (waypoints == null || waypoints.Count == 0)
            return false;

        Bounds pathBounds = ComputePathBounds(waypoints, margin);
        return pathBounds.Intersects(region);
    }

    /// <summary>
    /// Compute the union of two Bounds (encapsulates both).
    /// </summary>
    public static Bounds UnionBounds(Bounds a, Bounds b)
    {
        a.Encapsulate(b);
        return a;
    }
}
