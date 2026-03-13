using UnityEngine;

/// <summary>
/// Implemented by entities that can be selected by the player (Unit, Building, Castle).
/// </summary>
public interface ISelectable
{
    GameObject gameObject { get; }
    int TeamId { get; }
    string DisplayName { get; }
    Health Health { get; }
}
