using UnityEngine;

/// <summary>
/// Implemented by any entity that can be targeted and attacked (Unit, Building, Castle).
///
/// Combat is now world-space (not grid-based). Targets expose their world
/// position and a WorldBounds AABB. Range is computed via Bounds.ClosestPoint
/// projected to the XZ plane, so the same check works for point targets
/// (units) and rectangular targets (buildings/castles).
/// </summary>
public interface IAttackable
{
    GameObject gameObject { get; }
    Health Health { get; }
    int TeamId { get; }
    ArmorType ArmorType { get; }

    /// <summary>World position of the entity's center (used for line-of-sight, facing, default approach).</summary>
    Vector3 Position { get; }

    /// <summary>Target radius for range calculations (buildings use 0 since bounds already encode their size).</summary>
    float TargetRadius { get; }

    /// <summary>
    /// World-space axis-aligned bounds of the physical footprint. Used for
    /// closest-point distance checks. For point-like targets (units) this is
    /// a small sphere around Position; for buildings/castles it's the
    /// BoxCollider bounds.
    /// </summary>
    Bounds WorldBounds { get; }

    /// <summary>
    /// Target priority. Higher priority targets are preferred.
    /// Hard targets lock the attacker; soft targets allow upgrading.
    /// </summary>
    TargetPriority Priority { get; }
}

public enum TargetPriority
{
    /// <summary>Default objective (castle). Soft lock — always scanning for upgrades.</summary>
    Default = 0,
    /// <summary>Enemy building. Hard lock — no scanning while engaged.</summary>
    Building = 10,
    /// <summary>Enemy unit. Hard lock — no scanning while engaged.</summary>
    Unit = 20
}

public enum TargetLock
{
    /// <summary>Can be replaced by a higher-priority target during scan.</summary>
    Soft,
    /// <summary>Locked until target dies or leaves leash range.</summary>
    Hard
}
