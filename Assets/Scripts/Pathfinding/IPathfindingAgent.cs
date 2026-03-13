using UnityEngine;

/// <summary>
/// Abstraction over a game unit for pathfinding and steering.
/// Enables unit testing of density costs and Boids without MonoBehaviours.
/// </summary>
public interface IPathfindingAgent
{
    Vector3 Position { get; }
    Vector3 PreviousPosition { get; }
    float EffectiveRadius { get; }
    int TeamId { get; }
    bool IsDead { get; }
    UnitState CurrentState { get; }
    int InstanceId { get; }
    string Name { get; }
}
