using UnityEngine;

/// <summary>
/// Abstraction over the grid system for pathfinding.
/// Enables unit testing of pathfinding algorithms with fake grids.
/// </summary>
public interface IGrid
{
    int Width { get; }
    int Height { get; }
    float CellSize { get; }
    Vector3 GridOrigin { get; }
    bool IsWalkable(Vector2Int cell);
    bool IsInBounds(Vector2Int cell);
    Vector2Int WorldToCell(Vector3 worldPosition);
    Vector3 CellToWorld(Vector2Int cell);
    Vector3 FindNearestWalkablePosition(Vector3 desiredWorldPos, Vector3 referencePos);
    bool HasLineOfSight(Vector2Int from, Vector2Int to);
}
