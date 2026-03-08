using UnityEngine;

public static class BoundsHelper
{
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

    public static Bounds GetCombinedBounds(GameObject go, Bounds fallback)
    {
        return TryGetCombinedBounds(go, out var b) ? b : fallback;
    }

    public static Bounds GetCombinedBounds(GameObject go)
    {
        return GetCombinedBounds(go, new Bounds(go.transform.position, Vector3.one * 2f));
    }

    public static Vector3 GetCenter(GameObject go)
    {
        if (TryGetCombinedBounds(go, out var b))
        {
            Vector3 c = b.center;
            c.y = go.transform.position.y;
            return c;
        }
        return go.transform.position;
    }

    public static float GetRadius(GameObject go)
    {
        if (TryGetCombinedBounds(go, out var b))
            return Mathf.Max(b.extents.x, b.extents.z);
        return 0.5f;
    }

    public static Vector3 ClosestPoint(GameObject go, Vector3 from)
    {
        if (TryGetCombinedBounds(go, out var b))
            return b.ClosestPoint(from);

        var col = go.GetComponent<Collider>();
        if (col != null)
            return col.ClosestPoint(from);

        return go.transform.position;
    }
}
