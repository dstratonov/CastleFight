using UnityEngine;
using System.Collections.Generic;

public class SpatialHashGrid
{
    private readonly float cellSize;
    private readonly float inverseCellSize;
    private readonly Dictionary<long, List<Unit>> buckets = new();
    private readonly Dictionary<int, long> unitBucketMap = new();

    private static readonly List<Unit> emptyList = new();
    private readonly List<Unit> queryBuffer = new();

    public SpatialHashGrid(float cellSize)
    {
        this.cellSize = Mathf.Max(cellSize, 1f);
        inverseCellSize = 1f / this.cellSize;
    }

    private long HashKey(int cx, int cz)
    {
        return ((long)cx << 32) | (uint)cz;
    }

    private void GetCellCoords(Vector3 pos, out int cx, out int cz)
    {
        cx = Mathf.FloorToInt(pos.x * inverseCellSize);
        cz = Mathf.FloorToInt(pos.z * inverseCellSize);
    }

    public void Insert(Unit unit)
    {
        if (unit == null) return;
        GetCellCoords(unit.transform.position, out int cx, out int cz);
        long key = HashKey(cx, cz);

        if (!buckets.TryGetValue(key, out var list))
        {
            list = new List<Unit>(4);
            buckets[key] = list;
        }
        list.Add(unit);
        unitBucketMap[unit.GetInstanceID()] = key;
    }

    public void Remove(Unit unit)
    {
        if (unit == null) return;
        int id = unit.GetInstanceID();
        if (!unitBucketMap.TryGetValue(id, out long key)) return;

        if (buckets.TryGetValue(key, out var list))
        {
            list.Remove(unit);
            if (list.Count == 0)
                buckets.Remove(key);
        }
        unitBucketMap.Remove(id);
    }

    public void UpdateUnit(Unit unit)
    {
        if (unit == null) return;
        int id = unit.GetInstanceID();
        GetCellCoords(unit.transform.position, out int cx, out int cz);
        long newKey = HashKey(cx, cz);

        if (unitBucketMap.TryGetValue(id, out long oldKey) && oldKey == newKey)
            return;

        if (unitBucketMap.ContainsKey(id))
        {
            if (buckets.TryGetValue(oldKey, out var oldList))
            {
                oldList.Remove(unit);
                if (oldList.Count == 0)
                    buckets.Remove(oldKey);
            }
        }

        if (!buckets.TryGetValue(newKey, out var newList))
        {
            newList = new List<Unit>(4);
            buckets[newKey] = newList;
        }
        newList.Add(unit);
        unitBucketMap[id] = newKey;
    }

    public List<Unit> QueryRadius(Vector3 center, float radius)
    {
        queryBuffer.Clear();
        float radiusSq = radius * radius;

        GetCellCoords(center - new Vector3(radius, 0, radius), out int minCx, out int minCz);
        GetCellCoords(center + new Vector3(radius, 0, radius), out int maxCx, out int maxCz);

        for (int cx = minCx; cx <= maxCx; cx++)
        {
            for (int cz = minCz; cz <= maxCz; cz++)
            {
                long key = HashKey(cx, cz);
                if (!buckets.TryGetValue(key, out var list)) continue;

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var u = list[i];
                    if (u == null || u.IsDead) continue;
                    float distSq = (u.transform.position - center).sqrMagnitude;
                    if (distSq <= radiusSq)
                        queryBuffer.Add(u);
                }
            }
        }

        return queryBuffer;
    }

    public Unit FindNearest(Vector3 center, float maxRange, int excludeTeam)
    {
        float bestDistSq = maxRange * maxRange;
        Unit best = null;

        GetCellCoords(center - new Vector3(maxRange, 0, maxRange), out int minCx, out int minCz);
        GetCellCoords(center + new Vector3(maxRange, 0, maxRange), out int maxCx, out int maxCz);

        for (int cx = minCx; cx <= maxCx; cx++)
        {
            for (int cz = minCz; cz <= maxCz; cz++)
            {
                long key = HashKey(cx, cz);
                if (!buckets.TryGetValue(key, out var list)) continue;

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var u = list[i];
                    if (u == null || u.IsDead) continue;
                    if (u.TeamId == excludeTeam) continue;

                    float distSq = (u.transform.position - center).sqrMagnitude;
                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        best = u;
                    }
                }
            }
        }

        return best;
    }

    public void Clear()
    {
        buckets.Clear();
        unitBucketMap.Clear();
    }

    public int UnitCount => unitBucketMap.Count;
}
