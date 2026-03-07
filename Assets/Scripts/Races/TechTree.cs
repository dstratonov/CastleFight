using UnityEngine;

[System.Serializable]
public class TechTreeNode
{
    public string buildingId;
    public string[] prerequisites;
    [Tooltip("Tier level in the tech tree (0 = base, 1 = tier 1, etc.)")]
    public int tier;
}
