using UnityEngine;
using System.Collections.Generic;

public struct CostStamp
{
    public int Id;
    public Vector2Int Min;
    public Vector2Int Max;
    public float CostMultiplier;
    public float ExpirationTime;
}

public class CostStampManager
{
    private readonly List<CostStamp> stamps = new();
    private int nextId = 0;

    public int AddStamp(Vector2Int min, Vector2Int max, float costMultiplier, float duration = -1f)
    {
        var stamp = new CostStamp
        {
            Id = nextId++,
            Min = min,
            Max = max,
            CostMultiplier = costMultiplier,
            ExpirationTime = duration > 0 ? Time.time + duration : float.MaxValue
        };
        stamps.Add(stamp);
        return stamp.Id;
    }

    public void RemoveStamp(int stampId)
    {
        stamps.RemoveAll(s => s.Id == stampId);
    }

    public void Tick()
    {
        float now = Time.time;
        stamps.RemoveAll(s => s.ExpirationTime <= now);
    }

    public float GetCostMultiplier(Vector2Int cell)
    {
        float totalCost = 1f;
        for (int i = 0; i < stamps.Count; i++)
        {
            var s = stamps[i];
            if (cell.x >= s.Min.x && cell.x <= s.Max.x &&
                cell.y >= s.Min.y && cell.y <= s.Max.y)
            {
                totalCost *= s.CostMultiplier;
            }
        }
        return totalCost;
    }

    public IReadOnlyList<CostStamp> ActiveStamps => stamps;
}
