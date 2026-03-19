/// <summary>
/// Discrete size classes for pathfinding clearance.
/// Units are bucketed by EffectiveRadius into one of three classes,
/// each with a fixed clearance radius used for width-aware A* filtering.
/// </summary>
public enum UnitSizeClass : byte
{
    Small  = 0,  // radius <= 0.75
    Medium = 1,  // radius <= 1.5
    Large  = 2   // radius > 1.5
}

/// <summary>
/// Classification and clearance lookup for unit size classes.
/// Pure static utility — no Unity dependencies, fully testable.
/// </summary>
public static class SizeClassUtil
{
    public const int ClassCount = 3;

    /// <summary>Clearance radius required per size class (distance from cell center to nearest obstacle).</summary>
    public static readonly float[] ClearanceRadius = { 0.5f, 1.0f, 2.0f };

    private const float SmallMaxRadius  = 0.75f;
    private const float MediumMaxRadius = 1.5f;

    /// <summary>
    /// Classify a unit's EffectiveRadius into a size class.
    /// </summary>
    public static UnitSizeClass Classify(float effectiveRadius)
    {
        if (effectiveRadius <= SmallMaxRadius)  return UnitSizeClass.Small;
        if (effectiveRadius <= MediumMaxRadius) return UnitSizeClass.Medium;
        return UnitSizeClass.Large;
    }

    /// <summary>
    /// Get the clearance radius used for pathfinding cost fields.
    /// </summary>
    public static float GetClearanceRadius(UnitSizeClass sizeClass)
    {
        return ClearanceRadius[(int)sizeClass];
    }

    /// <summary>
    /// Minimum contiguous portal cells needed for a size class to pass through.
    /// </summary>
    public static int MinPortalWidth(UnitSizeClass sizeClass)
    {
        return sizeClass switch
        {
            UnitSizeClass.Small  => 1,
            UnitSizeClass.Medium => 1,
            UnitSizeClass.Large  => 2,
            _ => 1
        };
    }
}
