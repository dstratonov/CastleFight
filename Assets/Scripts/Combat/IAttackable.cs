using UnityEngine;

/// <summary>
/// Implemented by any entity that can be targeted and attacked (Unit, Building, Castle).
/// </summary>
public interface IAttackable
{
    GameObject gameObject { get; }
    Health Health { get; }
    int TeamId { get; }
    ArmorType ArmorType { get; }
    float TargetRadius { get; }

    /// <summary>
    /// Current grid cell of this entity. Used by chasers to detect when
    /// the target has moved and the path needs recalculation.
    /// </summary>
    Vector2Int CurrentCell { get; }

    /// <summary>
    /// Grid footprint size (NxN cells). Used for attack range rect intersection.
    /// </summary>
    int FootprintSize { get; }

    /// <summary>
    /// Actual footprint bounds on the grid (min, max inclusive).
    /// For units: computed from CurrentCell + FootprintSize via FootprintHelper.
    /// For buildings/castles: computed from actual occupied cells.
    /// </summary>
    (Vector2Int min, Vector2Int max) FootprintBounds { get; }

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
