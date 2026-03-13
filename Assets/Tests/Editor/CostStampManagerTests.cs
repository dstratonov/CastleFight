using NUnit.Framework;
using UnityEngine;

[TestFixture]
public class CostStampManagerTests
{
    [Test]
    public void AddStamp_IncrementsCost()
    {
        var mgr = new CostStampManager();
        mgr.AddStamp(new Vector2Int(0, 0), new Vector2Int(5, 5), 2f, currentTime: 0f);

        float cost = mgr.GetCostMultiplier(new Vector2Int(3, 3));
        Assert.AreEqual(2f, cost, 0.001f, "Cell inside stamp should have 2x cost");
    }

    [Test]
    public void GetCostMultiplier_DefaultIsOne()
    {
        var mgr = new CostStampManager();
        float cost = mgr.GetCostMultiplier(new Vector2Int(5, 5));
        Assert.AreEqual(1f, cost, 0.001f);
    }

    [Test]
    public void GetCostMultiplier_OutsideStamp_IsOne()
    {
        var mgr = new CostStampManager();
        mgr.AddStamp(new Vector2Int(0, 0), new Vector2Int(2, 2), 3f, currentTime: 0f);

        float cost = mgr.GetCostMultiplier(new Vector2Int(5, 5));
        Assert.AreEqual(1f, cost, 0.001f, "Cell outside stamp bounds should be 1");
    }

    [Test]
    public void MultipleStamps_Multiply()
    {
        var mgr = new CostStampManager();
        mgr.AddStamp(new Vector2Int(0, 0), new Vector2Int(5, 5), 2f, currentTime: 0f);
        mgr.AddStamp(new Vector2Int(3, 3), new Vector2Int(8, 8), 3f, currentTime: 0f);

        float cost = mgr.GetCostMultiplier(new Vector2Int(4, 4));
        Assert.AreEqual(6f, cost, 0.001f, "Overlapping stamps should multiply: 2 * 3 = 6");
    }

    [Test]
    public void RemoveStamp_RestoresCost()
    {
        var mgr = new CostStampManager();
        int id = mgr.AddStamp(new Vector2Int(0, 0), new Vector2Int(5, 5), 5f, currentTime: 0f);

        Assert.AreEqual(5f, mgr.GetCostMultiplier(new Vector2Int(3, 3)), 0.001f);

        mgr.RemoveStamp(id);
        Assert.AreEqual(1f, mgr.GetCostMultiplier(new Vector2Int(3, 3)), 0.001f,
            "After removal, cost should return to 1");
    }

    [Test]
    public void Tick_ExpiresStamps()
    {
        var mgr = new CostStampManager();
        mgr.AddStamp(new Vector2Int(0, 0), new Vector2Int(5, 5), 2f, currentTime: 0f, duration: 5f);

        Assert.AreEqual(2f, mgr.GetCostMultiplier(new Vector2Int(3, 3)), 0.001f);

        mgr.Tick(currentTime: 3f);
        Assert.AreEqual(2f, mgr.GetCostMultiplier(new Vector2Int(3, 3)), 0.001f,
            "Stamp should still be active at t=3");

        mgr.Tick(currentTime: 6f);
        Assert.AreEqual(1f, mgr.GetCostMultiplier(new Vector2Int(3, 3)), 0.001f,
            "Stamp should expire after duration elapses");
    }

    [Test]
    public void Tick_PermanentStamp_NeverExpires()
    {
        var mgr = new CostStampManager();
        mgr.AddStamp(new Vector2Int(0, 0), new Vector2Int(5, 5), 2f, currentTime: 0f);

        mgr.Tick(currentTime: 999999f);
        Assert.AreEqual(2f, mgr.GetCostMultiplier(new Vector2Int(3, 3)), 0.001f,
            "Permanent stamp (no duration) should never expire");
    }

    [Test]
    public void ActiveStamps_ReflectsCurrentState()
    {
        var mgr = new CostStampManager();
        Assert.AreEqual(0, mgr.ActiveStamps.Count);

        mgr.AddStamp(new Vector2Int(0, 0), new Vector2Int(1, 1), 2f, currentTime: 0f, duration: 5f);
        mgr.AddStamp(new Vector2Int(2, 2), new Vector2Int(3, 3), 3f, currentTime: 0f);
        Assert.AreEqual(2, mgr.ActiveStamps.Count);

        mgr.Tick(currentTime: 10f);
        Assert.AreEqual(1, mgr.ActiveStamps.Count, "Only the permanent stamp should remain");
    }

    [Test]
    public void BoundaryCell_IsIncludedInStamp()
    {
        var mgr = new CostStampManager();
        mgr.AddStamp(new Vector2Int(2, 2), new Vector2Int(4, 4), 2f, currentTime: 0f);

        Assert.AreEqual(2f, mgr.GetCostMultiplier(new Vector2Int(2, 2)), 0.001f, "Min corner");
        Assert.AreEqual(2f, mgr.GetCostMultiplier(new Vector2Int(4, 4)), 0.001f, "Max corner");
        Assert.AreEqual(1f, mgr.GetCostMultiplier(new Vector2Int(1, 2)), 0.001f, "Just outside");
    }
}
