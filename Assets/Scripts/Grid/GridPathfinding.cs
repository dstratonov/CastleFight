using UnityEngine;
using System.Collections.Generic;

public struct PathResult

{
    public List<Vector2Int> Path;
    public bool IsComplete;
    public Vector2Int ClosestReachableCell;

    public bool HasPath => Path != null && Path.Count > 0;
}

public static class GridPathfinding
{
    private const int MaxIterations = 2000;
    private const float DiagonalCost = 1.41421356f;

    private static readonly Vector2Int[] CardinalDirections =
    {
        new(0, 1), new(0, -1), new(1, 0), new(-1, 0)
    };

    private static readonly Vector2Int[] DiagonalDirections =
    {
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)
    };

    public static PathResult FindPath(Vector2Int start, Vector2Int goal, GridSystem grid)
    {
        var result = new PathResult
        {
            Path = null,
            IsComplete = false,
            ClosestReachableCell = start
        };

        if (grid == null || !grid.IsInBounds(start) || !grid.IsInBounds(goal))
            return result;

        if (start == goal)
        {
            result.Path = new List<Vector2Int> { start };
            result.IsComplete = true;
            result.ClosestReachableCell = goal;
            return result;
        }

        var openSet = new BinaryHeap();
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var gScore = new Dictionary<Vector2Int, float>();
        var fScore = new Dictionary<Vector2Int, float>();
        var closedSet = new HashSet<Vector2Int>();

        gScore[start] = 0;
        fScore[start] = Heuristic(start, goal);
        openSet.Insert(start, fScore[start]);

        float closestDistToGoal = Heuristic(start, goal);
        Vector2Int closestNode = start;

        int iterations = 0;

        while (openSet.Count > 0 && iterations < MaxIterations)
        {
            iterations++;
            Vector2Int current = openSet.ExtractMin();

            if (current == goal)
            {
                result.Path = ReconstructPath(cameFrom, current);
                result.IsComplete = true;
                result.ClosestReachableCell = goal;
                if (GameDebug.Pathfinding)
                    Debug.Log($"[Path] {start}->{goal} COMPLETE len={result.Path.Count} iter={iterations}");
                return result;
            }

            closedSet.Add(current);

            float distToGoal = Heuristic(current, goal);
            if (distToGoal < closestDistToGoal)
            {
                closestDistToGoal = distToGoal;
                closestNode = current;
            }

            foreach (var dir in CardinalDirections)
            {
                Vector2Int neighbor = current + dir;
                if (closedSet.Contains(neighbor) || !grid.IsInBounds(neighbor)) continue;
                if (!IsPassable(neighbor, goal, grid)) continue;

                float tentativeG = gScore[current] + 1f;
                if (!gScore.ContainsKey(neighbor) || tentativeG < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = tentativeG + Heuristic(neighbor, goal);
                    if (!openSet.Contains(neighbor))
                        openSet.Insert(neighbor, fScore[neighbor]);
                    else
                        openSet.Update(neighbor, fScore[neighbor]);
                }
            }

            foreach (var dir in DiagonalDirections)
            {
                Vector2Int neighbor = current + dir;
                if (closedSet.Contains(neighbor) || !grid.IsInBounds(neighbor)) continue;
                if (!IsPassable(neighbor, goal, grid)) continue;

                Vector2Int adj1 = new(current.x + dir.x, current.y);
                Vector2Int adj2 = new(current.x, current.y + dir.y);
                if (!IsPassable(adj1, goal, grid) || !IsPassable(adj2, goal, grid))
                    continue;

                float tentativeG = gScore[current] + DiagonalCost;
                if (!gScore.ContainsKey(neighbor) || tentativeG < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = tentativeG + Heuristic(neighbor, goal);
                    if (!openSet.Contains(neighbor))
                        openSet.Insert(neighbor, fScore[neighbor]);
                    else
                        openSet.Update(neighbor, fScore[neighbor]);
                }
            }
        }

        result.ClosestReachableCell = closestNode;
        if (closestNode != start)
        {
            result.Path = ReconstructPath(cameFrom, closestNode);
            result.IsComplete = false;
            if (GameDebug.Pathfinding)
                Debug.Log($"[Path] {start}->{goal} PARTIAL closest={closestNode} len={result.Path.Count} iter={iterations}");
        }
        else
        {
            if (GameDebug.Pathfinding)
                Debug.Log($"[Path] {start}->{goal} FAILED iter={iterations}");
        }

        return result;
    }

    /// <summary>
    /// Reduces a cell-by-cell path to a minimal set of waypoints using line-of-sight checks.
    /// </summary>
    public static List<Vector3> SmoothPath(List<Vector2Int> cellPath, GridSystem grid)
    {
        if (cellPath == null || cellPath.Count == 0 || grid == null)
            return new List<Vector3>();

        var waypoints = new List<Vector3>();
        waypoints.Add(grid.CellToWorld(cellPath[0]));

        if (cellPath.Count == 1)
            return waypoints;

        int anchor = 0;
        while (anchor < cellPath.Count - 1)
        {
            int farthest = anchor + 1;
            for (int i = anchor + 2; i < cellPath.Count; i++)
            {
                if (grid.HasLineOfSight(cellPath[anchor], cellPath[i]))
                    farthest = i;
                else
                    break;
            }
            waypoints.Add(grid.CellToWorld(cellPath[farthest]));
            anchor = farthest;
        }

        return waypoints;
    }

    private static bool IsPassable(Vector2Int cell, Vector2Int goal, GridSystem grid)
    {
        return grid.IsWalkable(cell);
    }

    private static float Heuristic(Vector2Int a, Vector2Int b)
    {
        int dx = Mathf.Abs(a.x - b.x);
        int dy = Mathf.Abs(a.y - b.y);
        return dx + dy + (DiagonalCost - 2f) * Mathf.Min(dx, dy);
    }

    private static List<Vector2Int> ReconstructPath(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int current)
    {
        var path = new List<Vector2Int> { current };
        while (cameFrom.ContainsKey(current))
        {
            current = cameFrom[current];
            path.Add(current);
        }
        path.Reverse();
        return path;
    }
}

