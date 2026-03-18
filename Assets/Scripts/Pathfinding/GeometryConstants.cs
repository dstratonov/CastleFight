using UnityEngine;

/// <summary>
/// Shared epsilon and tolerance constants for all pathfinding geometry operations.
/// Uses combined absolute + relative tolerance per realtimecollisiondetection.net:
/// |x - y| &lt;= absTol + relTol * max(|x|, |y|)
/// </summary>
public static class GeometryConstants
{
    /// <summary>
    /// Absolute tolerance for squared-distance comparisons (0.01 units = ~0.7cm).
    /// </summary>
    public const float ZeroDistSqr = 1e-4f;

    /// <summary>
    /// Degenerate triangle area threshold. Triangles with area below this are
    /// considered degenerate (roughly half a cell's area for cellSize=1).
    /// </summary>
    public const float DegenerateAreaThreshold = 1e-3f;

    /// <summary>
    /// Absolute tolerance for position comparisons. Two positions closer than
    /// this are considered identical.
    /// </summary>
    public const float PositionEpsilon = 0.01f;

    /// <summary>
    /// Relative tolerance for floating point comparisons.
    /// </summary>
    public const float RelativeTolerance = 1e-6f;

    /// <summary>
    /// Absolute tolerance for floating point comparisons.
    /// </summary>
    public const float AbsoluteTolerance = 1e-4f;

    /// <summary>
    /// Vertex deduplication precision (3 decimal places).
    /// </summary>
    public const float VertexDeduplicationScale = 1000f;

    /// <summary>
    /// Super-triangle size multiplier. Must be large enough to contain
    /// all circumcircles of the input point set.
    /// </summary>
    public const float SuperTriangleMultiplier = 100f;

    /// <summary>
    /// Maximum constraint edge length in cells. Longer edges are split
    /// to help CDT convergence.
    /// </summary>
    public const int MaxConstraintEdgeCells = 3;

    /// <summary>
    /// Combined absolute + relative tolerance for approximate equality.
    /// Per realtimecollisiondetection.net/pubs/Tolerances.
    /// </summary>
    public static bool ApproxEqual(float a, float b)
    {
        return Mathf.Abs(a - b) <= AbsoluteTolerance + RelativeTolerance * Mathf.Max(Mathf.Abs(a), Mathf.Abs(b));
    }

    /// <summary>
    /// Approximate equality for Vector2, using combined tolerance.
    /// </summary>
    public static bool ApproxEqual(Vector2 a, Vector2 b)
    {
        return (a - b).sqrMagnitude < ZeroDistSqr;
    }

    /// <summary>
    /// Scale-relative collinear epsilon for cross-product tests.
    /// Scales with segment length so that tolerance is proportional to coordinate magnitude.
    /// </summary>
    public static float CollinearEps(float segmentLength)
    {
        return AbsoluteTolerance * segmentLength;
    }

    /// <summary>
    /// Scale-relative perturbation for Simulation of Simplicity.
    /// </summary>
    public static float SoSPerturbation(float abX, float abY)
    {
        float scale = Mathf.Max(Mathf.Abs(abX), Mathf.Abs(abY));
        return Mathf.Max(1e-5f * scale, 1e-7f);
    }
}
