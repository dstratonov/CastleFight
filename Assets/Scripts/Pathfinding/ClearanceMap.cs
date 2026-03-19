using UnityEngine;
using System.Collections.Generic;

public class ClearanceMap
{
    private float[,] clearance;
    private int width;
    private int height;

    /// <summary>Bitfield per cell: bit 0=Small passable, bit 1=Medium, bit 2=Large.</summary>
    private byte[,] sizeClassPassable;

    public int Width => width;
    public int Height => height;

    public void ComputeFull(IGrid grid)
    {
        width = grid.Width;
        height = grid.Height;
        clearance = new float[width, height];

        var queue = new Queue<Vector2Int>();

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                var cell = new Vector2Int(x, y);
                if (!grid.IsWalkable(cell))
                {
                    clearance[x, y] = 0f;
                    queue.Enqueue(cell);
                }
                else
                {
                    clearance[x, y] = float.MaxValue;
                }
            }
        }

        BrushFireBFS(queue, grid);
        PrecomputeSizeClasses();
    }

    public void UpdateRegion(Vector2Int min, Vector2Int max, IGrid grid)
    {
        int pad = Mathf.CeilToInt(Mathf.Max(width, height));
        pad = Mathf.Min(pad, 20);

        int x0 = Mathf.Max(0, min.x - pad);
        int y0 = Mathf.Max(0, min.y - pad);
        int x1 = Mathf.Min(width - 1, max.x + pad);
        int y1 = Mathf.Min(height - 1, max.y + pad);

        var queue = new Queue<Vector2Int>();

        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                var cell = new Vector2Int(x, y);
                if (!grid.IsWalkable(cell))
                {
                    clearance[x, y] = 0f;
                    queue.Enqueue(cell);
                }
                else
                {
                    clearance[x, y] = float.MaxValue;
                }
            }
        }

        BrushFireBFS(queue, grid, x0, y0, x1, y1);
        PrecomputeSizeClasses();
    }

    private static readonly Vector2Int[] Directions =
    {
        new(0, 1), new(0, -1), new(1, 0), new(-1, 0),
        new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)
    };

    private void BrushFireBFS(Queue<Vector2Int> queue, IGrid grid)
    {
        float cellSize = grid.CellSize;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            float currentClear = clearance[current.x, current.y];

            for (int d = 0; d < 8; d++)
            {
                var dir = Directions[d];
                int nx = current.x + dir.x;
                int nz = current.y + dir.y;

                if (nx < 0 || nx >= width || nz < 0 || nz >= height) continue;

                float stepCost = (d >= 4) ? cellSize * 1.41421356f : cellSize;
                float newClear = currentClear + stepCost;

                if (newClear < clearance[nx, nz])
                {
                    clearance[nx, nz] = newClear;
                    queue.Enqueue(new Vector2Int(nx, nz));
                }
            }
        }
    }

    private void BrushFireBFS(Queue<Vector2Int> queue, IGrid grid, int x0, int y0, int x1, int y1)
    {
        float cellSize = grid.CellSize;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            float currentClear = clearance[current.x, current.y];

            for (int d = 0; d < 8; d++)
            {
                var dir = Directions[d];
                int nx = current.x + dir.x;
                int nz = current.y + dir.y;

                if (nx < x0 || nx > x1 || nz < y0 || nz > y1) continue;

                float stepCost = (d >= 4) ? cellSize * 1.41421356f : cellSize;
                float newClear = currentClear + stepCost;

                if (newClear < clearance[nx, nz])
                {
                    clearance[nx, nz] = newClear;
                    queue.Enqueue(new Vector2Int(nx, nz));
                }
            }
        }
    }

    public float GetClearance(Vector2Int cell)
    {
        if (cell.x < 0 || cell.x >= width || cell.y < 0 || cell.y >= height)
            return 0f;
        return clearance[cell.x, cell.y];
    }

    public bool CanPass(Vector2Int cell, float unitRadius)
    {
        return GetClearance(cell) >= unitRadius;
    }

    /// <summary>
    /// Fast size-class passability check using precomputed bitfield.
    /// </summary>
    public bool CanPass(Vector2Int cell, UnitSizeClass sizeClass)
    {
        if (cell.x < 0 || cell.x >= width || cell.y < 0 || cell.y >= height)
            return false;
        return (sizeClassPassable[cell.x, cell.y] & (1 << (int)sizeClass)) != 0;
    }

    /// <summary>
    /// Precompute per-cell bitfield for fast size-class passability checks.
    /// Called automatically after ComputeFull and UpdateRegion.
    /// </summary>
    private void PrecomputeSizeClasses()
    {
        if (sizeClassPassable == null || sizeClassPassable.GetLength(0) != width || sizeClassPassable.GetLength(1) != height)
            sizeClassPassable = new byte[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                byte bits = 0;
                float c = clearance[x, y];
                if (c >= SizeClassUtil.ClearanceRadius[0]) bits |= 1;
                if (c >= SizeClassUtil.ClearanceRadius[1]) bits |= 2;
                if (c >= SizeClassUtil.ClearanceRadius[2]) bits |= 4;
                sizeClassPassable[x, y] = bits;
            }
        }
    }
}
