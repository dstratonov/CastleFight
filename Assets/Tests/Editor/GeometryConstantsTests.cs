using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class GeometryConstantsTests
{
    // ================================================================
    //  ApproxEqual — float
    // ================================================================

    [Test]
    public void ApproxEqual_Float_CorrectAtDifferentScales()
    {
        Assert.IsTrue(GeometryConstants.ApproxEqual(1.0f, 1.00005f), "Close values");
        Assert.IsFalse(GeometryConstants.ApproxEqual(1.0f, 1.1f), "Distinct values");
        Assert.IsTrue(GeometryConstants.ApproxEqual(10000f, 10000.005f), "Large close values");
        Assert.IsFalse(GeometryConstants.ApproxEqual(10000f, 10001f), "Large distinct values");
        Assert.IsTrue(GeometryConstants.ApproxEqual(0f, 0.00005f), "Near-zero");
    }

    [Test]
    public void ApproxEqual_Float_Symmetric()
    {
        Assert.AreEqual(
            GeometryConstants.ApproxEqual(1.0f, 1.0001f),
            GeometryConstants.ApproxEqual(1.0001f, 1.0f),
            "ApproxEqual should be symmetric");
    }

    [Test]
    public void ApproxEqual_Float_NegativeValues()
    {
        Assert.IsTrue(GeometryConstants.ApproxEqual(-5f, -5.00005f), "Close negative values");
        Assert.IsFalse(GeometryConstants.ApproxEqual(-5f, -5.1f), "Distinct negative values");
        Assert.IsFalse(GeometryConstants.ApproxEqual(-1f, 1f), "Opposite signs are not equal");
    }

    [Test]
    public void ApproxEqual_Float_ExactlyEqual()
    {
        Assert.IsTrue(GeometryConstants.ApproxEqual(0f, 0f));
        Assert.IsTrue(GeometryConstants.ApproxEqual(42f, 42f));
        Assert.IsTrue(GeometryConstants.ApproxEqual(-100f, -100f));
    }

    // ================================================================
    //  ApproxEqual — Vector2
    // ================================================================

    [Test]
    public void ApproxEqual_Vector2_CloseAndFar()
    {
        Assert.IsTrue(GeometryConstants.ApproxEqual(
            new Vector2(1f, 2f), new Vector2(1.005f, 2.005f)), "Close");
        Assert.IsFalse(GeometryConstants.ApproxEqual(
            new Vector2(0f, 0f), new Vector2(1f, 0f)), "Far");
    }

    [Test]
    public void ApproxEqual_Vector2_OriginWithTinyOffset()
    {
        // Offset of 0.001 in both axes: sqrMag = 0.000002, threshold = 0.0001
        Assert.IsTrue(GeometryConstants.ApproxEqual(
            Vector2.zero, new Vector2(0.001f, 0.001f)),
            "Tiny offset from origin should be equal");
    }

    [Test]
    public void ApproxEqual_Vector2_JustBeyondThreshold()
    {
        // sqrMagnitude of (0.01, 0) = 0.0001 which is NOT < ZeroDistSqr (it's ==)
        Assert.IsFalse(GeometryConstants.ApproxEqual(
            Vector2.zero, new Vector2(0.01f, 0f)),
            "Exactly at threshold should be not-equal (strict less-than)");
    }

    // ================================================================
    //  CollinearEps — behavior tests
    // ================================================================

    [Test]
    public void CollinearEps_ScalesWithLength()
    {
        float epsShort = GeometryConstants.CollinearEps(1f);
        float epsLong = GeometryConstants.CollinearEps(100f);
        Assert.Greater(epsLong, epsShort, "Longer segments should have larger tolerance");
        Assert.AreEqual(100f, epsLong / epsShort, 0.01f, "Should scale linearly");
    }

    [Test]
    public void CollinearEps_ZeroLength_ReturnsZero()
    {
        float eps = GeometryConstants.CollinearEps(0f);
        Assert.AreEqual(0f, eps, 1e-10f);
    }

    [Test]
    public void CollinearEps_DetectsNearCollinearPoints()
    {
        // Three nearly collinear points: cross product should be within CollinearEps
        Vector2 a = new Vector2(0f, 0f);
        Vector2 b = new Vector2(10f, 0f);
        Vector2 c = new Vector2(5f, 0.0001f); // just barely off the line

        float segLen = (b - a).magnitude;
        float eps = GeometryConstants.CollinearEps(segLen);

        // Cross product of (b-a) x (c-a)
        Vector2 ab = b - a;
        Vector2 ac = c - a;
        float cross = ab.x * ac.y - ab.y * ac.x;

        Assert.Less(Mathf.Abs(cross), eps,
            "Near-collinear point should be within CollinearEps tolerance");
    }

    [Test]
    public void CollinearEps_RejectsNonCollinearPoints()
    {
        Vector2 a = new Vector2(0f, 0f);
        Vector2 b = new Vector2(10f, 0f);
        Vector2 c = new Vector2(5f, 2f); // clearly off the line

        float segLen = (b - a).magnitude;
        float eps = GeometryConstants.CollinearEps(segLen);

        Vector2 ab = b - a;
        Vector2 ac = c - a;
        float cross = ab.x * ac.y - ab.y * ac.x;

        Assert.Greater(Mathf.Abs(cross), eps,
            "Non-collinear point should exceed CollinearEps tolerance");
    }

    // ================================================================
    //  SoSPerturbation — behavior tests
    // ================================================================

    [Test]
    public void SoSPerturbation_ScalesWithCoordinates()
    {
        float epsSmall = GeometryConstants.SoSPerturbation(1f, 0f);
        float epsLarge = GeometryConstants.SoSPerturbation(100f, 0f);
        Assert.Greater(epsLarge, epsSmall, "Larger coordinates should produce larger perturbation");
    }

    [Test]
    public void SoSPerturbation_NearZero_ReturnsMinimum()
    {
        float eps = GeometryConstants.SoSPerturbation(0f, 0f);
        Assert.Greater(eps, 0f, "Should return a positive minimum even for zero coordinates");
    }

    [Test]
    public void SoSPerturbation_UsesMaxOfAbsXY()
    {
        float epsX = GeometryConstants.SoSPerturbation(50f, 10f);
        float epsY = GeometryConstants.SoSPerturbation(10f, 50f);
        Assert.AreEqual(epsX, epsY, 1e-10f, "Should use max of abs(x), abs(y)");
    }

    [Test]
    public void SoSPerturbation_NegativeCoordinates_SameAsMagnitude()
    {
        float epsPos = GeometryConstants.SoSPerturbation(50f, 30f);
        float epsNeg = GeometryConstants.SoSPerturbation(-50f, -30f);
        Assert.AreEqual(epsPos, epsNeg, 1e-10f,
            "Negative coordinates should produce same perturbation as positive");
    }

    [Test]
    public void SoSPerturbation_SmallEnoughToNotCorruptGeometry()
    {
        // For typical game coordinates (0-100), perturbation should be tiny
        // relative to the smallest meaningful geometry (PositionEpsilon = 0.01)
        float eps = GeometryConstants.SoSPerturbation(100f, 100f);
        Assert.Less(eps, GeometryConstants.PositionEpsilon,
            "SoS perturbation should be smaller than PositionEpsilon to avoid corrupting geometry");
    }

    // ================================================================
    //  ZeroDistSqr — behavioral validation
    // ================================================================

    [Test]
    public void ZeroDistSqr_DistinguishesNearAndFarPoints()
    {
        // Two points 0.005 apart: sqrDist = 0.000025 < ZeroDistSqr
        Vector2 a = Vector2.zero;
        Vector2 b = new Vector2(0.005f, 0f);
        Assert.Less((a - b).sqrMagnitude, GeometryConstants.ZeroDistSqr,
            "Points 0.005 apart should be within zero-distance threshold");

        // Two points 0.02 apart: sqrDist = 0.0004 > ZeroDistSqr
        Vector2 c = new Vector2(0.02f, 0f);
        Assert.Greater((a - c).sqrMagnitude, GeometryConstants.ZeroDistSqr,
            "Points 0.02 apart should exceed zero-distance threshold");
    }
}