public class BinaryHeap
{
    private readonly List<(Vector2Int cell, float priority)> heap = new();
    private readonly Dictionary<Vector2Int, int> indexMap = new();

    public int Count => heap.Count;

    public bool Contains(Vector2Int cell) => indexMap.ContainsKey(cell);

    public void Insert(Vector2Int cell, float priority)
    {
        heap.Add((cell, priority));
        int index = heap.Count - 1;
        indexMap[cell] = index;
        BubbleUp(index);
    }

    public Vector2Int ExtractMin()
    {
        var min = heap[0];
        int last = heap.Count - 1;

        heap[0] = heap[last];
        indexMap[heap[0].cell] = 0;

        heap.RemoveAt(last);
        indexMap.Remove(min.cell);

        if (heap.Count > 0)
            BubbleDown(0);

        return min.cell;
    }

    public void Update(Vector2Int cell, float newPriority)
    {
        if (!indexMap.TryGetValue(cell, out int index)) return;
        float oldPriority = heap[index].priority;
        heap[index] = (cell, newPriority);

        if (newPriority < oldPriority)
            BubbleUp(index);
        else
            BubbleDown(index);
    }

    private void BubbleUp(int index)
    {
        while (index > 0)
        {
            int parent = (index - 1) / 2;
            if (heap[index].priority >= heap[parent].priority) break;
            Swap(index, parent);
            index = parent;
        }
    }

    private void BubbleDown(int index)
    {
        int count = heap.Count;
        while (true)
        {
            int smallest = index;
            int left = 2 * index + 1;
            int right = 2 * index + 2;

            if (left < count && heap[left].priority < heap[smallest].priority)
                smallest = left;
            if (right < count && heap[right].priority < heap[smallest].priority)
                smallest = right;

            if (smallest == index) break;
            Swap(index, smallest);
            index = smallest;
        }
    }

    private void Swap(int a, int b)
    {
        (heap[a], heap[b]) = (heap[b], heap[a]);
        indexMap[heap[a].cell] = a;
        indexMap[heap[b].cell] = b;
    }
}
