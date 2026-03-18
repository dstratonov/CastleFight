using UnityEngine;

public static class BoundsHelper
{
    /// <summary>
    /// Visual bounds from all non-particle renderers. Only use for
    /// visual purposes like projectile targeting, health bar placement,
    /// and UI. NOT for spatial queries, footprints, or distance checks.
    /// </summary>
    public static bool TryGetCombinedBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return false;

        bool found = false;
        foreach (var r in renderers)
        {
            if (r is ParticleSystemRenderer) continue;
            if (!found) { bounds = r.bounds; found = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return found;
    }

    /// <summary>
    /// Physical bounds from non-trigger colliders. Prefers root-level
    /// colliders (explicit footprint colliders set by Building/Castle)
    /// over child colliders. This is the ONE authoritative source for
    /// all spatial operations: grid marking, NavMesh carving, attack
    /// positioning, and combat distance checks.
    /// </summary>
    public static bool TryGetColliderBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;

        var rootColliders = go.GetComponents<Collider>();
        bool found = false;
        foreach (var col in rootColliders)
        {
            if (col.isTrigger) continue;
            if (!found) { bounds = col.bounds; found = true; }
            else bounds.Encapsulate(col.bounds);
        }
        if (found) return true;

        var allColliders = go.GetComponentsInChildren<Collider>();
        foreach (var col in allColliders)
        {
            if (col.isTrigger) continue;
            if (!found) { bounds = col.bounds; found = true; }
            else bounds.Encapsulate(col.bounds);
        }
        return found;
    }

    /// <summary>
    /// Authoritative spatial bounds (colliders preferred, renderer fallback).
    /// Used for ALL spatial operations: footprints, distance checks,
    /// attack positioning, and pathfinding.
    /// </summary>
    public static Bounds GetPhysicalBounds(GameObject go)
    {
        if (TryGetColliderBounds(go, out var b))
            return b;
        if (TryGetCombinedBounds(go, out b))
            return b;
        return new Bounds(go.transform.position, Vector3.one * 2f);
    }

    public static Bounds GetCombinedBounds(GameObject go, Bounds fallback)
    {
        return TryGetCombinedBounds(go, out var b) ? b : fallback;
    }

    public static Bounds GetCombinedBounds(GameObject go)
    {
        return GetCombinedBounds(go, new Bounds(go.transform.position, Vector3.one * 2f));
    }

    /// <summary>
    /// XZ center of the physical bounds (colliders preferred).
    /// </summary>
    public static Vector3 GetCenter(GameObject go)
    {
        Bounds b = GetPhysicalBounds(go);
        Vector3 c = b.center;
        c.y = go.transform.position.y;
        return c;
    }

    /// <summary>
    /// XZ radius of the physical bounds (colliders preferred).
    /// </summary>
    public static float GetRadius(GameObject go)
    {
        Bounds b = GetPhysicalBounds(go);
        return Mathf.Max(b.extents.x, b.extents.z);
    }

    /// <summary>
    /// Closest point on the physical bounds (colliders preferred).
    /// Must use the same bounds as grid/NavMesh so that the distance
    /// a unit measures to a target matches the actual walkable gap.
    /// </summary>
    public static Vector3 ClosestPoint(GameObject go, Vector3 from)
    {
        if (TryGetColliderBounds(go, out var b))
            return b.ClosestPoint(from);

        if (TryGetCombinedBounds(go, out b))
            return b.ClosestPoint(from);

        return go.transform.position;
    }
}
